using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using PrismPane_Widgets.Models;
using PrismPane_Widgets.Services;
using Windows.Media.Control;

namespace PrismPane_Widgets.Views;

public partial class MediaControlWidget : Window
{
    private const byte VkMediaNextTrack = 0xB0;
    private const byte VkMediaPrevTrack = 0xB1;
    private const byte VkMediaPlayPause = 0xB3;
    private readonly string _widgetId;
    private readonly WidgetSettings _widgetSettings;
    private readonly AppSettings _appSettings;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private bool _isRefreshing;
    private bool _isMediaPlaying;
    private bool _isDragging;
    private int _dragStartCursorX;
    private int _dragStartRelativeX;
    private int? _customRelativeX;
    private int _lastRelativeX;

    private System.Windows.Controls.Button? PlayPauseButton => FindName("BtnPlayPause") as System.Windows.Controls.Button;

    public string WidgetId => _widgetId;

    public MediaControlWidget(string widgetId, WidgetSettings settings, AppSettings appSettings)
    {
        InitializeComponent();
        _widgetId = widgetId;
        _widgetSettings = settings;
        _appSettings = appSettings;

        RootBorder.MouseLeftButtonDown += RootBorder_MouseLeftButtonDown;
        RootBorder.MouseMove += RootBorder_MouseMove;
        RootBorder.MouseLeftButtonUp += RootBorder_MouseLeftButtonUp;

        if (_widgetSettings.Custom.TryGetValue("TaskbarOffsetX", out var saved) && int.TryParse(saved, out var x))
            _customRelativeX = x;

        ApplyWidgetSettingsFromModel();

        Loaded += MediaControlWidget_Loaded;
        Closed += MediaControlWidget_Closed;
        SourceInitialized += (_, _) => UpdateTaskbarPlacement();
        _refreshTimer.Tick += async (_, _) => await RefreshWidgetAsync();
    }

    public void ApplyWidgetSettingsFromModel()
    {
        _appSettings.Widgets[_widgetId] = _widgetSettings;
        ThemeHelper.ApplyToElement(this, _widgetSettings.CustomColors);
        Opacity = _widgetSettings.Opacity;
        Topmost = false;

        Width = _widgetSettings.Width is > 0 ? _widgetSettings.Width.Value : 320;
        Height = _widgetSettings.Height is > 0 ? _widgetSettings.Height.Value : 44;

        _widgetSettings.Topmost = false;
        _widgetSettings.Width = Width;
        _widgetSettings.Height = Height;
        _widgetSettings.DockEdge = DockEdge.None;
        _widgetSettings.DockThickness = null;
        _appSettings.Save();

        if (IsLoaded)
            UpdateTaskbarPlacement();
    }

