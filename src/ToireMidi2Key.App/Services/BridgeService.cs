using ToireMidi2Key;

namespace ToireMidi2Key.App.Services;

/// <summary>把 Core 的三块（MIDI 输入 / 按键注入 / 翻译引擎）组装起来，供 ViewModel 使用。</summary>
public sealed class BridgeService : IBridgeService
{
    private MidiInput? _midi;
    private KeySender? _sender;
    private BridgeEngine? _engine;

    public bool IsRunning => _engine != null && _midi is { IsOpen: true };

    public bool IsPaused => _engine is { Enabled: false };

    public IReadOnlyList<MidiDeviceInfo> Devices { get; private set; } = Array.Empty<MidiDeviceInfo>();

    public Mapping Mapping { get; private set; }

    public ToireMidi2KeyConfig Config { get; private set; }

    public string ConfigPath { get; }

    public LatencySummary QueueLatency => _engine?.QueueWaitStats.Summarize() ?? LatencySummary.Empty;

    public LatencySummary InjectionLatency => _engine?.SendStats.Summarize() ?? LatencySummary.Empty;

    public LatencySummary EngineLatency => _engine?.EngineTotalStats.Summarize() ?? LatencySummary.Empty;

    public void ResetLatency() => _engine?.ResetStats();

    public event Action<string>? Log;
    public event Action? StateChanged;
    public event Action<int, int>? NoteReceived;
    public event Action<int, int>? ControlReceived;

    public BridgeService()
    {
        ConfigPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        Config = ToireMidi2KeyConfig.Load(ConfigPath);
        Mapping = new Mapping(Config);
        RefreshDevices();
    }

    public void RefreshDevices()
    {
        Devices = MidiInput.ListDevices();
    }

    public void LoadConfig()
    {
        Config = ToireMidi2KeyConfig.Load(ConfigPath);
        Mapping = new Mapping(Config);
        foreach (string warning in Mapping.Warnings) Emit(warning);
        StateChanged?.Invoke();
    }

    public void ApplyConfig(ToireMidi2KeyConfig config)
    {
        // 就地更新同一个 Config 实例：运行中的引擎持有该引用，数值参数（移调 / 最小重触发 / 和弦错峰…）立刻生效；Mapping 是构造时注入的，改映射表仍需停止后重启。
        Config.Device = config.Device;
        Config.DeviceName = config.DeviceName;
        Config.Mode = config.Mode;
        Config.NoteNaming = config.NoteNaming;
        Config.Transpose = config.Transpose;
        Config.MinRetriggerMs = config.MinRetriggerMs;
        Config.MinPulseMs = config.MinPulseMs;
        Config.ChordSpreadMs = config.ChordSpreadMs;
        Config.VelocityThreshold = config.VelocityThreshold;
        Config.Unmapped = config.Unmapped;
        Config.ToggleCc = config.ToggleCc;
        Config.SustainCc = config.SustainCc;
        Config.SustainEnabled = config.SustainEnabled;
        Config.AutoElevate = config.AutoElevate;
        Config.Map = config.Map;

        Mapping = new Mapping(Config);
        foreach (string warning in Mapping.Warnings) Emit(warning);
        StateChanged?.Invoke();
    }

    public void SaveConfig()
    {
        Config.Save(ConfigPath);
        Emit($"已保存配置：{ConfigPath}");
    }

    public void Start(int deviceIndex)
    {
        if (IsRunning) return;

        _sender = new KeySender { Mode = Config.Mode };
        _sender.OnError += Emit;

        _engine = new BridgeEngine(Mapping, _sender, Config);
        _engine.OnLog += Emit;
        _engine.OnStateChanged += () => StateChanged?.Invoke();
        _engine.Start();

        _midi = new MidiInput(OnMidiMessage);
        try
        {
            _midi.Open(deviceIndex);
        }
        catch
        {
            _engine.Dispose();
            _engine = null;
            _midi = null;
            _sender.ReleaseAll();
            _sender = null;
            throw;
        }

        Emit($"已连接 MIDI 设备 #{deviceIndex}：{Devices.FirstOrDefault(d => d.Index == deviceIndex)?.Name ?? "?"}");
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        _midi?.Dispose();
        _midi = null;

        _engine?.Dispose();     // 内部会把所有按键松开
        _engine = null;

        _sender?.ReleaseAll();
        _sender = null;

        Emit("已停止。");
        StateChanged?.Invoke();
    }

    public void TogglePause() => _engine?.RequestToggle();

    public void Panic() => _engine?.Panic();

    private void OnMidiMessage(MidiMessage message)
    {
        try
        {
            if (message.IsNoteOn)
            {
                NoteReceived?.Invoke(message.Data1, message.Data2);
                _engine?.PostNoteOn(message.Data1, message.Data2, message.Timestamp);
            }
            else if (message.IsNoteOff)
            {
                _engine?.PostNoteOff(message.Data1, message.Timestamp);
            }
            else if (message.IsControlChange)
            {
                ControlReceived?.Invoke(message.Data1, message.Data2);
                _engine?.PostControlChange(message.Data1, message.Data2, message.Timestamp);
            }
        }
        catch
        {
            // MIDI 回调里绝不能抛异常
        }
    }

    private void Emit(string message) => Log?.Invoke(message);

    public void Dispose()
    {
        try { Stop(); } catch { /* 退出时忽略 */ }
    }
}
