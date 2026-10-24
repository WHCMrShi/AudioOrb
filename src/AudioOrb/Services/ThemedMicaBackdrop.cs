using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace AudioOrb.Services;

/// <summary>
/// A Mica backdrop whose light/dark theme can be changed independently of the
/// system setting, matching the in-app theme selector.
/// </summary>
public sealed class ThemedMicaBackdrop : SystemBackdrop
{
    private MicaController? _controller;
    private SystemBackdropConfiguration? _configuration;

    public ElementTheme Theme { get; private set; } = ElementTheme.Default;

    public void SetTheme(ElementTheme theme)
    {
        Theme = theme;
        if (_configuration is not null)
        {
            _configuration.Theme = theme switch
            {
                ElementTheme.Light => SystemBackdropTheme.Light,
                ElementTheme.Dark => SystemBackdropTheme.Dark,
                _ => SystemBackdropTheme.Default,
            };
        }
    }

    protected override void OnTargetConnected(
        ICompositionSupportsSystemBackdrop connectedTarget,
        XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        if (!MicaController.IsSupported())
        {
            return;
        }

        _controller = new MicaController { Kind = MicaKind.BaseAlt };
        _configuration = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = Theme switch
            {
                ElementTheme.Light => SystemBackdropTheme.Light,
                ElementTheme.Dark => SystemBackdropTheme.Dark,
                _ => SystemBackdropTheme.Default,
            },
        };

        _controller.AddSystemBackdropTarget(connectedTarget);
        _controller.SetSystemBackdropConfiguration(_configuration);
    }

    /// <summary>
    /// We own the SystemBackdropConfiguration, so the base implementation's
    /// default-config refresh is unnecessary. On Windows 11 26300 it throws
    /// ArgumentException when the XAML root theme changes while connected,
    /// which surfaced as an unhandled XAML exception during theme switching.
    /// </summary>
    protected override void OnDefaultSystemBackdropConfigurationChanged(
        ICompositionSupportsSystemBackdrop target,
        XamlRoot xamlRoot)
    {
        // Intentionally do not call base. SetTheme updates our configuration.
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
        _configuration = null;
        base.OnTargetDisconnected(disconnectedTarget);
    }
}
