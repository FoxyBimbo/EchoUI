using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Orientation = System.Windows.Controls.Orientation;

namespace PrismPane_Widgets.Views;

public partial class WidgetGroupWindow : Window
{
    // ── Per-tab state ───────────────────────────────────────

    private sealed class GroupEntry
    {
        public required string WidgetId;
        public required Window OriginalWindow;
        public required UIElement Content;
        public required string DisplayName;
        public required Border TabButton;
        public required TextBlock TabLabel;
        public required Border TabIndicator;

        // Saved border chrome so it can be restored on detach
        public Thickness SavedBorderThickness;
        public CornerRadius SavedCornerRadius;
        public Thickness SavedMargin;
        public System.Windows.Media.Effects.Effect? SavedEffect;

        // Title element hidden while grouped
        public TextBlock? HiddenTitleElement;
        public Visibility SavedTitleVisibility;

        // Minimize button hidden while grouped
        public Button? HiddenMinimizeButton;

        // Custom color resources moved from the original window to the content
        public ResourceDictionary? MovedResources;
    }

    private readonly List<GroupEntry> _entries = [];
    private int _activeIndex = -1;

    // ── Minimize state ──────────────────────────────────────
    private bool _isMinimized;
    private double _expandedHeight;
    private readonly System.Windows.Threading.DispatcherTimer _minimizeTimer = new()
        { Interval = TimeSpan.FromSeconds(1) };

    // ── Tab drag-to-reorder state ───────────────────────────
    private Border? _tabDragBorder;
    private System.Windows.Point _tabDragStart;
    private bool _isTabDragging;

    /// <summary>Unique identifier for this group, used for persistence.</summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>The currently active tab index.</summary>
    public int ActiveTabIndex => _activeIndex;

    /// <summary>Raised when the group window is fully dissolved (all tabs removed or closed).</summary>
    public event Action<WidgetGroupWindow>? Dissolved;

    /// <summary>Raised when a single widget is detached back to a standalone window.</summary>
    public event Action<WidgetGroupWindow, string, Window>? TabDetached;

    /// <summary>Raised when position/size changes so the host can persist state.</summary>
    public event Action<WidgetGroupWindow>? GroupChanged;

    public WidgetGroupWindow()
    {
        InitializeComponent();
        LocationChanged += (_, _) => GroupChanged?.Invoke(this);
        SizeChanged += (_, _) => GroupChanged?.Invoke(this);
        MouseEnter += (_, _) => OnMouseEnterGroup();
        MouseLeave += (_, _) => OnMouseLeaveGroup();
        _minimizeTimer.Tick += (_, _) => { _minimizeTimer.Stop(); if (_isMinimized && !IsMouseOver) CollapseGroup(); };
    }

    // ── Public API ──────────────────────────────────────────

    /// <summary>Find the RootBorder inside widget content (Grid → Border).</summary>
    private static Border? FindRootBorder(UIElement content)
    {
        if (content is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Border border)
            return border;
        if (content is Border b)
            return b;
        return null;
    }

    private static void StripBorderChrome(GroupEntry entry)
    {
        var border = FindRootBorder(entry.Content);
        if (border is null) return;

        entry.SavedBorderThickness = border.BorderThickness;
        entry.SavedCornerRadius = border.CornerRadius;
        entry.SavedMargin = border.Margin;
        entry.SavedEffect = border.Effect;

        border.BorderThickness = new Thickness(0);
        border.CornerRadius = new CornerRadius(0);
        border.Margin = new Thickness(0);
        border.Effect = null;

        // Hide the widget title TextBlock — it's redundant with the tab label
        var titleBlock = FindTitleTextBlock(border);
        if (titleBlock is not null)
        {
            entry.HiddenTitleElement = titleBlock;
            entry.SavedTitleVisibility = titleBlock.Visibility;
            titleBlock.Visibility = Visibility.Collapsed;
        }

        // Hide the widget's own minimize button — the group has its own
        var minimizeBtn = FindMinimizeButton(border);
        if (minimizeBtn is not null)
        {
            entry.HiddenMinimizeButton = minimizeBtn;
            minimizeBtn.Visibility = Visibility.Collapsed;
        }

        // Preserve custom colors: move resource dictionaries from the original window
        // to the content element so DynamicResource lookups still resolve correctly.
        if (entry.Content is FrameworkElement fe &&
            entry.OriginalWindow.Resources.MergedDictionaries.Count > 0)
        {
            var dict = entry.OriginalWindow.Resources.MergedDictionaries[0];
            entry.MovedResources = dict;
            entry.OriginalWindow.Resources.MergedDictionaries.Remove(dict);
            fe.Resources.MergedDictionaries.Add(dict);
        }
    }

