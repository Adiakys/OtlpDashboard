using System.Reflection;
using OpenTelemetryDashboard.Persistence;

namespace OpenTelemetryDashboard.UnitTests.Persistence;

public class BoundedRetryTests
{
    // BoundedRetry is internal — reach it via reflection rather than
    // widening its visibility just for tests.
    private static readonly MethodInfo ExecuteAsync = typeof(TelemetryDbContext)
        .Assembly
        .GetType("OpenTelemetryDashboard.Persistence.Ingestion.BoundedRetry", throwOnError: true)!
        .GetMethod("ExecuteAsync", BindingFlags.Public | BindingFlags.Static)!;

    private static readonly TimeSpan[] FastDelays =
    [
        TimeSpan.FromMilliseconds(1),
        TimeSpan.FromMilliseconds(1),
        TimeSpan.FromMilliseconds(1),
    ];

    private static Task Invoke(Func<CancellationToken, Task> action, CancellationToken ct, TimeSpan[]? delays = null)
        => (Task)ExecuteAsync.Invoke(null, [action, ct, delays ?? FastDelays])!;

    [Fact]
    public async Task Returns_on_first_success_without_retry()
    {
        var attempts = 0;
        await Invoke(_ =>
        {
            attempts++;
            return Task.CompletedTask;
        }, CancellationToken.None);

        attempts.ShouldBe(1);
    }

    [Fact]
    public async Task Retries_then_succeeds_on_third_attempt()
    {
        var attempts = 0;
        await Invoke(_ =>
        {
            attempts++;
            if (attempts < 3) throw new InvalidOperationException("transient");
            return Task.CompletedTask;
        }, CancellationToken.None);

        attempts.ShouldBe(3);
    }

    [Fact]
    public async Task Surfaces_last_exception_after_exhausting_attempts()
    {
        var attempts = 0;
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await Invoke(_ =>
            {
                attempts++;
                throw new InvalidOperationException($"fail {attempts}");
            }, CancellationToken.None));

        // 1 initial + 3 delays = 4 attempts before giving up.
        attempts.ShouldBe(4);
        ex.Message.ShouldBe("fail 4");
    }

    [Fact]
    public async Task Cancellation_propagates_immediately_without_retry()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var attempts = 0;
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await Invoke(ct =>
            {
                attempts++;
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }, cts.Token));

        attempts.ShouldBe(1);
    }
}
