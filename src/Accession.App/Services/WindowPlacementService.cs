using System.Windows;
using Accession.Core.Settings;

namespace Accession.App.Services;

public sealed class WindowPlacementService : IWindowPlacementService
{
    private const double MinVisibleWidth = 100;
    private const double MinVisibleHeight = 50;

    private readonly ISettingsService _settings;

    public WindowPlacementService(ISettingsService settings)
    {
        _settings = settings;
    }

    public void Restore(Window window, string key)
    {
        if (!_settings.Current.Windows.TryGetValue(key, out var placement) || !IsOnScreen(placement))
        {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = placement.Left;
        window.Top = placement.Top;
        window.Width = Math.Max(placement.Width, window.MinWidth);
        window.Height = Math.Max(placement.Height, window.MinHeight);
        if (placement.IsMaximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    public void Save(Window window, string key)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
            : window.RestoreBounds;

        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        _settings.Update(s => s.Windows[key] = new WindowPlacement
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            IsMaximized = window.WindowState == WindowState.Maximized,
        });
    }

    /// <summary>Ignores saved placements that would put the window off every monitor (e.g. a disconnected screen).</summary>
    private static bool IsOnScreen(WindowPlacement placement)
    {
        if (placement.Width <= 0 || placement.Height <= 0)
        {
            return false;
        }

        var screen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var window = new Rect(placement.Left, placement.Top, placement.Width, placement.Height);
        var visible = Rect.Intersect(screen, window);
        return !visible.IsEmpty && visible.Width >= MinVisibleWidth && visible.Height >= MinVisibleHeight;
    }
}
