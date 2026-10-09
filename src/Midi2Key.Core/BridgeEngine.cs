using System.Diagnostics;

namespace Midi2Key;

/// <summary>
/// 翻译引擎：吃进 MIDI 音符，吐出键盘按键。
///
/// 线程模型：
///   - MIDI 回调在系统线程，只做一件事：Post 到队列（绝不阻塞）
///   - 引擎自己有一条工作线程，串行处理队列 + 定时器 → 按键顺序完全确定
///   - UI 通过 OnLog / OnStateChanged 事件拿状态，更新界面时记得回 UI 线程
///
/// 延迟关键路径（决定手感）：MIDI 回调 → 入队 → 工作线程拾起 → SendInput。
/// 任何一步里做慢操作（同步写控制台、刷 UI）都会直接变成弹琴的延迟，
/// 所以：① 日志一律放在按键注入之后；② 订阅方必须非阻塞；③ 用自旋唤醒而不是纯阻塞等待。
/// </summary>
public sealed class BridgeEngine : IDisposable
{
    private readonly Mapping _mapping;
    private readonly KeySender _sender;
    private readonly Midi2KeyConfig _config;

    private readonly Queue<Action> _queue = new();
    private readonly object _gate = new();

    // 自旋 400 次再进入内核等待：事件到达时能被立刻拾起，省掉一次完整线程调度（约 0.3~1ms）
    private readonly ManualResetEventSlim _signal = new(false, 400);

    private readonly List<(double Due, Action Action)> _timers = new();
    private readonly Thread _worker;
    private volatile bool _stopping;
    private IntPtr _mmcssHandle = IntPtr.Zero;

    // 以下状态只在工作线程里改
    private readonly Dictionary<int, int> _noteCounts = new();      // 音 -> 还没 note-off 的按下次数
    private readonly Dictionary<int, string> _noteKeys = new();     // 音 -> 当前占用的按键
    private readonly Dictionary<string, int> _keyRefs = new();      // 按键 -> 引用计数
    private readonly Dictionary<string, ushort> _keyVks = new();    // 按键 -> 虚拟键码
    private readonly Dictionary<string, double> _lastRelease = new();
    private readonly Dictionary<string, int> _pressGeneration = new();
    private readonly HashSet<string> _sustained = new();

    private double _lastNoteOnTime;
    private double _lastPressScheduled;

    public Mapping Mapping => _mapping;
    public Midi2KeyConfig Config => _config;

    /// <summary>false = 暂停状态，一个按键都不会注入。</summary>
    public volatile bool Enabled = true;

    public bool SustainDown { get; private set; }
    public int NotesPlayed { get; private set; }
    public bool LogNotes { get; set; } = true;

    // ---------------------------------------------------------------- 延迟统计
    /// <summary>MIDI 回调入口 → 入队（我们自己这段代码的耗时）。</summary>
    public LatencyStats CallbackStats { get; } = new();
    /// <summary>入队 → 工作线程拾起（线程唤醒延迟）。</summary>
    public LatencyStats QueueWaitStats { get; } = new();
    /// <summary>入队 → 处理完成（含 SendInput），本条最重要。</summary>
    public LatencyStats EngineTotalStats { get; } = new();
    /// <summary>单次 SendInput 注入耗时。</summary>
    public LatencyStats SendStats { get; } = new();

    public void ResetStats()
    {
        CallbackStats.Clear();
        QueueWaitStats.Clear();
        EngineTotalStats.Clear();
        SendStats.Clear();
    }

    /// <summary>引擎线程抛出的事件（日志）。订阅方必须尽快返回，绝不要在这里写控制台/刷 UI。</summary>
    public event Action<string> OnLog;

    /// <summary>启用/暂停、延音状态变化。</summary>
    public event Action OnStateChanged;

    public BridgeEngine(Mapping mapping, KeySender sender, Midi2KeyConfig config, bool logNotes = true)
    {
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        LogNotes = logNotes;
        _worker = new Thread(Loop)
        {
            IsBackground = true,
            Name = "midi2key-engine",
            Priority = ThreadPriority.Highest
        };
    }

    public void Start()
    {
        // 1ms 定时器精度：否则"延迟按下/同音重触发/和弦错峰"会被默认的 15.6ms 精度拖慢
        CoreInfo.RaiseTimerResolution(1);
        _worker.Start();
    }

    // ---------------------------------------------------------------- 对外投递

    public void PostNoteOn(int note, int velocity, long callbackTimestamp = 0) =>
        Post(() => HandleNoteOn(note, velocity), callbackTimestamp);

    public void PostNoteOff(int note, long callbackTimestamp = 0) =>
        Post(() => HandleNoteOff(note), callbackTimestamp);

    public void PostControlChange(int cc, int value, long callbackTimestamp = 0) =>
        Post(() => HandleControlChange(cc, value), callbackTimestamp);

