using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.DynamicDependency;
using SteamBuildIdManager.Core.Services;

namespace SteamBuildIdManager;

internal static class Program
{
    private static Mutex? _singleInstanceMutex;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int command);

    private const int SwRestore = 9;

    [STAThread]
    private static void Main(string[] args)
    {
        // Elevate before taking the single-instance lock so the elevated copy owns it.
        if (ShouldElevate(args))
        {
            if (ElevationService.TryRestartElevated(string.Join(' ', args)))
            {
                return;
            }
        }

        _singleInstanceMutex = new Mutex(true, SingleInstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            var handle = FindWindow(null, AppInfo.WindowTitle);
            if (handle != IntPtr.Zero)
            {
                ShowWindow(handle, SwRestore);
                SetForegroundWindow(handle);
            }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            return;
        }

#if !WINDOWS_APP_SDK_SELF_CONTAINED
        var bootstrapInitialized = false;
#endif
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();

#if !WINDOWS_APP_SDK_SELF_CONTAINED
            var options = Bootstrap.InitializeOptions.OnNoMatch_ShowUI
                | Bootstrap.InitializeOptions.OnPackageIdentity_NOOP;
            if (Bootstrap.TryInitialize(0x00010008, "", new PackageVersion(), options, out var hResult))
            {
                bootstrapInitialized = true;
            }
            else
            {
                WriteCrashLog("Bootstrap.TryInitialize",
                    new InvalidOperationException($"Windows App SDK bootstrap failed: 0x{hResult:X8}"));
            }
#endif
            Application.Start(_ =>
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                new App();
            });
        }
        catch (Exception ex)
        {
            WriteCrashLog("Program.Main", ex);
            throw;
        }
        finally
        {
#if !WINDOWS_APP_SDK_SELF_CONTAINED
            if (bootstrapInitialized) Bootstrap.Shutdown();
#endif
        }
    }

    /// <summary>
    /// The "以管理员身份启动" setting is honoured on every launch. The "{noelevate}" argument
    /// suppresses it so a deliberately de-elevated start stays possible.
    /// </summary>
    private static bool ShouldElevate(string[] args)
    {
        if (args.Any(argument => string.Equals(argument, NoElevateArgument, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (ElevationService.IsElevated()) return false;

        try
        {
            return new SettingsService().Load().RunAsAdministrator;
        }
        catch
        {
            return false;
        }
    }

    private const string NoElevateArgument = "--no-elevate";

    private const string SingleInstanceName = @"Local\SteamBuildIDManager_SingleInstance";

    /// <summary>
    /// Hand the single-instance lock over before starting an elevated copy. Without this the new
    /// elevated process finds the mutex still held, activates the old window and exits, which made
    /// the "restart as administrator" button appear to do nothing.
    /// </summary>
    internal static void ReleaseSingleInstanceMutex()
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch
        {
            // Not the owner, or already released.
        }

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
    }

    /// <summary>Re-take the lock when the elevation prompt was cancelled and this process keeps running.</summary>
    internal static void ReacquireSingleInstanceMutex()
    {
        if (_singleInstanceMutex != null) return;
        _singleInstanceMutex = new Mutex(true, SingleInstanceName, out _);
    }

    internal static void WriteCrashLog(string source, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            var path = Path.Combine(AppPaths.LogDirectory, "startup.log");
            var builder = new StringBuilder()
                .AppendLine("==============================")
                .AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}")
                .AppendLine($"Source: {source}");
            if (exception != null)
            {
                builder.AppendLine($"Exception: {exception.GetType().FullName}")
                    .AppendLine($"Message: {exception.Message}")
                    .AppendLine(exception.StackTrace ?? "<no stack>");
                if (exception.InnerException != null)
                    builder.AppendLine(exception.InnerException.ToString());
            }
            File.AppendAllText(path, builder.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Logging must never mask the original failure.
        }
    }
}