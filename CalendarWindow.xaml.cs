using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Date.Services;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaPoint = System.Windows.Point;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Date;

public partial class CalendarWindow : Window
{
    private readonly Window _anchorWindow;
    private readonly FrameworkElement _anchorPill;

    private bool _isDarkMode;
    private int _displayYear;
    private int _displayMonth;
    private DateTime _selectedDate;
    private bool _isClosing;

    // 预创建 42 个日期格控件复用，切换月份零 GC 压力、丝滑流畅
    private readonly DayCellView[] _cells = new DayCellView[42];

    public static long LastClosedTick { get; private set; }

    // ─── macOS 26 Dual-Rim Liquid Glass 日历卡片材质（全部冻结零开销） ───
    private static readonly LinearGradientBrush DarkCardBg;
    private static readonly LinearGradientBrush DarkCardBorder;
    private static readonly LinearGradientBrush DarkCardInnerRim;
    private static readonly LinearGradientBrush DarkCardSheen;
    private static readonly LinearGradientBrush DarkTodayCellBg;
    private static readonly SolidColorBrush DarkFg;
    private static readonly SolidColorBrush DarkSecondaryFg;
    private static readonly SolidColorBrush DarkMutedFg;
    private static readonly SolidColorBrush DarkWeekendFg;
    private static readonly SolidColorBrush DarkAccent;
    private static readonly SolidColorBrush DarkFestival;
    private static readonly SolidColorBrush DarkSolarTerm;
    private static readonly SolidColorBrush DarkHover;
    private static readonly SolidColorBrush DarkPressed;
    private static readonly SolidColorBrush DarkSeparator;
    private static readonly SolidColorBrush DarkTodayPillBg;
    private static readonly SolidColorBrush DarkTodayPillHover;
    private static readonly SolidColorBrush DarkSelectedBg;
    private static readonly SolidColorBrush DarkSelectedBorder;
    private static readonly SolidColorBrush DarkFooterBg;
    private static readonly SolidColorBrush DarkFooterBorder;
    private static readonly SolidColorBrush DarkBadgeBg;
    private static readonly SolidColorBrush DarkFestivalPillBg;

    private static readonly LinearGradientBrush LightCardBg;
    private static readonly LinearGradientBrush LightCardBorder;
    private static readonly LinearGradientBrush LightCardInnerRim;
    private static readonly LinearGradientBrush LightCardSheen;
    private static readonly LinearGradientBrush LightTodayCellBg;
    private static readonly SolidColorBrush LightFg;
    private static readonly SolidColorBrush LightSecondaryFg;
    private static readonly SolidColorBrush LightMutedFg;
    private static readonly SolidColorBrush LightWeekendFg;
    private static readonly SolidColorBrush LightAccent;
    private static readonly SolidColorBrush LightFestival;
    private static readonly SolidColorBrush LightSolarTerm;
    private static readonly SolidColorBrush LightHover;
    private static readonly SolidColorBrush LightPressed;
    private static readonly SolidColorBrush LightSeparator;
    private static readonly SolidColorBrush LightTodayPillBg;
    private static readonly SolidColorBrush LightTodayPillHover;
    private static readonly SolidColorBrush LightSelectedBg;
    private static readonly SolidColorBrush LightSelectedBorder;
    private static readonly SolidColorBrush LightFooterBg;
    private static readonly SolidColorBrush LightFooterBorder;
    private static readonly SolidColorBrush LightBadgeBg;
    private static readonly SolidColorBrush LightFestivalPillBg;

    private static readonly SolidColorBrush PureWhiteBrush;
    private static readonly SolidColorBrush TodaySubWhiteBrush;
    private static readonly SolidColorBrush TransparentBrush;

    static CalendarWindow()
    {
        PureWhiteBrush = FreezeBrush(MediaColor.FromRgb(0xFF, 0xFF, 0xFF));
        TodaySubWhiteBrush = FreezeBrush(MediaColor.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));
        TransparentBrush = FreezeBrush(MediaColor.FromArgb(0x00, 0x00, 0x00, 0x00));

