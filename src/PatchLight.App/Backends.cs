using PatchLight.Core;
using PatchLight.Windows;

namespace PatchLight.App;

public static class Backends
{
    public static Backend Real()
    {
        var exe = Environment.ProcessPath ?? throw new PatchLightException("Couldn't find PatchLight's own program file.");
        var autostart = Autostart.ForCurrentUser();
        return new Backend
        {
            Service = PatchLightHost.CreateService(Program.RequireWindows11()),
            Actions = new RunAsAdministrator(),
            GetDailyCheckAsync = () => Task.Run(() => DailyTask.RegisteredExePath() switch
            {
                null => DailyCheckState.Off,
                var path when string.Equals(path, exe, StringComparison.OrdinalIgnoreCase) => DailyCheckState.On,
                _ => DailyCheckState.Moved,
            }),
            GetStartWithWindows = () => autostart.IsEnabled(exe),
            SetStartWithWindows = on =>
            {
                if (on) autostart.Enable(exe);
                else autostart.Disable();
            },
            BackgroundLogPath = PatchLightHost.BackgroundLogPath(SystemInfo.DataDirectory),
        };
    }

    public static Backend Demo()
    {
        var (service, actions) = DemoData.Create();
        var startWithWindows = true;
        return new Backend
        {
            Service = service,
            Actions = actions,
            GetDailyCheckAsync = () => Task.FromResult(actions.DailyCheckOn ? DailyCheckState.On : DailyCheckState.Off),
            GetStartWithWindows = () => startWithWindows,
            SetStartWithWindows = on => startWithWindows = on,
            IsDemo = true,
        };
    }
}
