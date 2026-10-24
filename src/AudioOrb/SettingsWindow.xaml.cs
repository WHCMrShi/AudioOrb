using System.Runtime.InteropServices;
using AudioOrb.Interop;
using AudioOrb.Pages;
using AudioOrb.Services;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;

namespace AudioOrb;

public sealed partial class SettingsWindow : Window
{
    private readonly AppHost _host;
    private readonly ThemedMicaBackdrop _backdrop = new();
    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private bool _allowClose;
    private bool _closed;
    private bool _navigationReady;
    private string? _appliedTheme;

    public SettingsWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        Title = "声枢 - 设置";

        _hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);

        SystemBackdrop = _backdrop;
        ApplyTheme();

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico");
        if (File.Exists(iconPath))
        {
            _appWindow.SetIcon(iconPath);
        }

        ResizeAndCenter();
        ContentFrame.SizeChanged += (_, _) => StretchPageContent();
        Activated += SettingsWindow_Activated;
        _host.SettingsChanged += Host_SettingsChanged;
        Closed += SettingsWindow_Closed;
    }

    public event EventHandler? Hidden;

    public void ShowAndActivate()
    {
        if (_allowClose)
        {
            return;
        }

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            if (presenter.State == OverlappedPresenterState.Minimized)
            {
                presenter.Restore();
            }

            // Stay above a potentially topmost game while the user is actively
            // editing settings. This is relaxed again when the window loses focus.
            presenter.IsAlwaysOnTop = true;
        }

        Activate();
        WindowEffects.ForceForeground(_hwnd);
    }

    private void SettingsWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_allowClose || _closed)
        {
            return;
        }

        try
        {
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = args.WindowActivationState != WindowActivationState.Deactivated;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or COMException or ObjectDisposedException)
        {
            // The window is in the process of closing.
        }
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    public void ApplyTheme()
    {
        if (_allowClose || _closed)
        {
            return;
        }

        try
        {
            ApplyThemeCore();
        }
        catch (Exception ex) when (ex is ArgumentException or COMException or ObjectDisposedException)
        {
            // The window can be torn down while a queued theme update is in flight.
        }
    }

    private void ApplyThemeCore()
    {
        var themeSetting = _host.Settings.Theme;
        var theme = ThemeService.ToElementTheme(themeSetting);
        var isDark = ThemeService.IsDark(themeSetting);

        RootLayout.RequestedTheme = theme;
        _backdrop.SetTheme(theme);
        WindowEffects.SetDarkMode(_hwnd, isDark);
        ThemeService.ApplyTitleBarTheme(_appWindow, isDark);

        var systemDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        var overridesSystem = themeSetting != "System" && isDark != systemDark;
        // A light tint over Mica keeps text readable when a bright app or
        // window sits directly behind the settings window. With no Mica
        // support the tint becomes fully opaque.
        ThemeScrim.Opacity = overridesSystem || !MicaController.IsSupported() ? 1 : 0.20;

        _appliedTheme = $"{themeSetting}:{isDark}";
    }

    private void ResizeAndCenter()
    {
        var dpi = NativeMethods.GetDpiForWindow(_hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var display = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        var width = Math.Min((int)Math.Round(1060 * dpi / 96.0), Math.Max(640, work.Width - 48));
        var height = Math.Min((int)Math.Round(720 * dpi / 96.0), Math.Max(480, work.Height - 48));
        _appWindow.Resize(new SizeInt32(width, height));

        var x = work.X + Math.Max(0, (work.Width - width) / 2);
        var y = work.Y + Math.Max(0, (work.Height - height) / 2);
        _appWindow.Move(new PointInt32(x, y));
    }

    private void RootLayout_Loaded(object sender, RoutedEventArgs e)
    {
        if (_navigationReady)
        {
            return;
        }

        _navigationReady = true;
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        var pageType = tag switch
        {
            "devices" => typeof(DevicesPage),
            "appearance" => typeof(AppearancePage),
            "general" => typeof(GeneralPage),
            "about" => typeof(AboutPage),
            _ => typeof(DevicesPage),
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType, _host);
            DispatcherQueue.TryEnqueue(StretchPageContent);

            // Card content is realized after the first layout pass; run the
            // right-axis adjustment once more when the page has settled.
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(180);
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                StretchPageContent();
            };
            timer.Start();
        }
    }

    private void StretchPageContent()
    {
        if (ContentFrame.ActualWidth <= 0 ||
            ContentFrame.Content is not Page page ||
            page.Content is not FrameworkElement root)
        {
            return;
        }

        // A StackPanel inside a ScrollViewer measures children with infinite
        // width, so SettingsCard would size to its own content instead of the
        // Windows Settings right axis. Give the page content the frame width.
        if (root is ScrollViewer scroll && scroll.Content is FrameworkElement content)
        {
            content.Width = ContentFrame.ActualWidth;
        }
        else
        {
            root.Width = ContentFrame.ActualWidth;
        }

        var cardWidth = Math.Max(0, ContentFrame.ActualWidth - 84);
        if (cardWidth > 0)
        {
            foreach (var card in FindDescendants<Controls.SettingsCard>(root))
            {
                card.Width = cardWidth;
                card.HorizontalAlignment = HorizontalAlignment.Stretch;


            }
        }
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private void Host_SettingsChanged(object? sender, EventArgs e)
    {
        var key = $"{_host.Settings.Theme}:{ThemeService.IsDark(_host.Settings.Theme)}";
        if (string.Equals(key, _appliedTheme, StringComparison.Ordinal))
        {
            return;
        }

        // Let the ComboBox popup finish its selection before swapping the
        // element theme; changing it synchronously can strand the popup open.
        DispatcherQueue.TryEnqueue(ApplyTheme);
    }

    private void SettingsWindow_Closed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _host.SettingsChanged -= Host_SettingsChanged;
        Hidden?.Invoke(this, EventArgs.Empty);
    }
}