        // ── 深色 Obsidian Dual-Rim Liquid Glass ──
        DarkCardBg = FreezeGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0xF2, 0x26, 0x27, 0x30), 0.0),
            (MediaColor.FromArgb(0xF6, 0x1A, 0x1B, 0x22), 1.0));
        DarkCardBorder = FreezeGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0x52, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 0.5),
            (MediaColor.FromArgb(0x34, 0xFF, 0xFF, 0xFF), 1.0));
        DarkCardInnerRim = FreezeGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.5),
            (MediaColor.FromArgb(0x0E, 0xFF, 0xFF, 0xFF), 1.0));
        DarkCardSheen = FreezeGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0));
        DarkTodayCellBg = FreezeGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromRgb(0x1E, 0x90, 0xFF), 0.0),
            (MediaColor.FromRgb(0x0A, 0x7A, 0xF5), 1.0));

        DarkFg = FreezeBrush(MediaColor.FromRgb(0xF5, 0xF5, 0xF7));
        DarkSecondaryFg = FreezeBrush(MediaColor.FromArgb(0xA6, 0xEB, 0xEB, 0xF5));
        DarkMutedFg = FreezeBrush(MediaColor.FromArgb(0x70, 0xEB, 0xEB, 0xF5));
        DarkWeekendFg = FreezeBrush(MediaColor.FromArgb(0xC8, 0xFF, 0x9B, 0x94));
        DarkAccent = FreezeBrush(MediaColor.FromRgb(0x0A, 0x84, 0xFF));
        DarkFestival = FreezeBrush(MediaColor.FromRgb(0xFF, 0x69, 0x61));
        DarkSolarTerm = FreezeBrush(MediaColor.FromRgb(0x47, 0xA3, 0xFF));
        DarkHover = FreezeBrush(MediaColor.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
        DarkPressed = FreezeBrush(MediaColor.FromArgb(0x2C, 0xFF, 0xFF, 0xFF));
        DarkSeparator = FreezeBrush(MediaColor.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        DarkTodayPillBg = FreezeBrush(MediaColor.FromArgb(0x26, 0x0A, 0x84, 0xFF));
        DarkTodayPillHover = FreezeBrush(MediaColor.FromArgb(0x3B, 0x0A, 0x84, 0xFF));
        DarkSelectedBg = FreezeBrush(MediaColor.FromArgb(0x22, 0x0A, 0x84, 0xFF));
        DarkSelectedBorder = FreezeBrush(MediaColor.FromArgb(0xB8, 0x0A, 0x84, 0xFF));
        DarkFooterBg = FreezeBrush(MediaColor.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        DarkFooterBorder = FreezeBrush(MediaColor.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
        DarkBadgeBg = FreezeBrush(MediaColor.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
        DarkFestivalPillBg = FreezeBrush(MediaColor.FromArgb(0x26, 0xFF, 0x69, 0x61));

        // ── 浅色 Crystal Dual-Rim Liquid Glass ──
        LightCardBg = FreezeGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0xF9, 0xFD, 0xFD, 0xFF), 0.0),
            (MediaColor.FromArgb(0xF4, 0xF2, 0xF4, 0xF9), 1.0));
        LightCardBorder = FreezeGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0x22, 0x00, 0x00, 0x00), 0.0),
            (MediaColor.FromArgb(0x28, 0x00, 0x00, 0x00), 0.5),
            (MediaColor.FromArgb(0x36, 0x00, 0x00, 0x00), 1.0));
        LightCardInnerRim = FreezeGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0xF2, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x5A, 0xFF, 0xFF, 0xFF), 0.5),
            (MediaColor.FromArgb(0x90, 0xFF, 0xFF, 0xFF), 1.0));
        LightCardSheen = FreezeGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromArgb(0x70, 0xFF, 0xFF, 0xFF), 0.0),
            (MediaColor.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0));
        LightTodayCellBg = FreezeGradient(
            new MediaPoint(0, 0), new MediaPoint(0, 1),
            (MediaColor.FromRgb(0x14, 0x86, 0xFF), 0.0),
            (MediaColor.FromRgb(0x00, 0x70, 0xF0), 1.0));

        LightFg = FreezeBrush(MediaColor.FromRgb(0x14, 0x14, 0x18));
        LightSecondaryFg = FreezeBrush(MediaColor.FromArgb(0xA6, 0x3C, 0x3C, 0x43));
        LightMutedFg = FreezeBrush(MediaColor.FromArgb(0x7A, 0x3C, 0x3C, 0x43));
        LightWeekendFg = FreezeBrush(MediaColor.FromArgb(0xC8, 0xD9, 0x38, 0x2E));
        LightAccent = FreezeBrush(MediaColor.FromRgb(0x00, 0x7A, 0xFF));
        LightFestival = FreezeBrush(MediaColor.FromRgb(0xE0, 0x31, 0x26));
        LightSolarTerm = FreezeBrush(MediaColor.FromRgb(0x00, 0x70, 0xE8));
        LightHover = FreezeBrush(MediaColor.FromArgb(0x0E, 0x00, 0x00, 0x00));
        LightPressed = FreezeBrush(MediaColor.FromArgb(0x18, 0x00, 0x00, 0x00));
        LightSeparator = FreezeBrush(MediaColor.FromArgb(0x14, 0x00, 0x00, 0x00));
        LightTodayPillBg = FreezeBrush(MediaColor.FromArgb(0x18, 0x00, 0x7A, 0xFF));
        LightTodayPillHover = FreezeBrush(MediaColor.FromArgb(0x28, 0x00, 0x7A, 0xFF));
        LightSelectedBg = FreezeBrush(MediaColor.FromArgb(0x14, 0x00, 0x7A, 0xFF));
        LightSelectedBorder = FreezeBrush(MediaColor.FromArgb(0xA8, 0x00, 0x7A, 0xFF));
        LightFooterBg = FreezeBrush(MediaColor.FromArgb(0x0A, 0x00, 0x00, 0x00));
        LightFooterBorder = FreezeBrush(MediaColor.FromArgb(0x14, 0x00, 0x00, 0x00));
        LightBadgeBg = FreezeBrush(MediaColor.FromArgb(0x0F, 0x00, 0x00, 0x00));
        LightFestivalPillBg = FreezeBrush(MediaColor.FromArgb(0x18, 0xE0, 0x31, 0x26));
    }

    private static SolidColorBrush FreezeBrush(MediaColor c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static LinearGradientBrush FreezeGradient(
        MediaPoint s,
        MediaPoint e,
        params (MediaColor Color, double Offset)[] stops)
    {
        var g = new LinearGradientBrush { StartPoint = s, EndPoint = e };
        foreach (var (c, o) in stops) g.GradientStops.Add(new GradientStop(c, o));
        g.Freeze();
        return g;
    }

    private sealed class DayCellView
    {
        public required Border Root { get; init; }
        public required TextBlock DayNumber { get; init; }
        public required TextBlock SubText { get; init; }
        public required Border MonthDot { get; init; }
        public DateTime Date { get; set; }
        public bool IsCurrentMonth { get; set; }
        public bool IsHovered { get; set; }
    }

    public CalendarWindow(Window anchorWindow, FrameworkElement anchorPill, bool isDarkMode)
    {
        _anchorWindow = anchorWindow;
        _anchorPill = anchorPill;
        _isDarkMode = isDarkMode;

        DateTime today = DateTime.Today;
        _displayYear = today.Year;
        _displayMonth = today.Month;
        _selectedDate = today;

        InitializeComponent();
        BuildDayCells();
        ApplyTheme(_isDarkMode);
        RenderMonth();

        ThemeService.ThemeChanged += OnThemeChanged;
        Loaded += CalendarWindow_Loaded;
        Deactivated += CalendarWindow_Deactivated;
        PreviewKeyDown += CalendarWindow_PreviewKeyDown;
        PreviewMouseWheel += CalendarWindow_PreviewMouseWheel;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // 标记为 WS_EX_TOOLWINDOW，绝不进入 Alt+Tab 窗口切换列表
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            int exStyle = TaskbarLocator.GetWindowLong(hwnd, TaskbarLocator.GWL_EXSTYLE);
            TaskbarLocator.SetWindowLong(hwnd, TaskbarLocator.GWL_EXSTYLE, exStyle | TaskbarLocator.WS_EX_TOOLWINDOW);
        }
    }

    private void CalendarWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateLayout();
        TaskbarLocator.PositionPopupAbovePill(this, CardContainer, _anchorWindow, _anchorPill, gapDip: 10.0);
        Activate();

        // macOS 26 柔和上浮淡入动效
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        CardTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10.0, 0.0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }

    private void CalendarWindow_Deactivated(object? sender, EventArgs e)
    {
        CloseSafely();
    }

    private void CalendarWindow_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseSafely();
        }
        else if (e.Key is Key.PageUp or Key.Left)
        {
            e.Handled = true;
            ShiftMonth(-1);
        }
        else if (e.Key is Key.PageDown or Key.Right)
        {
            e.Handled = true;
            ShiftMonth(1);
        }
        else if (e.Key == Key.Home)
        {
            e.Handled = true;
            JumpToToday();
        }
    }

    private void CalendarWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0) ShiftMonth(-1);
        else if (e.Delta < 0) ShiftMonth(1);
        e.Handled = true;
    }

    private void OnThemeChanged(bool isDarkMode)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_isClosing && IsLoaded)
            {
                ApplyTheme(isDarkMode);
                RenderMonth();
            }
        });
    }

    public void CloseSafely()
    {
        if (_isClosing) return;
        _isClosing = true;
        LastClosedTick = Environment.TickCount64;
        try { Close(); } catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        LastClosedTick = Environment.TickCount64;
        ThemeService.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }

    private void BuildDayCells()
    {
        DaysGrid.Children.Clear();

        for (int i = 0; i < 42; i++)
        {
            var dayNum = new TextBlock
            {
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Display, -apple-system, PingFang SC, Microsoft YaHei UI"),
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            Typography.SetNumeralAlignment(dayNum, FontNumeralAlignment.Tabular);

            var subText = new TextBlock
            {
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, -apple-system, PingFang SC, Microsoft YaHei UI"),
                FontSize = 9.5,
                Margin = new Thickness(0, 1, 0, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var monthDot = new Border
            {
                Width = 12,
                Height = 1.5,
                CornerRadius = new CornerRadius(1),
                Margin = new Thickness(0, 1.5, 0, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            stack.Children.Add(dayNum);
            stack.Children.Add(subText);
            stack.Children.Add(monthDot);

            var border = new Border
            {
                Height = 44,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(1.2),
                BorderBrush = TransparentBrush,
                Background = TransparentBrush,
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = stack
            };

            var cell = new DayCellView
            {
                Root = border,
                DayNumber = dayNum,
                SubText = subText,
                MonthDot = monthDot
            };

            border.MouseEnter += (_, _) =>
            {
                cell.IsHovered = true;
                RefreshCellVisual(cell, DateTime.Today);
            };
            border.MouseLeave += (_, _) =>
            {
                cell.IsHovered = false;
                RefreshCellVisual(cell, DateTime.Today);
            };
            border.MouseLeftButtonUp += (_, ev) =>
            {
                ev.Handled = true;
                SelectDate(cell.Date);
            };

            _cells[i] = cell;
            DaysGrid.Children.Add(border);
        }
    }

    private void ApplyTheme(bool isDarkMode)
    {
        _isDarkMode = isDarkMode;

        if (_isDarkMode)
        {
            CardContainer.Background = DarkCardBg;
            CardContainer.BorderBrush = DarkCardBorder;
            CardInnerRim.BorderBrush = DarkCardInnerRim;
            CardSheen.Background = DarkCardSheen;
            CardShadow.Color = MediaColor.FromRgb(0x00, 0x00, 0x00);
            CardShadow.Opacity = 0.46;

            var r = CardContainer.Resources;
            r["CalFgBrush"] = DarkFg;
            r["CalSecondaryFgBrush"] = DarkSecondaryFg;
            r["CalMutedFgBrush"] = DarkMutedFg;
            r["CalWeekendFgBrush"] = DarkWeekendFg;
            r["CalAccentBrush"] = DarkAccent;
            r["CalHoverBrush"] = DarkHover;
            r["CalPressedBrush"] = DarkPressed;
            r["CalSeparatorBrush"] = DarkSeparator;
            r["CalTodayPillBgBrush"] = DarkTodayPillBg;
            r["CalTodayPillHoverBrush"] = DarkTodayPillHover;
            r["CalFooterBgBrush"] = DarkFooterBg;
            r["CalFooterBorderBrush"] = DarkFooterBorder;
            r["CalBadgeBgBrush"] = DarkBadgeBg;
        }
        else
        {
            CardContainer.Background = LightCardBg;
            CardContainer.BorderBrush = LightCardBorder;
            CardInnerRim.BorderBrush = LightCardInnerRim;
            CardSheen.Background = LightCardSheen;
            CardShadow.Color = MediaColor.FromRgb(0x38, 0x42, 0x56);
            CardShadow.Opacity = 0.19;

            var r = CardContainer.Resources;
            r["CalFgBrush"] = LightFg;
            r["CalSecondaryFgBrush"] = LightSecondaryFg;
            r["CalMutedFgBrush"] = LightMutedFg;
            r["CalWeekendFgBrush"] = LightWeekendFg;
            r["CalAccentBrush"] = LightAccent;
            r["CalHoverBrush"] = LightHover;
            r["CalPressedBrush"] = LightPressed;
            r["CalSeparatorBrush"] = LightSeparator;
            r["CalTodayPillBgBrush"] = LightTodayPillBg;
            r["CalTodayPillHoverBrush"] = LightTodayPillHover;
            r["CalFooterBgBrush"] = LightFooterBg;
            r["CalFooterBorderBrush"] = LightFooterBorder;
            r["CalBadgeBgBrush"] = LightBadgeBg;
        }
    }

    private void RenderMonth()
    {
        DateTime today = DateTime.Today;
        TxtMonthTitle.Text = $"{_displayYear}年 {_displayMonth}月";
        TxtMonthSubtitle.Text = LunarCalendarService.GetMonthSubtitle(_displayYear, _displayMonth);

        bool isAtToday = _displayYear == today.Year && _displayMonth == today.Month && _selectedDate.Date == today;
        BtnToday.Opacity = isAtToday ? 0.52 : 1.0;

        // 计算周一为每周第一天对应的 42 格起始日期
        var firstOfMonth = new DateTime(_displayYear, _displayMonth, 1);
        int mondayOffset = ((int)firstOfMonth.DayOfWeek + 6) % 7;
        DateTime gridStart = firstOfMonth.AddDays(-mondayOffset);

        for (int i = 0; i < 42; i++)
        {
            DateTime d = gridStart.AddDays(i);
            var cell = _cells[i];
            cell.Date = d;
            cell.IsCurrentMonth = d.Year == _displayYear && d.Month == _displayMonth;
            RefreshCellVisual(cell, today);
        }

        UpdateFooter(today);
    }

    private void RefreshCellVisual(DayCellView cell, DateTime today)
    {
        var info = LunarCalendarService.GetDayInfo(cell.Date);
        bool isToday = cell.Date == today;
        bool isSelected = cell.Date == _selectedDate.Date;
        bool isWeekend = cell.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        cell.DayNumber.Text = cell.Date.Day.ToString();
        cell.SubText.Text = info.SubLabel;
        cell.Root.Opacity = cell.IsCurrentMonth ? 1.0 : 0.38;

        MediaBrush fg = _isDarkMode ? DarkFg : LightFg;
        MediaBrush muted = _isDarkMode ? DarkMutedFg : LightMutedFg;
        MediaBrush secondary = _isDarkMode ? DarkSecondaryFg : LightSecondaryFg;
        MediaBrush weekend = _isDarkMode ? DarkWeekendFg : LightWeekendFg;
        MediaBrush accent = _isDarkMode ? DarkAccent : LightAccent;
        MediaBrush festival = _isDarkMode ? DarkFestival : LightFestival;
        MediaBrush solarTerm = _isDarkMode ? DarkSolarTerm : LightSolarTerm;

        if (isToday)
        {
            cell.Root.Background = _isDarkMode ? DarkTodayCellBg : LightTodayCellBg;
            cell.Root.BorderBrush = TransparentBrush;
            cell.DayNumber.Foreground = PureWhiteBrush;
            cell.SubText.Foreground = TodaySubWhiteBrush;
            cell.SubText.FontWeight = info.AccentKind is DayAccentKind.Festival or DayAccentKind.SolarTerm
                ? FontWeights.SemiBold
                : FontWeights.Normal;
            cell.MonthDot.Visibility = Visibility.Collapsed;
            return;
        }

        if (isSelected)
        {
            cell.Root.Background = _isDarkMode ? DarkSelectedBg : LightSelectedBg;
            cell.Root.BorderBrush = _isDarkMode ? DarkSelectedBorder : LightSelectedBorder;
        }
        else if (cell.IsHovered)
        {
            cell.Root.Background = _isDarkMode ? DarkHover : LightHover;
            cell.Root.BorderBrush = TransparentBrush;
        }
        else
        {
            cell.Root.Background = TransparentBrush;
            cell.Root.BorderBrush = TransparentBrush;
        }

        cell.DayNumber.Foreground = isWeekend && cell.IsCurrentMonth ? weekend : fg;

        switch (info.AccentKind)
        {
            case DayAccentKind.Festival:
                cell.SubText.Foreground = festival;
                cell.SubText.FontWeight = FontWeights.SemiBold;
                cell.MonthDot.Visibility = Visibility.Collapsed;
                break;

            case DayAccentKind.SolarTerm:
                cell.SubText.Foreground = solarTerm;
                cell.SubText.FontWeight = FontWeights.SemiBold;
                cell.MonthDot.Visibility = Visibility.Collapsed;
                break;

            case DayAccentKind.LunarMonthFirst:
                cell.SubText.Foreground = secondary;
                cell.SubText.FontWeight = FontWeights.SemiBold;
                cell.MonthDot.Background = accent;
                cell.MonthDot.Visibility = Visibility.Visible;
                break;

            default:
                cell.SubText.Foreground = muted;
                cell.SubText.FontWeight = FontWeights.Normal;
                cell.MonthDot.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void UpdateFooter(DateTime today)
    {
        var info = LunarCalendarService.GetDayInfo(_selectedDate);
        TxtSelectedSolar.Text = $"{_selectedDate:yyyy年M月d日} · {_selectedDate:dddd}";
        TxtSelectedLunar.Text = string.IsNullOrEmpty(info.FullLunarText)
            ? _selectedDate.ToString("yyyy-MM-dd")
            : info.FullLunarText;

        TxtRelativeDay.Text = LunarCalendarService.GetRelativeDayText(_selectedDate, today);

        if (!string.IsNullOrEmpty(info.FestivalOrTermBadge))
        {
            TxtSpecialBadge.Text = info.FestivalOrTermBadge;
            bool isFestival = info.AccentKind == DayAccentKind.Festival;
            BadgeSpecial.Background = isFestival
                ? (_isDarkMode ? DarkFestivalPillBg : LightFestivalPillBg)
                : (_isDarkMode ? DarkTodayPillBg : LightTodayPillBg);
            TxtSpecialBadge.Foreground = isFestival
                ? (_isDarkMode ? DarkFestival : LightFestival)
                : (_isDarkMode ? DarkSolarTerm : LightSolarTerm);
            BadgeSpecial.Visibility = Visibility.Visible;
        }
        else
        {
            BadgeSpecial.Visibility = Visibility.Collapsed;
        }
    }

    private void SelectDate(DateTime date)
    {
        _selectedDate = date.Date;
        if (date.Year != _displayYear || date.Month != _displayMonth)
        {
            _displayYear = date.Year;
            _displayMonth = date.Month;
        }
        RenderMonth();
    }

    private void ShiftMonth(int delta)
    {
        var target = new DateTime(_displayYear, _displayMonth, 1).AddMonths(delta);
        if (target.Year is < 1902 or > 2099) return;
        _displayYear = target.Year;
        _displayMonth = target.Month;
        RenderMonth();
    }

    private void JumpToToday()
    {
        DateTime today = DateTime.Today;
        _displayYear = today.Year;
        _displayMonth = today.Month;
        _selectedDate = today;
        RenderMonth();
    }

    private void BtnPrevMonth_Click(object sender, RoutedEventArgs e) => ShiftMonth(-1);

    private void BtnNextMonth_Click(object sender, RoutedEventArgs e) => ShiftMonth(1);

    private void BtnToday_Click(object sender, RoutedEventArgs e) => JumpToToday();
}
