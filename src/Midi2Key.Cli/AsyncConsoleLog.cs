using System.Collections.Concurrent;

namespace Midi2Key.Cli;

/// <summary>
/// 异步控制台日志。
///
/// 为什么需要它：写控制台（尤其是 Windows 终端，中文还会走 WriteConsoleW + 滚动重绘）
/// 单次可能花 1~20ms。引擎线程一旦同步写日志，就会卡住后面所有音符的按键注入——
/// 这就是"日志一开就感觉有延迟"的原因。所以日志全部丢给独立线程，热路径只入队。
/// </summary>
internal sealed class AsyncConsoleLog : IDisposable
{
    private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>(), 8192);
    private readonly Thread _thread;

    public AsyncConsoleLog()
    {
        _thread = new Thread(Pump)
        {
            IsBackground = true,
            Name = "console-log",
            Priority = ThreadPriority.BelowNormal
        };
        _thread.Start();
    }

    public void Write(string line)
    {
        // 队列满就直接丢：宁可在终端里少几行日志，也绝不让日志拖慢按键注入
        _queue.TryAdd(line);
    }

    private void Pump()
    {
        foreach (string line in _queue.GetConsumingEnumerable())
        {
            try { Console.WriteLine(line); }
            catch { /* 终端被关掉之类的，忽略 */ }
        }
    }

    public void Dispose()
    {
        try { _queue.CompleteAdding(); } catch { /* 忽略 */ }
        try { _thread.Join(800); } catch { /* 忽略 */ }
        _queue.Dispose();
    }
}
