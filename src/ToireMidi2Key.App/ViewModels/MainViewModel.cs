using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToireMidi2Key;
using ToireMidi2Key.App.Services;

namespace ToireMidi2Key.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IBridgeService _bridge;
    private bool _disposed;

    // 日志先进队列，由定时器批量刷进界面。
    // 直接一行一个 Dispatcher.Post + ObservableCollection.Add 会在快速弹奏时把 UI 线程刷爆。
    private readonly ConcurrentQueue<string> _pendingLogs = new();
    private readonly DispatcherTimer _uiTimer;

    public ObservableCollection<MidiDeviceInfo> Devices { get; } = new();
    public ObservableCollection<MappingRowViewModel> Rows { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public IReadOnlyList<string> ModeOptions { get; } = new[] { "scancode", "vk" };
    public IReadOnlyList<string> UnmappedOptions { get; } = new[] { "nearest", "ignore" };

    [ObservableProperty] public partial MidiDeviceInfo? SelectedDevice { get; set; }
    [ObservableProperty] public partial string Mode { get; set; } = "scancode";
    [ObservableProperty] public partial string Unmapped { get; set; } = "nearest";
    [ObservableProperty] public partial decimal? Transpose { get; set; } = 0;
    [ObservableProperty] public partial decimal? MinRetriggerMs { get; set; } = 30;
    [ObservableProperty] public partial decimal? ChordSpreadMs { get; set; } = 0;
    [ObservableProperty] public partial decimal? PresetBaseNote { get; set; } = 48;
    [ObservableProperty] public partial bool SustainEnabled { get; set; }
    [ObservableProperty] public partial bool LearnMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartStopText))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseButtonText))]
    public partial bool IsPaused { get; set; }

    [ObservableProperty] public partial string StatusText { get; set; } = "未启动";
    [ObservableProperty] public partial string DeviceSummary { get; set; } = "";
    [ObservableProperty] public partial string LastNoteText { get; set; } = "—";
    [ObservableProperty] public partial string LastControlText { get; set; } = "—";
    [ObservableProperty] public partial string LatencyText { get; set; } = "延迟：未运行";
    [ObservableProperty] public partial int NotesPlayed { get; set; }
    [ObservableProperty] public partial string AdminHint { get; set; } = "";
    [ObservableProperty] public partial bool IsAdmin { get; set; }
    [ObservableProperty] public partial string ConfigPathText { get; set; } = "";

    public string StartStopText => IsRunning ? "停止" : "启动";
    public string PauseButtonText => IsPaused ? "恢复注入" : "暂停注入";

    /// <summary>供 XAML 设计器使用。</summary>
    public MainViewModel() : this(new BridgeService()) { }

    public MainViewModel(IBridgeService bridge)
    {
        _bridge = bridge;
        ConfigPathText = bridge.ConfigPath;

        IsAdmin = CoreInfo.IsAdministrator();
        AdminHint = IsAdmin
            ? "管理员权限：注入通道已就绪"
            : "非管理员：注入到以管理员运行的游戏（如原神）可能无效 → 点右边「以管理员重启」";

        _bridge.Log += OnBridgeLog;
        _bridge.StateChanged += OnBridgeStateChanged;
        _bridge.NoteReceived += OnNoteReceived;
        _bridge.ControlReceived += OnControlReceived;

        LoadFromConfig(_bridge.Config);
        RefreshDevices();
        foreach (string warning in _bridge.Mapping.Warnings) AppendLog(warning);

        AppendLog("就绪：选设备 → 点「启动」→ 打开游戏里的乐器界面。");
        AppendLog("暂停注入（或踩下 CC66 / ⏸ 按钮）后仍会监听 MIDI，方便用「学习模式」配置映射。");
        AppendLog($"配置文件：{_bridge.ConfigPath}");

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _uiTimer.Tick += (_, _) => FlushUi();
        _uiTimer.Start();
    }


    private void LoadFromConfig(ToireMidi2KeyConfig config)
    {
        Mode = config.Mode;
        Unmapped = config.Unmapped;
        Transpose = config.Transpose;
        MinRetriggerMs = config.MinRetriggerMs;
        ChordSpreadMs = config.ChordSpreadMs;
        SustainEnabled = config.SustainEnabled;

        Rows.Clear();
        foreach (KeyValuePair<string, string> entry in OrderMap(config.Map))
            Rows.Add(new MappingRowViewModel(entry.Key, entry.Value, RemoveRow));
    }

    private static IEnumerable<KeyValuePair<string, string>> OrderMap(Dictionary<string, string> map)
    {
        return map.OrderBy(e =>
        {
            try { return NoteName.Parse(e.Key, NoteName.NamingScientific); }
            catch { return int.MaxValue; }
        });
    }

    private void ApplyUiToConfig()
    {
        ToireMidi2KeyConfig config = _bridge.Config.Clone();
        config.Device = SelectedDevice?.Index ?? config.Device;
        config.Mode = Mode;
        config.Unmapped = Unmapped;
        config.Transpose = (int)(Transpose ?? 0);
        config.MinRetriggerMs = (int)(MinRetriggerMs ?? 30);
        config.ChordSpreadMs = (int)(ChordSpreadMs ?? 0);
        config.SustainEnabled = SustainEnabled;
        config.Map = new Dictionary<string, string>();

        foreach (MappingRowViewModel row in Rows)
        {
            if (string.IsNullOrWhiteSpace(row.NoteText) || string.IsNullOrWhiteSpace(row.KeyText)) continue;
            config.Map[row.NoteText.Trim()] = row.KeyText.Trim().ToUpperInvariant();
        }

        _bridge.ApplyConfig(config);
    }


    [RelayCommand]
    private void RefreshDevices()
    {
        _bridge.RefreshDevices();
        Devices.Clear();
        foreach (MidiDeviceInfo device in _bridge.Devices) Devices.Add(device);

        SelectedDevice = Devices.FirstOrDefault(d => d.Index == _bridge.Config.Device) ?? Devices.FirstOrDefault();

        DeviceSummary = Devices.Count == 0
            ? "没找到 MIDI 输入设备：检查 USB / 驱动，并关掉其它占用它的软件（DAW、MIDI 工具等）"
            : $"发现 {Devices.Count} 个 MIDI 输入设备";
        AppendLog(DeviceSummary);
    }

    [RelayCommand]
    private void StartStop()
    {
        try
        {
            if (_bridge.IsRunning)
            {
                _bridge.Stop();
            }
            else
            {
                if (SelectedDevice is null)
                {
                    AppendLog("没有可用的 MIDI 设备，先插上键盘并点「刷新」。");
                    return;
                }
                ApplyUiToConfig();          // 启动前把界面上的设置同步进引擎
                _bridge.Start(SelectedDevice.Index);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"错误：{ex.Message}");
        }
        SyncState();
    }

    [RelayCommand]
    private void TogglePause() => _bridge.TogglePause();

    [RelayCommand]
    private void Panic()
    {
        _bridge.Panic();
        AppendLog("已紧急松开所有按键。");
    }

    [RelayCommand]
    private void ResetLatency()
    {
        _bridge.ResetLatency();
        AppendLog("已重置延迟统计。");
    }

    [RelayCommand]
    private void SaveConfig()
    {
        ApplyUiToConfig();
        _bridge.SaveConfig();
    }

    [RelayCommand]
    private void ReloadConfig()
    {
        _bridge.LoadConfig();
        LoadFromConfig(_bridge.Config);
        AppendLog("已从磁盘重新载入配置。");
    }

    [RelayCommand]
    private void AddRow() => Rows.Add(new MappingRowViewModel("", "", RemoveRow));

    private void RemoveRow(MappingRowViewModel row) => Rows.Remove(row);

    [RelayCommand]
    private void ApplyGenshinPreset()
    {
        int baseNote = (int)(PresetBaseNote ?? GenshinPreset.DefaultBaseNote);
        Dictionary<string, string> map = GenshinPreset.Build(baseNote);

        Rows.Clear();
        foreach (KeyValuePair<string, string> entry in OrderMap(map))
            Rows.Add(new MappingRowViewModel(entry.Key, entry.Value, RemoveRow));

        AppendLog($"已套用原神乐器预设：最低音 MIDI {baseNote}（{NoteName.Describe(baseNote)}）→ Z 排，共 {map.Count} 个音。");
        AppendLog("记得点「保存配置」；进了游戏对着乐器界面弹一下，音不对就调整「最低音」这个数字。");
    }

    [RelayCommand]
    private void ClearLog() => Logs.Clear();

    [RelayCommand]
    private void OpenConfigFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_bridge.ConfigPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog($"打开目录失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private void RestartAsAdmin()
    {
        if (IsAdmin)
        {
            AppendLog("已经是管理员权限了。");
            return;
        }
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                lifetime.Shutdown();
        }
        catch (Exception ex)
        {
            AppendLog($"提权被取消或失败：{ex.Message}");
        }
    }


    private void OnBridgeLog(string message) => AppendLog(message);   // 只入队，不碰界面

    private void OnBridgeStateChanged() => Dispatcher.UIThread.Post(SyncState);

    private void OnNoteReceived(int note, int velocity) => Dispatcher.UIThread.Post(() =>
    {
        NotesPlayed++;
        LastNoteText = $"{NoteName.Scientific(note)} / {NoteName.Yamaha(note)}(Yamaha) · MIDI {note} · 力度 {velocity}";
        if (LearnMode) LearnNote(note);
    });

    private void OnControlReceived(int cc, int value) => Dispatcher.UIThread.Post(() =>
    {
        LastControlText = $"CC {cc} = {value}";
    });

    private void LearnNote(int note)
    {
        string text = note.ToString();
        MappingRowViewModel? row = Rows.FirstOrDefault(r => r.NoteText.Trim() == text);

        if (row is null)
        {
            row = new MappingRowViewModel(text, GuessKeyFor(note), RemoveRow);
            Rows.Add(row);
            AppendLog($"学到新音：MIDI {note}（{NoteName.Describe(note)}）→ 目标按键 = {row.KeyText}（可以在表里改）");
        }
        else
        {
            AppendLog($"学到：MIDI {note}（{NoteName.Describe(note)}）→ 已有映射 {row.KeyText}");
        }

        foreach (MappingRowViewModel r in Rows) r.IsHighlighted = false;
        row.IsHighlighted = true;
        LearnMode = true;
    }

    private string GuessKeyFor(int note)
    {
        Mapping mapping = _bridge.Mapping;
        if (mapping.TryResolve(note, Mapping.ModeIgnore, out string exact, out _, out _)) return exact;
        if (mapping.TryResolve(note, Mapping.ModeNearest, out string nearest, out _, out _)) return nearest;
        return "";
    }

    private void SyncState()
    {
        IsRunning = _bridge.IsRunning;
        IsPaused = _bridge.IsPaused;
        StatusText = !IsRunning ? "未启动" : IsPaused ? "运行中（已暂停注入）" : "运行中";
    }


    private void FlushUi()
    {
        int budget = 400;
        while (budget-- > 0 && _pendingLogs.TryDequeue(out string? line))
        {
            Logs.Add(line);
        }
        while (Logs.Count > 500) Logs.RemoveAt(0);

        if (!_bridge.IsRunning)
        {
            LatencyText = "延迟：未运行";
            return;
        }

        LatencySummary total = _bridge.EngineLatency;
        LatencySummary send = _bridge.InjectionLatency;
        LatencySummary queue = _bridge.QueueLatency;
        LatencyText = total.Count == 0
            ? "延迟：等着弹琴…"
            : $"本程序延迟：总 {total.Average:F2}ms（p95 {total.P95:F2}）｜唤醒 {queue.Average:F2}ms ｜注入 {send.Average:F2}ms";
    }

    private void AppendLog(string message)
    {
        _pendingLogs.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  {message}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _uiTimer.Stop();
        _bridge.Log -= OnBridgeLog;
        _bridge.StateChanged -= OnBridgeStateChanged;
        _bridge.NoteReceived -= OnNoteReceived;
        _bridge.ControlReceived -= OnControlReceived;
        _bridge.Dispose();
    }
}
