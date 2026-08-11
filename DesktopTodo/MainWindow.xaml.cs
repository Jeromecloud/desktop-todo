using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;
using Color = System.Windows.Media.Color;
using DragDropEffects = System.Windows.DragDropEffects;

namespace DesktopTodo;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private enum DockEdge { None, Left, Right, Top, Bottom }

    private readonly AppState _state = LocalStore.Load();
    private readonly ObservableCollection<TodoItem> _allItems = [];
    public ObservableCollection<TodoItem> VisibleItems { get; } = [];
    private string _filter = "Today";
    private bool _reallyExit;
    private readonly WinForms.NotifyIcon _tray;
    private readonly System.Drawing.Icon _appIcon;
    private System.Windows.Point _dragStart;
    private bool _isCompact;
    private bool _manuallyHidden;
    private bool _hiddenForFullscreen;
    private DockEdge _dockedEdge;
    private bool _forcedCompactByDock;
    private bool _forcedCompactByBorderToggle;
    private bool _isDraggingWindow;
    private double _preDockLeft;
    private double _preDockTop;
    private double _preDockWidth;
    private double _preDockHeight;
    private readonly DispatcherTimer _dockHideTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(220)
    };
    private int _transitionVersion;
    private readonly Dictionary<FrameworkElement, double> _expandedHeights = [];
    public Visibility ItemActionsVisibility => _isCompact ? Visibility.Collapsed : Visibility.Visible;
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _appIcon = CreateCircleIcon();
        Icon = Imaging.CreateBitmapSourceFromHIcon(
            _appIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
        foreach (var item in _state.Items.OrderBy(x => x.CreatedAt))
        {
            item.PropertyChanged += Item_PropertyChanged;
            _allItems.Add(item);
        }
        _tray = new WinForms.NotifyIcon
        {
            Icon = _appIcon,
            Text = "桌面提醒",
            Visible = true,
            ContextMenuStrip = new WinForms.ContextMenuStrip()
        };
        _tray.ContextMenuStrip.Items.Add("显示", null, (_, _) => ShowFromTray());
        _tray.ContextMenuStrip.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApp));
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        timer.Tick += Reminder_Tick;
        timer.Start();
        var fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        fullscreenTimer.Tick += FullscreenTimer_Tick;
        fullscreenTimer.Start();
        _dockHideTimer.Tick += DockHideTimer_Tick;
        Refresh();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Width = Math.Max(MinWidth, _state.Width);
        Height = Math.Max(MinHeight, _state.Height);
        if (!double.IsNaN(_state.Left) && !double.IsNaN(_state.Top))
        {
            Left = _state.Left; Top = _state.Top;
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = _state.IsTopmost;
        UpdatePin();
        UpdateCompactMode();
        UpdateDynamicMinimum();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WindowMessageHook);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsLoaded) UpdateCompactMode();
    }

    private void UpdateCompactMode()
    {
        var compact = _forcedCompactByDock || _forcedCompactByBorderToggle || (_isCompact
            ? ActualWidth < 350 || ActualHeight < Math.Max(460, MinHeight + 8)
            : ActualWidth < 330 || ActualHeight < 430);
        if (_isCompact == compact &&
            HeaderPanel.Visibility == (compact ? Visibility.Collapsed : Visibility.Visible)) return;

        _isCompact = compact;
        AnimateChrome(compact);
        AnimateMargin(WindowSurface, compact ? new Thickness(4) : new Thickness(10));
        AnimateMargin(TodoList, compact ? new Thickness(4) : new Thickness(10, 0, 10, 0));
        WindowSurface.CornerRadius = compact ? new CornerRadius(12) : new CornerRadius(16);
        ScrollViewer.SetVerticalScrollBarVisibility(
            TodoList, compact ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto);
        UpdateDynamicMinimum();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ItemActionsVisibility)));
    }

    private void UpdateDynamicMinimum()
    {
        MinWidth = 220;
        MinHeight = _isCompact
            ? Math.Max(90, VisibleItems.Count * 62 + 20)
            : 150;
    }

    private void AnimateChrome(bool compact)
    {
        var elements = new FrameworkElement[] { HeaderPanel, TitlePanel, ToolsPanel, AddButton };
        var version = ++_transitionVersion;
        var duration = new Duration(TimeSpan.FromMilliseconds(190));
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };

        if (compact)
        {
            foreach (var element in elements)
            {
                if (element.ActualHeight > 0) _expandedHeights[element] = element.ActualHeight;
                element.ClipToBounds = true;
                element.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(element.Opacity, 0, duration) { EasingFunction = easing });
                element.BeginAnimation(MaxHeightProperty,
                    new DoubleAnimation(Math.Max(0, element.ActualHeight), 0, duration)
                    { EasingFunction = easing });
            }

            var finish = new DispatcherTimer { Interval = duration.TimeSpan + TimeSpan.FromMilliseconds(15) };
            finish.Tick += (_, _) =>
            {
                finish.Stop();
                if (version != _transitionVersion || !_isCompact) return;
                foreach (var element in elements)
                {
                    element.BeginAnimation(OpacityProperty, null);
                    element.BeginAnimation(MaxHeightProperty, null);
                    element.Opacity = 1;
                    element.MaxHeight = double.PositiveInfinity;
                    element.Visibility = Visibility.Collapsed;
                }
            };
            finish.Start();
            return;
        }

        foreach (var element in elements)
        {
            element.Visibility = Visibility.Visible;
            var target = _expandedHeights.TryGetValue(element, out var height)
                ? height
                : element == HeaderPanel || element == AddButton ? 52 : element == TitlePanel ? 82 : 92;
            element.MaxHeight = target;
            element.Opacity = 1;
            element.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
            element.BeginAnimation(MaxHeightProperty,
                new DoubleAnimation(0, target, duration) { EasingFunction = easing });
        }

        var restore = new DispatcherTimer { Interval = duration.TimeSpan + TimeSpan.FromMilliseconds(15) };
        restore.Tick += (_, _) =>
        {
            restore.Stop();
            if (version != _transitionVersion || _isCompact) return;
            foreach (var element in elements)
            {
                element.BeginAnimation(OpacityProperty, null);
                element.BeginAnimation(MaxHeightProperty, null);
                element.Opacity = 1;
                element.MaxHeight = double.PositiveInfinity;
            }
        };
        restore.Start();
    }

    private static void AnimateMargin(FrameworkElement element, Thickness target)
    {
        var animation = new ThicknessAnimation(element.Margin, target,
            TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        element.BeginAnimation(MarginProperty, animation);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var item = new TodoItem { Title = "新提醒事项", CreatedAt = DateTime.Now };
        item.PropertyChanged += Item_PropertyChanged;
        _allItems.Add(item);
        _filter = "All";
        Refresh();
        TodoList.SelectedItem = item;
        TodoList.ScrollIntoView(item);
        Dispatcher.BeginInvoke(() =>
        {
            if (FindVisualChild<TextBox>(TodoList.ItemContainerGenerator.ContainerFromItem(item)) is { } box)
            { box.Focus(); box.SelectAll(); }
        });
        Save();
    }

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.DataContext is TodoItem item)
            item.IsCompleted = checkBox.IsChecked == true;
        Dispatcher.BeginInvoke(() => { Refresh(); Save(); }, DispatcherPriority.DataBind);
    }
    private void Title_LostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TodoItem item && string.IsNullOrWhiteSpace(item.Title))
            Remove(item);
        Save();
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TodoItem item) return;
        var menu = new ContextMenu();
        var today = new MenuItem { Header = "今天 18:00" };
        today.Click += (_, _) => { item.DueAt = DateTime.Today.AddHours(18); Save(); Refresh(); };
        var tomorrow = new MenuItem { Header = "明天 09:00" };
        tomorrow.Click += (_, _) => { item.DueAt = DateTime.Today.AddDays(1).AddHours(9); Save(); Refresh(); };
        var noDate = new MenuItem { Header = "清除时间" };
        noDate.Click += (_, _) => { item.DueAt = null; Save(); Refresh(); };
        var delete = new MenuItem { Header = "删除", Foreground = Brushes.Firebrick };
        delete.Click += (_, _) => Remove(item);
        menu.Items.Add(today); menu.Items.Add(tomorrow); menu.Items.Add(noDate);
        menu.Items.Add(new Separator()); menu.Items.Add(delete);
        menu.IsOpen = true;
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        _filter = (sender as FrameworkElement)?.Tag?.ToString() ?? "Today";
        PageTitle.Text = _filter switch { "All" => "全部", "Completed" => "已完成", _ => "今天" };
        Refresh();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (!IsInitialized) return;
        var query = _allItems.AsEnumerable();
        query = _filter switch
        {
            "Completed" => query.Where(x => x.IsCompleted),
            "Today" => query.Where(x => !x.IsCompleted && (x.DueAt == null || x.DueAt.Value.Date <= DateTime.Today)),
            _ => query
        };
        var search = SearchBox?.Text?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(x => x.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        VisibleItems.Clear();
        foreach (var item in query) VisibleItems.Add(item);
        CountText.Text = $"{VisibleItems.Count(x => !x.IsCompleted)} 项待完成";
        EmptyText.Visibility = VisibleItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateDynamicMinimum();
    }

    private void TodoList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_isCompact || e.Delta >= 0) return;
        EnsureDraftCard();
        e.Handled = true;
    }

    private void EnsureDraftCard()
    {
        var draft = _allItems.FirstOrDefault(x => x.IsDraft);
        if (draft == null)
        {
            draft = new TodoItem { Title = "", CreatedAt = DateTime.Now, IsDraft = true };
            draft.PropertyChanged += Item_PropertyChanged;
            _allItems.Add(draft);
            Refresh();
        }

        TodoList.SelectedItem = draft;
        TodoList.ScrollIntoView(draft);
        Dispatcher.BeginInvoke(() =>
        {
            if (FindVisualChild<TextBox>(TodoList.ItemContainerGenerator.ContainerFromItem(draft)) is { } box)
                box.Focus();
        }, DispatcherPriority.Input);
    }

    private void TodoList_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var emptyDrafts = _allItems.Where(x => x.IsDraft && string.IsNullOrWhiteSpace(x.Title)).ToList();
        if (emptyDrafts.Count > 0)
        {
            foreach (var draft in emptyDrafts) _allItems.Remove(draft);
            Refresh();
        }
        RestoreCompactMinimumHeight();
    }

    private void RestoreCompactMinimumHeight()
    {
        if (!_isCompact) return;
        UpdateDynamicMinimum();
        var target = MinHeight;
        if (Math.Abs(ActualHeight - target) < 1) return;

        var animation = new DoubleAnimation(ActualHeight, target,
            TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        animation.Completed += (_, _) =>
        {
            BeginAnimation(HeightProperty, null);
            Height = target;
        };
        BeginAnimation(HeightProperty, animation);
    }

    private void ClearCompleted_Click(object sender, RoutedEventArgs e)
    {
        var done = _allItems.Where(x => x.IsCompleted).ToList();
        if (done.Count == 0) return;
        if (MessageBox.Show($"确定清除 {done.Count} 条已完成事项吗？", "确认清除",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var item in done) _allItems.Remove(item);
        Refresh(); Save();
    }

    private void TodoList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Delete && TodoList.SelectedItem is TodoItem item) { Remove(item); e.Handled = true; }
        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { Add_Click(sender, e); e.Handled = true; }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Add_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.A && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Pin_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Remove(TodoItem item) { _allItems.Remove(item); Refresh(); Save(); }
    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is TodoItem { IsDraft: true } draft &&
            e.PropertyName == nameof(TodoItem.Title) &&
            !string.IsNullOrWhiteSpace(draft.Title))
        {
            draft.IsDraft = false;
            Save();
        }
        if (e.PropertyName == nameof(TodoItem.IsCompleted)) Refresh();
    }

    private void Reminder_Tick(object? sender, EventArgs e)
    {
        foreach (var item in _allItems.Where(x => !x.IsCompleted && !x.ReminderShown && x.DueAt <= DateTime.Now))
        {
            _tray.BalloonTipTitle = "提醒事项";
            _tray.BalloonTipText = item.Title;
            _tray.ShowBalloonTip(8000);
            item.ReminderShown = true;
        }
        Save();
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        if (Topmost && _dockedEdge != DockEdge.None)
            UndockFromEdge(true);
        UpdatePin();
        Save();
    }
    private void UpdatePin() => PinButton.Foreground = Topmost ? new SolidColorBrush(Color.FromRgb(22,119,255)) : Brushes.Gray;
    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        _manuallyHidden = true;
        Hide();
    }
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApp();
    private void ShowFromTray()
    {
        _manuallyHidden = false;
        _hiddenForFullscreen = false;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void ShowFromExternalLaunch() => ShowFromTray();
    private void ExitApp()
    {
        _reallyExit = true;
        Save();
        _tray.Visible = false;
        _tray.Dispose();
        _appIcon.Dispose();
        Close();
        System.Windows.Application.Current.Shutdown();
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_reallyExit)
        {
            e.Cancel = true;
            _manuallyHidden = true;
            Hide();
            Save();
        }
    }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        _isDraggingWindow = true;
        try { DragMove(); }
        finally
        {
            _isDraggingWindow = false;
            TryDockAfterDrag();
        }
    }

    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isCompact || e.ButtonState != MouseButtonState.Pressed) return;
        if (e.OriginalSource is DependencyObject source &&
            FindVisualParent<ListBoxItem>(source) is null)
        {
            _isDraggingWindow = true;
            try { DragMove(); }
            finally
            {
                _isDraggingWindow = false;
                TryDockAfterDrag();
            }
        }
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dockedEdge == DockEdge.None || Topmost) return;
        ExpandDockedWindow();
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // 展开状态保持不变；仅当指针进入其他应用窗口时才收回。
    }

    private void DockHideTimer_Tick(object? sender, EventArgs e)
    {
        if (_dockedEdge != DockEdge.None && _forcedCompactByDock &&
            !_isDraggingWindow && IsCursorOverOtherAppWindow())
        {
            _dockHideTimer.Stop();
            CollapseDockedWindow();
        }
    }

    private void TryDockAfterDrag()
    {
        if (Topmost) return;
        var edge = FindNearestDockEdge();
        if (edge != DockEdge.None)
            DockToEdge(edge);
        else if (_dockedEdge != DockEdge.None)
            UndockFromEdge(false);
    }

    private DockEdge FindNearestDockEdge()
    {
        var work = GetCurrentWorkArea();
        var candidates = new (DockEdge Edge, double Distance)[]
        {
            (DockEdge.Left, Math.Abs(Left - work.Left)),
            (DockEdge.Right, Math.Abs(work.Right - (Left + ActualWidth))),
            (DockEdge.Top, Math.Abs(Top - work.Top)),
            (DockEdge.Bottom, Math.Abs(work.Bottom - (Top + ActualHeight)))
        };
        var nearest = candidates.OrderBy(x => x.Distance).First();
        return nearest.Distance <= 24 ? nearest.Edge : DockEdge.None;
    }

    private void DockToEdge(DockEdge edge)
    {
        if (Topmost || edge == DockEdge.None) return;
        if (_dockedEdge == DockEdge.None)
        {
            _preDockLeft = Left;
            _preDockTop = Top;
            _preDockWidth = ActualWidth;
            _preDockHeight = ActualHeight;
        }
        _dockedEdge = edge;
        _forcedCompactByDock = false;
        _dockHideTimer.Stop();
        MoveToHiddenStrip(true);
    }

    private void ExpandDockedWindow()
    {
        if (_dockedEdge == DockEdge.None || _forcedCompactByDock) return;
        _forcedCompactByDock = true;
        Width = Math.Clamp(_preDockWidth, 250, 320);
        UpdateCompactMode();
        Height = MinHeight;
        _dockHideTimer.Start();

        var work = GetCurrentWorkArea();
        const double inset = 2;
        var targetLeft = _dockedEdge switch
        {
            DockEdge.Left => work.Left + inset,
            DockEdge.Right => work.Right - Width - inset,
            _ => Math.Clamp(_preDockLeft, work.Left, work.Right - Width)
        };
        var targetTop = _dockedEdge switch
        {
            DockEdge.Top => work.Top + inset,
            DockEdge.Bottom => work.Bottom - Height - inset,
            _ => Math.Clamp(_preDockTop, work.Top, work.Bottom - Height)
        };
        AnimateWindowPosition(targetLeft, targetTop, null);
    }

    private void CollapseDockedWindow()
    {
        if (_dockedEdge == DockEdge.None) return;
        _dockHideTimer.Stop();
        var (targetLeft, targetTop) = GetHiddenStripPosition();
        AnimateWindowPosition(targetLeft, targetTop, () =>
        {
            Width = _preDockWidth;
            Height = _preDockHeight;
            _forcedCompactByDock = false;
            UpdateCompactMode();
            MoveToHiddenStrip(false);
        });
    }

    private void MoveToHiddenStrip(bool animated)
    {
        if (_dockedEdge == DockEdge.None) return;
        var (targetLeft, targetTop) = GetHiddenStripPosition();
        if (animated)
            AnimateWindowPosition(targetLeft, targetTop, null);
        else
        {
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = targetLeft;
            Top = targetTop;
        }
    }

    private (double Left, double Top) GetHiddenStripPosition()
    {
        var work = GetCurrentWorkArea();
        const double visibleStrip = 15;
        var left = _dockedEdge switch
        {
            DockEdge.Left => work.Left - ActualWidth + visibleStrip,
            DockEdge.Right => work.Right - visibleStrip,
            _ => Math.Clamp(Left, work.Left, work.Right - ActualWidth)
        };
        var top = _dockedEdge switch
        {
            DockEdge.Top => work.Top - ActualHeight + visibleStrip,
            DockEdge.Bottom => work.Bottom - visibleStrip,
            _ => Math.Clamp(Top, work.Top, work.Bottom - ActualHeight)
        };
        return (left, top);
    }

    private void UndockFromEdge(bool restoreOriginalPosition)
    {
        if (_dockedEdge == DockEdge.None) return;
        _dockHideTimer.Stop();
        var currentLeft = Left;
        var currentTop = Top;
        _dockedEdge = DockEdge.None;
        _forcedCompactByDock = false;
        Width = _preDockWidth;
        Height = _preDockHeight;
        var work = GetCurrentWorkArea();
        Left = restoreOriginalPosition
            ? _preDockLeft
            : Math.Clamp(currentLeft, work.Left, work.Right - Width);
        Top = restoreOriginalPosition
            ? _preDockTop
            : Math.Clamp(currentTop, work.Top, work.Bottom - Height);
        UpdateCompactMode();
        Save();
    }

    private void AnimateWindowPosition(double targetLeft, double targetTop, Action? completed)
    {
        var duration = TimeSpan.FromMilliseconds(190);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var leftAnimation = new DoubleAnimation(Left, targetLeft, duration) { EasingFunction = easing };
        var topAnimation = new DoubleAnimation(Top, targetTop, duration) { EasingFunction = easing };
        topAnimation.Completed += (_, _) =>
        {
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = targetLeft;
            Top = targetTop;
            completed?.Invoke();
        };
        BeginAnimation(LeftProperty, leftAnimation);
        BeginAnimation(TopProperty, topAnimation);
    }

    private Rect GetCurrentWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            return SystemParameters.WorkArea;
        if (PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
            return SystemParameters.WorkArea;
        var fromDevice = target.TransformFromDevice;
        var topLeft = fromDevice.Transform(new System.Windows.Point(info.WorkArea.Left, info.WorkArea.Top));
        var bottomRight = fromDevice.Transform(new System.Windows.Point(info.WorkArea.Right, info.WorkArea.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private bool IsCursorOverOtherAppWindow()
    {
        if (!GetCursorPos(out var cursor)) return false;
        var window = GetAncestor(WindowFromPoint(cursor), 2);
        if (window == IntPtr.Zero) return false;

        GetWindowThreadProcessId(window, out var processId);
        if (processId == (uint)Environment.ProcessId) return false;

        var className = new System.Text.StringBuilder(64);
        GetClassName(window, className, className.Capacity);
        return className.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd");
    }

    private void Save()
    {
        _state.Items = _allItems.Where(x => !x.IsDraft).ToList();
        if (IsLoaded && _dockedEdge == DockEdge.None)
        {
            _state.Left = Left;
            _state.Top = Top;
            _state.Width = Width;
            _state.Height = Height;
        }
        _state.IsTopmost = Topmost;
        try { LocalStore.Save(_state); } catch { }
    }

    private void Item_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { _dragStart = e.GetPosition(null); return; }
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(pos.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance)
            if ((sender as FrameworkElement)?.DataContext is TodoItem item)
                DragDrop.DoDragDrop((DependencyObject)sender, item, DragDropEffects.Move);
    }
    private void Item_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(TodoItem)) || (sender as FrameworkElement)?.DataContext is not TodoItem target) return;
        var source = (TodoItem)e.Data.GetData(typeof(TodoItem));
        if (source == target) return;
        var targetIndex = _allItems.IndexOf(target);
        _allItems.Move(_allItems.IndexOf(source), targetIndex);
        Refresh(); Save();
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) return null;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            if (FindVisualChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T match) return match;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void ToggleDefaultCompactFromBorder()
    {
        const double defaultWidth = 410;
        const double defaultHeight = 650;
        var currentlyCompact = _isCompact || _forcedCompactByDock || _forcedCompactByBorderToggle;

        if (currentlyCompact)
        {
            if (_dockedEdge != DockEdge.None)
                UndockFromEdge(true);
            _forcedCompactByDock = false;
            _forcedCompactByBorderToggle = false;
            AnimateWindowSize(defaultWidth, defaultHeight, () =>
            {
                var work = GetCurrentWorkArea();
                Left = Math.Clamp(Left, work.Left, work.Right - Width);
                Top = Math.Clamp(Top, work.Top, work.Bottom - Height);
                UpdateCompactMode();
                Save();
            });
            return;
        }

        _forcedCompactByBorderToggle = true;
        UpdateCompactMode();
        var compactHeight = Math.Max(90, VisibleItems.Count * 62 + 20);
        AnimateWindowSize(300, compactHeight, () =>
        {
            _forcedCompactByBorderToggle = false;
            UpdateCompactMode();
            Save();
        });
    }

    private void AnimateWindowSize(double targetWidth, double targetHeight, Action? completed)
    {
        var duration = TimeSpan.FromMilliseconds(210);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var widthAnimation = new DoubleAnimation(ActualWidth, targetWidth, duration)
        {
            EasingFunction = easing
        };
        var heightAnimation = new DoubleAnimation(ActualHeight, targetHeight, duration)
        {
            EasingFunction = easing
        };
        heightAnimation.Completed += (_, _) =>
        {
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            Width = targetWidth;
            Height = targetHeight;
            completed?.Invoke();
        };
        BeginAnimation(WidthProperty, widthAnimation);
        BeginAnimation(HeightProperty, heightAnimation);
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmNcHitTest = 0x0084;
        const int WmNcLeftButtonDoubleClick = 0x00A3;
        if (message == WmNcLeftButtonDoubleClick)
        {
            var hit = wParam.ToInt32();
            if (hit is >= 10 and <= 17)
            {
                handled = true;
                Dispatcher.BeginInvoke(ToggleDefaultCompactFromBorder);
                return IntPtr.Zero;
            }
        }
        if (message != WmNcHitTest || WindowState != WindowState.Normal)
            return IntPtr.Zero;

        var packed = lParam.ToInt64();
        var screenPoint = new System.Windows.Point(
            unchecked((short)(packed & 0xFFFF)),
            unchecked((short)((packed >> 16) & 0xFFFF)));
        var point = PointFromScreen(screenPoint);
        const double edge = 12;
        var left = point.X <= edge;
        var right = point.X >= ActualWidth - edge;
        var top = point.Y <= edge;
        var bottom = point.Y >= ActualHeight - edge;

        var result = (left, right, top, bottom) switch
        {
            (true, _, true, _) => 13,   // HTTOPLEFT
            (_, true, true, _) => 14,   // HTTOPRIGHT
            (true, _, _, true) => 16,   // HTBOTTOMLEFT
            (_, true, _, true) => 17,   // HTBOTTOMRIGHT
            (true, _, _, _) => 10,      // HTLEFT
            (_, true, _, _) => 11,      // HTRIGHT
            (_, _, true, _) => 12,      // HTTOP
            (_, _, _, true) => 15,      // HTBOTTOM
            _ => 0
        };
        if (result == 0) return IntPtr.Zero;
        handled = true;
        return new IntPtr(result);
    }

    private void FullscreenTimer_Tick(object? sender, EventArgs e)
    {
        var fullscreen = IsAnotherAppFullscreen();
        if (fullscreen && IsVisible && !_manuallyHidden)
        {
            _hiddenForFullscreen = true;
            Hide();
        }
        else if (!fullscreen && _hiddenForFullscreen && !_manuallyHidden)
        {
            _hiddenForFullscreen = false;
            Show();
        }
    }

    private bool IsAnotherAppFullscreen()
    {
        var foreground = GetForegroundWindow();
        var ownHandle = new WindowInteropHelper(this).Handle;
        if (foreground == IntPtr.Zero || foreground == ownHandle || !IsWindowVisible(foreground))
            return false;

        var className = new System.Text.StringBuilder(64);
        GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd")
            return false;

        if (!GetWindowRect(foreground, out var windowRect))
            return false;
        var monitor = MonitorFromWindow(foreground, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            return false;

        const int tolerance = 2;
        return windowRect.Left <= info.Monitor.Left + tolerance &&
               windowRect.Top <= info.Monitor.Top + tolerance &&
               windowRect.Right >= info.Monitor.Right - tolerance &&
               windowRect.Bottom >= info.Monitor.Bottom - tolerance;
    }

    private static System.Drawing.Icon CreateCircleIcon()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(System.Drawing.Color.Transparent);
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(22, 119, 255));
        graphics.FillEllipse(brush, 3, 3, 26, 26);
        var handle = bitmap.GetHicon();
        try
        {
            return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr handle, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, System.Text.StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }
}
