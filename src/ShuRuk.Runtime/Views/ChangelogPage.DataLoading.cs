using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ShuRuk.Runtime.Core;
using ShuRuk.Runtime.Contracts;
using ShuRuk.Runtime.Helpers;
using ShuRuk.Runtime.Models;
using ShuRuk.Runtime.Services.Changelog;
using ShuRuk.Runtime.ViewModels;
using System.Collections.ObjectModel;

namespace ShuRuk.Runtime.Views;

public sealed partial class ChangelogPage : Page
{
    // Minimum loading display time / 最小加载展示时间
    private const int MinLoadTimeMs = 1000;
    private DateTime _loadStartTime;
    // Whether loading overlay is active (defers card display) / 加载遮罩激活中(延迟卡片显示)
    private bool _isLoadingOverlayActive;

    // Fetch data on navigation via injected provider / 导航时通过注入的提供者获取数据
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Apply localized empty state text / 应用本地化空状态文字
        EmptyTitle = LocalizationHelper.Runtime.GetString("RuntimeChangelogEmptyTitle");
        EmptyDescription = LocalizationHelper.Runtime.GetString("RuntimeChangelogEmptyDescription");
        // Set static localized expand/collapse text for binding / 设置静态本地化展开收起文字供绑定
        ChangelogEntry.ExpandText = LocalizationHelper.Runtime.GetString("RuntimeChangelogExpand");
        ChangelogEntry.CollapseText = LocalizationHelper.Runtime.GetString("RuntimeChangelogCollapse");

        // Read navigation parameter / 读取导航参数
        var param = e.Parameter as ChangelogPageParameter;
        var repoUrl = param?.RepoUrl ?? "microsoft/PowerToys";
        var sectionFilter = param?.SectionFilter;
        ViewModel.CurrentRepoUrl = repoUrl;
        ViewModel.CurrentSectionFilter = sectionFilter;

        // Restore UI preferences (image toggle, filter) / 恢复UI偏好(图片开关,过滤)
        var prefs = await ChangelogCacheService.LoadPrefsAsync();
        _showImages = prefs.ShowImages;
        ShowImagesToggle.IsChecked = _showImages;
        ToolTipService.SetToolTip(ShowImagesToggle,
            LocalizationHelper.Runtime.GetString(
                _showImages ? "RuntimeChangelogHideImages" : "RuntimeChangelogShowImages"));
        ToolTipService.SetToolTip(RefreshButton, LocalizationHelper.Runtime.GetString("RuntimeChangelogRefresh"));
        _filterTag = prefs.FilterTag;
        FilterButton.Content = _filterTag == ChangelogViewModel.FilterTagExcludePrerelease
            ? FilterExcludePrereleaseText : FilterAllText;

        var provider = RuntimeServices.ChangelogDataProvider;
        // Test mode with empty state: skip fetch, show empty / 测试模式空状态:跳过获取显示空状态
        var testMode = RuntimeServices.TestModeService;
        if (provider is null || !provider.IsAvailable ||
            (testMode is not null && testMode.IsPageCovered(TestModeCoverage.Changelog) && !testMode.IsFillDataEnabled))
        {
            Entries = [];
            return;
        }

