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
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);

    private readonly AppOptions options;
    private readonly AppController controller;
    private MainWindow? window;
    private TaskbarIcon? tray;
    private DispatcherQueueTimer? timer;

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
        FirstRunSetup();

        controller.Changed += () => window.DispatcherQueue.TryEnqueue(UpdateTray);
        controller.AlertRaised += alert => window.DispatcherQueue.TryEnqueue(() => tray?.ShowNotification(alert.Title, alert.Body));
        ListenForShowSignal();

        if (!options.StartHidden) window.ShowAndFocus();

        timer = window.DispatcherQueue.CreateTimer();
        timer.Interval = CheckEvery;
        timer.Tick += async (_, _) => await controller.CheckAsync();
        timer.Start();
        _ = controller.CheckAsync();
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

    // The first time PatchLight runs it starts with Windows, so the tray light is always there.
    private void FirstRunSetup()
    {
        var marker = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PatchLight",
            options.Demo ? "demo-first-run-done" : "first-run-done");
        if (File.Exists(marker)) return;
        try
        {
            if (!options.Demo) controller.StartWithWindows = true;
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
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