    private static void RestoreBorderChrome(GroupEntry entry)
    {
        var border = FindRootBorder(entry.Content);
        if (border is null) return;

        border.BorderThickness = entry.SavedBorderThickness;
        border.CornerRadius = entry.SavedCornerRadius;
        border.Margin = entry.SavedMargin;
        border.Effect = entry.SavedEffect;

        // Restore the title TextBlock
        if (entry.HiddenTitleElement is not null)
        {
            entry.HiddenTitleElement.Visibility = entry.SavedTitleVisibility;
            entry.HiddenTitleElement = null;
        }

        // Restore the minimize button
        if (entry.HiddenMinimizeButton is not null)
        {
            entry.HiddenMinimizeButton.Visibility = Visibility.Visible;
            entry.HiddenMinimizeButton = null;
        }

        // Move custom color resources back to the original window
        if (entry.MovedResources is not null && entry.Content is FrameworkElement fe)
        {
            fe.Resources.MergedDictionaries.Remove(entry.MovedResources);
            entry.OriginalWindow.Resources.MergedDictionaries.Add(entry.MovedResources);
            entry.MovedResources = null;
        }
    }

    /// <summary>
    /// Walk the visual tree to find the title TextBlock inside the widget's HeaderPanel.
    /// Convention: HeaderPanel (DockPanel) → last child TextBlock with FontWeight Bold
    /// or a named title element (TxtTitle, TxtFolderName, TxtFeedTitle, TxtPanelTitle).
    /// </summary>
    private static TextBlock? FindTitleTextBlock(Border rootBorder)
    {
        // RootBorder → Grid (InnerLayout) → first child should be the HeaderPanel DockPanel
        if (rootBorder.Child is not Grid innerGrid) return null;

        DockPanel? headerPanel = null;
        foreach (UIElement child in innerGrid.Children)
        {
            if (child is DockPanel dp)
            {
                headerPanel = dp;
                break;
            }
        }
        if (headerPanel is null) return null;

        // Find the title TextBlock — it's typically the last non-Button child in the DockPanel,
        // or a named TextBlock with Bold font weight.
        TextBlock? candidate = null;
        foreach (UIElement child in headerPanel.Children)
        {
            if (child is TextBlock tb && tb.FontWeight == FontWeights.Bold)
                candidate = tb;
            // Also check inside a Border wrapper (ShortcutPanelWidget wraps its title)
            if (child is Border b && b.Child is TextBlock inner && inner.FontWeight == FontWeights.Bold)
                candidate = inner;
        }
        return candidate;
    }

    /// <summary>Find the BtnMinimize button inside the widget's HeaderPanel DockPanel.</summary>
    private static Button? FindMinimizeButton(Border rootBorder)
    {
        if (rootBorder.Child is not Grid innerGrid) return null;

        DockPanel? headerPanel = null;
        foreach (UIElement child in innerGrid.Children)
        {
            if (child is DockPanel dp)
            {
                headerPanel = dp;
                break;
            }
        }
        if (headerPanel is null) return null;

        foreach (UIElement child in headerPanel.Children)
        {
            if (child is Button btn && btn.Content is string s && s is "—" or "▢")
                return btn;
        }
        return null;
    }

    /// <summary>
    /// Refresh custom color resources for a grouped widget after its settings changed.
    /// Call this after <c>ThemeHelper.ApplyToElement</c> runs on the original window.
    /// </summary>
    public void RefreshWidgetResources(string widgetId)
    {
        var entry = _entries.FirstOrDefault(e => e.WidgetId == widgetId);
        if (entry is null || entry.Content is not FrameworkElement fe) return;

        // Remove the old moved dictionary from the content element
        if (entry.MovedResources is not null)
        {
            fe.Resources.MergedDictionaries.Remove(entry.MovedResources);
            entry.MovedResources = null;
        }

        // If the apply callback added a new dictionary to the original window, move it
        if (entry.OriginalWindow.Resources.MergedDictionaries.Count > 0)
        {
            var dict = entry.OriginalWindow.Resources.MergedDictionaries[0];
            entry.MovedResources = dict;
            entry.OriginalWindow.Resources.MergedDictionaries.Remove(dict);
            fe.Resources.MergedDictionaries.Add(dict);
        }
        else
        {
            // Custom colors were removed — clear any leftover on the content
            fe.Resources.MergedDictionaries.Clear();
        }
    }

