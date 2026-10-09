using Midi2Key;

namespace Midi2Key.App.Services;

/// <summary>
/// ViewModel 只依赖这个接口，永远不碰 P/Invoke。
/// 所有事件都可能从非 UI 线程触发，订阅方负责切回 UI 线程。
/// </summary>
public interface IBridgeService : IDisposable
{
    bool IsRunning { get; }

    /// <summary>引擎暂停状态：仍然监听 MIDI（学习模式可用），但不注入按键。</summary>
    bool IsPaused { get; }

    IReadOnlyList<MidiDeviceInfo> Devices { get; }

    Mapping Mapping { get; }

    Midi2KeyConfig Config { get; }

    string ConfigPath { get; }

    /// <summary>入队 → 工作线程拾起（线程唤醒延迟）。</summary>
    LatencySummary QueueLatency { get; }

    /// <summary>单次 SendInput 注入耗时。</summary>
    LatencySummary InjectionLatency { get; }

    /// <summary>入队 → 注入完成（本程序内部的总延迟）。</summary>
    LatencySummary EngineLatency { get; }

    void ResetLatency();

    event Action<string> Log;
    event Action StateChanged;
    event Action<int, int> NoteReceived;      // note, velocity
    event Action<int, int> ControlReceived;   // cc, value

    void RefreshDevices();
    void LoadConfig();
    void ApplyConfig(Midi2KeyConfig config);
    void SaveConfig();

    void Start(int deviceIndex);
    void Stop();
    void TogglePause();
    void Panic();
}
