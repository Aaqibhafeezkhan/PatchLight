using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using PatchLight.Core;
using PatchLight.Windows;

namespace PatchLight.App;

public static class Program
{
    private const string InstanceName = "PatchLight.App.Instance";
    private const string ShowSignalName = "PatchLight.App.Show";

    [STAThread]
    public static int Main(string[] args)
    {
        // Headless modes: the daily task (as SYSTEM) and the "Run as administrator" helper.
        if (args.Contains(DailyTask.Argument)) return RunBackgroundCheck();
        if (ElevatedCommand.Parse(args) is { } command) return RunElevated(command);

        var demo = args.Contains("--demo");
        using var instance = new Mutex(initiallyOwned: true, demo ? InstanceName + ".Demo" : InstanceName, out var isFirst);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, demo ? ShowSignalName + ".Demo" : ShowSignalName);
        if (!isFirst)
        {
            // Already running (probably in the tray): bring that window forward instead.
            showSignal.Set();
            return 0;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(callback =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(new AppOptions(StartHidden: args.Contains("--tray"), Demo: demo, ShowSignal: showSignal));
        });
        return 0;
    }

    private static int RunBackgroundCheck()
    {
        var logPath = PatchLightHost.BackgroundLogPath(SystemInfo.DataDirectory);
        try
        {
            DataFolder.Secure(SystemInfo.DataDirectory);
            var service = PatchLightHost.CreateService(RequireWindows11());
            BackgroundCheck.RunAsync(service, logPath, TimeProvider.System, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // Couldn't even start the check; still leave a note for the app.
            var note = new BackgroundCheckLog(DateTimeOffset.UtcNow, [],
                ex is PatchLightException ? ex.Message : $"Something went wrong: {ex.GetType().Name}.");
            try
            {
                Directory.CreateDirectory(SystemInfo.DataDirectory);
                File.WriteAllText(logPath, System.Text.Json.JsonSerializer.Serialize(note));
            }
            catch (Exception)
            {
            }
        }
        return 0;
    }

    private static int RunElevated(ElevatedCommand command)
    {
        IElevatedActions actions;
        try
        {
            DataFolder.Secure(SystemInfo.DataDirectory);
            actions = new ElevatedActions(PatchLightHost.CreateService(RequireWindows11()),
                Environment.ProcessPath ?? throw new PatchLightException("Couldn't find PatchLight's own program file."));
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(command.ResultPath, ex is PatchLightException ? ex.Message : "Something went wrong.");
            }
            catch (Exception)
            {
            }
            return 2;
        }
        return command.RunAsync(actions, CancellationToken.None).GetAwaiter().GetResult();
    }

    public static string RequireWindows11()
    {
        var windows = SystemInfo.Read();
        if (!windows.IsWindows11 || windows.DisplayVersion is null)
            throw new PatchLightException("PatchLight works on Windows 11 only for now.");
        return windows.DisplayVersion;
    }
}

public sealed record AppOptions(bool StartHidden, bool Demo, EventWaitHandle ShowSignal);