    public void Post(Action action, long callbackTimestamp = 0)
    {
        if (action == null) return;

        long posted = Stopwatch.GetTimestamp();
        if (callbackTimestamp != 0) CallbackStats.Add(ToMilliseconds(posted - callbackTimestamp));

        void Measured()
        {
            QueueWaitStats.Add(ToMilliseconds(Stopwatch.GetTimestamp() - posted));
            action();
            EngineTotalStats.Add(ToMilliseconds(Stopwatch.GetTimestamp() - posted));
        }

        lock (_gate) _queue.Enqueue(Measured);
        _signal.Set();
    }

    /// <summary>暂停 / 恢复（UI 按钮和 MIDI CC 都走这里）。</summary>
    public void RequestToggle() => Post(() => SetEnabled(!Enabled));

    private void SetEnabled(bool value)
    {
        Enabled = value;
        if (!value) ReleaseEverything();
        Log(value ? "▶ 已启用：MIDI 键盘 → 电脑按键" : "⏸ 已暂停：不再注入任何按键");
        OnStateChanged?.Invoke();
    }

    /// <summary>紧急松开所有按键（可以从任何线程调，含 UI 线程）。</summary>
    public void Panic()
    {
        _sender.ReleaseAll();                    // 先立刻松开，不等工作线程
        Post(() =>
        {
            _keyRefs.Clear();
            _noteCounts.Clear();
            _noteKeys.Clear();
            _sustained.Clear();
            SustainDown = false;
        });
    }

    // ---------------------------------------------------------------- 工作线程

    private void Loop()
    {
        EnterLowLatencyMode();
        try
        {
            while (!_stopping)
            {
                // 先清信号再取队列：这样"清信号"和"取队列"之间到达的事件不会丢，也不会空转
                _signal.Reset();
                DrainQueue();
                RunDueTimers();
                _signal.Wait(ComputeWaitMs());
            }
            DrainQueue();
            RunDueTimers();
            ReleaseEverything();
        }
        finally
        {
            ExitLowLatencyMode();
        }
    }

    private void EnterLowLatencyMode()
    {
        _mmcssHandle = CoreInfo.EnterProAudio();
    }

    private void ExitLowLatencyMode()
    {
        CoreInfo.ExitProAudio(_mmcssHandle);
        _mmcssHandle = IntPtr.Zero;
    }

    private void DrainQueue()
    {
        while (true)
        {
            Action action;
            lock (_gate)
            {
                if (_queue.Count == 0) return;
                action = _queue.Dequeue();
            }
            try { action(); }
            catch (Exception ex) { Log($"[错误] {ex.Message}"); }
        }
    }

    private void RunDueTimers()
    {
        while (true)
        {
            double now = Now;
            int index = -1;
            double best = double.MaxValue;
            for (int i = 0; i < _timers.Count; i++)
            {
                if (_timers[i].Due < best) { best = _timers[i].Due; index = i; }
            }
            if (index < 0 || best > now) return;

            Action action = _timers[index].Action;
            _timers.RemoveAt(index);
            try { action(); }
            catch (Exception ex) { Log($"[错误] {ex.Message}"); }
        }
    }

    private int ComputeWaitMs()
    {
        if (_timers.Count == 0) return Timeout.Infinite;
        double best = double.MaxValue;
        foreach ((double due, Action _) in _timers)
        {
            if (due < best) best = due;
        }
        double ms = (best - Now) * 1000.0;
        if (ms <= 0) return 0;
        return Math.Max(1, (int)Math.Ceiling(Math.Min(ms, 1000.0)));
    }

    private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

    private static double ToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private void Schedule(double due, Action action) => _timers.Add((due, action));

    // ---------------------------------------------------------------- 音符处理

    private void HandleNoteOn(int note, int velocity)
    {
        if (!Enabled)
        {
            Log($"（已暂停）忽略 {NoteName.Scientific(note)}");
            return;
        }
        if (velocity < _config.VelocityThreshold) return;

        int lookupNote = note + _config.Transpose;
        if (!_mapping.TryResolve(lookupNote, _config.Unmapped, out string keyName, out ushort vk, out int mappedNote))
        {
            if (LogNotes) Log($"• 未映射，跳过：{NoteName.Describe(note)}（力度 {velocity}）");
            return;
        }

        NotesPlayed++;
        double now = Now;
        double due = now;

        // 和弦错峰：和上一个音几乎同时到达的音，依次推开一点点
        if (_config.ChordSpreadMs > 0 && now - _lastNoteOnTime < 0.03)
            due = Math.Max(now, _lastPressScheduled + _config.ChordSpreadMs / 1000.0);
        _lastNoteOnTime = now;
        _lastPressScheduled = due;

        _noteCounts.TryGetValue(note, out int existingCount);
        if (existingCount > 0)
        {
            // 同一个音又按了一次（上一个还没松）：必须先弹起来，否则游戏只会"继续按住"，不会出第二声
            string oldKey = _noteKeys[note];
            ForceRelease(oldKey);
            double lastRelease = _lastRelease.TryGetValue(oldKey, out double releasedAt) ? releasedAt : 0;
            due = Math.Max(due, lastRelease + _config.MinRetriggerMs / 1000.0);
        }

        _noteCounts[note] = existingCount + 1;
        _noteKeys[note] = keyName;
        _keyVks[keyName] = vk;
        int generation = BumpGeneration(keyName);

        // 先注入按键，再打日志：日志（可能写控制台/刷界面）绝不能挡在按键前面
        if (due <= now + 0.0005) PressKey(keyName, vk);
        else Schedule(due, () => FireScheduledPress(keyName, vk, note, generation));

        if (LogNotes)
        {
            string folded = mappedNote != note
                ? $"（{NoteName.Scientific(note)} 就近映射到 {NoteName.Scientific(mappedNote)}）"
                : "";
            Log($"♪ {NoteName.Scientific(note)} 力度{velocity} → 按键 {keyName}{folded}");
        }
    }

