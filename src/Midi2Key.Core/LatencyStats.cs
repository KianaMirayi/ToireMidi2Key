namespace Midi2Key;

/// <summary>
/// 毫秒级延迟采样器（滚动窗口 + 百分位）。
/// 线程安全：MIDI 回调线程、引擎线程、UI 线程都可能往里写。
/// </summary>
public sealed class LatencyStats
{
    private readonly object _gate = new();
    private readonly Queue<double> _samples = new();
    private double _sum;

    /// <summary>滚动窗口大小，超过就丢最旧的。</summary>
    public int Capacity { get; set; } = 4000;

    public void Add(double milliseconds)
    {
        if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds)) return;
        lock (_gate)
        {
            _samples.Enqueue(milliseconds);
            _sum += milliseconds;
            while (_samples.Count > Capacity) _sum -= _samples.Dequeue();
        }
    }

    public int Count
    {
        get { lock (_gate) return _samples.Count; }
    }

    public double Average
    {
        get { lock (_gate) return _samples.Count == 0 ? 0 : _sum / _samples.Count; }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _samples.Clear();
            _sum = 0;
        }
    }

    public LatencySummary Summarize()
    {
        double[] data;
        double average;
        lock (_gate)
        {
            if (_samples.Count == 0) return LatencySummary.Empty;
            data = _samples.ToArray();
            average = _sum / data.Length;
        }

        Array.Sort(data);
        return new LatencySummary
        {
            Count = data.Length,
            Min = data[0],
            Average = average,
            P50 = Percentile(data, 0.50),
            P90 = Percentile(data, 0.90),
            P95 = Percentile(data, 0.95),
            P99 = Percentile(data, 0.99),
            Max = data[^1]
        };
    }

    private static double Percentile(double[] sorted, double p)
    {
        int index = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}

public readonly struct LatencySummary
{
    public static readonly LatencySummary Empty = new();

    public int Count { get; init; }
    public double Min { get; init; }
    public double Average { get; init; }
    public double P50 { get; init; }
    public double P90 { get; init; }
    public double P95 { get; init; }
    public double P99 { get; init; }
    public double Max { get; init; }

    public override string ToString() => Count == 0
        ? "无样本"
        : $"n={Count,-5} 平均 {Average,6:F2}ms  p50 {P50,6:F2}  p90 {P90,6:F2}  p95 {P95,6:F2}  p99 {P99,6:F2}  最大 {Max,7:F2}";
}
