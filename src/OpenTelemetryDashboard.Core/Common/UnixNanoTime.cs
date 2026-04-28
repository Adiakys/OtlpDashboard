namespace OpenTelemetryDashboard.Core.Common;

/// <summary>
/// Conversions between Unix epoch nanoseconds (the native OTLP representation)
/// and <see cref="DateTimeOffset"/>. Sub-100-ns precision is lost because
/// <see cref="DateTimeOffset"/> is 100-ns granular; adequate for dashboard
/// display and query windows.
/// </summary>
public static class UnixNanoTime
{
    private const long NanosecondsPerTick = 100L;
    private const long EpochTicks = 621_355_968_000_000_000L; // DateTime(1970, 1, 1, 0, 0, 0, Utc).Ticks

    public static DateTimeOffset FromUnixNanoseconds(long unixNanoseconds)
    {
        var ticks = EpochTicks + unixNanoseconds / NanosecondsPerTick;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    public static long ToUnixNanoseconds(DateTimeOffset value)
    {
        var utcTicks = value.UtcTicks;
        return (utcTicks - EpochTicks) * NanosecondsPerTick;
    }
}
