using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using ShuRuk.Host.Services;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace ShuRuk.Host;

public sealed partial class MainWindow : Window
{
    public string AppTitle => AppTitleBar.Title;
    public string AppSubtitleText => AppTitleBar.Subtitle;

    private Frame ContentFrame = null!;
    private string? _lastSidebarTag = null;
    private readonly Stack<string> _history = new();
    private int _layoutRetryCount = 0;

    public MainWindow()
    {
        Title = "ShuRuk";
        var appWindow = AppWindow;

        try
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        }
        catch { }

        ExtendsContentIntoTitleBar = true;

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            titleBar.BackgroundColor = Colors.Transparent;
            titleBar.InactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
            presenter.SetBorderAndTitleBar(true, true);
        }

        InitializeComponent();
        SetTitleBar(AppTitleBar);

        AppTitleBar.Loaded += AppTitleBar_Loaded;
        AppTitleBar.SizeChanged += AppTitleBar_SizeChanged;

        var languageService = App.Services.GetRequiredService<LanguageService>();
        languageService.LanguageChanged += OnLanguageChanged;

        var themeService = App.Services.GetRequiredService<ThemeService>();
        var initialTheme = themeService.ToElementTheme();
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = initialTheme;
        }

        RefreshNavItems();

        ContentFrame = new Frame();
        ContentFrame.HorizontalAlignment = HorizontalAlignment.Stretch;
        ContentFrame.VerticalAlignment = VerticalAlignment.Stretch;
        var host = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children = { ContentFrame }
        };
        NavView.Content = host;

        NavView.SelectedItem = NavView.MenuItems[0];
        LocalizationHelper.ApplyUidResources(NavView);

        NavView.Loaded += NavView_Loaded;

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
        {
            if (_sizeInitialized) return;
            InitializeWindowSize();
        });

        Activated += MainWindow_Activated;
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshNavItems();
        RefreshNavBuiltInItems();
    }

    private bool _sizeInitialized;

    private void InitializeWindowSize()
    {
        _sizeInitialized = true;

        var appWindow = AppWindow;
        var hWnd = WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hWnd);
        var scale = dpi / 96.0;

        var width = (int)(1200 * scale);
        var height = (int)(800 * scale);

        SetWindowMinSize(800, 600);

        var screenRect = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var x = (screenRect.Width - width) / 2;
        var y = (screenRect.Height - height) / 2;

        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
    }

    private bool _titleBarThemeApplied;

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (!_sizeInitialized)
        {
            InitializeWindowSize();
        }

        if (!_titleBarThemeApplied)
        {
            _titleBarThemeApplied = true;
            var themeService = App.Services.GetRequiredService<ThemeService>();
            ApplyTheme(themeService.ToElementTheme());
        }
    }

    public void ApplyTheme(ElementTheme theme)
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (Content is FrameworkElement r)
            {
                var isDark = r.ActualTheme == ElementTheme.Dark;
                ApplyTitleBarDarkMode(isDark);
            }
        });
    }

    private void ApplyTitleBarDarkMode(bool isDark)
    {
        var hWnd = WindowNative.GetWindowHandle(this);
        var value = isDark ? 1 : 0;
        DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE_20, ref value, sizeof(int));
        DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE_19, ref value, sizeof(int));

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = AppWindow.TitleBar;
            var fgColor = isDark ? Colors.White : Colors.Black;
            var hoverBg = isDark
                ? ColorHelper.FromArgb(40, 255, 255, 255)
                : ColorHelper.FromArgb(40, 0, 0, 0);
            var pressedBg = isDark
                ? ColorHelper.FromArgb(60, 255, 255, 255)
                : ColorHelper.FromArgb(60, 0, 0, 0);

            titleBar.ButtonForegroundColor = fgColor;
            titleBar.ButtonHoverForegroundColor = fgColor;
            titleBar.ButtonHoverBackgroundColor = hoverBg;
            titleBar.ButtonPressedForegroundColor = fgColor;
            titleBar.ButtonPressedBackgroundColor = pressedBg;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(150, fgColor.R, fgColor.G, fgColor.B);
        }
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_20 = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_19 = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    // ── Title bar template adjustments / 标题栏模板调整 ──────────

    private void AppTitleBar_Loaded(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(ApplyTitleBarTemplateOverrides);
        AppTitleBar.LayoutUpdated += AppTitleBar_LayoutUpdated;
    }

    private void AppTitleBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(ApplyTitleBarTemplateOverrides);
    }

    private void AppTitleBar_LayoutUpdated(object? sender, object e)
    {
        if (++_layoutRetryCount > 5)
        {
            AppTitleBar.LayoutUpdated -= AppTitleBar_LayoutUpdated;
            return;
        }
        ApplyTitleBarTemplateOverrides();
    }

    private void ApplyTitleBarTemplateOverrides()
    {
        try
        {
            const double buttonSize = 40;

            foreach (var button in EnumerateVisualChildren<Button>(AppTitleBar))
            {
                if (button.Name != "PART_BackButton" && button.Name != "PART_PaneToggleButton")
                    continue;

                button.Width = buttonSize;
                button.Height = buttonSize;
                button.MinWidth = buttonSize;
                button.MinHeight = buttonSize;
                button.HorizontalAlignment = HorizontalAlignment.Center;
                button.VerticalAlignment = VerticalAlignment.Center;
                button.Padding = new Thickness(0);

                button.ApplyTemplate();
                foreach (var border in EnumerateVisualChildren<Border>(button))
                {
                    border.ClearValue(FrameworkElement.WidthProperty);
                    border.ClearValue(FrameworkElement.HeightProperty);
                    border.ClearValue(FrameworkElement.MinWidthProperty);
                    border.ClearValue(FrameworkElement.MinHeightProperty);
                }
            }

            var title = AppTitleBar.Title;
            var subtitle = AppTitleBar.Subtitle;
            foreach (var tb in EnumerateVisualChildren<TextBlock>(AppTitleBar))
            {
                if (tb.Text == title || tb.Text == subtitle)
                    tb.ClearValue(FrameworkElement.MinWidthProperty);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ApplyTitleBarTemplateOverrides error: {ex}");
        }
    }


    private static IEnumerable<T> EnumerateVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var grandChild in EnumerateVisualChildren<T>(child)) yield return grandChild;
        }
    }

    // ── TitleBar events ───────────────────────────────────────────

    private void OnLanguageChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            RefreshNavItems();
            RefreshNavBuiltInItems();
            LocalizationHelper.ApplyUidResources(NavView);
        });
    }

    private void RefreshNavItems()
    {
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>().Concat(NavView.FooterMenuItems.OfType<NavigationViewItem>()))
        {
            item.Content = item.Tag?.ToString() switch
            {
                "home" => LocalizationHelper.GetString("NavHome.Content"),
                "modules" => LocalizationHelper.GetString("NavModules.Content"),
                "changelog" => LocalizationHelper.GetString("NavChangelog.Content"),
                "monitor" => LocalizationHelper.GetString("NavMonitor.Content"),
                _ => item.Content
            };
        }
    }

    private void RefreshNavBuiltInItems()
    {
        var settingsText = LocalizationHelper.GetString("String_NavSettings");
        var backText = LocalizationHelper.GetString("String_NavBack");
        var paneOpenText = LocalizationHelper.GetString("String_NavPaneOpen");
        var paneCloseText = LocalizationHelper.GetString("String_NavPaneClose");

        if (NavView.SettingsItem is NavigationViewItem settingsNavItem)
        {
            settingsNavItem.Content = settingsText;
        }

        foreach (var item in EnumerateVisualChildren<NavigationViewItem>(NavView))
        {
            if (ReferenceEquals(item, NavView.SettingsItem) ||
                item.Name == "SettingsNavViewItem" ||
                item.Name == "SettingsItem")
            {
                item.Content = settingsText;
            }
        }

        foreach (var button in EnumerateVisualChildren<Button>(AppTitleBar))
        {
            if (button.Name == "PART_BackButton")
            {
                ToolTipService.SetToolTip(button, backText);
            }
            else if (button.Name == "PART_PaneToggleButton")
            {
                ToolTipService.SetToolTip(button, NavView.IsPaneOpen ? paneCloseText : paneOpenText);
            }
        }

        foreach (var button in EnumerateVisualChildren<Button>(NavView))
        {
            if (button.Name == "BackButton")
            {
                ToolTipService.SetToolTip(button, backText);
            }
            else if (button.Name == "TogglePaneButton" ||
                     button.Name == "PaneToggleButton")
            {
                ToolTipService.SetToolTip(button, NavView.IsPaneOpen ? paneCloseText : paneOpenText);
            }
        }
    }

    private void ReNavigateCurrentPage()
    {
        if (ContentFrame.Content is not Page currentPage) return;
        var pageType = currentPage.GetType();
        ContentFrame.Navigate(pageType);
        ContentFrame.BackStack.Clear();
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
        RefreshNavBuiltInItems();
    }

    private void AppTitleBar_BackRequested(TitleBar sender, object args)
    {
        if (_history.Count == 0) return;
        var previousTag = _history.Pop();
        NavigateTo(previousTag, pushHistory: false);
    }

    // ── NavigationView events ─────────────────────────────────────

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString();
        if (tag is null) return;
        NavigateTo(tag);
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            NavigateTo("settings");
            return;
        }

        var tag = args.InvokedItemContainer?.Tag?.ToString();
        if (tag is null) return;
        NavigateTo(tag);
    }

    private void NavItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is NavigationViewItem item && item.Tag is string tag)
        {
            NavigateTo(tag);
        }
    }

    public void NavigateTo(string tag, bool pushHistory = true)
    {
        try
        {
            Type? pageType = tag switch
            {
                "home" => typeof(Pages.HomePage),
                "modules" => typeof(Pages.ModulePage),
                "changelog" => typeof(Pages.ChangelogPage),
                "monitor" => typeof(Pages.MonitorPage),
                "settings" => typeof(Pages.SettingsPage),
                _ => null
            };
            if (pageType is null) return;
            if (ContentFrame.Content?.GetType() == pageType) return;

            if (pushHistory && _lastSidebarTag is not null && _lastSidebarTag != tag)
            {
                _history.Push(_lastSidebarTag);
            }

            // Slide navigation transition: vertical slide from bottom.
            var transition = new SlideNavigationTransitionInfo
            {
                Effect = SlideNavigationTransitionEffect.FromBottom
            };

            ContentFrame.Navigate(pageType, null, transition);
            _lastSidebarTag = tag;
            AppTitleBar.IsBackButtonEnabled = _history.Count > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NavigateTo error: {ex}");
        }
    }

    // ── Window sizing / subclassing ───────────────────────────────

    private int _minWidth = 800;
    private int _minHeight = 600;
    private IntPtr _hWnd;
    private SUBCLASSPROC? _subclassProc;
    private IntPtr _subclassId;

    private void SetWindowMinSize(int minWidth, int minHeight)
    {
        _minWidth = minWidth;
        _minHeight = minHeight;
        _hWnd = WindowNative.GetWindowHandle(this);
        _subclassProc = SubclassWndProc;
        SetWindowSubclass(_hWnd, _subclassProc, IntPtr.Zero, out _subclassId);
    }

    private IntPtr SubclassWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        const uint WM_GETMINMAXINFO = 0x0024;

        if (uMsg == WM_GETMINMAXINFO)
        {
            var dpi = GetDpiForWindow(hWnd);
            var scale = dpi / 96.0;
            var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            info.ptMinTrackSize.x = (int)(_minWidth * scale);
            info.ptMinTrackSize.y = (int)(_minHeight * scale);
            Marshal.StructureToPtr(info, lParam, false);
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass, out IntPtr pdwRefData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);
}