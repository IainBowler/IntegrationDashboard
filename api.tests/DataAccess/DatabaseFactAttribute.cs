using System.Diagnostics;

namespace Api.Tests.DataAccess;

/// <summary>
/// A [Fact] that runs only when Docker is available. CI (ubuntu runners) has
/// Docker, so these always run there; on a dev machine without Docker the
/// tests skip instead of failing, keeping a plain `dotnet test` green.
/// </summary>
public sealed class DatabaseFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> DockerAvailable = new(DetectDocker);

    public DatabaseFactAttribute()
    {
        if (!DockerAvailable.Value)
        {
            Skip = "Docker is not available on this machine";
        }
    }

    private static bool DetectDocker()
    {
        try
        {
            var startInfo = new ProcessStartInfo("docker", "info --format {{.ServerVersion}}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }
            if (!process.WaitForExit(10_000))
            {
                try { process.Kill(); } catch { /* already exited */ }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
