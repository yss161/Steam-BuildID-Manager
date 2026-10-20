using System.Diagnostics;
using System.Security.Principal;

namespace SteamBuildIdManager.Core.Services;

/// <summary>Process elevation state plus a self-relaunch helper. Every failure degrades to "not elevated".</summary>
public static class ElevationService
{
    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Start a second copy of this executable with the "runas" verb. The caller must exit right after
    /// a successful start so only one elevated instance remains.
    /// </summary>
    public static bool TryRestartElevated(string? arguments = null, string? workingDirectory = null)
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath)) return false;

            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                Arguments = arguments ?? "",
                WorkingDirectory = workingDirectory ?? AppContext.BaseDirectory,
                UseShellExecute = true,
                Verb = "runas",
            };
            using var process = Process.Start(startInfo);
            return process != null;
        }
        catch
        {
            // The user declined the UAC prompt or the launch failed; keep running unelevated.
            return false;
        }
    }
}
