#if WINDOWS
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;

using Microsoft.UI.Xaml.Media;

namespace ChatAIBox;

// Keep WinUI's own background transparent so the HWND host-backdrop visual can show through.
internal sealed class TransparentHostBackdrop : SystemBackdrop
{
    private Windows.UI.Composition.Compositor? compositor;
    private Windows.UI.Composition.CompositionColorBrush? brush;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        compositor = new Windows.UI.Composition.Compositor();
        brush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        target.SystemBackdrop = brush;
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        target.SystemBackdrop = null;
        brush = null;
        compositor = null;
    }
}

internal sealed class ClearDesktopAcrylicBackdrop(bool keepActive) : SystemBackdrop
{
    private DesktopAcrylicController? controller;
    public bool KeepActive { get; } = keepActive;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        // Contrast comes from the app's theme wash; avoid a second opaque tint in the material.
        controller = new DesktopAcrylicController { TintOpacity = 0, LuminosityOpacity = 0 };
        UpdateConfiguration(target, xamlRoot);
        controller.AddSystemBackdropTarget(target);
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
        => UpdateConfiguration(target, xamlRoot);

    private void UpdateConfiguration(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        var configuration = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        if (KeepActive) configuration.IsInputActive = true;
        controller?.SetSystemBackdropConfiguration(configuration);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        controller?.RemoveSystemBackdropTarget(target);
        controller?.Dispose();
        controller = null;
    }
}
#endif
