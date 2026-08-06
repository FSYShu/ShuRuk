using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using ShuRuk.App.Helpers;
using ShuRuk.App.Pages;
using ShuRuk.App;
using ShuRuk.App.Services;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace ShuRuk.App;

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
        Title = AppConstants.AppName;
        var appWindow = AppWindow;

        try
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"MicaBackdrop not supported: {ex}"); }

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

        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"InitializeComponent failed: {ex}");
            var msg = ex.Message;
            if (ex.Data is not null)
            {
                foreach (System.Collections.DictionaryEntry de in ex.Data)
                    msg += $"\n  Data[{de.Key}] = {de.Value}";
            }
            for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
                msg += $"\n  Inner: {inner.GetType().Name}: {inner.Message}";
            throw new Exception($"MainWindow InitializeComponent failed:\n{msg}", ex);
        }
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
        ContentFrame.NavigationFailed += ContentFrame_NavigationFailed;
        var host = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children = { ContentFrame }
        };
        NavView.Content = host;

        _suppressNavigation = true;
        NavView.SelectedItem = NavView.MenuItems[0];
        _suppressNavigation = false;
        NavigateTo(AppConstants.NavTags.Home, pushHistory: false);
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

    private void ContentFrame_NavigationFailed(object sender, Microsoft.UI.Xaml.Navigation.NavigationFailedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"ContentFrame_NavigationFailed: {e.Exception}");
        e.Handled = true;
    }

    private bool _sizeInitialized;

    private void InitializeWindowSize()
    {
        _sizeInitialized = true;

        var appWindow = AppWindow;
        var hWnd = WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hWnd);
        var scale = dpi / AppConstants.DpiBase;

        var width = (int)(AppConstants.DefaultWindowWidth * scale);
        var height = (int)(AppConstants.DefaultWindowHeight * scale);

        SetWindowMinSize(AppConstants.MinWindowWidth, AppConstants.MinWindowHeight);

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
        DwmSetWindowAttribute(hWnd, AppConstants.DwmwaUseImmersiveDarkMode20, ref value, sizeof(int));
        DwmSetWindowAttribute(hWnd, AppConstants.DwmwaUseImmersiveDarkMode19, ref value, sizeof(int));

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

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_20 = AppConstants.DwmwaUseImmersiveDarkMode20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_19 = AppConstants.DwmwaUseImmersiveDarkMode19;

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
        if (++_layoutRetryCount > AppConstants.LayoutRetryLimit)
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
            const double buttonSize = AppConstants.TitleBarButtonSize;

            foreach (var button in UiTreeHelper.EnumerateVisualChildren<Button>(AppTitleBar))
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
                foreach (var border in UiTreeHelper.EnumerateVisualChildren<Border>(button))
                {
                    border.ClearValue(FrameworkElement.WidthProperty);
                    border.ClearValue(FrameworkElement.HeightProperty);
                    border.ClearValue(FrameworkElement.MinWidthProperty);
                    border.ClearValue(FrameworkElement.MinHeightProperty);
                }
            }

            var title = AppTitleBar.Title;
            var subtitle = AppTitleBar.Subtitle;
            foreach (var tb in UiTreeHelper.EnumerateVisualChildren<TextBlock>(AppTitleBar))
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
                AppConstants.NavTags.Home => LocalizationHelper.GetString("NavHome.Content"),
                AppConstants.NavTags.Modules => LocalizationHelper.GetString("NavModules.Content"),
                AppConstants.NavTags.Changelog => LocalizationHelper.GetString("NavChangelog.Content"),
                AppConstants.NavTags.Monitor => LocalizationHelper.GetString("NavMonitor.Content"),
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

        foreach (var item in UiTreeHelper.EnumerateVisualChildren<NavigationViewItem>(NavView))
        {
            if (ReferenceEquals(item, NavView.SettingsItem) ||
                item.Name == "SettingsNavViewItem" ||
                item.Name == "SettingsItem")
            {
                item.Content = settingsText;
            }
        }

        foreach (var button in UiTreeHelper.EnumerateVisualChildren<Button>(AppTitleBar))
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

        foreach (var button in UiTreeHelper.EnumerateVisualChildren<Button>(NavView))
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

    private bool _suppressNavigation;

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressNavigation) return;

        if (args.IsSettingsSelected)
        {
            NavigateTo(AppConstants.NavTags.Settings);
            return;
        }

        var container = args.SelectedItemContainer as NavigationViewItem;
        var tag = container?.Tag?.ToString();
        if (tag is not null)
            NavigateTo(tag);
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (_suppressNavigation) return;

        if (args.IsSettingsInvoked)
        {
            NavigateTo(AppConstants.NavTags.Settings);
            return;
        }

        var invokedTag = args.InvokedItemContainer?.Tag?.ToString();
        if (invokedTag is not null)
        {
            NavigateTo(invokedTag);
            return;
        }

        var selectedItem = sender.SelectedItem as NavigationViewItem;
        var fallbackTag = selectedItem?.Tag?.ToString();
        if (fallbackTag is not null)
            NavigateTo(fallbackTag);
    }

    public void NavigateTo(string tag, bool pushHistory = true)
    {
        Type? pageType = tag switch
        {
            AppConstants.NavTags.Home => typeof(HomePage),
            AppConstants.NavTags.Modules => typeof(ModulePage),
            AppConstants.NavTags.Changelog => typeof(ChangelogPage),
            AppConstants.NavTags.Monitor => typeof(MonitorPage),
            AppConstants.NavTags.Settings => typeof(SettingsPage),
            _ => null
        };
        if (pageType is null) return;
        if (ContentFrame.Content?.GetType() == pageType) return;

        if (pushHistory && _lastSidebarTag is not null && _lastSidebarTag != tag)
        {
            _history.Push(_lastSidebarTag);
        }

        // Sync NavigationView selected item / 同步导航栏选中项
        _suppressNavigation = true;
        try
        {
            if (tag == AppConstants.NavTags.Settings)
            {
                NavView.SelectedItem = NavView.SettingsItem;
            }
            else
            {
                var target = NavView.MenuItems.OfType<NavigationViewItem>()
                    .Concat(NavView.FooterMenuItems.OfType<NavigationViewItem>())
                    .FirstOrDefault(i => i.Tag?.ToString() == tag);
                if (target is not null && !ReferenceEquals(NavView.SelectedItem, target))
                    NavView.SelectedItem = target;
            }
        }
        finally
        {
            _suppressNavigation = false;
        }

        // Attempt Frame.Navigate; fallback to direct content assignment on failure
        // 尝试Frame.Navigate；失败时回退为直接赋值
        var transition = new SlideNavigationTransitionInfo
        {
            Effect = SlideNavigationTransitionEffect.FromBottom
        };

        try
        {
            var navigated = ContentFrame.Navigate(pageType, null, transition);
            if (!navigated)
            {
                System.Diagnostics.Debug.WriteLine($"NavigateTo: Frame.Navigate returned false for {tag}, falling back to direct content");
                if (Activator.CreateInstance(pageType) is Page pageInstance)
                    ContentFrame.Content = pageInstance;
            }
        }
        catch (Exception navEx)
        {
            System.Diagnostics.Debug.WriteLine($"NavigateTo: Frame.Navigate threw for {tag}: {navEx}");
            try
            {
                if (Activator.CreateInstance(pageType) is Page fallbackInstance)
                    ContentFrame.Content = fallbackInstance;
            }
            catch (Exception fbEx)
            {
                System.Diagnostics.Debug.WriteLine($"NavigateTo: fallback also failed for {tag}: {fbEx}");
            }
        }

        _lastSidebarTag = tag;
        AppTitleBar.IsBackButtonEnabled = _history.Count > 0;
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
        if (uMsg == AppConstants.WmGetMinMaxInfo)
        {
            var dpi = GetDpiForWindow(hWnd);
            var scale = dpi / AppConstants.DpiBase;
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