    private void MediaControlWidget_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateTaskbarPlacement();
        _ = RefreshWidgetAsync();
        _refreshTimer.Start();
    }

    private void MediaControlWidget_Closed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        ShellInterop.DetachFromTaskbar(this);
    }

    private async Task RefreshWidgetAsync()
    {
        if (_isRefreshing)
            return;

        _isRefreshing = true;
        UpdateTaskbarPlacement();
        try
        {
            // Re-request the session manager each time so newly started
            // browser / app sessions are always discovered.
            try
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            }
            catch
            {
                // SMTC unavailable on this OS version; go straight to audio fallback.
                _sessionManager = null;
            }

            _currentSession = _sessionManager is not null ? SelectSession(_sessionManager) : null;

            if (_currentSession is null)
            {
                ApplyAudioFallbackState();
                return;
            }

            var playbackInfo = _currentSession.GetPlaybackInfo();
            var controls = playbackInfo.Controls;
            var mediaProperties = await _currentSession.TryGetMediaPropertiesAsync();

            var title = string.IsNullOrWhiteSpace(mediaProperties.Title)
                ? ExtractSourceName(_currentSession.SourceAppUserModelId)
                : mediaProperties.Title;

            var subtitle = BuildSubtitle(
                string.IsNullOrWhiteSpace(mediaProperties.Artist) ? mediaProperties.AlbumTitle : mediaProperties.Artist,
                playbackInfo.PlaybackStatus,
                _currentSession.SourceAppUserModelId);

            TxtTitle.Text = string.IsNullOrWhiteSpace(title) ? "Media session detected" : title;
            TxtStatus.Text = subtitle;
            _isMediaPlaying = playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            BtnPrevious.IsEnabled = controls.IsRewindEnabled || controls.IsPreviousEnabled;
            BtnNext.IsEnabled = controls.IsNextEnabled;
            SetPlayPauseButtonState(
                controls.IsPlayEnabled || controls.IsPauseEnabled,
                _isMediaPlaying ? "⏸" : "▶",
                _isMediaPlaying ? "Pause" : "Play");
        }
        catch
        {
            _currentSession = null;
            ApplyAudioFallbackState();
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private static GlobalSystemMediaTransportControlsSession? SelectSession(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        try
        {
            var current = manager.GetCurrentSession();
            if (current is not null && IsUsableSession(current))
                return current;
        }
        catch { }

        try
        {
            var sessions = manager.GetSessions();
            GlobalSystemMediaTransportControlsSession? best = null;
            var bestScore = -1;

            foreach (var session in sessions)
            {
                try
                {
                    if (!IsUsableSession(session))
                        continue;

                    var score = GetSessionPriority(session);
                    if (IsBrowserSession(session))
                        score += 10;

                    if (score > bestScore)
                    {
                        best = session;
                        bestScore = score;
                    }
                }
                catch
                {
                    // Skip sessions that throw; don't abort the loop.
                }
            }

            return best;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsUsableSession(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var playbackInfo = session.GetPlaybackInfo();
            var controls = playbackInfo.Controls;
            return playbackInfo.PlaybackStatus is GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused
                or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened
                or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing
                || controls.IsPlayEnabled
                || controls.IsPauseEnabled
                || controls.IsNextEnabled
                || controls.IsPreviousEnabled
                || controls.IsRewindEnabled;
        }
        catch
        {
            return false;
        }
    }

    private static int GetSessionPriority(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var status = session.GetPlaybackInfo().PlaybackStatus;
            return status switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => 5,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => 4,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => 3,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => 2,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => 1,
                _ => 0
            };
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsBrowserSession(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var source = session.SourceAppUserModelId ?? string.Empty;
            return source.Contains("msedge", StringComparison.OrdinalIgnoreCase)
                || source.Contains("chrome", StringComparison.OrdinalIgnoreCase)
                || source.Contains("brave", StringComparison.OrdinalIgnoreCase)
                || source.Contains("firefox", StringComparison.OrdinalIgnoreCase)
                || source.Contains("opera", StringComparison.OrdinalIgnoreCase)
                || source.Contains("vivaldi", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string BuildSubtitle(string? artist, GlobalSystemMediaTransportControlsSessionPlaybackStatus playbackStatus, string sourceAppUserModelId)
    {
        var stateText = playbackStatus switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "Playing",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "Paused",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => "Stopped",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => "Opened",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => "Changing",
            _ => "Detected"
        };

        var source = ExtractSourceName(sourceAppUserModelId);
        if (!string.IsNullOrWhiteSpace(artist))
            return $"{artist} • {stateText}";

        return string.IsNullOrWhiteSpace(source)
            ? stateText
            : $"{source} • {stateText}";
    }

    private static string ExtractSourceName(string? sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
            return string.Empty;

        var segments = sourceAppUserModelId.Split('!');
        var rawName = segments[0];
        var lastSegment = rawName.Split('.').LastOrDefault();
        return string.IsNullOrWhiteSpace(lastSegment) ? rawName : lastSegment;
    }

    private void ApplyUnavailableState(string title, string status)
    {
        TxtTitle.Text = title;
        TxtStatus.Text = status;
        _isMediaPlaying = false;
        BtnPrevious.IsEnabled = false;
        BtnNext.IsEnabled = false;
        SetPlayPauseButtonState(true, "▶", "Play");
    }

    private void ApplyAudioFallbackState()
    {
        var snapshot = MediaPlaybackMonitor.GetSnapshot();
        if (!snapshot.IsPlaying)
        {
            ApplyUnavailableState("No media playing", "Start media in Edge, Chrome, or another app to use the controls.");
            return;
        }

        _currentSession = null;
        TxtTitle.Text = string.IsNullOrWhiteSpace(snapshot.DisplayName) ? "Media activity detected" : snapshot.DisplayName;
        TxtStatus.Text = $"Audio session detected • Peak {snapshot.PeakValue:P0}";
        _isMediaPlaying = snapshot.IsPlaying;
        BtnPrevious.IsEnabled = true;
        BtnNext.IsEnabled = true;
        SetPlayPauseButtonState(true, _isMediaPlaying ? "⏸" : "▶", _isMediaPlaying ? "Pause" : "Play");
    }

    private void SetPlayPauseButtonState(bool isEnabled, string content, string toolTip)
    {
        if (PlayPauseButton is not { } button)
            return;

        button.IsEnabled = isEnabled;
        button.Content = content;
        button.ToolTip = toolTip;
    }

    private void UpdateTaskbarPlacement()
    {
        var attached = ShellInterop.TryAttachToTaskbar(this);
        if (!ShellInterop.TryGetTaskbarBounds(out var taskbarBounds))
            return;

        var widthPx = ShellInterop.DipToPixel(Width);
        var heightPx = ShellInterop.DipToPixel(Height);
        const int horizontalPaddingPx = 8;
        const int trayReservePx = 320;
        const int verticalPaddingPx = 4;

        if (attached)
        {
            var relativeWidth = Math.Max(140, Math.Min(widthPx, taskbarBounds.Width - (horizontalPaddingPx * 2)));
            var relativeHeight = Math.Max(30, Math.Min(heightPx, taskbarBounds.Height - (verticalPaddingPx * 2)));
            var relativeY = Math.Max(verticalPaddingPx, (taskbarBounds.Height - relativeHeight) / 2);
            var relativeX = _customRelativeX
                ?? (taskbarBounds.Edge is DockEdge.Top or DockEdge.Bottom
                    ? Math.Max(horizontalPaddingPx, taskbarBounds.Width - relativeWidth - trayReservePx)
                    : Math.Max(horizontalPaddingPx, (taskbarBounds.Width - relativeWidth) / 2));

            relativeX = Math.Clamp(relativeX, horizontalPaddingPx,
                Math.Max(horizontalPaddingPx, taskbarBounds.Width - relativeWidth - horizontalPaddingPx));
            _lastRelativeX = relativeX;

            ShellInterop.PositionWindowRelativeToTaskbar(this, relativeX, relativeY, relativeWidth, relativeHeight);
            return;
        }

        var defaultOffsetX = Math.Max(horizontalPaddingPx, taskbarBounds.Width - widthPx - trayReservePx);
        var offsetX = _customRelativeX ?? defaultOffsetX;
        offsetX = Math.Clamp(offsetX, horizontalPaddingPx,
            Math.Max(horizontalPaddingPx, taskbarBounds.Width - widthPx - horizontalPaddingPx));
        _lastRelativeX = offsetX;

        var x = taskbarBounds.X + offsetX;
        var y = taskbarBounds.Y + Math.Max(verticalPaddingPx, (taskbarBounds.Height - heightPx) / 2);
        ShellInterop.PositionWindow(this, x, y, widthPx, heightPx);
    }

    private void RootBorder_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var pt = default(POINT);
        if (!GetCursorPos(ref pt))
            return;

        _dragStartCursorX = pt.X;
        _dragStartRelativeX = _lastRelativeX;
        _isDragging = true;
        RootBorder.CaptureMouse();
    }

    private void RootBorder_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging)
            return;

        var pt = default(POINT);
        if (!GetCursorPos(ref pt))
            return;

        _customRelativeX = _dragStartRelativeX + (pt.X - _dragStartCursorX);
        UpdateTaskbarPlacement();
    }

    private void RootBorder_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        RootBorder.ReleaseMouseCapture();

        if (_customRelativeX.HasValue)
        {
            _widgetSettings.Custom["TaskbarOffsetX"] = _customRelativeX.Value.ToString();
            _appSettings.Save();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private async Task ExecuteMediaCommandAsync(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command, byte fallbackVirtualKey)
    {
        var success = false;

        try
        {
            var session = _currentSession ?? _sessionManager?.GetCurrentSession();
            if (session is not null)
                success = await command(session);
        }
        catch
        {
            success = false;
        }

        if (!success)
            ShellInterop.SendMediaKey(fallbackVirtualKey);

        await RefreshWidgetAsync();
    }

    private async void BtnPrevious_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteMediaCommandAsync(async session =>
        {
            var controls = session.GetPlaybackInfo().Controls;
            if (controls.IsRewindEnabled)
                return await session.TryRewindAsync();

            if (controls.IsPreviousEnabled)
                return await session.TrySkipPreviousAsync();

            return false;
        }, VkMediaPrevTrack);
    }

    private async void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteMediaCommandAsync(async session =>
        {
            var toggled = await session.TryTogglePlayPauseAsync();
            if (toggled)
                return true;

            return _isMediaPlaying
                ? await session.TryPauseAsync()
                : await session.TryPlayAsync();
        }, VkMediaPlayPause);
    }

    private async void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteMediaCommandAsync(session => session.TrySkipNextAsync().AsTask(), VkMediaNextTrack);
    }

    private void MenuSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_appSettings, null, _widgetId, _widgetSettings, ApplyWidgetSettingsFromModel)
        {
            Owner = this
        };
        win.ShowDialog();
    }

    private void MenuClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
