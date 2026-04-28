using System.Runtime.CompilerServices;

namespace OpenTelemetryDashboard.IntegrationTests.Fixtures;

internal static class TestcontainersModuleInitializer
{
    /// <summary>
    /// Disabilita il Ryuk resource-reaper di Testcontainers per supportare Docker
    /// in modalità rootless (path <c>/var/run/docker.sock</c> non esistente).
    /// Cleanup dei container è responsabilità di <see cref="IAsyncLifetime.DisposeAsync"/>.
    /// L'override può essere disattivato impostando esplicitamente la variabile
    /// d'ambiente <c>TESTCONTAINERS_RYUK_DISABLED</c> prima di lanciare i test.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED") is null)
        {
            Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
        }
    }
}