    /// <summary>延迟按下：如果期间这个音已经松开了，就按一下立刻松开（脉冲），保证能听到一声。</summary>
    private void FireScheduledPress(string keyName, ushort vk, int note, int generation)
    {
        if (_pressGeneration.TryGetValue(keyName, out int current) && current != generation) return;
        if (_sender.IsDown(keyName)) return;

        PressKey(keyName, vk);

        if (!_noteCounts.TryGetValue(note, out int count) || count <= 0)
            Schedule(Now + _config.MinPulseMs / 1000.0, () => ReleaseKey(keyName));
    }

    private void HandleNoteOff(int note)
    {
        if (!_noteCounts.TryGetValue(note, out int count) || count <= 0) return;   // 没有对应的按下，忽略

        count--;
        if (count > 0)
        {
            _noteCounts[note] = count;     // 还有重叠的同音按着，先不松
            return;
        }
        _noteCounts.Remove(note);

        if (!_noteKeys.TryGetValue(note, out string keyName)) return;
        _noteKeys.Remove(note);

        if (_config.SustainEnabled && SustainDown)
        {
            _sustained.Add(keyName);
            if (LogNotes) Log($"（延音）{NoteName.Scientific(note)} → 保留按键 {keyName}");
            return;
        }

        BumpGeneration(keyName);           // 撤销可能还在队列里的"重新按下"
        ReleaseKey(keyName);
    }

    private void HandleControlChange(int cc, int value)
    {
        if (cc == _config.ToggleCc && cc >= 0)
        {
            SetEnabled(!Enabled);
            return;
        }

        if (_config.SustainEnabled && cc == _config.SustainCc)
        {
            bool down = value >= 64;
            if (down == SustainDown) return;
            SustainDown = down;
            Log(down ? "🎹 延音踩下" : "🎹 延音松开");
            if (!down) FlushSustain();
            OnStateChanged?.Invoke();
        }
    }

    // ---------------------------------------------------------------- 按键状态机

    private void PressKey(string keyName, ushort vk)
    {
        _keyRefs.TryGetValue(keyName, out int refs);
        _keyRefs[keyName] = refs + 1;
        _keyVks[keyName] = vk;
        if (refs != 0) return;

        long started = Stopwatch.GetTimestamp();
        _sender.Down(keyName, vk);
        SendStats.Add(ToMilliseconds(Stopwatch.GetTimestamp() - started));
    }

    private void ReleaseKey(string keyName)
    {
        if (!_keyRefs.TryGetValue(keyName, out int refs) || refs <= 0) return;
        refs--;
        if (refs > 0)
        {
            _keyRefs[keyName] = refs;
            return;
        }
        _keyRefs.Remove(keyName);

        long started = Stopwatch.GetTimestamp();
        _sender.Up(keyName);
        SendStats.Add(ToMilliseconds(Stopwatch.GetTimestamp() - started));
        _lastRelease[keyName] = Now;
    }

    private void ForceRelease(string keyName)
    {
        if (!_keyRefs.TryGetValue(keyName, out int refs) || refs <= 0) return;
        _keyRefs[keyName] = 0;
        _sender.Up(keyName);
        _lastRelease[keyName] = Now;
        BumpGeneration(keyName);
    }

    private int BumpGeneration(string keyName)
    {
        _pressGeneration.TryGetValue(keyName, out int generation);
        generation++;
        _pressGeneration[keyName] = generation;
        return generation;
    }

    private void FlushSustain()
    {
        foreach (string key in _sustained.ToList()) ReleaseKey(key);
        _sustained.Clear();
    }

    private void ReleaseEverything()
    {
        foreach (string key in _keyRefs.Keys.ToList()) _sender.Up(key);
        _keyRefs.Clear();
        _noteCounts.Clear();
        _noteKeys.Clear();
        FlushSustain();
        _sender.ReleaseAll();     // 兜底：任何残留都松开
    }

    private void Log(string message) => OnLog?.Invoke(message);

    public void Dispose()
    {
        _stopping = true;
        _signal.Set();
        try { _worker.Join(1500); } catch { /* 忽略 */ }
        _sender.ReleaseAll();
        _signal.Dispose();
        CoreInfo.RestoreTimerResolution(1);
    }
}
