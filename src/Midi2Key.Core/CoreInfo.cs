using System.Runtime.InteropServices;

namespace Midi2Key;

/// <summary>运行时环境探测 + 低延迟相关的小开关（权限、定时器精度、MMCSS 线程调度）。</summary>
public static class CoreInfo
{
    [DllImport("shell32.dll")]
    private static extern bool IsUserAnAdmin();

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint timeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint timeEndPeriod(uint milliseconds);

    [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AvSetMmThreadCharacteristics(string taskName, out uint taskIndex);

    [DllImport("avrt.dll", SetLastError = true)]
    private static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);

    /// <summary>当前进程是否以管理员身份运行。注入到以管理员运行的游戏（比如原神）通常需要。</summary>
    public static bool IsAdministrator()
    {
        try { return IsUserAnAdmin(); }
        catch { return false; }
    }

    /// <summary>SendInput 的 INPUT 结构体字节数：x64 应为 40，x86 应为 28。</summary>
    public static int SendInputStructSize => IntPtr.Size == 8 ? 40 : 28;

    public static bool Is64BitProcess => IntPtr.Size == 8;

    /// <summary>把系统定时器精度提到 1ms（默认 15.6ms 会拖慢"延迟按下/重触发/和弦错峰"）。</summary>
    public static void RaiseTimerResolution(uint milliseconds = 1)
    {
        try { timeBeginPeriod(milliseconds); } catch { /* 失败也不致命 */ }
    }

    public static void RestoreTimerResolution(uint milliseconds = 1)
    {
        try { timeEndPeriod(milliseconds); } catch { /* 忽略 */ }
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
