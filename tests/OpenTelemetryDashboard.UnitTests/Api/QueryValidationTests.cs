using OpenTelemetryDashboard.Api;
using OpenTelemetryDashboard.Api.Endpoints;

namespace OpenTelemetryDashboard.UnitTests.Api;

public sealed class QueryValidationTests
{
    private static readonly QueryApiOptions Options = new()
    {
        DefaultLimit = 100,
        MaxLimit = 1_000,
        MaxWindowHours = 24,
    };

    private static DateTimeOffset T(int hour) => new(2026, 1, 1, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Missing_From_And_To_Produces_Two_Errors()
    {
        var parameters = new LogQueryParameters(null, null, null, null);

        QueryValidation.TryBuildLogQuery(parameters, Options, out _, out var errors).ShouldBeFalse();
        errors.ShouldNotBeNull();
        errors.Keys.ShouldContain("from");
        errors.Keys.ShouldContain("to");
    }

    [Fact]
    public void From_Greater_Or_Equal_To_Is_Rejected()
    {
        var parameters = new LogQueryParameters(T(10), T(10), null, null);

        QueryValidation.TryBuildLogQuery(parameters, Options, out _, out var errors).ShouldBeFalse();
        errors.ShouldNotBeNull();
        errors.Keys.ShouldContain("to");
    }

    [Fact]
    public void Window_Exceeding_Max_Is_Rejected()
    {
        var from = T(0);
        var to = from.AddHours(Options.MaxWindowHours + 1);
        var parameters = new LogQueryParameters(from, to, null, null);

        QueryValidation.TryBuildLogQuery(parameters, Options, out _, out var errors).ShouldBeFalse();
        errors.ShouldNotBeNull();
        errors.Keys.ShouldContain("to");
    }

    [Fact]
    public void Limit_Greater_Than_Max_Is_Rejected()
    {
        var parameters = new LogQueryParameters(T(0), T(1), Options.MaxLimit + 1, null);

        QueryValidation.TryBuildLogQuery(parameters, Options, out _, out var errors).ShouldBeFalse();
        errors.ShouldNotBeNull();
        errors.Keys.ShouldContain("limit");
    }

    [Fact]
    public void Limit_Zero_Is_Rejected()
    {
        var parameters = new LogQueryParameters(T(0), T(1), 0, null);

        QueryValidation.TryBuildLogQuery(parameters, Options, out _, out var errors).ShouldBeFalse();
        errors.ShouldNotBeNull();
        errors.Keys.ShouldContain("limit");
    }

    [Fact]
    public void Default_Limit_Applied_When_Absent()
    {
        var parameters = new LogQueryParameters(T(0), T(1), null, null);

        QueryValidation.TryBuildLogQuery(parameters, Options, out var query, out _).ShouldBeTrue();
        query!.Limit.ShouldBe(Options.DefaultLimit);
    }

    [Fact]
    public void Invalid_Cursor_Is_Rejected()
    {
        var parameters = new LogQueryParameters(T(0), T(1), 10, "!!!not-a-cursor!!!");

        QueryValidation.TryBuildLogQuery(parameters, Options, out _, out var errors).ShouldBeFalse();
        errors.ShouldNotBeNull();
        errors.Keys.ShouldContain("cursor");
    }

    [Fact]
    public void Valid_Cursor_Is_Decoded()
    {
        var cursor = CursorCodec.EncodeLog(123, 456);
        var parameters = new LogQueryParameters(T(0), T(1), 10, cursor);

        QueryValidation.TryBuildLogQuery(parameters, Options, out var query, out _).ShouldBeTrue();
        query!.After.ShouldNotBeNull();
        query.After!.Value.Time.ShouldBe(123L);
        query.After.Value.SecondaryKey.ShouldBe(456L);
    }

    [Fact]
    public void Non_UTC_From_Is_Rejected()
    {
        var from = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.FromHours(2));
        var to = from.AddHours(1);
        var parameters = new LogQueryParameters(from, to, null, null);

        QueryValidation.TryBuildLogQuery(parameters, Options, out _, out var errors).ShouldBeFalse();
        errors.ShouldNotBeNull();
        errors.Keys.ShouldContain("from");
    }

    [Fact]
    public void Non_UTC_To_Is_Rejected()
    {
        var from = T(0);
        var to = new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.FromHours(-5));
        var parameters = new LogQueryParameters(from, to, null, null);

        QueryValidation.TryBuildLogQuery(parameters, Options, out _, out var errors).ShouldBeFalse();
        errors.ShouldNotBeNull();
        errors.Keys.ShouldContain("to");
    }

    [Fact]
    public void Log_Cursor_Rejected_At_Trace_Endpoint()
    {
        var logCursor = CursorCodec.EncodeLog(1, 2);
        var parameters = new TraceQueryParameters(T(0), T(1), 10, logCursor);

        QueryValidation.TryBuildTraceQuery(parameters, Options, out _, out var errors).ShouldBeFalse();
        errors.ShouldNotBeNull();
        errors.Keys.ShouldContain("cursor");
    }

    [Fact]
    public void Trace_Query_Missing_Parameters_Is_Rejected()
    {
        var parameters = new TraceQueryParameters(null, null, null, null);

        QueryValidation.TryBuildTraceQuery(parameters, Options, out _, out var errors).ShouldBeFalse();
        errors.ShouldNotBeNull();
    }

    [Fact]
    public void Trace_Query_Valid_Parameters_Succeed()
    {
        var parameters = new TraceQueryParameters(T(0), T(1), 50, null);

        QueryValidation.TryBuildTraceQuery(parameters, Options, out var query, out _).ShouldBeTrue();
        query!.Limit.ShouldBe(50);
    }

    [Fact]
    public void MetricPoints_Missing_From_And_To_Are_Rejected()
    {
        // The four identity fields are valid; the missing window must surface
        // as an error rather than silently letting the reader scan everything.
        var parameters = new MetricPointsQueryParameters(
            ResourceHash: "abcd",
            ScopeName: "tests",
            InstrumentName: "memory",
            Kind: "Gauge",
            From: null,
            To: null);

        QueryValidation.TryBuildMetricPointsQuery(parameters, Options, out var key, out var window, out var errors).ShouldBeFalse();
        key.ShouldBeNull();
        window.ShouldBeNull();
        errors.ShouldNotBeNull();
        errors.Keys.ShouldContain("from");
        errors.Keys.ShouldContain("to");
    }

    [Fact]
    public void MetricPoints_From_Without_To_Is_Rejected()
    {
        var parameters = new MetricPointsQueryParameters(
            ResourceHash: "abcd",
            ScopeName: "tests",
            InstrumentName: "memory",
            Kind: "Gauge",
            From: T(0),
            To: null);

        QueryValidation.TryBuildMetricPointsQuery(parameters, Options, out _, out _, out var errors).ShouldBeFalse();
        errors.ShouldNotBeNull();
        errors.Keys.ShouldContain("to");
    }

    [Fact]
    public void MetricPoints_Valid_Parameters_Succeed()
    {
        var parameters = new MetricPointsQueryParameters(
            ResourceHash: "abcd",
            ScopeName: "tests",
            InstrumentName: "memory",
            Kind: "Gauge",
            From: T(0),
            To: T(1));

        QueryValidation.TryBuildMetricPointsQuery(parameters, Options, out var key, out var window, out _).ShouldBeTrue();
        key.ShouldNotBeNull();
        window.ShouldNotBeNull();
    }
}
