using System.Diagnostics;

namespace Flirty.Tests.Persistence;

/// <summary>
/// Einmalige, gecachte Prüfung, ob ein Docker-Daemon erreichbar ist. Die Provider-Migrationstests
/// gegen PostgreSQL und SQL Server benötigen Docker (Testcontainers); ohne Docker werden sie
/// übersprungen. Auf CI (ubuntu-latest) ist Docker vorhanden, sodass die Tests dort laufen.
/// </summary>
internal static class DockerAvailability
{
    private static readonly Lazy<bool> LazyAvailable = new(Probe);

    /// <summary>Gibt an, ob ein Docker-Daemon erreichbar ist (Ergebnis wird gecacht).</summary>
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
                    // Prozess bereits beendet – ignorieren.
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // docker-CLI nicht im PATH oder Prozessstart fehlgeschlagen -> Docker gilt als nicht verfügbar.
            return false;
        }
    }
}
