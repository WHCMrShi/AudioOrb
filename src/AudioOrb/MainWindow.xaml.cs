using System.Runtime.InteropServices;
using AudioOrb.Interop;
using AudioOrb.Services;
using AudioSourceSelect.Core.Audio;
using AudioSourceSelect.Core.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace AudioOrb;

/// <summary>
/// The floating acrylic card. It intentionally looks like a small Start-menu
/// surface rather than a glossy orb: neutral tint, rounded corners, no accent
/// gradient, device name always visible.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int WidgetHeightDip = 56;
    private static readonly UIntPtr SubclassId = new(0xA0B1);

    private readonly AppHost _host;
    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly FullscreenWatcher _fullscreenWatcher;
    private readonly NativeMethods.SubclassProc _subclassProc;
    private readonly DispatcherQueueTimer _dragWatch;
    private readonly DispatcherQueueTimer _hoverWatch;
    private readonly DispatcherQueueTimer _volumeWatch;

    private CancellationTokenSource? _transientMessageCts;
    private DateTime _messageUntil;

    private uint _pressedPointerId;
    private NativeMethods.Point _dragStartCursor;
    private PointInt32 _dragStartWindow;
    private bool _pressed;
    private bool _dragging;
    private bool _suppressClick;
    private bool _isSwitching;
    private bool _hiddenForFullscreen;
    private bool _closed;
    private bool _volumeViewVisible;
    private bool _actionViewVisible;
    private int _actionHover = -1;
    private int _contentSwitchVersion;
    private NativeMethods.WinEventDelegate? _foregroundCallback;
    private IntPtr _foregroundHook;


    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        Title = "声枢";

        _hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow;
        _subclassProc = WindowSubclassProc;

        ConfigureWindow();
        NativeMethods.SetWindowSubclass(_hwnd, _subclassProc, SubclassId, IntPtr.Zero);

        _foregroundCallback = ForegroundChanged;
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground,
            IntPtr.Zero,
            _foregroundCallback,
            0,
            0,
            NativeMethods.WineventOutOfContext);

        _fullscreenWatcher = new FullscreenWatcher(DispatcherQueue);
        _fullscreenWatcher.FullscreenChanged += FullscreenWatcher_FullscreenChanged;

        _dragWatch = DispatcherQueue.CreateTimer();
        _dragWatch.Interval = TimeSpan.FromMilliseconds(16);
        _dragWatch.IsRepeating = true;
        _dragWatch.Tick += DragWatch_Tick;

        // WinUI does not surface hover pointer events for this no-activate
        // window. A 180 ms cursor probe is the smallest reliable cross-WinUI
        // hover source and costs practically nothing at idle.
        _hoverWatch = DispatcherQueue.CreateTimer();
        _hoverWatch.Interval = TimeSpan.FromMilliseconds(180);
        _hoverWatch.IsRepeating = true;
        _hoverWatch.Tick += HoverWatch_Tick;

        // One cheap COM read per second keeps the card's volume level in sync
        // without a second audio event subscription.
        _volumeWatch = DispatcherQueue.CreateTimer();
        _volumeWatch.Interval = TimeSpan.FromSeconds(1);
        _volumeWatch.IsRepeating = true;
        _volumeWatch.Tick += VolumeWatch_Tick;


        _host.DevicesUpdated += Host_DevicesUpdated;
        _host.SettingsChanged += Host_SettingsChanged;
        _host.VolumeChanged += Host_VolumeChanged;

        _appWindow.Closing += AppWindow_Closing;
        Closed += MainWindow_Closed;

        ApplyWidgetSize();
        RestorePosition();
        ApplyAppearance();
        UpdateVisuals(animate: false);

        AutomationProperties.SetName(RootGrid, "声枢输出设备切换");
        AutomationProperties.SetHelpText(RootGrid, "单击切换输出设备，右键打开菜单，拖动可移动位置。");
    }

    private void ConfigureWindow()
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = false;
        }

        _appWindow.IsShownInSwitchers = false;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico");
        if (File.Exists(iconPath))
        {
            _appWindow.SetIcon(iconPath);
        }

        WindowEffects.ApplyDesktopWidgetStyle(_hwnd);
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        AppLog.Info($"桌面组件已加载，隐藏全屏={_host.Settings.HideInFullscreen}。");
        // WinUI may reset extended window styles while activating the window.
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = false;
        }

        WindowEffects.ApplyDesktopWidgetStyle(_hwnd);
        ApplyWidgetSize();
        UpdateVisuals(animate: false);
        _host.RefreshVolume();
        UpdateVolume();
        _hoverWatch.Start();
        _volumeWatch.Start();
        _fullscreenWatcher.SetEnabled(_host.Settings.HideInFullscreen);
    }

    private void ApplyWidgetSize()
    {
        var dpi = NativeMethods.GetDpiForWindow(_hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var level = GetSizeLevel(_host.Settings.WidgetWidth);
        var widthDip = level switch
        {
            0 => 56,
            1 => 168,
            2 => 236,
            _ => 280,
        };
        var width = Math.Max(56, (int)Math.Round(widthDip * dpi / 96.0));
        var height = Math.Max(40, (int)Math.Round(WidgetHeightDip * dpi / 96.0));

        var oldPosition = _appWindow.Position;
        var oldSize = _appWindow.Size;
        if (oldSize.Width != width || oldSize.Height != height)
        {
            _appWindow.Resize(new SizeInt32(width, height));

            // Keep a card anchored to the right/bottom corner when switching
            // between the full card and the compact square.
            var display = DisplayArea.GetFromPoint(
                new PointInt32(oldPosition.X + oldSize.Width / 2, oldPosition.Y + oldSize.Height / 2),
                DisplayAreaFallback.Nearest);
            var work = display.WorkArea;
            var x = oldPosition.X + oldSize.Width / 2 > work.X + work.Width / 2
                ? oldPosition.X + oldSize.Width - width
                : oldPosition.X;
            var y = oldPosition.Y + oldSize.Height / 2 > work.Y + work.Height / 2
                ? oldPosition.Y + oldSize.Height - height
                : oldPosition.Y;
            _appWindow.Move(new PointInt32(x, y));
        }

        ApplyLayout(level);
        ClampPosition();
    }

    private static int GetSizeLevel(double width) =>
        width <= 80 ? 0 :
        width <= 210 ? 1 :
        width <= 250 ? 2 : 3;

    private void ApplyLayout(int level)
    {
        var compact = level == 0;
        var nameOnly = level == 1;

        ContentRoot.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactRoot.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        VolumePanel.Visibility = nameOnly || compact ? Visibility.Collapsed : Visibility.Visible;
        WidgetSurface.CornerRadius = new CornerRadius(8);
        ContentRoot.Padding = new Thickness(16, 0, 16, 0);
        VolumeFullPanel.Visibility = level >= 2 ? Visibility.Visible : Visibility.Collapsed;
        VolumeCompactPanel.Visibility = level <= 1 ? Visibility.Visible : Visibility.Collapsed;
        VolumeCompactBar.Visibility = level == 0 ? Visibility.Collapsed : Visibility.Visible;
        VolumeCompactText.FontSize = level == 0 ? 18 : 20;
        WindowEffects.SetRoundedWindowCorners(_hwnd, rounded: true);

        if (nameOnly)
        {
            UpdateVolume();
        }

    }

    private void RestorePosition()
    {
        var width = _appWindow.Size.Width;
        var height = _appWindow.Size.Height;
        var margin = DipToPx(20);
        PointInt32 position;

        var saved = _host.Settings.BallPosition;
        if (saved is not null)
        {
            var display = DisplayArea.GetFromPoint(
                new PointInt32(saved.X, saved.Y),
                DisplayAreaFallback.Nearest);
            var work = display.WorkArea;
            var x = Math.Clamp(saved.X, work.X + margin, Math.Max(work.X + margin, work.X + work.Width - width - margin));
            var y = Math.Clamp(saved.Y, work.Y + margin, Math.Max(work.Y + margin, work.Y + work.Height - height - margin));
            position = new PointInt32(x, y);
        }
        else
        {
            var work = DisplayArea.Primary.WorkArea;
            position = new PointInt32(
                work.X + work.Width - width - margin,
                work.Y + work.Height - height - margin);
        }

        _appWindow.Move(position);
    }

    private void SnapToEdge()
    {
        var position = _appWindow.Position;
        var width = _appWindow.Size.Width;
        var height = _appWindow.Size.Height;
        var center = new PointInt32(position.X + width / 2, position.Y + height / 2);
        var display = DisplayArea.GetFromPoint(center, DisplayAreaFallback.Nearest);
        var work = display.WorkArea;
        var margin = DipToPx(20);
        var threshold = DipToPx(48);

        var x = position.X;
        var y = position.Y;

        if (x - work.X <= threshold)
        {
            x = work.X + margin;
        }
        else if (work.X + work.Width - (x + width) <= threshold)
        {
            x = work.X + work.Width - width - margin;
        }

        if (y - work.Y <= threshold)
        {
            y = work.Y + margin;
        }
        else if (work.Y + work.Height - (y + height) <= threshold)
        {
            y = work.Y + work.Height - height - margin;
        }

        x = Math.Clamp(x, work.X + margin, Math.Max(work.X + margin, work.X + work.Width - width - margin));
        y = Math.Clamp(y, work.Y + margin, Math.Max(work.Y + margin, work.Y + work.Height - height - margin));

        _appWindow.Move(new PointInt32(x, y));
    }

    private void ClampPosition()
    {
        var position = _appWindow.Position;
        var width = _appWindow.Size.Width;
        var height = _appWindow.Size.Height;
        var center = new PointInt32(position.X + width / 2, position.Y + height / 2);
        var display = DisplayArea.GetFromPoint(center, DisplayAreaFallback.Nearest);
        var work = display.WorkArea;
        var margin = DipToPx(12);

        var x = Math.Clamp(position.X, work.X + margin, Math.Max(work.X + margin, work.X + work.Width - width - margin));
        var y = Math.Clamp(position.Y, work.Y + margin, Math.Max(work.Y + margin, work.Y + work.Height - height - margin));
        if (x != position.X || y != position.Y)
        {
            _appWindow.Move(new PointInt32(x, y));
        }
    }

    private void SaveWidgetPosition()
    {
        var position = _appWindow.Position;
        _host.Settings.BallPosition = new BallPosition { X = position.X, Y = position.Y };
        _host.SaveSettings();
    }

    public void ResetBallPosition()
    {
        _host.Settings.BallPosition = null;
        _host.SaveSettings();
        RestorePosition();
    }

    private int DipToPx(int dip)
    {
        var dpi = NativeMethods.GetDpiForWindow(_hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        return (int)Math.Round(dip * dpi / 96.0);
    }

    private void ApplyAppearance()
    {
        var theme = ThemeService.ToElementTheme(_host.Settings.Theme);
        var dark = ThemeService.IsDark(_host.Settings.Theme);

        RootGrid.RequestedTheme = theme;
        WindowEffects.SetDarkMode(_hwnd, dark);

        // Fluent 2 solid material: an opaque surface, with color and a
        // continuous 1px stroke doing the hierarchy work instead of a
        // translucent material that WinUI cannot render on this window.
        var fill = new SolidColorBrush(
            dark ? Color.FromArgb(0xFF, 0x29, 0x29, 0x29) : Color.FromArgb(0xFF, 0xFA, 0xFA, 0xFA));
        RootGrid.Background = fill;
        ActionView.Background = fill;

        ApplyWidgetSize();

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = false;
        }

        WindowEffects.PlaceAboveDesktop(_hwnd);
        UpdateVisuals(animate: false);
    }

    private void UpdateVisuals(bool animate)
    {
        var current = _host.CurrentDefault;
        var headphones = current?.IsHeadphones == true;
        var glyph = current?.IconGlyph ?? "\uE767";
        var (title, subtitle) = SplitDeviceName(current);
        var detail = string.IsNullOrWhiteSpace(subtitle)
            ? "单击切换 · 右键更多"
            : subtitle;
        var changed = DeviceIcon.Glyph != glyph ||
                      CompactIcon.Glyph != glyph ||
                      DeviceNameText.Text != title ||
                      DeviceDetailText.Text != detail;

        void Apply()
        {
            DeviceIcon.Glyph = glyph;
            CompactIcon.Glyph = glyph;
            DeviceNameText.Text = title;
            DeviceDetailText.Text = detail;
            DeviceIcon.Opacity = current is null ? 0.45 : 1.0;
            CompactIcon.Opacity = current is null ? 0.45 : 1.0;
            DeviceNameText.Opacity = current is null ? 0.6 : 1.0;
            UpdateVolume();
        }

        if (animate && changed && _host.Settings.Animations)
        {
            PlayContentCrossFade(Apply);
        }
        else
        {
            Apply();
        }
    }

    private void Host_VolumeChanged(object? sender, EventArgs e) => UpdateVolume();

    private void VolumeWatch_Tick(DispatcherQueueTimer sender, object args)
    {
        _host.RefreshVolume();
        UpdateVolume();
    }

    private void ShowVolumeView()
    {
        if (_actionViewVisible)
        {
            HideActionView();
        }

        if (_volumeViewVisible)
        {
            // Re-entering the already visible volume view (another wheel tick)
            // must only update the value, never replay the transition.
            UpdateVolumeView();
            return;
        }

        _volumeViewVisible = true;
        VolumeView.Visibility = Visibility.Visible;
        VolumeView.Opacity = 0;
        VolumeViewScale.ScaleX = 0.96;
        VolumeViewScale.ScaleY = 0.96;
        ContentRoot.Opacity = 1;
        CompactRoot.Opacity = 1;
        UpdateVolumeView();

        if (_host.Settings.Animations)
        {
            var fade = new DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(140)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            var scaleX = new DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(140)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            var scaleY = new DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(140)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            var contentFade = new DoubleAnimation
            {
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(110)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            };
            Storyboard.SetTarget(fade, VolumeView);
            Storyboard.SetTargetProperty(fade, nameof(UIElement.Opacity));
            Storyboard.SetTarget(scaleX, VolumeViewScale);
            Storyboard.SetTargetProperty(scaleX, nameof(ScaleTransform.ScaleX));
            Storyboard.SetTarget(scaleY, VolumeViewScale);
            Storyboard.SetTargetProperty(scaleY, nameof(ScaleTransform.ScaleY));
            Storyboard.SetTarget(contentFade, ContentRoot);
            Storyboard.SetTargetProperty(contentFade, nameof(UIElement.Opacity));

            var compactFade = new DoubleAnimation
            {
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(110)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            };
            Storyboard.SetTarget(compactFade, CompactRoot);
            Storyboard.SetTargetProperty(compactFade, nameof(UIElement.Opacity));

            var storyboard = new Storyboard();
            storyboard.Children.Add(fade);
            storyboard.Children.Add(scaleX);
            storyboard.Children.Add(scaleY);
            storyboard.Children.Add(contentFade);
            storyboard.Children.Add(compactFade);
            storyboard.Begin();
        }
        else
        {
            VolumeView.Opacity = 1;
            VolumeViewScale.ScaleX = 1;
            VolumeViewScale.ScaleY = 1;
            ContentRoot.Opacity = 0;
            CompactRoot.Opacity = 0;
        }

    }

    private void HideVolumeView()
    {
        if (!_volumeViewVisible)
        {
            return;
        }

        _volumeViewVisible = false;

        if (!_host.Settings.Animations)
        {
            ContentRoot.Opacity = 1;
            CompactRoot.Opacity = 1;
            VolumeView.Visibility = Visibility.Collapsed;
            return;
        }

        var fade = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(130)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        var contentFade = new DoubleAnimation
        {
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var compactFade = new DoubleAnimation
        {
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(fade, VolumeView);
        Storyboard.SetTargetProperty(fade, nameof(UIElement.Opacity));
        Storyboard.SetTarget(contentFade, ContentRoot);
        Storyboard.SetTargetProperty(contentFade, nameof(UIElement.Opacity));
        Storyboard.SetTarget(compactFade, CompactRoot);
        Storyboard.SetTargetProperty(compactFade, nameof(UIElement.Opacity));

        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Children.Add(contentFade);
        storyboard.Children.Add(compactFade);
        storyboard.Completed += (_, _) =>
        {
            if (!_volumeViewVisible)
            {
                VolumeView.Visibility = Visibility.Collapsed;
                VolumeView.Opacity = 1;
                ContentRoot.Opacity = 1;
                CompactRoot.Opacity = 1;
            }
        };
        storyboard.Begin();
    }

    private void UpdateVolumeView()
    {
        var percent = (int)Math.Round(_host.VolumeLevel * 100);
        var label = _host.IsMuted ? "静音" : $"{percent}%";
        var glyph = _host.IsMuted ? "\uE74F" : "\uE767";
        VolumeViewIcon.Glyph = glyph;
        VolumeViewText.Text = label;
        VolumeCompactText.Text = label;
        VolumeViewBarFill.Width = _host.IsMuted ? 0 : 64 * Math.Clamp(_host.VolumeLevel, 0f, 1f);
        VolumeCompactBarFill.Width = _host.IsMuted ? 0 : 64 * Math.Clamp(_host.VolumeLevel, 0f, 1f);
    }

    private void UpdateVolume()
    {
        var percent = (int)Math.Round(_host.VolumeLevel * 100);
        var label = _host.IsMuted ? "静音" : $"{percent}%";
        VolumeText.Text = label;
        VolumeBarFill.Height = _host.IsMuted ? 0 : 34 * Math.Clamp(_host.VolumeLevel, 0f, 1f);
        if (GetSizeLevel(_host.Settings.WidgetWidth) == 1)
        {
            DeviceDetailText.Text = _host.IsMuted ? "静音" : $"音量 {percent}%";
        }

        UpdateVolumeView();
    }

    private static (string Title, string Subtitle) SplitDeviceName(AudioEndpoint? endpoint)
    {
        if (endpoint is null)
        {
            return ("未检测到输出设备", "右键打开设置");
        }

        var name = endpoint.Name;
        var title = name;
        var subtitle = string.Empty;
        var bracket = name.IndexOf(" (", StringComparison.Ordinal);
        if (bracket > 0 && name.EndsWith(')'))
        {
            title = name[..bracket];
            subtitle = name[(bracket + 2)..^1];
        }

        if (endpoint.IsHeadphones && string.Equals(title, "扬声器", StringComparison.Ordinal))
        {
            title = "耳机";
        }

        return (title, subtitle);
    }

    private void PlayContentCrossFade(Action apply)
    {
        // Cross-fade only: the text is swapped while the content is fully
        // transparent, so no element moves or pops in either direction.
        var version = ++_contentSwitchVersion;

        var fadeOut = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(80)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        Storyboard.SetTarget(fadeOut, ContentRoot);
        Storyboard.SetTargetProperty(fadeOut, nameof(UIElement.Opacity));

        var compactFadeOut = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(80)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        Storyboard.SetTarget(compactFadeOut, CompactRoot);
        Storyboard.SetTargetProperty(compactFadeOut, nameof(UIElement.Opacity));

        var fadeOutStoryboard = new Storyboard();
        fadeOutStoryboard.Children.Add(fadeOut);
        fadeOutStoryboard.Children.Add(compactFadeOut);
        fadeOutStoryboard.Completed += (_, _) =>
        {
            if (version != _contentSwitchVersion)
            {
                return;
            }

            apply();

            var fadeIn = new DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(fadeIn, ContentRoot);
            Storyboard.SetTargetProperty(fadeIn, nameof(UIElement.Opacity));

            var compactFadeIn = new DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(compactFadeIn, CompactRoot);
            Storyboard.SetTargetProperty(compactFadeIn, nameof(UIElement.Opacity));

            var fadeInStoryboard = new Storyboard();
            fadeInStoryboard.Children.Add(fadeIn);
            fadeInStoryboard.Children.Add(compactFadeIn);
            fadeInStoryboard.Begin();
        };
        fadeOutStoryboard.Begin();
    }

    private void AnimatePress(bool pressed)
    {
        // Deliberately no scale/translate on press: the click feedback is the
        // content cross-fade, so the card never appears to bounce.
    }

    private static void AnimateScale(ScaleTransform transform, double to, int milliseconds)
    {
        var scaleX = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var scaleY = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        Storyboard.SetTarget(scaleX, transform);
        Storyboard.SetTargetProperty(scaleX, nameof(ScaleTransform.ScaleX));
        Storyboard.SetTarget(scaleY, transform);
        Storyboard.SetTargetProperty(scaleY, nameof(ScaleTransform.ScaleY));

        var storyboard = new Storyboard();
        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        storyboard.Begin();
    }

    private async Task PerformCycleAsync(int direction)
    {
        if (_isSwitching)
        {
            return;
        }

        await PerformSwitchAsync(() => _host.CycleAsync(direction));
    }

    private async Task PerformSwitchAsync(Func<Task<AudioSwitchResult>> action)
    {
        if (_isSwitching)
        {
            return;
        }

        _isSwitching = true;
        SetBusy(true);
        try
        {
            var result = await action();
            if (result.Success)
            {
                UpdateVisuals(animate: true);
            }
            else
            {
                UpdateVisuals(animate: false);
                ShowTransientMessage(result.ErrorMessage ?? "切换失败，请打开设置检查输出设备。");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("切换输出设备时出现异常。", ex);
            ShowTransientMessage("切换失败，请查看日志或重试。");
        }
        finally
        {
            SetBusy(false);
            _isSwitching = false;
        }
    }

    private void SetBusy(bool busy)
    {
        // The content cross-fade already masks the device switch. Swapping the
        // icon for a spinner here caused a visible high-frequency flash on the
        // left edge, especially when the host raised several update events.
        BusyRing.IsActive = false;
        BusyRing.Visibility = Visibility.Collapsed;
        CompactBusyRing.IsActive = false;
        CompactBusyRing.Visibility = Visibility.Collapsed;
        DeviceIcon.Visibility = Visibility.Visible;
        CompactIcon.Visibility = Visibility.Visible;
    }

    private void ShowTransientMessage(string message)
    {
        _transientMessageCts?.Cancel();
        _transientMessageCts?.Dispose();
        _transientMessageCts = new CancellationTokenSource();
        _messageUntil = DateTime.UtcNow.AddSeconds(3.2);

        // There is no hover popup anymore; surface transient errors directly
        // in the card's secondary line, then restore the adapter name.
        DeviceDetailText.Text = message;
        _ = RestoreMessageAsync(_transientMessageCts.Token);
    }

    private async Task RestoreMessageAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(3200, token);
            if (DateTime.UtcNow < _messageUntil)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() => UpdateVisuals(animate: false));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ShowActionView()
    {
        if (_actionViewVisible)
        {
            return;
        }

        if (_volumeViewVisible)
        {
            _volumeViewVisible = false;
            VolumeView.Visibility = Visibility.Collapsed;
            VolumeView.Opacity = 1;
        }

        _actionViewVisible = true;
        _actionHover = -1;
        SetActionButtonBackground(ActionSettingsButton, false);
        ActionView.Background = RootGrid.Background;
        ActionView.Visibility = Visibility.Visible;
        ActionView.Opacity = 0;
        ActionViewScale.ScaleX = 0.96;
        ActionViewScale.ScaleY = 0.96;
        ContentRoot.Opacity = 1;
        CompactRoot.Opacity = 1;

        if (_host.Settings.Animations)
        {
            var fade = new DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(140)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            var scaleX = new DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(140)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            var scaleY = new DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(140)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            var contentFade = new DoubleAnimation
            {
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(110)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            };
            var compactFade = new DoubleAnimation
            {
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(110)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            };
            Storyboard.SetTarget(fade, ActionView);
            Storyboard.SetTargetProperty(fade, nameof(UIElement.Opacity));
            Storyboard.SetTarget(scaleX, ActionViewScale);
            Storyboard.SetTargetProperty(scaleX, nameof(ScaleTransform.ScaleX));
            Storyboard.SetTarget(scaleY, ActionViewScale);
            Storyboard.SetTargetProperty(scaleY, nameof(ScaleTransform.ScaleY));
            Storyboard.SetTarget(contentFade, ContentRoot);
            Storyboard.SetTargetProperty(contentFade, nameof(UIElement.Opacity));
            Storyboard.SetTarget(compactFade, CompactRoot);
            Storyboard.SetTargetProperty(compactFade, nameof(UIElement.Opacity));

            var storyboard = new Storyboard();
            storyboard.Children.Add(fade);
            storyboard.Children.Add(scaleX);
            storyboard.Children.Add(scaleY);
            storyboard.Children.Add(contentFade);
            storyboard.Children.Add(compactFade);
            storyboard.Begin();
        }
        else
        {
            ActionView.Opacity = 1;
            ActionViewScale.ScaleX = 1;
            ActionViewScale.ScaleY = 1;
            ContentRoot.Opacity = 0;
            CompactRoot.Opacity = 0;
        }
    }

    private void HideActionView()
    {
        if (!_actionViewVisible)
        {
            return;
        }

        _actionViewVisible = false;
        _actionHover = -1;

        if (!_host.Settings.Animations)
        {
            ContentRoot.Opacity = 1;
            CompactRoot.Opacity = 1;
            ActionView.Visibility = Visibility.Collapsed;
            ActionView.Opacity = 1;
            ActionViewScale.ScaleX = 1;
            ActionViewScale.ScaleY = 1;
            return;
        }

        var fade = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(130)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        var contentFade = new DoubleAnimation
        {
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var compactFade = new DoubleAnimation
        {
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(fade, ActionView);
        Storyboard.SetTargetProperty(fade, nameof(UIElement.Opacity));
        Storyboard.SetTarget(contentFade, ContentRoot);
        Storyboard.SetTargetProperty(contentFade, nameof(UIElement.Opacity));
        Storyboard.SetTarget(compactFade, CompactRoot);
        Storyboard.SetTargetProperty(compactFade, nameof(UIElement.Opacity));

        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Children.Add(contentFade);
        storyboard.Children.Add(compactFade);
        storyboard.Completed += (_, _) =>
        {
            if (!_actionViewVisible)
            {
                ActionView.Visibility = Visibility.Collapsed;
                ActionView.Opacity = 1;
                ActionViewScale.ScaleX = 1;
                ActionViewScale.ScaleY = 1;
                ContentRoot.Opacity = 1;
                CompactRoot.Opacity = 1;
            }
        };
        storyboard.Begin();
    }

    private void UpdateActionHover()
    {
        if (!_actionViewVisible || !NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        var hover = IsCursorOver(ActionSettingsButton, cursor.X, cursor.Y) ? 0 : -1;

        if (hover == _actionHover)
        {
            return;
        }

        _actionHover = hover;
        SetActionButtonBackground(ActionSettingsButton, hover == 0);
    }

    private bool IsCursorOver(FrameworkElement element, int x, int y)
    {
        var topLeft = element.TransformToVisual(null).TransformPoint(new Point(0, 0));
        var scale = element.XamlRoot?.RasterizationScale ?? 1;
        var position = _appWindow.Position;
        var left = position.X + topLeft.X * scale;
        var top = position.Y + topLeft.Y * scale;
        return x >= left && x < left + element.ActualWidth * scale &&
               y >= top && y < top + element.ActualHeight * scale;
    }

    private void SetActionButtonBackground(Button button, bool hover)
    {
        button.Background = !hover
            ? new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))
            : RootGrid.ActualTheme == ElementTheme.Dark
                ? new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0x14, 0x00, 0x00, 0x00));
    }

    private void ActionSettings_Click(object sender, RoutedEventArgs e)
    {
        HideActionView();
        (Application.Current as App)?.OpenSettings();
    }

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(RootGrid);
        if (_volumeViewVisible)
        {
            // The volume view is a stable transient state. A mouse button is
            // an explicit request to return to the device view.
            HideVolumeView();
            e.Handled = true;
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pressed = true;
        _dragging = false;
        _suppressClick = false;
        _pressedPointerId = e.Pointer.PointerId;
        AnimatePress(pressed: true);

        if (NativeMethods.GetCursorPos(out _dragStartCursor))
        {
            _dragStartWindow = _appWindow.Position;
        }

        RootGrid.CapturePointer(e.Pointer);

        // WinUI does not reliably deliver PointerMoved for this no-activate
        // window. A timer that runs only while the left mouse button is held
        // makes dragging dependable without idle overhead.
        _dragWatch.Start();

        e.Handled = true;
    }

    private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed || e.Pointer.PointerId != _pressedPointerId)
        {
            return;
        }

        if (UpdateDragFromCursor())
        {
            e.Handled = true;
        }
    }

    private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_actionViewVisible)
        {
            _suppressClick = true;
            return;
        }

        if (!_pressed || e.Pointer.PointerId != _pressedPointerId)
        {
            return;
        }

        FinishPointerInteraction(fromReleaseEvent: true);
        e.Handled = true;
    }

    private void RootGrid_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        CancelPointerInteraction();
    }

    private void RootGrid_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        CancelPointerInteraction();
    }

    private void DragWatch_Tick(DispatcherQueueTimer sender, object args)
    {
        if (!_pressed)
        {
            sender.Stop();
            return;
        }

        if ((NativeMethods.GetAsyncKeyState(NativeMethods.VkLeftButton) & 0x8000) == 0)
        {
            sender.Stop();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_pressed)
                {
                    FinishPointerInteraction(fromReleaseEvent: false);
                }
            });
            return;
        }

        UpdateDragFromCursor();
    }

    private bool UpdateDragFromCursor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return false;
        }

        var dx = cursor.X - _dragStartCursor.X;
        var dy = cursor.Y - _dragStartCursor.Y;

        if (!_dragging && (Math.Abs(dx) > DipToPx(4) || Math.Abs(dy) > DipToPx(4)))
        {
            _dragging = true;
        }

        if (!_dragging)
        {
            return false;
        }

        _appWindow.Move(new PointInt32(_dragStartWindow.X + dx, _dragStartWindow.Y + dy));
        return true;
    }

    private void FinishPointerInteraction(bool fromReleaseEvent)
    {
        if (!_pressed)
        {
            return;
        }

        _pressed = false;
        _dragWatch.Stop();
        try
        {
            RootGrid.ReleasePointerCaptures();
        }
        catch (Exception ex)
        {
            AppLog.Warning($"释放指针捕获失败：{ex.Message}");
        }

        var wasDragging = _dragging;
        _dragging = false;
        AnimatePress(pressed: false);

        if (wasDragging)
        {
            SnapToEdge();
            SaveWidgetPosition();
        }
        else if (!_suppressClick && (fromReleaseEvent || IsCursorInsideWidget()))
        {
            _ = PerformCycleAsync(1);
        }

        _suppressClick = false;
    }

    private bool IsCursorInsideWidget()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return false;
        }

        var position = _appWindow.Position;
        var size = _appWindow.Size;
        return cursor.X >= position.X &&
               cursor.Y >= position.Y &&
               cursor.X < position.X + size.Width &&
               cursor.Y < position.Y + size.Height;
    }

    private void CancelPointerInteraction()
    {
        if (!_pressed)
        {
            return;
        }

        _pressed = false;
        _dragging = false;
        _suppressClick = false;
        _dragWatch.Stop();
        AnimatePress(pressed: false);
    }

    private void ForegroundChanged(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (!_closed)
        {
            DispatcherQueue.TryEnqueue(EnsureTopmost);
        }
    }

    private void HoverWatch_Tick(DispatcherQueueTimer sender, object args)
    {
        EnsureTopmost();
        if (!IsCursorInsideWidget())
        {
            if (_volumeViewVisible && !_pressed)
            {
                HideVolumeView();
            }

            if (_actionViewVisible)
            {
                HideActionView();
            }
        }
        else if (_actionViewVisible)
        {
            UpdateActionHover();
        }
    }

    private void RootGrid_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(RootGrid).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        if (string.Equals(_host.Settings.WheelAction, "Volume", StringComparison.OrdinalIgnoreCase))
        {
            if (_host.AdjustVolume(delta > 0 ? 1 : -1))
            {
                UpdateVolume();
                ShowVolumeView();
            }
        }
        else
        {
            _ = PerformCycleAsync(delta < 0 ? 1 : -1);
        }

        e.Handled = true;
    }

    private void RootGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        _suppressClick = true;
        ShowActionView();
        e.Handled = true;
    }

    private void Host_DevicesUpdated(object? sender, EventArgs e)
    {
        _host.RefreshVolume();
        UpdateVisuals(animate: true);
        UpdateVolume();
    }

    private void Host_SettingsChanged(object? sender, EventArgs e)
    {
        ApplyAppearance();
        _fullscreenWatcher.SetEnabled(_host.Settings.HideInFullscreen);
        if (!_host.Settings.HideInFullscreen && _hiddenForFullscreen)
        {
            ShowFromFullscreen();
        }
    }

    private void FullscreenWatcher_FullscreenChanged(object? sender, bool fullscreen)
    {
        if (!_host.Settings.HideInFullscreen)
        {
            if (_hiddenForFullscreen)
            {
                ShowFromFullscreen();
            }

            return;
        }

        if (fullscreen && !_hiddenForFullscreen)
        {
            _hiddenForFullscreen = true;
            _hoverWatch.Stop();
            WindowEffects.Hide(_hwnd);
        }
        else if (!fullscreen && _hiddenForFullscreen)
        {
            ShowFromFullscreen();
        }
    }

    private void ShowFromFullscreen()
    {
        _hiddenForFullscreen = false;
        WindowEffects.ShowDesktopWindow(_hwnd);
        _hoverWatch.Start();
    }

    private void EnsureTopmost()
    {
        if (_hiddenForFullscreen)
        {
            return;
        }

        // Win+D raises the desktop above normal windows. While the desktop is
        // foreground the card joins the topmost band so it survives "show
        // desktop"; as soon as an application takes focus it drops back below
        // normal windows, exactly like a Windows 7 desktop gadget.
        var shouldBeTopmost = IsDesktopForeground(NativeMethods.GetForegroundWindow());

        // Do not cache this as a bool: WinUI can reset WS_EX_TOPMOST while the
        // window is still alive, so a cached "true" made the card disappear
        // on the next Win+D. Compare the actual extended style instead.
        var extendedStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle).ToInt64();
        var isTopmost = (extendedStyle & NativeMethods.WsExTopmost) != 0;
        if (isTopmost == shouldBeTopmost)
        {
            return;
        }

        if (shouldBeTopmost)
        {
            NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HwndTopmost,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoMove |
                NativeMethods.SwpNoSize |
                NativeMethods.SwpNoActivate);
        }
        else
        {
            NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HwndNotTopmost,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoMove |
                NativeMethods.SwpNoSize |
                NativeMethods.SwpNoActivate);
            WindowEffects.PlaceAboveDesktop(_hwnd);
        }
    }

    private static bool IsDesktopForeground(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        if (foreground == NativeMethods.GetShellWindow())
        {
            return !IsCursorOverTaskbar();
        }

        var className = new System.Text.StringBuilder(96);
        NativeMethods.GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is not ("Progman" or "WorkerW"))
        {
            return false;
        }

        // Clicking empty taskbar space temporarily makes Progman the
        // foreground window, even though the maximized app is still visible.
        // Only treat the desktop as frontmost when the pointer is not over the
        // taskbar; otherwise the card would jump above the app.
        return !IsCursorOverTaskbar();
    }

    private static bool IsCursorOverTaskbar()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return false;
        }

        // The taskbar lives in the monitor's non-work area. WindowFromPoint is
        // not reliable here: on some Windows 11 builds the taskbar window's
        // root is Progman, so checking only the class name misses the click.
        var monitor = NativeMethods.MonitorFromPoint(
            cursor,
            NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo
        {
            cbSize = Marshal.SizeOf<NativeMethods.MonitorInfo>(),
        };

        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        return cursor.X < info.rcWork.Left ||
               cursor.X >= info.rcWork.Right ||
               cursor.Y < info.rcWork.Top ||
               cursor.Y >= info.rcWork.Bottom;
    }

    private IntPtr WindowSubclassProc(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        IntPtr refData)
    {
        try
        {
            if (message == App.OpenSettingsMessageId && App.OpenSettingsMessageId != 0)
            {
                DispatcherQueue.TryEnqueue(() =>
                    (Application.Current as App)?.RequestOpenSettingsFromOtherInstance());
                return IntPtr.Zero;
            }

            if (message == NativeMethods.WmDpiChanged)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ApplyWidgetSize();
                    ClampPosition();
                });
            }

            if (message == NativeMethods.WmMouseActivate)
            {
                return new IntPtr(NativeMethods.MaNoActivate);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("窗口消息处理失败。", ex);
        }

        return NativeMethods.DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (Application.Current is App app && !app.IsExiting)
        {
            args.Cancel = true;
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        if (_foregroundHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }

        _transientMessageCts?.Cancel();
        _transientMessageCts?.Dispose();
        _hoverWatch.Stop();
        _dragWatch.Stop();
        _fullscreenWatcher.FullscreenChanged -= FullscreenWatcher_FullscreenChanged;
        _fullscreenWatcher.Dispose();
        _host.DevicesUpdated -= Host_DevicesUpdated;
        _host.SettingsChanged -= Host_SettingsChanged;
        _host.VolumeChanged -= Host_VolumeChanged;
        _volumeWatch.Stop();
        NativeMethods.RemoveWindowSubclass(_hwnd, _subclassProc, SubclassId);
    }
}
