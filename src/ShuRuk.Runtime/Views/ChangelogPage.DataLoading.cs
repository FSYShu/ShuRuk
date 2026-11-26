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

        // Show loading overlay immediately: the page is already visible during
        // async operations below, so without this the content area appears blank.
        // / 立即显示加载遮罩:下面的异步操作期间页面已可见,
        // 不设置此遮罩则内容区会空白无加载动画。
        IsLoading = true;
        LoadingText = LocalizationHelper.Runtime.GetString("RuntimeChangelogLoading");

        // Apply localized empty state text / 应用本地化空状态文字
        EmptyTitle = LocalizationHelper.Runtime.GetString("RuntimeChangelogEmptyTitle");
        EmptyDescription = LocalizationHelper.Runtime.GetString("RuntimeChangelogEmptyDescription");
        // Set static localized expand/collapse text for binding / 设置静态本地化展开收起文字供绑定
        ChangelogEntry.ExpandText = LocalizationHelper.Runtime.GetString("RuntimeChangelogExpand");
        ChangelogEntry.CollapseText = LocalizationHelper.Runtime.GetString("RuntimeChangelogCollapse");


        // Read navigation parameter / 读取导航参数
        var param = e.Parameter as ChangelogPageParameter;
        var repoUrl = param?.RepoUrl ?? string.Empty;
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
            IsLoading = false;
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
            var cachedEntries = cache.Entries;
            var latestTag = cache.LatestTag;

            Action assignEntries = () =>
            {
                // Trigger FadeOut via IsLoading=false BEFORE assigning Entries,
                // so the overlay begins fading while cards render underneath.
                // / 先通过IsLoading=false触发淡出再赋值Entries,
                // 让遮罩开始淡出同时卡片在下方渲染
                IsLoading = false;
                Entries = cachedEntries;
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
                        // Add to source and filtered list / 添加到源和过滤列表
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
                            // Assign card modes and load all items / 分配卡片模式并加载全部条目
                            ViewModel.AssignCardModes();
                            if (_displayItems.Count == 0)
                                ViewModel.LoadAll();
                            UpdateNavButtons();
                            UpdateEmptyState();
                        }
                    });
                });
                HideLoadingAndRevealAfterMinTime();
                await ViewModel.SaveCacheAsync(allEntries);
                // After streaming load: trigger incremental check to fetch ETag and any newer entries
                // / 流式加载后:触发增量检查获取ETag和更新的条目
                if (allEntries.Count > 0)
                {
                    var latestTag = allEntries[0].Version;
                    _ = ViewModel.CheckForNewReleasesAsync(provider, latestTag, null);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ChangelogPage fetch failed: {ex}");
                _isLoadingOverlayActive = false;
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
        var isTestEmptyState = testMode is not null && testMode.IsEnabled && !testMode.IsFillDataEnabled;

        _loadStartTime = DateTime.UtcNow;
        _isLoadingOverlayActive = true;
        IsLoading = true;
        LoadingText = LocalizationHelper.Runtime.GetString("RuntimeChangelogLoading");
        RefreshButton.IsEnabled = false;

        if (isTestEmptyState)
        {
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
            ChangelogCacheService.DeleteCache(ViewModel.CurrentRepoUrl, ViewModel.CurrentSectionFilter);
            _cardHeightCache.Clear();
            _expandedHistoryEntry = null;
            ClearStickyOverlays();
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
                    if (!_isLoadingOverlayActive)
                    {
                        ViewModel.AssignCardModes();
                        if (_displayItems.Count == 0)
                            ViewModel.LoadAll();
                        UpdateNavButtons();
                        UpdateEmptyState();
                    }
                });
            });
            await ViewModel.SaveCacheAsync(allEntries);
            HideCacheInfoBar();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Changelog refresh failed: {ex}");
            _isLoadingOverlayActive = false;
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

    // Reveal content: clear overlay flag, hide overlay, load all items
    // / 显示内容:清遮罩标志,隐藏遮罩,加载全部条目
    private void RevealContent()
    {
        if (!_isLoadingOverlayActive) return;
        _isLoadingOverlayActive = false;
        IsLoading = false;

        // Assign card modes and load all items / 分配卡片模式并加载全部条目
        ViewModel.AssignCardModes();
        if (_displayItems.Count == 0)
            ViewModel.LoadAll();
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateEmptyState();
            UpdateNavButtons();
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
        await ViewModel.SavePrefsAsync();
        if (_sourceEntries.Count == 0) return;
        _expandedHistoryEntry = null;
        ViewModel.ApplyFilter();
        // Load all items after filter applied / 过滤后加载全部条目
        ClearStickyOverlays();
        ViewModel.LoadAll();
    }

    // Toggle image visibility in all markdown blocks / 切换所有markdown块的图片可见性
    private async void ShowImagesToggle_Click(object sender, RoutedEventArgs e)
    {
        _showImages = ShowImagesToggle.IsChecked == true;
        ToolTipService.SetToolTip(ShowImagesToggle,
            LocalizationHelper.Runtime.GetString(
                _showImages ? "RuntimeChangelogHideImages" : "RuntimeChangelogShowImages"));
        await ViewModel.SavePrefsAsync();

        ShowImagesIcon.Visibility = Visibility.Collapsed;
        ShowImagesProgress.Visibility = Visibility.Visible;
        ShowImagesProgress.IsActive = true;
        ShowImagesToggle.IsEnabled = false;

        await ViewModel.UpdateAllImageVisibilityAsync();

        ShowImagesProgress.IsActive = false;
        ShowImagesProgress.Visibility = Visibility.Collapsed;
        ShowImagesIcon.Visibility = Visibility.Visible;
        ShowImagesToggle.IsEnabled = true;
    }

    private void UpdateEmptyState()
    {
        var hasEntries = _allEntries.Count > 0;
        var showEmpty = !hasEntries && ErrorState.Visibility != Visibility.Visible;
        if (showEmpty) FadeIn(EmptyState); else FadeOut(EmptyState);
        if (hasEntries) FadeOut(ErrorState);
    }

    // Show dedicated error state with message / 显示独立错误状态
    private void ShowErrorState(string message)
    {
        ErrorTitleText.Text = LocalizationHelper.Runtime.GetString("RuntimeChangelogErrorTitle");
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
