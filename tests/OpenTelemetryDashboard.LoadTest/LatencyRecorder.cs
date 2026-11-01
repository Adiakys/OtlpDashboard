namespace OpenTelemetryDashboard.LoadTest;

internal sealed class LatencyRecorder
{
    private readonly List<double> _samples = [];
    private readonly Lock _lock = new();

    public void Record(double milliseconds)
    {
        lock (_lock)
        {
            _samples.Add(milliseconds);
        }
    }

    public LatencySummary Summarize()
    {
        double[] sorted;
        lock (_lock)
        {
            sorted = [.. _samples];
        }
        Array.Sort(sorted);
        return new LatencySummary(
            Count: sorted.Length,
            P50: Percentile(sorted, 0.50),
            P95: Percentile(sorted, 0.95),
            P99: Percentile(sorted, 0.99),
            Max: sorted.Length == 0 ? 0 : sorted[^1]);
    }

    private static double Percentile(double[] sorted, double q) =>
        sorted.Length == 0 ? 0 : Math.Round(sorted[(int)Math.Min(sorted.Length - 1, Math.Ceiling(q * sorted.Length) - 1)], 2);
}

internal sealed record LatencySummary(int Count, double P50, double P95, double P99, double Max);
