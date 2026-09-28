using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Accession.Core.Settings;
using Accession.Core.Threading;
using Microsoft.Win32;

namespace Accession.App.Views;

/// <summary>
/// Keeps the progress spinner turning while the UI thread is busy. The web page (and its spinner) is only drawn while
/// the UI thread is free, and opening an inventory keeps it busy for moments (building the screens, the first render).
/// While the busy overlay is up and the UI thread does not answer, this shows the same card, drawn by a window on its
/// own thread, over the page's card; it goes away once the UI thread is free again.
/// </summary>
public sealed class StallSpinner : IDisposable
{
    /// <summary>The UI thread counts as busy when it has not answered for this long.</summary>
    public static readonly TimeSpan StallAfter = TimeSpan.FromMilliseconds(200);

    /// <summary>The card stays until the UI thread has answered for this long (so it does not flicker between short breaks).</summary>
    public static readonly TimeSpan FreeFor = TimeSpan.FromMilliseconds(300);

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(50);

    private readonly Window _owner;
    private readonly BusyTracker _busy;
    private readonly ISettingsService _settings;
    private Dispatcher? _dispatcher;
    private SpinnerWindow? _window;

    // Set by the UI thread when it answers a ping; read on the spinner's thread.
    private volatile Snapshot? _snapshot;
    private volatile bool _waiting;
    private long _sentAt;
    private long _answeredAt;
    private long _freeSince;

    public StallSpinner(Window owner, BusyTracker busy, ISettingsService settings)
    {
        _owner = owner;
        _busy = busy;
        _settings = settings;
    }

    public void Start()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "Busy spinner" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public void Dispose() => _dispatcher?.InvokeShutdown();

    private void Run()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        var timer = new DispatcherTimer(Tick, DispatcherPriority.Background, (_, _) => Check(), _dispatcher);
        timer.Start();
        Dispatcher.Run();
    }

    /// <summary>On the spinner's thread, every 50 ms.</summary>
    private void Check()
    {
        if (!_busy.IsBusy)
        {
            _window?.Hide();
            if (!_waiting && Stopwatch.GetElapsedTime(_answeredAt) > TimeSpan.FromSeconds(1))
            {
                Ping(); // keeps the page's position current for when the next busy moment starts
            }

            return;
        }

        if (_waiting)
        {
            if (Stopwatch.GetElapsedTime(_sentAt) > StallAfter && _snapshot is { } stalled && GetForegroundWindow() == stalled.Handle)
            {
                _freeSince = 0;
                Show(stalled, _busy.Message ?? string.Empty);
            }

            return;
        }

        // The UI thread answered the last ping.
        if (_window is { IsVisible: true })
        {
            _freeSince = _freeSince == 0 ? Stopwatch.GetTimestamp() : _freeSince;
            if (Stopwatch.GetElapsedTime(_freeSince) > FreeFor)
            {
                _window.Hide();
            }
        }

        Ping();
    }

    private void Ping()
    {
        _waiting = true;
        _sentAt = Stopwatch.GetTimestamp();
        _owner.Dispatcher.BeginInvoke(DispatcherPriority.Send, Answer);
    }

    /// <summary>On the UI thread: where the page is and which theme it shows.</summary>
    private void Answer()
    {
        try
        {
            if (_owner.Content is FrameworkElement page && page.IsLoaded && PresentationSource.FromVisual(page) is not null)
            {
                var topLeft = page.PointToScreen(new Point(0, 0));
                var bottomRight = page.PointToScreen(new Point(page.ActualWidth, page.ActualHeight));
                _snapshot = new Snapshot(new WindowInteropHelper(_owner).Handle, new Int32Rect(
                    (int)topLeft.X, (int)topLeft.Y, (int)(bottomRight.X - topLeft.X), (int)(bottomRight.Y - topLeft.Y)), IsDark());
            }
        }
        catch (InvalidOperationException)
        {
            // not on screen (closing, minimized)
        }
        finally
        {
            _answeredAt = Stopwatch.GetTimestamp();
            _waiting = false;
        }
    }

    private bool IsDark()
    {
        var theme = _settings.Current.WebTheme;
        if (theme != WebThemes.System)
        {
            return theme == WebThemes.Dark;
        }

        // "Match Windows": the web view follows the apps setting.
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    private void Show(Snapshot snapshot, string message)
    {
        _window ??= new SpinnerWindow();
        _window.Update(message, snapshot.Dark);
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        // Device pixels, over the page: the card is centered, like the page's.
        SetWindowPos(new WindowInteropHelper(_window).Handle, HwndTopmost, snapshot.Bounds.X, snapshot.Bounds.Y,
            snapshot.Bounds.Width, snapshot.Bounds.Height, SwpNoActivate | SwpShowWindow);
    }

    private sealed record Snapshot(IntPtr Handle, Int32Rect Bounds, bool Dark);

    /// <summary>A transparent window with the busy card of the web page (accession.css: .overlay-card, .spinner).</summary>
    private sealed class SpinnerWindow : Window
    {
        private readonly Border _card;
        private readonly Ellipse _track;
        private readonly Path _arc;
        private readonly TextBlock _text;

        public SpinnerWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Focusable = false;
            Width = Height = 1;

            _track = new Ellipse { Width = 30, Height = 30, StrokeThickness = 3 };
            _arc = new Path
            {
                Width = 30,
                Height = 30,
                StrokeThickness = 3,
                Data = Geometry.Parse("M 5.454,5.454 A 13.5,13.5 0 0 1 24.546,5.454"),
                RenderTransformOrigin = new Point(.5, .5),
            };
            var rotation = new RotateTransform();
            _arc.RenderTransform = rotation;
            rotation.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(.8)) { RepeatBehavior = RepeatBehavior.Forever });

            _text = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 14, 0, 0),
            };
            var spinner = new Grid { Width = 30, Height = 30, HorizontalAlignment = HorizontalAlignment.Center };
            spinner.Children.Add(_track);
            spinner.Children.Add(_arc);
            var content = new StackPanel();
            content.Children.Add(spinner);
            content.Children.Add(_text);
            _card = new Border
            {
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(28, 24, 28, 24),
                MinWidth = 320,
                MaxWidth = 440,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = content,
            };
            Content = _card;
            SourceInitialized += (_, _) =>
            {
                // Never takes the focus or the mouse from the main window.
                var handle = new WindowInteropHelper(this).Handle;
                SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExNoActivate | WsExToolWindow | WsExTransparent);
            };
        }

        public void Update(string message, bool dark)
        {
            _text.Text = message;
            _card.Background = Brush(dark ? "#121827" : "#ffffff");
            _track.Stroke = Brush(dark ? "#1c2436" : "#eef0f4");
            _arc.Stroke = Brush(dark ? "#6d6af8" : "#4f46e5");
            _text.Foreground = Brush(dark ? "#e7eaf1" : "#101828");
        }

        private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    }

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x10;
    private const uint SwpShowWindow = 0x40;
    private static readonly IntPtr HwndTopmost = new(-1);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int newLong);
}
