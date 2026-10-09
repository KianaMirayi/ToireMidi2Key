using System.Runtime.InteropServices;

namespace ToireMidi2Key;

/// <summary>运行时环境探测 + 低延迟相关的小开关（权限、定时器精度、MMCSS 线程调度）。</summary>
public static class CoreInfo
{
    [DllImport("shell32.dll")]
    private static extern bool IsUserAnAdmin();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, out uint tokenInformation, uint tokenInformationLength, out uint returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint timeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint timeEndPeriod(uint milliseconds);

    [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AvSetMmThreadCharacteristics(string taskName, out uint taskIndex);

    [DllImport("avrt.dll", SetLastError = true)]
    private static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);

    private const uint TokenQuery = 0x0008;

    // 坑：权限检测必须用 TokenElevation=20（DWORD 0/1），不是 TokenElevationType=18（1=Default/2=Full/3=Limited，判 !=0 会恒为真）
    private const int TokenElevationClass = 20;
    private const int TokenElevationTypeClass = 18;

    /// <summary>权威判断：当前进程令牌是否已提升（= 真正以管理员身份在跑）。</summary>
    public static bool IsElevated() => ReadTokenUInt(TokenElevationClass) == 1;

    /// <summary>1=Default（UAC 关闭或非管理员账户）2=Full（已提升）3=Limited（是管理员但被 UAC 限制）。</summary>
    public static uint ElevationType() => ReadTokenUInt(TokenElevationTypeClass);

    /// <summary>给日志用的一行诊断信息。</summary>
    public static string DescribeElevation()
    {
        uint elevated = ReadTokenUInt(TokenElevationClass);
        uint type = ElevationType();
        string typeText = type switch
        {
            1 => "Default",
            2 => "Full(已提升)",
            3 => "Limited(UAC 受限)",
            uint.MaxValue => "读取失败",
            _ => $"未知({type})"
        };
        return $"TokenElevation={elevated} ({typeText})  IsUserAnAdmin(旧接口)={IsUserAnAdminLegacy()}  x64={Is64BitProcess}";
    }

    private static uint ReadTokenUInt(int informationClass)
    {
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token)) return uint.MaxValue;
            if (!GetTokenInformation(token, informationClass, out uint value, 4, out _)) return uint.MaxValue;
            return value;
        }
        catch
        {
            return uint.MaxValue;
        }
        finally
        {
            if (token != IntPtr.Zero) CloseHandle(token);
        }
    }

    /// <summary>老判断（shell32 IsUserAnAdmin），只在日志里用于对比排查。</summary>
    public static bool IsUserAnAdminLegacy()
    {
        try { return IsUserAnAdmin(); }
        catch { return false; }
    }

    /// <summary>是否以管理员身份运行。注入到以管理员运行的游戏（比如原神）需要。</summary>
    public static bool IsAdministrator() => IsElevated();

    /// <summary>SendInput 的 INPUT 结构体字节数：x64 应为 40，x86 应为 28。</summary>
    public static int SendInputStructSize => IntPtr.Size == 8 ? 40 : 28;

    public static bool Is64BitProcess => IntPtr.Size == 8;

    /// <summary>把系统定时器精度提到 1ms（默认 15.6ms 会拖慢延迟按下/重触发/和弦错峰）。</summary>
    public static void RaiseTimerResolution(uint milliseconds = 1)
    {
        try { timeBeginPeriod(milliseconds); } catch { }
    }

    public static void RestoreTimerResolution(uint milliseconds = 1)
    {
        try { timeEndPeriod(milliseconds); } catch { }
    }

    /// <summary>登记为 MMCSS "Pro Audio" 线程（ASIO 驱动同款调度），减少 CPU 满载时的唤醒抖动。</summary>
    public static IntPtr EnterProAudio()
    {
        try { return AvSetMmThreadCharacteristics("Pro Audio", out uint _); }
        catch { return IntPtr.Zero; }
    }

    public static void ExitProAudio(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        try { AvRevertMmThreadCharacteristics(handle); } catch { /* 忽略 */ }
    }
}
