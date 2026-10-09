using System.Runtime.InteropServices;

namespace Midi2Key;

/// <summary>
/// 用 SendInput 注入按键，等同真实按键。
/// scancode（默认，游戏/DirectInput 认）或 vk（个别老程序只认这个）。
/// </summary>
public sealed class KeySender
{
    public const string ModeScancode = "scancode";
    public const string ModeVk = "vk";

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    public string Mode { get; set; } = ModeScancode;
    public bool DryRun { get; set; }
    public int Failures { get; private set; }

    /// <summary>按键事件流水，供离线自检断言用。</summary>
    public readonly List<(string Key, bool Down)> Log = new();

    // UI 线程（紧急松开按钮）和引擎线程都会碰这里的状态，所以必须加锁
    private readonly object _gate = new();
    private readonly Dictionary<string, ushort> _down = new(StringComparer.Ordinal);

    /// <summary>注入失败时抛出（GUI 没有控制台，不能 Console.WriteLine）。</summary>
    public event Action<string> OnError;

    public static ushort ScanCode(ushort vk) => (ushort)(MapVirtualKey(vk, MAPVK_VK_TO_VSC) & 0xFF);

    /// <summary>INPUT 结构体实际字节数（x64 = 40，x86 = 28）。对不上说明结构体定义错了。</summary>
    public static int InputStructSize => Marshal.SizeOf<INPUT>();

    public bool IsDown(string keyName)
    {
        lock (_gate) return _down.ContainsKey(keyName);
    }

    public void Down(string keyName, ushort vk)
    {
        lock (_gate)
        {
            if (_down.ContainsKey(keyName)) return;
            _down[keyName] = vk;
            Log.Add((keyName, true));
            Emit(vk, up: false);
        }
    }

    public void Up(string keyName)
    {
        lock (_gate)
        {
            if (!_down.TryGetValue(keyName, out ushort vk)) return;
            _down.Remove(keyName);
            Log.Add((keyName, false));
            Emit(vk, up: true);
        }
    }

    /// <summary>紧急松开：把所有还按着的键都弹起来（任何线程都可以调）。</summary>
    public void ReleaseAll()
    {
        lock (_gate)
        {
            foreach (string key in _down.Keys.ToList()) Up(key);
        }
    }

    private void Emit(ushort vk, bool up)
    {
        if (DryRun) return;

        var input = new INPUT { type = INPUT_KEYBOARD };
        bool useScan = Mode == ModeScancode;
        input.U.ki.wVk = useScan ? (ushort)0 : vk;
        input.U.ki.wScan = useScan ? ScanCode(vk) : (ushort)0;

        uint flags = 0;
        if (useScan) flags |= KEYEVENTF_SCANCODE;
        if (up) flags |= KEYEVENTF_KEYUP;
        if (IsExtended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        input.U.ki.dwFlags = flags;

        uint sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (sent != 1)
        {
            Failures++;
            OnError?.Invoke($"[警告] SendInput 注入失败（Win32 错误码 {Marshal.GetLastWin32Error()}）。请用管理员权限运行；UAC / 安全软件也可能拦截。");
        }
    }

    /// <summary>方向键、小键盘除号、右 Ctrl/Alt 等需要扩展键标志，否则系统认成另一个键。</summary>
    public static bool IsExtended(ushort vk) => vk switch
    {
        0x21 or 0x22 or 0x23 or 0x24 => true,   // PgUp PgDn End Home
        0x25 or 0x26 or 0x27 or 0x28 => true,   // 方向键
        0x2C or 0x2D or 0x2E => true,           // PrtSc Insert Delete
        0x5B or 0x5C or 0x5D => true,           // Win 键 / 菜单键
        0x6F or 0x90 or 0xA3 or 0xA5 => true,   // 小键盘除号 / NumLock / 右Ctrl / 右Alt
        _ => false
    };
}
