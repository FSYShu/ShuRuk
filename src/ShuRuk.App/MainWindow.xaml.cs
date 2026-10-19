using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using ShuRuk.App.Views;
using ShuRuk.App;
using ShuRuk.App.Helpers;
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
        Title = "ShuRuk";
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
        NavigateTo("home", pushHistory: false);
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
        // Check inner detail page first / 先检查内嵌详情页返回
        if (ContentFrame.Content is ModulePage modulePage && modulePage.CanGoBackInner)
        {
            modulePage.GoBackInner();
            return;
        }

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
            NavigateTo("settings");
            return;
        }

        var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag?.ToString();
        System.Diagnostics.Debug.WriteLine($"NavView_SelectionChanged: tag={tag}");
        if (tag is not null)
            NavigateTo(tag);
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (_suppressNavigation) return;

        if (args.IsSettingsInvoked)
        {
            NavigateTo("settings");
            return;
        }

        var tag = args.InvokedItemContainer?.Tag?.ToString()
            ?? (sender.SelectedItem as NavigationViewItem)?.Tag?.ToString();
        System.Diagnostics.Debug.WriteLine($"NavView_ItemInvoked: tag={tag}");
        if (tag is not null)
            NavigateTo(tag);
    }

    public void NavigateTo(string tag, bool pushHistory = true)
    {
        System.Diagnostics.Debug.WriteLine($"NavigateTo: tag={tag}, current={ContentFrame.Content?.GetType().Name}");
        // Module/submodule navigation routes to host page / 模块/子模块导航路由到承载页
        Type? pageType = tag.StartsWith("module:", StringComparison.Ordinal) || tag.StartsWith("submodule:", StringComparison.Ordinal)
            ? typeof(ShuRuk.App.Views.ModuleHostPage)
            : tag switch
            {
                "home" => typeof(HomePage),
                "modules" => typeof(ModulePage),

                "changelog" => typeof(ChangelogPage),
                "monitor" => typeof(MonitorPage),
                "settings" => typeof(SettingsPage),
                _ => null
            };
        if (pageType is null) { System.Diagnostics.Debug.WriteLine($"NavigateTo: pageType is null for {tag}"); return; }
        if (ContentFrame.Content?.GetType() == pageType) { System.Diagnostics.Debug.WriteLine($"NavigateTo: already on {tag}"); return; }

        if (pushHistory && _lastSidebarTag is not null && _lastSidebarTag != tag)
        {
            _history.Push(_lastSidebarTag);
        }

        // Sync NavigationView selected item / 同步导航栏选中项
        _suppressNavigation = true;
        try
        {
            if (tag == "settings")
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
                ContentFrame.Content = (Page)Activator.CreateInstance(pageType)!;
            }
        }
        catch (Exception navEx)
        {
            System.Diagnostics.Debug.WriteLine($"NavigateTo: Frame.Navigate threw for {tag}: {navEx}");
            try
            {
                ContentFrame.Content = (Page)Activator.CreateInstance(pageType)!;
            }
            catch (Exception fbEx)
            {
                System.Diagnostics.Debug.WriteLine($"NavigateTo: fallback also failed for {tag}: {fbEx}");
                _ = ShowNavigationErrorAsync(tag, fbEx);
            }
        }

        _lastSidebarTag = tag;
        AppTitleBar.IsBackButtonEnabled = _history.Count > 0;
    }

    private async Task ShowNavigationErrorAsync(string tag, Exception ex)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = "导航失败",
                Content = $"无法导航到 {tag}:\n{ex}",
                CloseButtonText = "确定",
                XamlRoot = NavView.XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch { }
    }

    // Update back button enabled state from navigation history / 根据导航历史更新返回按钮状态
    public void UpdateBackButtonState()
    {
        AppTitleBar.IsBackButtonEnabled = _history.Count > 0;
    }

    // Handle shuruk:// protocol activation for module handover / 处理shuruk://协议唤起模块接管
    public void HandleProtocolActivation(Uri uri)
    {
        // Parse shuruk://module/{id} or shuruk://submodule/{id} / 解析协议参数
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return;
        var kind = segments[0].ToLowerInvariant();
        var id = segments[1];
        // Navigate to module page, select requested module / 跳转模块页并定位请求的模块
        NavigateTo("modules");
        System.Diagnostics.Debug.WriteLine($"HandleProtocolActivation: kind={kind}, id={id}");
    }

    // Populate installed modules as nav items with sub-module children / 填充已安装模块为导航条目，子模块为子条目
    public void PopulateModuleNavItems()
    {
        try
        {
            var manager = App.Services.GetRequiredService<ShuRuk.Runtime.Abstractions.Interfaces.IModuleManager>();
            var modules = manager.GetInstalledModules();

            // Remove existing dynamic module items / 移除已有的动态模块条目
            var toRemove = NavView.MenuItems.OfType<NavigationViewItem>()
                .Where(i => i.Tag?.ToString()?.StartsWith("module:", StringComparison.Ordinal) == true)
                .ToList();
            foreach (var item in toRemove)
                NavView.MenuItems.Remove(item);

            // Add module nav items / 添加模块导航条目
            foreach (var module in modules)
            {
                var moduleItem = new NavigationViewItem
                {
                    Content = module.DisplayName,
                    Tag = $"module:{module.ModuleId ?? module.Name}",
                    Icon = new FontIcon { Glyph = "\uE739" }
                };

                // Add sub-module children if present / 添加子模块子条目
                if (module.SubModules is { Count: > 0 })
                {
                    foreach (var sub in module.SubModules)
                    {
                        moduleItem.MenuItems.Add(new NavigationViewItem
                        {
                            Content = sub.Name,
                            Tag = $"submodule:{sub.ModuleId}",
                            Icon = new FontIcon { Glyph = sub.Light ? "\uE7A7" : "\uE739" }
                        });
                    }
                }

                NavView.MenuItems.Add(moduleItem);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PopulateModuleNavItems error: {ex}");
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