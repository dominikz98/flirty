using System.Diagnostics;

namespace Flirty.Tests.Persistence;

/// <summary>
/// One-off, cached check whether a Docker daemon is reachable. The provider migration tests against
/// PostgreSQL and SQL Server need Docker (Testcontainers); without Docker they skip themselves. On CI
/// (ubuntu-latest) Docker is present, so the tests run there.
/// </summary>
internal static class DockerAvailability
{
    private static readonly Lazy<bool> LazyAvailable = new(Probe);

    /// <summary>Whether a Docker daemon is reachable (the result is cached).</summary>
    public static bool IsAvailable => LazyAvailable.Value;

    private static bool Probe()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(10_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Process already exited – ignore.
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // docker CLI not on PATH or the process failed to start -> Docker counts as unavailable.
            return false;
        }
    }
}
