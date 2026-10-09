using System.Runtime.InteropServices;

namespace ToireMidi2Key;

/// <summary>一个已解析的 MIDI 短消息（3 字节）。</summary>
public readonly struct MidiMessage
{
    public readonly byte Status;
    public readonly byte Data1;
    public readonly byte Data2;

    /// <summary>回调被系统调用那一刻的时间戳，用于测量延迟。</summary>
    public readonly long Timestamp;

    public MidiMessage(byte status, byte data1, byte data2)
    {
        Status = status;
        Data1 = data1;
        Data2 = data2;
        Timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    private int Command => Status & 0xF0;
    public int Channel => Status & 0x0F;
    public bool IsNoteOn => Command == 0x90 && Data2 > 0;
    public bool IsNoteOff => Command == 0x80 || (Command == 0x90 && Data2 == 0);
    public bool IsControlChange => Command == 0xB0;
    public bool IsPitchBend => Command == 0xE0;
}

public sealed class MidiDeviceInfo
{
    // 用属性而不是字段：Avalonia 的编译期绑定不认公开字段
    public int Index { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>用 winmm.dll 打开 MIDI 输入端口；回调来自系统线程，必须立刻返回、绝不能抛异常。</summary>
public sealed class MidiInput : IDisposable
{
    private const int CALLBACK_FUNCTION = 0x00030000;
    private const int MIM_DATA = 0x3C3;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MIDIINCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public uint dwSupport;
    }

    private delegate void MidiInProc(IntPtr hMidiIn, uint wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint midiInGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint midiInGetDevCaps(UIntPtr uDeviceID, ref MIDIINCAPS caps, uint cbMidiInCaps);

    [DllImport("winmm.dll")]
    private static extern uint midiInOpen(out IntPtr handle, uint uDeviceID, MidiInProc callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")] private static extern uint midiInStart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint midiInStop(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint midiInReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint midiInClose(IntPtr handle);

    /// <summary>枚举系统里所有 MIDI 输入设备。</summary>
    public static List<MidiDeviceInfo> ListDevices()
    {
        var list = new List<MidiDeviceInfo>();
        uint count = midiInGetNumDevs();
        for (uint i = 0; i < count; i++)
        {
            var caps = new MIDIINCAPS { szPname = string.Empty };
            uint rc = midiInGetDevCaps(new UIntPtr(i), ref caps, (uint)Marshal.SizeOf<MIDIINCAPS>());
            list.Add(new MidiDeviceInfo
            {
                Index = (int)i,
                Name = rc == 0 ? caps.szPname : "<名称读取失败>"
            });
        }
        return list;
    }

    // 必须一直持有这个委托，否则 GC 回收后系统回调踩空
    private readonly MidiInProc _proc;
    private readonly Action<MidiMessage> _onMessage;
    private IntPtr _handle = IntPtr.Zero;

    public int DeviceIndex { get; private set; } = -1;
    public bool IsOpen => _handle != IntPtr.Zero;

    public MidiInput(Action<MidiMessage> onMessage)
    {
        _onMessage = onMessage;
        _proc = OnMidiIn;
    }

    public void Open(int deviceIndex)
    {
        uint rc = midiInOpen(out _handle, (uint)deviceIndex, _proc, IntPtr.Zero, CALLBACK_FUNCTION);
        if (rc != 0)
            throw new InvalidOperationException($"midiInOpen 失败（错误码 {rc}）：设备可能被别的程序占用，或用的是 Windows 自带合成器端口。");

        rc = midiInStart(_handle);
        if (rc != 0)
        {
            midiInClose(_handle);
            _handle = IntPtr.Zero;
            throw new InvalidOperationException($"midiInStart 失败（错误码 {rc}）。");
        }
        DeviceIndex = deviceIndex;
    }

    private void OnMidiIn(IntPtr hMidiIn, uint wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)
    {
        if (wMsg != MIM_DATA) return;   // 只关心短消息（音符/CC）
        long packed = dwParam1.ToInt64();
        try
        {
            _onMessage(new MidiMessage(
                (byte)(packed & 0xFF),
                (byte)((packed >> 8) & 0xFF),
                (byte)((packed >> 16) & 0xFF)));
        }
        catch
        {
            // 回调里不能抛异常，否则会把进程带走
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        midiInStop(_handle);
        midiInReset(_handle);
        midiInClose(_handle);
        _handle = IntPtr.Zero;
    }
}
