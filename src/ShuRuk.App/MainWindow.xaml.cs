using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
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
using ShuRuk.Runtime.Abstractions.Interfaces;

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



    // Initialize main window: backdrop, title bar, nav, theme / 初始化主窗口:背景、标题栏、导航、主题
    public MainWindow()
    {
        Title = "ShuRuk";
        var appWindow = AppWindow;

        try
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop
            {
                Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base
            };
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
            // Listen for system theme changes to update title bar / 监听系统主题变化更新标题栏
            root.ActualThemeChanged += (_, _) =>
            {
                var isDark = root.ActualTheme == ElementTheme.Dark;
                ApplyTitleBarDarkMode(isDark);
            };
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
        if (NavView.MenuItems.Count > 0)
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
        SizeChanged += MainWindow_SizeChanged;
    }

    // Invalidate layout on resize to refresh MicaBackdrop without rebuilding / 缩放时仅刷新布局,不重建MicaBackdrop对象避免闪烁
    private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs e)
    {
        if (Content is not FrameworkElement root) return;
        root.InvalidateMeasure();
        root.InvalidateArrange();
    }

    // Refresh built-in nav items when NavigationView finishes loading / 导航视图加载完成时刷新内置导航项
    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshNavBuiltInItems();
    }

    // Log navigation failures and mark as handled / 记录导航失败并标记为已处理
    private void ContentFrame_NavigationFailed(object sender, Microsoft.UI.Xaml.Navigation.NavigationFailedEventArgs e)
    {
        CrashLogger.LogCrash("ContentFrame_NavigationFailed", e.Exception);
        e.Handled = true;
    }

    private bool _sizeInitialized;

    // Compute DPI-scaled size and center window on screen / 计算DPI缩放尺寸并居中窗口
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

    // First activation: init size and apply theme / 首次激活:初始化尺寸并应用主题
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

    // Apply theme to root element and reload MonitorPage / 应用主题到根元素并重载监控页
    public void ApplyTheme(ElementTheme theme)
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }

        // Apply title bar dark mode and reload MonitorPage in one dispatch / 一次调度应用标题栏暗色模式并重载监控页
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (Content is FrameworkElement r)
            {
                var isDark = r.ActualTheme == ElementTheme.Dark;
                ApplyTitleBarDarkMode(isDark);
            }

            // Reload MonitorPage on theme change to refresh alt rows / 主题切换时重载监控页刷新交错行
            if (ContentFrame.Content is MonitorPage)
            {
                ContentFrame.Navigate(typeof(MonitorPage));
                ContentFrame.BackStack.Clear();
            }
        });
    }

    // Cache IsCustomizationSupported result (constant per session) / 缓存标题栏自定义支持结果(会话内不变)
    private static readonly bool _titleBarCustomizationSupported = AppWindowTitleBar.IsCustomizationSupported();

    // Apply DWM dark mode and title bar button colors / 应用DWM暗色模式及标题栏按钮颜色
    private void ApplyTitleBarDarkMode(bool isDark)
    {
        var hWnd = WindowNative.GetWindowHandle(this);
        var value = isDark ? 1 : 0;
        DwmSetWindowAttribute(hWnd, AppConstants.DwmwaUseImmersiveDarkMode20, ref value, sizeof(int));
        DwmSetWindowAttribute(hWnd, AppConstants.DwmwaUseImmersiveDarkMode19, ref value, sizeof(int));

        if (_titleBarCustomizationSupported)
        {
            var titleBar = AppWindow.TitleBar;
            var fgColor = isDark ? Colors.White : Colors.Black;
            var hoverBg = isDark
                ? ColorHelper.FromArgb(AppConstants.TitleBarHoverBgAlpha, 255, 255, 255)
                : ColorHelper.FromArgb(AppConstants.TitleBarHoverBgAlpha, 0, 0, 0);
            var pressedBg = isDark
                ? ColorHelper.FromArgb(AppConstants.TitleBarPressedBgAlpha, 255, 255, 255)
                : ColorHelper.FromArgb(AppConstants.TitleBarPressedBgAlpha, 0, 0, 0);

            titleBar.ButtonForegroundColor = fgColor;
            titleBar.ButtonHoverForegroundColor = fgColor;
            titleBar.ButtonHoverBackgroundColor = hoverBg;
            titleBar.ButtonPressedForegroundColor = fgColor;
            titleBar.ButtonPressedBackgroundColor = pressedBg;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(AppConstants.TitleBarInactiveFgAlpha, fgColor.R, fgColor.G, fgColor.B);
        }
    }


    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    // ── Title bar template adjustments / 标题栏模板调整 ──────────

    // TitleBar loaded: apply overrides and track layout updates / 标题栏加载:应用覆盖并跟踪布局更新
    private void AppTitleBar_Loaded(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(ApplyTitleBarTemplateOverrides);
        AppTitleBar.LayoutUpdated += AppTitleBar_LayoutUpdated;
    }

    // TitleBar size changed: re-apply template overrides / 标题栏尺寸变化:重新应用模板覆盖
    private void AppTitleBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(ApplyTitleBarTemplateOverrides);
    }

    private void AppTitleBar_LayoutUpdated(object? sender, object e)
    {
        // LayoutUpdated fires repeatedly until layout stabilizes; apply once then detach
        // LayoutUpdated 在布局稳定前会反复触发,应用一次后取消订阅
        AppTitleBar.LayoutUpdated -= AppTitleBar_LayoutUpdated;
        try { ApplyTitleBarTemplateOverrides(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"AppTitleBar_LayoutUpdated: {ex}"); }
    }


    // Resize back/pane toggle buttons and clear text min-widths / 调整返回/面板按钮尺寸并清除文本最小宽度
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

                // Clear pressed background on disabled back button's RootGrid / 清除禁用返回键RootGrid的按下背景
                if (button.Name == "PART_BackButton" && !button.IsEnabled)
                {
                    foreach (var grid in UiTreeHelper.EnumerateVisualChildren<Grid>(button))
                    {
                        if (grid.Name == "RootGrid")
                            grid.Background = new SolidColorBrush(Colors.Transparent);
                    }
                }

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

    // Language changed: refresh nav items and re-apply resources / 语言变更:刷新导航项并重新应用资源
    private void OnLanguageChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            RefreshNavItems();
            RefreshNavBuiltInItems();
            LocalizationHelper.ApplyUidResources(NavView);
        });
    }

    // Update localized content for nav menu items / 更新导航菜单项的本地化内容
    private void RefreshNavItems()
    {
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>().Concat(NavView.FooterMenuItems.OfType<NavigationViewItem>()))
        {
            item.Content = item.Tag?.ToString() switch
            {
                var t when t == AppConstants.NavTags.Home => LocalizationHelper.GetString("NavHome.Content"),
                var t when t == AppConstants.NavTags.Modules => LocalizationHelper.GetString("NavModules.Content"),
                var t when t == AppConstants.NavTags.Changelog => LocalizationHelper.GetString("NavChangelog.Content"),
                var t when t == AppConstants.NavTags.Monitor => LocalizationHelper.GetString("NavMonitor.Content"),
                _ => item.Content
            };
        }
    }

    // Update settings item and back/pane button tooltips / 更新设置项及返回/面板按钮提示
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


    // Toggle nav pane open/close from title bar / 从标题栏切换导航面板开/关
    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
        RefreshNavBuiltInItems();
    }

    // Back requested: inner detail first, then history stack / 返回请求:优先内嵌详情,再历史栈
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

    // Nav selection changed: navigate to selected tag / 导航选择变更:跳转到选中标签
    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressNavigation) return;

        if (args.IsSettingsSelected)
        {
            NavigateTo("settings");
            return;
        }

        var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag?.ToString();

        if (tag is not null)
            NavigateTo(tag);
    }

    // Nav item invoked: navigate to invoked tag / 导航项调用:跳转到调用标签
    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (_suppressNavigation) return;

        if (args.IsSettingsInvoked)
        {
            NavigateTo("settings");
            return;
        }

        var tag = args.InvokedItemContainer?.Tag?.ToString();
        if (tag is not null)
            NavigateTo(tag);
    }


    // Navigate to page by tag with history and error fallback / 按标签导航页面,含历史记录与错误回退
    public void NavigateTo(string tag, bool pushHistory = true, object? parameter = null)
    {

        // Module/submodule navigation routes to host page / 模块/子模块导航路由到承载页
        Type? pageType = tag.StartsWith("module:", StringComparison.Ordinal) || tag.StartsWith("submodule:", StringComparison.Ordinal)
            ? typeof(ModulePage)
            : tag switch
            {
                "home" => typeof(ShuRuk.Runtime.Views.HomePage),
                "modules" => typeof(ModulePage),

                "changelog" => typeof(ShuRuk.Runtime.Views.ChangelogPage),
                "monitor" => typeof(MonitorPage),
                "settings" => typeof(ShuRuk.Runtime.Views.SettingsPage),
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

        // Auto-construct changelog parameter from test mode config / 从测试模式配置自动构造更新日志参数
        if (tag == "changelog" && parameter is null)
        {
            var testMode = App.Services.GetService<ITestModeService>();
            if (testMode is not null)
            {
                parameter = new ShuRuk.Runtime.Views.ChangelogPageParameter
                {
                    RepoUrl = testMode.ChangelogRepoUrl,
                    SectionFilter = string.IsNullOrEmpty(testMode.ChangelogSectionFilter) ? null : testMode.ChangelogSectionFilter
                };
            }
        }

        // Attempt Frame.Navigate; fallback to direct content assignment on failure
        // 尝试Frame.Navigate；失败时回退为直接赋值
        var transition = new SlideNavigationTransitionInfo
        {
            Effect = SlideNavigationTransitionEffect.FromBottom
        };

        try
        {
            var navigated = ContentFrame.Navigate(pageType, parameter, transition);
            if (!navigated)
            {

                ContentFrame.Content = (Page)Activator.CreateInstance(pageType)!;
            }
        }
        catch (Exception)
        {
            try
            {
                ContentFrame.Content = (Page)Activator.CreateInstance(pageType)!;
            }
            catch (Exception fbEx)
            {
                _ = ShowNavigationErrorAsync(tag, fbEx);
            }
        }

        _lastSidebarTag = tag;
        AppTitleBar.IsBackButtonEnabled = _history.Count > 0;
        EnqueueTitleBarFix();
    }

    // Schedule a low-priority override to run after TitleBar updates / 低优先级调度,确保在TitleBar更新后执行
    private void EnqueueTitleBarFix()
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ApplyTitleBarTemplateOverrides);
    }

    // Show error dialog when navigation fails / 导航失败时显示错误对话框
    private async Task ShowNavigationErrorAsync(string tag, Exception ex)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = "导航失败",
                Content = $"无法导航到 {tag}:\n{ex}",
                CloseButtonText = LocalizationHelper.GetString("String_Ok"),
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
        EnqueueTitleBarFix();
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
                    Icon = new FontIcon { Glyph = AppConstants.ModuleIconGlyph }
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
                            Icon = new FontIcon { Glyph = sub.Light ? AppConstants.SubModuleLightIconGlyph : AppConstants.ModuleIconGlyph }
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

    // Install subclass proc to enforce min window size / 安装子类化过程以强制窗口最小尺寸
    private void SetWindowMinSize(int minWidth, int minHeight)
    {
        _minWidth = minWidth;
        _minHeight = minHeight;
        _hWnd = WindowNative.GetWindowHandle(this);
        _subclassProc = SubclassWndProc;
        SetWindowSubclass(_hWnd, _subclassProc, IntPtr.Zero, out _subclassId);
    }

    // Subclass wndproc: clamp min size via WM_GETMINMAXINFO / 子类化窗口过程:通过WM_GETMINMAXINFO限制最小尺寸
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