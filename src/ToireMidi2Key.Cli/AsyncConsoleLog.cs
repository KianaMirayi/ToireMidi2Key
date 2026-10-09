using System.Collections.Concurrent;

namespace ToireMidi2Key.Cli;

/// <summary>
/// 异步控制台日志：写终端单次可能花 1~20ms，同步写会卡住引擎线程的按键注入，
/// 所以日志交给独立线程，热路径只入队（队列满就丢）。
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