        // Try cache first: show immediately then check for new releases
        // / 先尝试缓存:立即显示再检查新release
        var cache = await ViewModel.LoadCacheAsync();
        if (cache is not null && cache.Entries.Count > 0)
        {
            // Wait for ScrollViewer ViewportHeight > 0 before assigning Entries
            // / 等ScrollViewer ViewportHeight>0再赋值Entries
            // Navigation transition (FromBottom) keeps ViewportHeight=0 until animation ends
            // / 导航过渡(FromBottom)期间ViewportHeight=0
            // ItemsRepeater virtualization skips realization when ViewportHeight=0 -> blank page
            // / ViewportHeight=0时ItemsRepeater虚拟化跳过实现->空白
            // OnEntriesChanged also polls InvalidateMeasure as a backstop if ViewportHeight settles later
            // / OnEntriesChanged亦轮询InvalidateMeasure兜底视口延迟就绪
            var cachedEntries = cache.Entries;
            var latestTag = cache.LatestTag;
            ChangelogCacheService.NavLog(
                $"CACHE path: entries={cachedEntries.Count} " +
                $"scrollH={ChangelogScrollViewer.ActualHeight:F1} " +
                $"viewportH={ChangelogScrollViewer.ViewportHeight:F1}");
            Action assignEntries = () =>
            {
                ChangelogCacheService.NavLog(
                    $"CACHE assignEntries: scrollH={ChangelogScrollViewer.ActualHeight:F1} " +
                    $"viewportH={ChangelogScrollViewer.ViewportHeight:F1} " +
                    $"repeaterH={ChangelogRepeater.ActualHeight:F1}");
                Entries = cachedEntries;
                IsLoading = false;
            };
            if (ChangelogScrollViewer.ViewportHeight > 0)
            {
                assignEntries();
            }
            else
            {
                bool assigned = false;
                SizeChangedEventHandler onSizeChanged = null!;
                onSizeChanged = (s, args) =>
                {
                    if (ChangelogScrollViewer.ViewportHeight > 0)
                    {
                        ChangelogScrollViewer.SizeChanged -= onSizeChanged;
                        assigned = true;
                        assignEntries();
                    }
                };
                ChangelogScrollViewer.SizeChanged += onSizeChanged;
                // Fallback: force assign after 500ms if SizeChanged never fires
                // / 兜底:500ms后SizeChanged未触发则强制赋值
                // OnEntriesChanged poll will handle realization once ViewportHeight becomes positive
                // / 赋值后OnEntriesChanged轮询会在ViewportHeight变正时触发实现
                var fallback = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                fallback.Tick += (_, _) =>
                {
                    fallback.Stop();
                    if (!assigned)
                    {
                        ChangelogScrollViewer.SizeChanged -= onSizeChanged;
                        assignEntries();
                    }
                };
                fallback.Start();
            }
            // Background incremental check for new releases / 后台增量检测新release
            _ = ViewModel.CheckForNewReleasesAsync(provider, latestTag, cache.FirstPageEtag);
        }
        else
        {
            // No cache: streaming load with progressive display / 无缓存:流式加载渐进显示
            _loadStartTime = DateTime.UtcNow;
            _isLoadingOverlayActive = true;
            IsLoading = true;
            LoadingText = LocalizationHelper.Runtime.GetString("RuntimeChangelogLoading");
            try
            {
                var allEntries = new List<ChangelogEntry>();
                var firstBatch = true;
                await provider.GetEntriesStreamingAsync(repoUrl, sectionFilter, batch =>
                {
                    allEntries.AddRange(batch);
                    // Marshal to UI thread to append entries / 切换UI线程追加条目
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (firstBatch)
                        {
                            firstBatch = false;
                            HideLoadingAndRevealAfterMinTime();
                        }
                        // Add to source and filtered list, let LoadMore handle display
                        // / 添加到源和过滤列表,由LoadMore控制显示
                        foreach (var entry in batch)
                        {
                            ChangelogViewModel.PrepareEntry(entry);
                            _sourceEntries.Add(entry);
                            if (_filterTag == ChangelogViewModel.FilterTagAll || !entry.IsPrerelease)
                            {
                                _allEntries.Add(entry);
                            }
                        }
                        // Show content only after loading overlay hidden / 仅加载遮罩隐藏后才显示内容
                        if (!_isLoadingOverlayActive)
                        {
                            if (_displayItems.Count < ChangelogViewModel.InitialLoadCount)
                                ViewModel.LoadMore(isInitial: true);
                            UpdateNavButtons();
                            UpdateEmptyState();
                        }
                    });
                });
                HideLoadingAndRevealAfterMinTime();
                await ViewModel.SaveCacheAsync(allEntries);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ChangelogPage fetch failed: {ex}");
                _isLoadingOverlayActive = false;
                // Partial batch may have loaded before failure: keep it + InfoBar
                // / 失败前可能已加载部分批次:保留+InfoBar
                if (_sourceEntries.Count > 0)
                {
                    HideLoadingAndRevealAfterMinTime();
                    ShowCacheInfoBar();
                }
                else
                {
                    ViewModel.ClearAll();
                    HideLoadingAndShowErrorAfterMinTime(ex.Message);
                }
            }
        }
    }

    // Refresh: delete cache and full reload / 刷新:删除缓存并全量重新加载
    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        var provider = RuntimeServices.ChangelogDataProvider;
        var testMode = RuntimeServices.TestModeService;
        // Test mode empty state: show loading animation then keep empty
        // / 测试模式空状态:显示加载动画后保持空状态
        var isTestEmptyState = testMode is not null && testMode.IsEnabled && !testMode.IsFillDataEnabled;

        // Reuse loading overlay instead of in-button animation / 复用加载遮罩而非按钮内动画
        _loadStartTime = DateTime.UtcNow;
        _isLoadingOverlayActive = true;
        IsLoading = true;
        LoadingText = LocalizationHelper.Runtime.GetString("RuntimeChangelogLoading");
        RefreshButton.IsEnabled = false;

        if (isTestEmptyState)
        {
            // Min display time then restore empty state / 最小展示时间后恢复空状态
            await Task.Delay(MinLoadTimeMs);
            _isLoadingOverlayActive = false;
            IsLoading = false;
            RefreshButton.IsEnabled = true;
            ViewModel.ClearAll();
            UpdateEmptyState();
            return;
        }

        if (provider is null || !provider.IsAvailable || string.IsNullOrEmpty(ViewModel.CurrentRepoUrl))
        {
            _isLoadingOverlayActive = false;
            HideLoadingAfterMinTime();
            RefreshButton.IsEnabled = true;
            return;
        }

        try
        {
            // Delete cache file for current repo+filter / 删除当前仓库+过滤的缓存文件
            ChangelogCacheService.DeleteCache(ViewModel.CurrentRepoUrl, ViewModel.CurrentSectionFilter);

            // Clear current display before streaming reload / 流式重新加载前清空当前显示
            _cardHeightCache.Clear();
            ViewModel.ClearAll();

            var allEntries = new List<ChangelogEntry>();
            var firstBatch = true;

            await provider.GetEntriesStreamingAsync(ViewModel.CurrentRepoUrl, ViewModel.CurrentSectionFilter, batch =>
            {
                allEntries.AddRange(batch);
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (firstBatch) { firstBatch = false; HideLoadingAndRevealAfterMinTime(); }
                    foreach (var entry in batch)
                    {
                        ChangelogViewModel.PrepareEntry(entry);
                        _sourceEntries.Add(entry);
                        if (_filterTag == ChangelogViewModel.FilterTagAll || !entry.IsPrerelease)
                        {
                            _allEntries.Add(entry);
                        }
                    }
                    // Show content only after loading overlay hidden / 仅加载遮罩隐藏后才显示内容
                    if (!_isLoadingOverlayActive)
                    {
                        if (_displayItems.Count < ChangelogViewModel.InitialLoadCount)
                            ViewModel.LoadMore(isInitial: true);
                        UpdateNavButtons();
                        UpdateEmptyState();
                    }
                });
            });
            await ViewModel.SaveCacheAsync(allEntries);
            // Refresh succeeded: dismiss any stale cache InfoBar / 刷新成功:关闭残留缓存InfoBar
            HideCacheInfoBar();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Changelog refresh failed: {ex}");
            _isLoadingOverlayActive = false;
            // Partial batch may have loaded before failure: keep it + InfoBar
            // / 失败前可能已加载部分批次:保留+InfoBar
            if (_sourceEntries.Count > 0)
            {
                HideLoadingAndRevealAfterMinTime();
                ShowCacheInfoBar();
            }
            else
            {
                ViewModel.ClearAll();
                HideLoadingAndShowErrorAfterMinTime(ex.Message);
            }
        }
        finally
        {
            HideLoadingAndRevealAfterMinTime();
            RefreshButton.IsEnabled = true;
        }
    }

    // Run action on UI thread after minimum load time elapsed / 最小加载时间后在UI线程执行操作
    private void RunAfterMinTime(Action action)
    {
        var remaining = TimeSpan.FromMilliseconds(MinLoadTimeMs) - (DateTime.UtcNow - _loadStartTime);
        if (remaining > TimeSpan.Zero)
            _ = Task.Delay(remaining).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => action()));
        else
            action();
    }

    // Hide loading overlay after min time / 最小时间后隐藏加载遮罩
    private void HideLoadingAfterMinTime() => RunAfterMinTime(() => IsLoading = false);

    // Hide overlay after min time then reveal content / 最小时间后隐藏遮罩并显示内容
    private void HideLoadingAndRevealAfterMinTime() => RunAfterMinTime(RevealContent);

    // Reveal content: clear overlay flag, hide overlay, show cards
    // (guarded against double-call)
    // / 显示内容:清遮罩标志,隐藏遮罩,显示卡片(防止重复调用)
    private void RevealContent()
    {
        if (!_isLoadingOverlayActive) return;
        _isLoadingOverlayActive = false;
        IsLoading = false;

        int prevCount = _displayItems.Count;
        ViewModel.LoadMore();
        // Defer to next frame: let MarqueeText initialize before card animation
        // / 延迟到下一帧:让跑马灯在卡片动画前初始化
        DispatcherQueue.TryEnqueue(() =>
        {
            for (int i = prevCount; i < _displayItems.Count; i++)
            {
                try
                {
                    if (ChangelogRepeater.GetOrCreateElement(i) is FrameworkElement fe)
                    {
                        PlaySlideInAnimation(fe, CardSlideDistance, SlideInAnimMs);
                        RefreshMarqueeInCard(fe);
                    }
                }
                catch { }
            }
            UpdateEmptyState();
            UpdateNavButtons();
            // Link styles now applied per-MTB via Loaded/SizeChanged events (no batch traversal)
            // / 链接样式现在通过每个MTB的Loaded/SizeChanged事件应用(无需批量遍历)
            CreateTimer(150, () =>
            {
                EnsureCardsVisible();
            });
        });
    }

    // Hide overlay after min time then show error state / 最小时间后隐藏遮罩并显示错误状态
    private void HideLoadingAndShowErrorAfterMinTime(string errorMessage)
        => RunAfterMinTime(() => { IsLoading = false; ShowErrorState(errorMessage); });

    // Show InfoBar indicating cached data is displayed (network failure or offline)
    // / 显示InfoBar提示正在显示缓存数据(网络失败或离线)
    private void ShowCacheInfoBar()
    {
        if (CacheInfoBar is null) return;
        CacheInfoBar.IsOpen = true;
        CacheInfoBar.Severity = InfoBarSeverity.Warning;
        CacheInfoBar.Title = LocalizationHelper.Runtime.GetString("RuntimeChangelogCacheTitle");
        CacheInfoBar.Message = LocalizationHelper.Runtime.GetString("RuntimeChangelogCacheMessage");
    }

    // Hide the cache InfoBar (e.g. on successful refresh) / 隐藏缓存InfoBar(如刷新成功)
    private void HideCacheInfoBar()
    {
        if (CacheInfoBar is null) return;
        CacheInfoBar.IsOpen = false;
    }

    private async void FilterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item) return;
        _filterTag = item.Tag?.ToString() ?? ChangelogViewModel.FilterTagAll;
        FilterButton.Content = item.Text;
        // Persist preference / 持久化偏好
        await ViewModel.SavePrefsAsync();
        // Ignore during init before source data loaded / 初始化时源数据未加载则忽略
        if (_sourceEntries.Count == 0) return;
        ViewModel.ApplyFilter();
    }

    // Toggle image visibility in all markdown blocks / 切换所有markdown块的图片可见性
    private async void ShowImagesToggle_Click(object sender, RoutedEventArgs e)
    {
        _showImages = ShowImagesToggle.IsChecked == true;
        // Update tooltip to reflect current state
        // / 更新提示反映当前状态
        ToolTipService.SetToolTip(ShowImagesToggle,
            LocalizationHelper.Runtime.GetString(
                _showImages ? "RuntimeChangelogHideImages" : "RuntimeChangelogShowImages"));
        // Persist preference / 持久化偏好
        await ViewModel.SavePrefsAsync();

        // Show progress indicator, disable button during update / 更新期间显示进度指示并禁用按钮
        ShowImagesIcon.Visibility = Visibility.Collapsed;
        ShowImagesProgress.Visibility = Visibility.Visible;
        ShowImagesProgress.IsActive = true;
        ShowImagesToggle.IsEnabled = false;

        await ViewModel.UpdateAllImageVisibilityAsync();

        // Restore button state / 恢复按钮状态
        ShowImagesProgress.IsActive = false;
        ShowImagesProgress.Visibility = Visibility.Collapsed;
        ShowImagesIcon.Visibility = Visibility.Visible;
        ShowImagesToggle.IsEnabled = true;
    }

    private void UpdateEmptyState()
    {
        var hasEntries = _allEntries.Count > 0;
        // Empty state only when no entries and no error / 无条目且无错误时才显示空状态
        var showEmpty = !hasEntries && ErrorState.Visibility != Visibility.Visible;
        if (showEmpty) FadeIn(EmptyState); else FadeOut(EmptyState);
        // Hide error state when entries exist / 有条目时隐藏错误状态
        if (hasEntries) FadeOut(ErrorState);
    }

    // Show dedicated error state with message / 显示独立错误状态
    private void ShowErrorState(string message)
    {
        ErrorTitleText.Text = LocalizationHelper.Runtime.GetString("RuntimeChangelogErrorTitle");
        // Localized description + technical detail / 本地化描述+技术详情
        ErrorDescriptionText.Text = LocalizationHelper.Runtime.GetString("RuntimeChangelogErrorDescription");
        FadeOut(EmptyState);
        FadeIn(ErrorState);
    }

    private static void OnIsLoadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChangelogPage page)
        {
            var loading = (bool)e.NewValue;
            if (loading)
            {
                FadeIn(page.LoadingOverlay);
                // Hide empty/error states while loading to avoid overlap / 加载时隐藏空/错误状态避免叠加
                FadeOut(page.EmptyState);
                FadeOut(page.ErrorState);
            }
            else
            {
                FadeOut(page.LoadingOverlay);
            }
        }
    }

    private static void OnLoadingTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChangelogPage page)
        {
            page.LoadingTextBlock.Text = (string)e.NewValue;
        }
    }
}
