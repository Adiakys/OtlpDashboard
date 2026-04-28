using System.Diagnostics;

namespace OpenTelemetryDashboard.IntegrationTests.Fixtures;

internal static class DockerAvailability
{
    public static bool IsDockerAvailable { get; } = ProbeDocker();

    private static bool ProbeDocker()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null) return false;
            process.WaitForExit(2000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
