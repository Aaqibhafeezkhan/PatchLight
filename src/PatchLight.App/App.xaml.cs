using System.Windows.Input;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PatchLight.Core;
using PatchLight.Windows;

namespace PatchLight.App;

public partial class App : Application
{
    // Same pace as the automatic check, so what it did reaches the person within the hour.
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1);
    private static readonly TimeSpan AppUpdateCheckEvery = TimeSpan.FromDays(1);

    private readonly AppOptions options;
    private readonly AppController controller;
    private MainWindow? window;
    private TaskbarIcon? tray;
    private DispatcherQueueTimer? timer;
    private AppUpdater? updater;
    private DispatcherQueueTimer? updateTimer;

    public App(AppOptions options)
    {
        this.options = options;
        InitializeComponent();
        var stateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PatchLight");
        controller = new AppController(options.Demo ? Backends.Demo : Backends.Real,
            new AppStateStore(Path.Combine(stateDir, options.Demo ? "demo-state.json" : "app-state.json")));
        UnhandledException += (_, e) =>
        {
            ErrorLog.Write(e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow(controller);
        CreateTray();

        controller.Changed += () => window.DispatcherQueue.TryEnqueue(UpdateTray);
        controller.AlertRaised += alert => window.DispatcherQueue.TryEnqueue(() => tray?.ShowNotification(alert.Title, alert.Body));
        ListenForShowSignal();

        if (!options.StartHidden) window.ShowAndFocus();

        timer = window.DispatcherQueue.CreateTimer();
        timer.Interval = CheckEvery;
        timer.Tick += async (_, _) => await controller.CheckAsync();
        timer.Start();
        _ = controller.CheckAsync();

        if (!options.Demo) StartAppUpdateChecks();
    }

    // ---- PatchLight's own updates ----

    private void StartAppUpdateChecks()
    {
        updater = PatchLightHost.CreateAppUpdater(typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0));
        updater.CleanUp();
        window!.CheckForAppUpdate = CheckForAppUpdateAsync;
        window.InstallAppUpdate = InstallAppUpdateAsync;

        updateTimer = window.DispatcherQueue.CreateTimer();
        updateTimer.Interval = AppUpdateCheckEvery;
        updateTimer.Tick += async (_, _) => await CheckForAppUpdateAsync();
        updateTimer.Start();
        _ = FirstAppUpdateCheckAsync();
    }

    // A little after starting, so it doesn't compete with the first update check.
    private async Task FirstAppUpdateCheckAsync()
    {
        await Task.Delay(TimeSpan.FromMinutes(1));
        await CheckForAppUpdateAsync();
    }

    private async Task<AppUpdateCheck> CheckForAppUpdateAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var check = await updater!.CheckAsync(cts.Token);
        if (check.Newer is { } release)
        {
            window!.ShowAppUpdate(release, CanInstallUpdates());
            if (controller.MarkAppUpdateNotified(release.Version))
                tray?.ShowNotification($"PatchLight {release.Version.ToString(3)} is available", "Open PatchLight to update.");
        }
        return check;
    }

    // Copies from the zip live wherever the person unzipped them; the installer would put a second copy in
    // Program Files, so those get a link to the download page instead.
    private static bool CanInstallUpdates() =>
        Environment.ProcessPath is { } exe && InstallLocation.IsAdminOnly(exe);

    // Returns a message when the update didn't start; otherwise PatchLight closes and the installer takes over.
    private async Task<string?> InstallAppUpdateAsync(AppRelease release)
    {
        FileStream installer;
        try
        {
            installer = await updater!.DownloadAsync(release, CancellationToken.None);
        }
        catch (PatchLightException ex)
        {
            return ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return "Couldn't download the update. Check your internet connection and try again.";
        }

        using (installer)
        {
            if (!AppUpdater.StartInstaller(installer)) return "Update cancelled. PatchLight wasn't changed.";
        }
        options.Instance.Dispose();
        Quit();
        return null;
    }

    private void CreateTray()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Open PatchLight", Command = new Command(() => window!.ShowAndFocus()) });
        menu.Items.Add(new MenuFlyoutItem { Text = "Check now", Command = new Command(() => _ = controller.CheckAsync()) });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "Exit", Command = new Command(Quit) });

        tray = new TaskbarIcon
        {
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            LeftClickCommand = new Command(() => window!.ShowAndFocus()),
            NoLeftClickDelay = true,
        };
        UpdateTray();
        tray.ForceCreate();
    }

    private void UpdateTray()
    {
        if (tray is null) return;
        var name = controller.Light switch
        {
            Light.Green => "green",
            Light.Yellow => "yellow",
            Light.Red => "red",
            _ => "gray",
        };
        tray.IconSource = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", $"tray-{name}.ico")));
        tray.ToolTipText = $"PatchLight: {controller.Headline}";
    }

    // A second launch of PatchLight signals this one to show its window.
    private void ListenForShowSignal()
    {
        var queue = window!.DispatcherQueue;
        var thread = new Thread(() =>
        {
            while (options.ShowSignal.WaitOne())
                queue.TryEnqueue(() => window.ShowAndFocus());
        })
        { IsBackground = true, Name = "PatchLight show signal" };
        thread.Start();
    }

    private void Quit()
    {
        timer?.Stop();
        updateTimer?.Stop();
        tray?.Dispose();
        if (window is not null)
        {
            window.AllowClose = true;
            window.Close();
        }
        Current.Exit();
    }

    private sealed class Command(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}
