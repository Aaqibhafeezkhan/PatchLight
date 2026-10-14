using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using PatchLight.Core;
using PatchLight.Windows;
using Windows.Graphics;
using Windows.UI;

namespace PatchLight.App;

public sealed partial class MainWindow : Window
{
    private static readonly Color RedColor = Color.FromArgb(255, 239, 68, 68);
    private static readonly Color YellowColor = Color.FromArgb(255, 250, 204, 21);
    private static readonly Color GreenColor = Color.FromArgb(255, 34, 197, 94);
    private static readonly Color OffColor = Color.FromArgb(255, 55, 65, 81);

    private readonly AppController controller;
    private bool updatingSettings;

    public bool AllowClose { get; set; }

    public MainWindow(AppController controller)
    {
        this.controller = controller;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1040 * scale), (int)(720 * scale)));

        // Closing keeps PatchLight running in the tray; "Exit" in the tray menu really quits.
        AppWindow.Closing += (sender, args) =>
        {
            if (AllowClose) return;
            args.Cancel = true;
            sender.Hide();
        };

        controller.Changed += () => DispatcherQueue.TryEnqueue(Refresh);
        Refresh();
    }

    public void ShowAndFocus()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        Activate();
    }

    private void Refresh()
    {
        if (controller.IsDemo) TitleText.Text = "PatchLight — demo (made-up updates)";

        var light = controller.Light;
        RedLamp.Fill = Brush(light == Light.Red ? RedColor : OffColor);
        YellowLamp.Fill = Brush(light == Light.Yellow ? YellowColor : OffColor);
        GreenLamp.Fill = Brush(light == Light.Green ? GreenColor : OffColor);

        HeadlineText.Text = controller.Headline;
        DetailText.Text = controller.Detail;
        CheckedText.Text = controller.CheckedText;
        Busy.IsActive = controller.IsChecking;
        CheckButton.IsEnabled = !controller.IsChecking;

        if (Pages.SelectedItem == UpdatesPage) ShowUpdates();
    }

    private async void OnCheckClick(object sender, RoutedEventArgs e) => await controller.CheckAsync();

    private void OnPageChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        Message.IsOpen = false;
        if (sender.SelectedItem == UpdatesPage) ShowUpdates();
        else if (sender.SelectedItem == HistoryPage) _ = ShowHistoryAsync();
        else _ = ShowSettingsAsync();
    }

    // ---- Updates ----

    private void ShowUpdates()
    {
        PageContent.Children.Clear();
        if (controller.Result is null)
        {
            if (controller.Error is not null) PageContent.Children.Add(Note(controller.Error));
            return;
        }
        if (controller.Cards.Count == 0)
        {
            PageContent.Children.Add(Note("Nothing is waiting to install. You're up to date."));
            return;
        }
        foreach (var card in controller.Cards) PageContent.Children.Add(UpdateCardView(card));
    }

    private UIElement UpdateCardView(UpdateCard card)
    {
        var body = new StackPanel { Spacing = 6 };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 12, Height = 12, Fill = Brush(LightColor(card.Light)), VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new TextBlock
        {
            Text = card.Badge, FontWeight = FontWeights.SemiBold, Foreground = Brush(LightColor(card.Light)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (card.PausedText is not null)
            header.Children.Add(new TextBlock
            {
                Text = "· " + card.PausedText, Foreground = Secondary(), VerticalAlignment = VerticalAlignment.Center,
            });
        body.Children.Add(header);

        body.Children.Add(new TextBlock { Text = card.Title, Style = Style("BodyStrongTextBlockStyle"), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = card.Reason, TextWrapping = TextWrapping.Wrap, Foreground = Secondary() });

        foreach (var issue in card.Issues)
        {
            var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            line.Inlines.Add(new Run { Text = "• " + issue.Title + " " });
            line.Inlines.Add(new Run { Text = $"({StatusText(issue.Status)}) ", Foreground = Secondary() });
            var link = new Hyperlink { NavigateUri = new Uri(issue.Url) };
            link.Inlines.Add(new Run { Text = "Read Microsoft's note" });
            line.Inlines.Add(link);
            body.Children.Add(line);
        }

        if (card.FixCount > 0)
            body.Children.Add(new TextBlock
            {
                Text = card.FixCount == 1 ? "Fixes 1 known problem." : $"Fixes {card.FixCount} known problems.",
                Foreground = Brush(GreenColor),
            });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        if (card.CanPause)
        {
            var pause = new Button
            {
                Content = $"Pause for {controller.State.PauseDays} days",
                Style = card.Light == Light.Red ? Style("AccentButtonStyle") : null,
            };
            pause.Click += async (_, _) => await RunCardAction(pause, () => controller.PauseAsync(card),
                $"Paused {card.Kb}. The daily check resumes it once Microsoft fixes it, and by " +
                $"{DateTime.Now.AddDays(controller.State.PauseDays):MMM d} at the latest.");
            actions.Children.Add(pause);
        }
        if (card.CanResume)
        {
            var resume = new Button { Content = "Resume" };
            resume.Click += async (_, _) => await RunCardAction(resume, () => controller.ResumeAsync(card),
                $"Resumed {card.Kb}. Windows Update will install it.");
            actions.Children.Add(resume);
        }
        if (actions.Children.Count > 0) body.Children.Add(actions);

        return Card(body);
    }

    private async Task RunCardAction(Button button, Func<Task<string?>> action, string success)
    {
        button.IsEnabled = false;
        var error = await action();
        button.IsEnabled = true;
        Show(error is null ? InfoBarSeverity.Success : InfoBarSeverity.Warning, error ?? success);
    }

    // ---- History ----

    private async Task ShowHistoryAsync()
    {
        PageContent.Children.Clear();
        PageContent.Children.Add(Note("Reading update history..."));
        var (items, error) = await controller.HistoryAsync();
        if (Pages.SelectedItem != HistoryPage) return;

        PageContent.Children.Clear();
        PageContent.Children.Add(new TextBlock
        {
            Text = "Updates installed in the last 60 days. If something broke, the likely cause is marked.",
            TextWrapping = TextWrapping.Wrap, Foreground = Secondary(),
        });
        if (error is not null)
        {
            PageContent.Children.Add(Note(error));
            return;
        }
        if (items.Count == 0) PageContent.Children.Add(Note("No Windows updates were installed in this period."));

        foreach (var item in items)
        {
            var body = new StackPanel { Spacing = 4 };
            body.Children.Add(new TextBlock
            {
                Text = item.Update.InstalledOn.ToLocalTime().ToString("MMM d, yyyy"), Foreground = Secondary(),
                Style = Style("CaptionTextBlockStyle"),
            });
            body.Children.Add(new TextBlock { Text = item.Update.Title, TextWrapping = TextWrapping.Wrap });
            if (item.Issues.Count == 0)
            {
                body.Children.Add(new TextBlock { Text = "No known problems.", Foreground = Brush(GreenColor) });
            }
            else
            {
                body.Children.Add(new TextBlock { Text = "Likely cause of:", Foreground = Brush(RedColor), FontWeight = FontWeights.SemiBold });
                foreach (var issue in item.Issues)
                {
                    var line = new TextBlock { TextWrapping = TextWrapping.Wrap };
                    line.Inlines.Add(new Run { Text = "• " + issue.Title + " " });
                    var link = new Hyperlink { NavigateUri = new Uri(issue.Url) };
                    link.Inlines.Add(new Run { Text = "Read Microsoft's note" });
                    line.Inlines.Add(link);
                    body.Children.Add(line);
                }
            }
            PageContent.Children.Add(Card(body));
        }
    }

    // ---- Settings ----

    private async Task ShowSettingsAsync()
    {
        PageContent.Children.Clear();
        updatingSettings = true;

        var startWithWindows = new ToggleSwitch { Header = "Start with Windows", IsOn = controller.StartWithWindows };
        startWithWindows.Toggled += (_, _) =>
        {
            if (!updatingSettings) controller.StartWithWindows = startWithWindows.IsOn;
        };
        PageContent.Children.Add(Card(Section(startWithWindows,
            "PatchLight waits quietly in the tray, checks every few hours and tells you when an update has known problems.")));

        var daily = new ToggleSwitch { Header = "Daily automatic check" };
        var dailyText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Secondary() };
        var dailyPanel = Section(daily,
            "Once a day, even when PatchLight is closed, Windows runs a quick check that resumes paused updates " +
            "Microsoft has fixed and ends pauses that ran out. Pausing an update turns it on. " +
            "Turning it on or off here asks for administrator permission.");
        dailyPanel.Children.Add(dailyText);
        PageContent.Children.Add(Card(dailyPanel));

        var days = new NumberBox
        {
            Header = "Pause updates for (days)", Minimum = 1, Maximum = HoldPolicy.MaxDays, Value = controller.State.PauseDays,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, Width = 220, HorizontalAlignment = HorizontalAlignment.Left,
        };
        days.ValueChanged += (_, args) =>
        {
            if (!double.IsNaN(args.NewValue)) controller.SetPauseDays((int)args.NewValue);
        };
        PageContent.Children.Add(Card(Section(days,
            $"How long a pause lasts at most. PatchLight resumes it earlier if Microsoft fixes the problem. Up to {HoldPolicy.MaxDays} days, because security updates matter.")));

        var about = new StackPanel { Spacing = 4 };
        about.Children.Add(new TextBlock { Text = $"PatchLight {AppVersion()}", Style = Style("BodyStrongTextBlockStyle") });
        about.Children.Add(new TextBlock
        {
            Text = "Free and open source. Data comes straight from Microsoft's public Windows release health pages. No account, no server, no tracking.",
            TextWrapping = TextWrapping.Wrap, Foreground = Secondary(),
        });
        about.Children.Add(new HyperlinkButton { Content = "Project page on GitHub", NavigateUri = new Uri(PatchLightHost.ProjectUrl), Padding = new Thickness(0) });
        PageContent.Children.Add(Card(about));

        var state = await controller.GetDailyCheckAsync();
        daily.IsOn = state == DailyCheckState.On;
        dailyText.Text = DailyText(state);
        updatingSettings = false;

        daily.Toggled += async (_, _) =>
        {
            if (updatingSettings) return;
            daily.IsEnabled = false;
            var error = await controller.SetDailyCheckAsync(daily.IsOn);
            var now = await controller.GetDailyCheckAsync();
            updatingSettings = true;
            daily.IsOn = now == DailyCheckState.On;
            updatingSettings = false;
            dailyText.Text = DailyText(now);
            daily.IsEnabled = true;
            if (error is not null) Show(InfoBarSeverity.Warning, error);
        };
    }

    private string DailyText(DailyCheckState state)
    {
        var lastRun = controller.DailyCheckLastRunText();
        return state switch
        {
            DailyCheckState.Moved => "PatchLight was moved to another folder. Turn the daily check off and on again so Windows finds it.",
            DailyCheckState.On => lastRun.Length > 0 ? lastRun : "On. It hasn't run yet.",
            _ => "Off. Paused updates then stay paused until you press Resume.",
        };
    }

    // ---- Helpers ----

    private void Show(InfoBarSeverity severity, string text)
    {
        Message.Severity = severity;
        Message.Message = text;
        Message.IsOpen = true;
    }

    private static StackPanel Section(UIElement control, string help)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(control);
        panel.Children.Add(new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, Foreground = Secondary() });
        return panel;
    }

    private static Border Card(UIElement child) => new()
    {
        Child = child,
        Padding = new Thickness(16),
        CornerRadius = new CornerRadius(8),
        Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
        BorderThickness = new Thickness(1),
    };

    private static Border Note(string text) =>
        Card(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });

    private static Style? Style(string key) => Application.Current.Resources[key] as Style;

    private static Brush Secondary() => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

    private static SolidColorBrush Brush(Color color) => new(color);

    private static Color LightColor(Light light) => light switch
    {
        Light.Green => GreenColor,
        Light.Yellow => Color.FromArgb(255, 202, 138, 4),
        _ => RedColor,
    };

    private static string StatusText(IssueStatus status) => status switch
    {
        IssueStatus.Confirmed => "confirmed",
        IssueStatus.Investigating => "being looked into",
        IssueStatus.Mitigated => "has a workaround",
        IssueStatus.Resolved => "fixed",
        _ => "status unknown",
    };

    private static string AppVersion() =>
        typeof(MainWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
