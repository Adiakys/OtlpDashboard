using System.Globalization;

namespace OpenTelemetryDashboard.LoadTest;

/// <summary>
/// Reads RSS and CPU time of the dashboard process from /proc. Works for a
/// local process and for a container's main process seen from the host.
/// </summary>
internal sealed class ProcessSampler(int pid)
{
    private static readonly double TicksPerSecond = 100;

    private double _lastCpuSeconds;
    private DateTime _lastSampleAt;

    public ProcessSample? Sample()
    {
        try
        {
            var status = File.ReadAllLines($"/proc/{pid}/status");
            var rssKb = status.Where(l => l.StartsWith("VmRSS:", StringComparison.Ordinal))
                .Select(l => long.Parse(l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture))
                .FirstOrDefault();

            var stat = File.ReadAllText($"/proc/{pid}/stat");
            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            var cpuSeconds = (double.Parse(fields[11], CultureInfo.InvariantCulture) + double.Parse(fields[12], CultureInfo.InvariantCulture)) / TicksPerSecond;

            var now = DateTime.UtcNow;
            var cpuPercent = _lastSampleAt == default
                ? 0
                : (cpuSeconds - _lastCpuSeconds) / (now - _lastSampleAt).TotalSeconds * 100;
            _lastCpuSeconds = cpuSeconds;
            _lastSampleAt = now;

            return new ProcessSample(Math.Round(rssKb / 1024.0, 1), Math.Round(cpuPercent, 1));
        }
        catch (IOException)
        {
            return null;
        }
    }
}

internal sealed record ProcessSample(double RssMb, double CpuPercent);