    /// <summary>Widget IDs currently in this group.</summary>
    public IReadOnlyList<string> GetWidgetIds() =>
        _entries.Select(e => e.WidgetId).ToList();

    /// <summary>Whether this group contains a specific widget.</summary>
    public bool ContainsWidget(string widgetId) =>
        _entries.Any(e => e.WidgetId == widgetId);

    /// <summary>
    /// Add a widget to this group. Its visual content is moved
    /// from the original <see cref="Window"/> into a tab.
    /// </summary>
    public void AddWidget(string widgetId, Window widgetWindow, string displayName)
    {
        // Steal content from the widget window
        if (widgetWindow.Content is not UIElement content) return;
        widgetWindow.Content = null;
        widgetWindow.Hide();

        // Build a tab header
        var label = new TextBlock
        {
            Text = displayName,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("MutedForegroundBrush")
        };

        var closeBtn = new Button
        {
            Content = "✕",
            FontSize = 10,
            Padding = new Thickness(2, 0, 0, 0),
            Margin = new Thickness(6, 0, 0, 0),
            Background = Brushes.Transparent,
            Foreground = (Brush)FindResource("MutedForegroundBrush"),
            BorderBrush = Brushes.Transparent,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0,
            MinHeight = 0,
            ToolTip = "Detach"
        };

        // Bottom accent line that shows on the active tab
        var indicator = new Border
        {
            Height = 2,
            CornerRadius = new CornerRadius(1),
            Background = Brushes.Transparent,
            Margin = new Thickness(4, 2, 4, 0)
        };

        var tabBorder = new Border
        {
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Padding = new Thickness(10, 6, 8, 0),
            Margin = new Thickness(0, 0, 2, 0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { label, closeBtn }
                    },
                    indicator
                }
            }
        };

        var index = _entries.Count;

        // ── Tab drag-to-reorder + click-to-switch ───────────
        tabBorder.MouseLeftButtonDown += (s, e) =>
        {
            var border = (Border)s;
            _tabDragBorder = border;
            _tabDragStart = e.GetPosition(TabStrip);
            _isTabDragging = false;
            border.CaptureMouse();
            e.Handled = true;
        };

        tabBorder.MouseMove += (s, e) =>
        {
            if (_tabDragBorder != s || e.LeftButton != MouseButtonState.Pressed)
                return;

            var pos = e.GetPosition(TabStrip);

            if (!_isTabDragging)
            {
                if (Math.Abs(pos.X - _tabDragStart.X) > SystemParameters.MinimumHorizontalDragDistance)
                {
                    _isTabDragging = true;
                    _tabDragBorder.Opacity = 0.55;
                }
                else
                    return;
            }

            ReorderDraggedTab(pos.X);
        };

        tabBorder.MouseLeftButtonUp += (s, e) =>
        {
            var border = (Border)s;
            border.ReleaseMouseCapture();

            if (_tabDragBorder == border)
            {
                if (_isTabDragging)
                {
                    border.Opacity = 1.0;
                }
                else
                {
                    // Simple click — switch to this tab
                    var idx = _entries.FindIndex(x => x.TabButton == border);
                    if (idx >= 0) SetActiveTab(idx);
                }
            }

            _tabDragBorder = null;
            _isTabDragging = false;
            e.Handled = true;
        };

        tabBorder.LostMouseCapture += (s, _) =>
        {
            if (_tabDragBorder == s)
            {
                ((Border)s).Opacity = 1.0;
                _tabDragBorder = null;
                _isTabDragging = false;
            }
        };

        closeBtn.Click += (_, _) =>
        {
            var idx = _entries.FindIndex(x => x.TabButton == tabBorder);
            if (idx >= 0) DetachTab(idx);
        };

        TabStrip.Children.Add(tabBorder);

        var entry = new GroupEntry
        {
            WidgetId = widgetId,
            OriginalWindow = widgetWindow,
            Content = content,
            DisplayName = displayName,
            TabButton = tabBorder,
            TabLabel = label,
            TabIndicator = indicator
        };

        StripBorderChrome(entry);
        _entries.Add(entry);

        SetActiveTab(_entries.Count - 1);
        ExpandWidthForTabs();

        // Deferred re-check in case the new tab hasn't been fully measured yet
        Dispatcher.InvokeAsync(ExpandWidthForTabs, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Switch to a specific tab by index (used when restoring saved state).</summary>
    public void ActivateTab(int index) => SetActiveTab(index);

    /// <summary>
    /// Detach a widget by ID for a drag operation. Restores content to the
    /// original window at the group's current position without showing it
    /// (the caller is expected to call <c>DragMove()</c> immediately after).
    /// Returns the detached window, or null if the widget was not found.
    /// </summary>
    public Window? DetachForDrag(string widgetId)
    {
        var index = _entries.FindIndex(e => e.WidgetId == widgetId);
        if (index < 0) return null;

        var entry = _entries[index];

        if (_activeIndex == index)
            ContentHost.Content = null;

        RestoreBorderChrome(entry);
        entry.OriginalWindow.Content = entry.Content;
        entry.OriginalWindow.Left = Left;
        entry.OriginalWindow.Top = Top;

        TabStrip.Children.Remove(entry.TabButton);
        _entries.RemoveAt(index);

        TabDetached?.Invoke(this, entry.WidgetId, entry.OriginalWindow);

        if (_entries.Count <= 1)
        {
            if (_entries.Count == 1)
            {
                var last = _entries[0];
                ContentHost.Content = null;
                RestoreBorderChrome(last);
                last.OriginalWindow.Content = last.Content;
                last.OriginalWindow.Left = Left;
                last.OriginalWindow.Top = Top;
                last.OriginalWindow.Width = Width;
                last.OriginalWindow.Height = Height;
                last.OriginalWindow.Show();
                TabStrip.Children.Remove(last.TabButton);
                TabDetached?.Invoke(this, last.WidgetId, last.OriginalWindow);
                _entries.Clear();
            }

            Dissolved?.Invoke(this);
            Close();
        }
        else
        {
            if (_activeIndex >= _entries.Count)
                SetActiveTab(_entries.Count - 1);
            else
                SetActiveTab(Math.Min(index, _entries.Count - 1));
        }

        return entry.OriginalWindow;
    }

    /// <summary>
    /// Detach a tab, restoring its content to the original window.
    /// </summary>
    public void DetachTab(int index)
    {
        if (index < 0 || index >= _entries.Count) return;

        var entry = _entries[index];

        // Clear content host if this was the active tab
        if (_activeIndex == index)
            ContentHost.Content = null;

        // Restore content to the original widget window, placed to the right of the group
        RestoreBorderChrome(entry);
        entry.OriginalWindow.Content = entry.Content;
        entry.OriginalWindow.Left = Left + ActualWidth;
        entry.OriginalWindow.Top = Top;
        entry.OriginalWindow.Show();

        // Remove tab UI
        TabStrip.Children.Remove(entry.TabButton);
        _entries.RemoveAt(index);

        TabDetached?.Invoke(this, entry.WidgetId, entry.OriginalWindow);

        // Auto-ungroup when one or fewer tabs remain
        if (_entries.Count <= 1)
        {
            if (_entries.Count == 1)
            {
                var last = _entries[0];
                ContentHost.Content = null;
                RestoreBorderChrome(last);
                last.OriginalWindow.Content = last.Content;
                last.OriginalWindow.Left = Left;
                last.OriginalWindow.Top = Top;
                last.OriginalWindow.Width = Width;
                last.OriginalWindow.Height = Height;
                last.OriginalWindow.Show();
                TabStrip.Children.Remove(last.TabButton);
                TabDetached?.Invoke(this, last.WidgetId, last.OriginalWindow);
                _entries.Clear();
            }

            Dissolved?.Invoke(this);
            Close();
            return;
        }

        // Re-activate a tab
        if (_activeIndex >= _entries.Count)
            SetActiveTab(_entries.Count - 1);
        else
            SetActiveTab(Math.Min(index, _entries.Count - 1));
    }

    /// <summary>Remove a widget by its ID (used when externally closing a grouped widget).</summary>
    public void RemoveWidget(string widgetId)
    {
        var index = _entries.FindIndex(e => e.WidgetId == widgetId);
        if (index < 0) return;

        var entry = _entries[index];

        if (_activeIndex == index)
            ContentHost.Content = null;

        RestoreBorderChrome(entry);
        entry.OriginalWindow.Content = entry.Content;

        TabStrip.Children.Remove(entry.TabButton);
        _entries.RemoveAt(index);

        if (_entries.Count <= 1)
        {
            if (_entries.Count == 1)
            {
                var last = _entries[0];
                ContentHost.Content = null;
                RestoreBorderChrome(last);
                last.OriginalWindow.Content = last.Content;
                last.OriginalWindow.Left = Left;
                last.OriginalWindow.Top = Top;
                last.OriginalWindow.Width = Width;
                last.OriginalWindow.Height = Height;
                last.OriginalWindow.Show();
                TabStrip.Children.Remove(last.TabButton);
                TabDetached?.Invoke(this, last.WidgetId, last.OriginalWindow);
                _entries.Clear();
            }

            Dissolved?.Invoke(this);
            Close();
            return;
        }

        if (_activeIndex >= _entries.Count)
            SetActiveTab(_entries.Count - 1);
        else
            SetActiveTab(Math.Min(index, _entries.Count - 1));
    }

    // ── Tab switching ───────────────────────────────────────

    /// <summary>Update the display name shown on a tab for the given widget.</summary>
    public void UpdateTabDisplayName(string widgetId, string displayName)
    {
        var entry = _entries.FirstOrDefault(e => e.WidgetId == widgetId);
        if (entry is null) return;
        entry.DisplayName = displayName;
        entry.TabLabel.Text = displayName;
    }

    // ── Tab drag-to-reorder ─────────────────────────────────

    private void ReorderDraggedTab(double mouseX)
    {
        if (_tabDragBorder is null) return;

        var dragIdx = _entries.FindIndex(x => x.TabButton == _tabDragBorder);
        if (dragIdx < 0) return;

        // Check left neighbor — swap if mouse crossed its center
        if (dragIdx > 0)
        {
            var leftTab = _entries[dragIdx - 1].TabButton;
            var leftPos = leftTab.TranslatePoint(new System.Windows.Point(0, 0), TabStrip);
            var leftCenter = leftPos.X + leftTab.ActualWidth / 2;
            if (mouseX < leftCenter)
            {
                MoveTab(dragIdx, dragIdx - 1);
                return;
            }
        }

        // Check right neighbor — swap if mouse crossed its center
        if (dragIdx < _entries.Count - 1)
        {
            var rightTab = _entries[dragIdx + 1].TabButton;
            var rightPos = rightTab.TranslatePoint(new System.Windows.Point(0, 0), TabStrip);
            var rightCenter = rightPos.X + rightTab.ActualWidth / 2;
            if (mouseX > rightCenter)
            {
                MoveTab(dragIdx, dragIdx + 1);
            }
        }
    }

    private void MoveTab(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex) return;

        var entry = _entries[fromIndex];
        _entries.RemoveAt(fromIndex);
        _entries.Insert(toIndex, entry);

        TabStrip.Children.Remove(entry.TabButton);
        TabStrip.Children.Insert(toIndex, entry.TabButton);

        // Keep _activeIndex tracking the same content
        if (_activeIndex == fromIndex)
            _activeIndex = toIndex;
        else if (fromIndex < toIndex && _activeIndex > fromIndex && _activeIndex <= toIndex)
            _activeIndex--;
        else if (fromIndex > toIndex && _activeIndex >= toIndex && _activeIndex < fromIndex)
            _activeIndex++;

        GroupChanged?.Invoke(this);
    }

    private void SetActiveTab(int index)
    {
        if (index < 0 || index >= _entries.Count) return;

        _activeIndex = index;
        ContentHost.Content = _entries[index].Content;

        var accent = (Brush)FindResource("AccentBrush");
        var fg = (Brush)FindResource("ForegroundBrush");
        var muted = (Brush)FindResource("MutedForegroundBrush");

        for (int i = 0; i < _entries.Count; i++)
        {
            bool active = i == index;
            _entries[i].TabLabel.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            _entries[i].TabLabel.Foreground = active ? fg : muted;
            _entries[i].TabButton.Background = active
                ? new SolidColorBrush(Color.FromArgb(20, 255, 255, 255))
                : Brushes.Transparent;
            _entries[i].TabIndicator.Background = active ? accent : Brushes.Transparent;
        }

        GroupChanged?.Invoke(this);
    }

    private void ExpandWidthForTabs()
    {
        // Force layout so ActualWidth values are current
        TabStrip.UpdateLayout();
        ButtonsPanel.UpdateLayout();

        // Measure fixed chrome: drag grip (28) + buttons + RootBorder margin/border
        var dragGripWidth = 28.0;
        var buttonsWidth = ButtonsPanel.ActualWidth > 0 ? ButtonsPanel.ActualWidth : 56;
        var rootMarginH = RootBorder.Margin.Left + RootBorder.Margin.Right;
        var rootBorderH = RootBorder.BorderThickness.Left + RootBorder.BorderThickness.Right;

        var neededWidth = TabStrip.ActualWidth + dragGripWidth + buttonsWidth + rootMarginH + rootBorderH + 8;
        if (neededWidth > Width)
            Width = neededWidth;
    }

    // ── Drag ────────────────────────────────────────────────

    private void TabArea_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // If the click landed on a tab or its children, let the tab handle it
        if (IsInsideTab(e.OriginalSource as DependencyObject))
            return;

        // Empty space in the tab area — drag the whole group
        e.Handled = true;
        DragMove();
        MainWindow.SnapManager.OnDragCompleted(this);
    }

    private bool IsInsideTab(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            // Reached the ScrollViewer / TabStrip boundary — not inside a tab
            if (current == TabScrollViewer || current == TabStrip)
                return false;

            // Check if this element is one of the tab borders
            if (current is Border border && _entries.Any(e => e.TabButton == border))
                return true;

            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
            MainWindow.SnapManager.OnDragCompleted(this);
            e.Handled = true;
        }
    }

    private void DragGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
            MainWindow.SnapManager.OnDragCompleted(this);
            e.Handled = true;
        }
    }

    // ── Minimize ─────────────────────────────────────────────

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        if (_isMinimized)
        {
            _isMinimized = false;
            ExpandGroup();
        }
        else
        {
            _expandedHeight = Height;
            _isMinimized = true;
            CollapseGroup();
        }

        BtnMinimize.Content = _isMinimized ? "▢" : "—";
        BtnMinimize.ToolTip = _isMinimized ? "Restore" : "Minimize";
    }

    private void OnMouseEnterGroup()
    {
        if (!_isMinimized) return;
        _minimizeTimer.Stop();
        ExpandGroup();
    }

    private void OnMouseLeaveGroup()
    {
        if (!_isMinimized) return;
        _minimizeTimer.Stop();
        _minimizeTimer.Start();
    }

    private void CollapseGroup()
    {
        ContentBorder.Visibility = Visibility.Collapsed;
        UpdateLayout();
        Height = GetMinimizedHeight();
    }

    private void ExpandGroup()
    {
        ContentBorder.Visibility = Visibility.Visible;
        if (_expandedHeight > 0)
            Height = _expandedHeight;
    }

    private double GetMinimizedHeight()
    {
        var rootMargin = RootBorder.Margin.Top + RootBorder.Margin.Bottom;
        var border = RootBorder.BorderThickness.Top + RootBorder.BorderThickness.Bottom;
        TabStrip.UpdateLayout();
        var tabStripHeight = TabStrip.ActualHeight > 0 ? TabStrip.ActualHeight : 30;
        // tab strip margin (6+2) + some padding
        return tabStripHeight + 8 + rootMargin + border + 8;
    }

    // ── Ungroup ─────────────────────────────────────────────

    private void BtnCloseGroup_Click(object sender, RoutedEventArgs e)
    {
        // Ungroup all widgets — restore each to a standalone window
        const double staggerOffset = 30;
        var entries = _entries.ToList();

        ContentHost.Content = null;

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            RestoreBorderChrome(entry);
            entry.OriginalWindow.Content = entry.Content;
            entry.OriginalWindow.Left = Left + (i * staggerOffset);
            entry.OriginalWindow.Top = Top + (i * staggerOffset);
            entry.OriginalWindow.Width = Width;
            entry.OriginalWindow.Height = Height;
            entry.OriginalWindow.Show();
            TabStrip.Children.Remove(entry.TabButton);
            TabDetached?.Invoke(this, entry.WidgetId, entry.OriginalWindow);
        }

        _entries.Clear();
        Dissolved?.Invoke(this);
        Close();
    }

}
