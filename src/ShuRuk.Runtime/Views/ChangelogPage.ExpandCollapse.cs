using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using ShuRuk.Runtime.Helpers;
using ShuRuk.Runtime.Models;
using Windows.Foundation;

namespace ShuRuk.Runtime.Views;

public sealed partial class ChangelogPage : Page
{
    // Animation constants / 动画常量
    private const int SlideInAnimMs = 150;   // Slide-in duration (content/cards) / 滑入时长(内容/卡片)
    private const int StateAnimMs = 300;     // State transition duration / 状态切换时长
    private const double CardSlideDistance = 16;  // Card slide-up distance / 卡片上滑距离
    private const double PanelSlideDistance = 20; // Panel slide-up distance / 面板上滑距离

    // Last expanded entry for floating collapse button / 最近展开条目供浮动收起
    private ChangelogEntry? _lastExpandedEntry;
    private int _lastExpandedIndex = -1;

    // Floating collapse button state / 浮动收起按钮状态
    private bool _floatingCollapseVisible;
    private bool _isCollapsing;
    // Active flight storyboard, stopped before next flight to break HoldEnd
    // / 当前飞行动画Storyboard,下次飞行前停止以断开HoldEnd
    private Storyboard? _flightStoryboard;
    // Active height animation storyboard for card collapse / 收起高度动画Storyboard
    private Storyboard? _heightStoryboard;
    // Active fade-in storyboard for expand button / 展开按钮淡入动画Storyboard
    private Storyboard? _fadeInStoryboard;
    // Active below-cards fly-out animations, stopped before next expand/collapse
    // / 下方卡片飞出动画,下次展开/收起前停止
    private readonly List<(Storyboard sb, TranslateTransform tt)> _belowCardsAnims = [];

    // Toggle expand/collapse; fade in content on expand (opacity only, no layout impact)
    // / 切换展开折叠;展开时淡入内容(仅透明度,不触发布局)
    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var entry = btn.DataContext as ChangelogEntry ?? btn.Tag as ChangelogEntry;
        if (entry is null) return;
        PlayButtonPress(btn);

        if (!entry.IsExpanded)
        {
            // EXPANDING: compute offset BEFORE toggle (button at collapsed position)
            // / 展开:切换前计算偏移(按钮在收起态位置)
            _lastExpandedEntry = entry;
            _lastExpandedIndex = _displayItems.IndexOf(entry);

            // Stop prior storyboards to break HoldEnd / 停止之前动画断开HoldEnd
            _flightStoryboard?.Stop();
            _flightStoryboard = null;
            _fadeInStoryboard?.Stop();
            _fadeInStoryboard = null;
            _heightStoryboard?.Stop();
            _heightStoryboard = null;
            StopBelowCardsAnimations();

            double offsetX = 0, offsetY = 0;
            FrameworkElement? fadeTarget = null;
            try
            {
                // Make floating button participate in layout (invisible) for TransformToVisual
                // / 使浮动按钮参与布局(不可见)以便TransformToVisual
                CollapseFloatingBorder.Visibility = Visibility.Visible;
                CollapseFloatingBorder.Opacity = 0;
                // Force layout so first-time Visible yields correct ActualWidth/Height + TransformToVisual
                // / 强制布局,首次Visible时ActualWidth/Height和TransformToVisual才准确
                CollapseFloatingBorder.UpdateLayout();
                if (_lastExpandedIndex >= 0 && _lastExpandedIndex < _displayItems.Count
                    && ChangelogRepeater.GetOrCreateElement(_lastExpandedIndex) is FrameworkElement root
                    && FindExpandButton(root, entry) is Button eb)
                {
                    fadeTarget = eb.Parent as FrameworkElement
                        ?? VisualTreeHelper.GetParent(eb) as FrameworkElement ?? eb;
                    fadeTarget.Opacity = 0;
                    // Offset = expand button center - floating button fixed center
                    // / 偏移=展开按钮中心-浮动按钮固定中心
                    var relPos = eb.TransformToVisual(CollapseFloatingBorder).TransformPoint(new Point(0, 0));
                    offsetX = relPos.X + eb.ActualWidth / 2 - CollapseFloatingBorder.ActualWidth / 2;
                    offsetY = relPos.Y + eb.ActualHeight / 2 - CollapseFloatingBorder.ActualHeight / 2;
                }
            }
            catch { }

            // Record card height BEFORE toggle for below-cards fly-out offset
            // / 切换前记录卡片高度供下方卡片飞出偏移
            double expandCardHeightBefore = 0;
            if (_lastExpandedIndex >= 0 && _lastExpandedIndex < _displayItems.Count)
            {
                try
                {
                    if (ChangelogRepeater.GetOrCreateElement(_lastExpandedIndex) is FrameworkElement rootExp)
                        expandCardHeightBefore = rootExp.ActualHeight;
                }
                catch { }
            }

            // Now toggle expand (card height changes) / 现在切换展开(卡片高度变化)
            entry.IsExpanded = true;
            _floatingCollapseVisible = true;

            // Below cards fly-out: animate from -deltaY to 0 (pure render)
            // / 下方卡片飞出:从-deltaY动画到0(纯渲染)
            if (expandCardHeightBefore > 0 && _lastExpandedIndex >= 0 && _lastExpandedIndex < _displayItems.Count)
            {
                try
                {
                    ChangelogRepeater.UpdateLayout();
                    if (ChangelogRepeater.GetOrCreateElement(_lastExpandedIndex) is FrameworkElement rootExp)
                    {
                        double deltaY = rootExp.ActualHeight - expandCardHeightBefore;
                        PlayBelowCardsFlyOut(_lastExpandedIndex, deltaY);
                    }
                }
                catch { }
            }

            // Flight-in: floating button flies from expand button position to fixed bottom
            // / 飞入:浮动按钮从展开按钮位置飞到固定底部
            if (offsetX != 0 || offsetY != 0)
            {
                PlayFlightIn(offsetX, offsetY);
                // Schedule expand button fade-in after flight completes / 飞行完成后调度展开按钮淡入
                if (fadeTarget is not null)
                {
                    var fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                    fadeTimer.Tick += (_, _) => { fadeTimer.Stop(); PlayFadeIn(fadeTarget); };
                    fadeTimer.Start();
                }
            }
            else
            {
                SetFloatingCollapseVisible(true);
            }

            // Fade in expanded content (pure render, avoids LayoutCycle)
            // / 淡入展开内容(纯渲染,避免布局循环)
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_lastExpandedIndex >= 0 && _lastExpandedIndex < _displayItems.Count
                    && ChangelogRepeater.GetOrCreateElement(_lastExpandedIndex) is FrameworkElement root
                    && UiTreeHelper.FindDescendant<ItemsRepeater>(root) is ItemsRepeater content)
                    PlayContentFadeIn(content);
            });
        }
        else
        {
            // COLAPSING via card button (not floating) / 通过卡片按钮收起(非浮动)
            _isCollapsing = true;
            _flightStoryboard?.Stop();
            _flightStoryboard = null;
            _fadeInStoryboard?.Stop();
            _fadeInStoryboard = null;
            _heightStoryboard?.Stop();
            _heightStoryboard = null;
            StopBelowCardsAnimations();

            int collapseIdx = _displayItems.IndexOf(entry);
            // Wrap collapse logic in action called after height animation (or immediately)
            // / 收起逻辑包在action中,高度动画后调用(或立即)
            Action completeCardCollapse = () =>
            {
                entry.IsExpanded = false;
                _lastExpandedEntry = null;
                _lastExpandedIndex = -1;
                SetFloatingCollapseVisible(false);
                EnsureNextCardTopVisible(collapseIdx);
                var tc = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
                tc.Tick += (_, _) => { tc.Stop(); _isCollapsing = false; };
                tc.Start();
            };

            // Try height animation: shrink card MaxHeight before collapse
            // / 尝试高度动画:收起前动画缩小卡片MaxHeight
            bool cardHeightAnimStarted = false;
            if (collapseIdx >= 0 && collapseIdx < _displayItems.Count)
            {
                try
                {
                    if (ChangelogRepeater.GetOrCreateElement(collapseIdx) is FrameworkElement root
                        && UiTreeHelper.FindDescendantByName(root, "CardContentGrid") is FrameworkElement cardContent)
                    {
                        double fromH = cardContent.ActualHeight;
                        double toH = ChangelogEntry.CollapsedMaxHeight;
                        if (fromH > toH + 1)
                        {
                            var anim = new DoubleAnimation
                            {
                                From = fromH,
                                To = toH,
                                Duration = TimeSpan.FromMilliseconds(260),
                                EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
                            };
                            anim.EnableDependentAnimation = true;
                            Storyboard.SetTarget(anim, cardContent);
                            Storyboard.SetTargetProperty(anim, "MaxHeight");
                            var sb = new Storyboard();
                            sb.Children.Add(anim);
                            sb.Completed += (_, _) => { _heightStoryboard = null; completeCardCollapse(); };
                            _heightStoryboard = sb;
                            sb.Begin();
                            cardHeightAnimStarted = true;
                        }
                    }
                }
                catch { }
            }

            if (!cardHeightAnimStarted)
            {
                completeCardCollapse();
            }
        }
    }

    // Fade in content opacity (no EnableDependentAnimation, pure render)
    // / 淡入内容透明度(无依赖动画,纯渲染)
    private static void PlayContentFadeIn(FrameworkElement target)
    {
        var anim = new DoubleAnimation
        {
            From = 0.4,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, "Opacity");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }

    // Stop active below-cards fly-out animations and reset Y / 停止下方卡片飞出动画并重置Y
    private void StopBelowCardsAnimations()
    {
        foreach (var (sb, tt) in _belowCardsAnims)
        {
            sb.Stop();
            tt.Y = 0;
        }
        _belowCardsAnims.Clear();
    }

    // Below cards fly-out: animate from -deltaY to 0 (pure render, no layout)
    // / 下方卡片飞出:从-deltaY动画到0(纯渲染,不触发布局)
    private void PlayBelowCardsFlyOut(int expandedIndex, double deltaY)
    {
        StopBelowCardsAnimations();
        if (deltaY <= 1) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(220);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(ChangelogRepeater); i++)
        {
            if (VisualTreeHelper.GetChild(ChangelogRepeater, i) is not FrameworkElement fe) continue;
            int idx;
            try { idx = ChangelogRepeater.GetElementIndex(fe); } catch { continue; }
            if (idx <= expandedIndex) continue;
            var tt = fe.RenderTransform as TranslateTransform ?? new TranslateTransform();
            tt.Y = -deltaY;
            fe.RenderTransform = tt;
            var sb = new Storyboard();
            var yAnim = new DoubleAnimation { From = -deltaY, To = 0, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(yAnim, tt);
            Storyboard.SetTargetProperty(yAnim, "Y");
            sb.Children.Add(yAnim);
            _belowCardsAnims.Add((sb, tt));
            sb.Begin();
        }
    }

    // Button press scale feedback (fixed-size element, no layout cycle)
    // / 按钮按下缩放反馈(固定尺寸,无布局循环)
    private static void PlayButtonPress(Button btn)
    {
        var st = btn.RenderTransform as ScaleTransform ?? new ScaleTransform();
        btn.RenderTransform = st;
        btn.RenderTransformOrigin = new Point(0.5, 0.5);
        var anim = new DoubleAnimation
        {
            From = 0.92,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, st);
        Storyboard.SetTargetProperty(anim, "ScaleX");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        var animY = new DoubleAnimation
        {
            From = 0.92,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animY, st);
        Storyboard.SetTargetProperty(animY, "ScaleY");
        sb.Children.Add(animY);
        sb.Begin();
    }

    // Show floating collapse button when expanded card bottom is off-screen
    // / 展开卡片底部不可见时显示浮动收起按钮
    private void UpdateCollapseButton()
    {
        // Skip during collapse to let slide-out animation play / 收起过程中跳过以让滑出动画播放
        if (_isCollapsing) return;
        // Show floating button only while expanded card bottom is off-screen
        // / 仅展开卡片底部不可见时显示浮动收起按钮
        if (_lastExpandedEntry is null || !_lastExpandedEntry.IsExpanded
            || _lastExpandedIndex < 0 || _lastExpandedIndex >= _displayItems.Count)
        {
            SetFloatingCollapseVisible(false);
            return;
        }
        var top = GetItemTop(_lastExpandedIndex);
        if (top is null) { SetFloatingCollapseVisible(false); return; }
        try
        {
            var el = ChangelogRepeater.GetOrCreateElement(_lastExpandedIndex);
            if (el is not FrameworkElement fe) { SetFloatingCollapseVisible(false); return; }
            double bottom = top.Value + fe.ActualHeight;
            SetFloatingCollapseVisible(bottom > ChangelogScrollViewer.ViewportHeight);
        }
        catch { SetFloatingCollapseVisible(false); }
    }

    private void SetFloatingCollapseVisible(bool visible)
    {
        if (visible == _floatingCollapseVisible) return;
        _floatingCollapseVisible = visible;
        var tt = CollapseFloatingBorder.RenderTransform as TranslateTransform ?? new TranslateTransform();
        CollapseFloatingBorder.RenderTransform = tt;
        if (visible)
        {
            CollapseFloatingBorder.Visibility = Visibility.Visible;
            // Slide in from above (expand button direction) + fade / 从上方(展开按钮方向)滑入+淡入
            PlaySlideFade(tt, CollapseFloatingBorder, fromY: -40, toY: 0, fromOp: 0, toOp: 1, () =>
            {
                // Persist values after animation (HoldEnd may be lost on GC)
                // / 动画后保持值(HoldEnd可能因GC失效)
                CollapseFloatingBorder.Opacity = 1;
                tt.Y = 0;
            });
        }
        else
        {
            // Collapse: hide directly, animation plays on expand button
            // / 收起:直接隐藏,动画在展开按钮上播放
            CollapseFloatingBorder.Visibility = Visibility.Collapsed;
            CollapseFloatingBorder.Opacity = 0;
            tt.Y = 0;
        }
    }

    // Slide Y + opacity fade (pure render, no layout impact) / Y位移+透明度淡入淡出(纯渲染,不触发布局)
    private static void PlaySlideFade(TranslateTransform tt, FrameworkElement target,
        double fromY, double toY, double fromOp, double toOp, Action? onComplete)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var sb = new Storyboard();
        var yAnim = new DoubleAnimation
        {
            From = fromY,
            To = toY,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = ease
        };
        Storyboard.SetTarget(yAnim, tt);
        Storyboard.SetTargetProperty(yAnim, "Y");
        sb.Children.Add(yAnim);
        var opAnim = new DoubleAnimation
        {
            From = fromOp,
            To = toOp,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = ease
        };
        Storyboard.SetTarget(opAnim, target);
        Storyboard.SetTargetProperty(opAnim, "Opacity");
        sb.Children.Add(opAnim);
        if (onComplete != null) sb.Completed += (_, _) => onComplete();
        sb.Begin();
    }

    // Ensure next card top enters viewport after collapse / 收起后保证下一卡片顶部进入视口
    private void EnsureNextCardTopVisible(int collapsedIndex)
    {
        int nextIdx = collapsedIndex + 1;
        if (nextIdx >= _displayItems.Count) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                ChangelogRepeater.UpdateLayout();
                double? top = GetItemTop(nextIdx);
                if (top is null) return;
                double viewportH = ChangelogScrollViewer.ViewportHeight;
                // Scroll only when next card top is below viewport / 仅下一卡片顶部在视口下方时滚动
                if (top.Value > viewportH - 40)
                {
                    // Align next card top to viewport center / 下一卡片顶部对齐视口中间
                    double targetOffset = ChangelogScrollViewer.VerticalOffset + top.Value - viewportH / 2;
                    targetOffset = Math.Clamp(targetOffset, 0, ChangelogScrollViewer.ScrollableHeight);
                    ChangelogScrollViewer.ChangeView(null, targetOffset, null, false);
                }
            }
            catch { }
        });
    }

    // Play slide-up + fade-in with CubicEase on element / 上滑+淡入动画(CubicEase缓动)
    // Set Opacity=1 explicitly before animation: navigation transition may leave Opacity=0
    // / 动画前显式设Opacity=1:导航过渡可能使Opacity=0
    // and if the storyboard is interrupted, the card stays invisible. / 若动画被中断,卡片会保持不可见。
    // The Y slide animation (fromY -> 0) still plays for visual polish.
    // / Y滑动动画(fromY->0)仍播放以增强视觉效果。
    private static void PlaySlideInAnimation(FrameworkElement element, double fromY, int durationMs)
    {
        element.Opacity = 1;
        var tt = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        tt.Y = fromY;
        element.RenderTransform = tt;
        var sb = new Storyboard();
        var yAnim = new DoubleAnimation
        {
            From = fromY,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(yAnim, tt);
        Storyboard.SetTargetProperty(yAnim, "Y");
        sb.Children.Add(yAnim);

        sb.Begin();
    }

    // Content panel appear animation / 内容面板出现动画
    private void PlayAppearAnimation() => PlaySlideInAnimation(ContentPanel, PanelSlideDistance, SlideInAnimMs);

    private void CollapseFloatingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastExpandedEntry is null) return;
        var entry = _lastExpandedEntry;
        var idx = _lastExpandedIndex;
        _isCollapsing = true;

        // Stop prior flight + fade-in + height storyboards to break their HoldEnd
        // / 停止之前飞行+淡入+高度动画Storyboard断开HoldEnd
        _flightStoryboard?.Stop();
        _flightStoryboard = null;
        _fadeInStoryboard?.Stop();
        _fadeInStoryboard = null;
        _heightStoryboard?.Stop();
        _heightStoryboard = null;
        StopBelowCardsAnimations();

        // Find expand button + compute flight offset BEFORE collapse / 收起前查找展开按钮+计算飞行偏移
        Button? expandBtn = null;
        FrameworkElement? fadeTarget = null;
        double offsetX = 0, offsetY = 0;
        // Card height shrink amount: expand button moves up by this much after collapse
        // / 卡片高度缩小量:收起后展开按钮上移此量
        double cardShrinkDelta = 0;
        if (idx >= 0 && idx < _displayItems.Count)
        {
            try
            {
                if (ChangelogRepeater.GetOrCreateElement(idx) is FrameworkElement root
                    && FindExpandButton(root, entry) is Button eb)
                {
                    expandBtn = eb;
                    fadeTarget = eb.Parent as FrameworkElement
                        ?? VisualTreeHelper.GetParent(eb) as FrameworkElement ?? eb;
                    fadeTarget.Opacity = 0;
                    var relPos = eb.TransformToVisual(CollapseFloatingBorder).TransformPoint(new Point(0, 0));
                    offsetX = relPos.X + eb.ActualWidth / 2 - CollapseFloatingBorder.ActualWidth / 2;
                    offsetY = relPos.Y + eb.ActualHeight / 2 - CollapseFloatingBorder.ActualHeight / 2;
                    // Measure card content height to predict post-collapse expand button position
                    // / 测量卡片内容高度预测收起后展开按钮位置
                    if (UiTreeHelper.FindDescendantByName(root, "CardContentGrid") is FrameworkElement cardContent)
                        cardShrinkDelta = Math.Max(0, cardContent.ActualHeight - ChangelogEntry.CollapsedMaxHeight);
                }
            }
            catch { }
        }

        // Flight target = post-collapse expand button position (button moves up by cardShrinkDelta)
        // / 飞行目标=收起后展开按钮位置(按钮上移cardShrinkDelta)
        // Parallel with height anim: fly to visible target instead of off-screen current position
        // / 与高度动画并行:飞向可见目标而非视口外当前位置
        double flightOffsetY = offsetY - cardShrinkDelta;

        Action completeCollapse = () =>
        {
            entry.IsExpanded = false;
            _lastExpandedEntry = null;
            _lastExpandedIndex = -1;
            _floatingCollapseVisible = false;
            EnsureNextCardTopVisible(idx);

            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            t.Tick += (_, _) => { t.Stop(); _isCollapsing = false; };
            t.Start();
        };

        // Track both animations; completeCollapse runs only after BOTH finish
        // / 跟踪两个动画;completeCollapse仅在两者都完成后运行
        bool heightDone = false;
        bool flightDone = false;
        bool collapseExecuted = false;
        Action onBothDone = () =>
        {
            if (!collapseExecuted && heightDone && flightDone)
            {
                collapseExecuted = true;
                completeCollapse();
            }
        };

        // Start flight animation simultaneously with height animation / 飞行动画与高度动画同时启动
        if (expandBtn is not null)
        {
            PlayFlightFade(offsetX, flightOffsetY);
            // Flight+fade total = 180+100 = 280ms; mark done + fade in expand button
            // / 飞行+淡出总180+100=280ms;标记完成+淡入展开按钮
            var flightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
            flightTimer.Tick += (_, _) =>
            {
                flightTimer.Stop();
                flightDone = true;
                onBothDone();
                if (fadeTarget is not null) PlayFadeIn(fadeTarget);
            };
            flightTimer.Start();
        }
        else
        {
            SetFloatingCollapseVisible(false);
            flightDone = true;
        }

        // Height animation: shrink card MaxHeight / 高度动画:缩小卡片MaxHeight
        // Collapse shrinks height, no markdown re-measure expansion -> avoids LayoutCycle
        // / 收起缩小高度,无需markdown重新测量扩展->避免布局循环
        bool heightAnimStarted = false;
        if (idx >= 0 && idx < _displayItems.Count)
        {
            try
            {
                if (ChangelogRepeater.GetOrCreateElement(idx) is FrameworkElement root
                    && UiTreeHelper.FindDescendantByName(root, "CardContentGrid") is FrameworkElement cardContent)
                {
                    double fromHeight = cardContent.ActualHeight;
                    double toHeight = ChangelogEntry.CollapsedMaxHeight;
                    if (fromHeight > toHeight + 1)
                    {
                        var anim = new DoubleAnimation
                        {
                            From = fromHeight,
                            To = toHeight,
                            Duration = TimeSpan.FromMilliseconds(260),
                            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
                        };
                        anim.EnableDependentAnimation = true;
                        Storyboard.SetTarget(anim, cardContent);
                        Storyboard.SetTargetProperty(anim, "MaxHeight");
                        var sb = new Storyboard();
                        sb.Children.Add(anim);
                        sb.Completed += (_, _) => { _heightStoryboard = null; heightDone = true; onBothDone(); };
                        _heightStoryboard = sb;
                        sb.Begin();
                        heightAnimStarted = true;
                    }
                }
            }
            catch { }
        }

        if (!heightAnimStarted)
        {
            heightDone = true;
            onBothDone();
        }
    }

    // Flight animation: floating button flies to target offset + fade out, then hide
    // / 飞行动画:浮动按钮飞向目标偏移+淡出后隐藏
    private void PlayFlightFade(double offsetX, double offsetY)
    {
        // Stop prior flight storyboard to break its HoldEnd / 停止之前飞行动画Storyboard断开HoldEnd
        _flightStoryboard?.Stop();
        var tt = CollapseFloatingBorder.RenderTransform as TranslateTransform ?? new TranslateTransform();
        CollapseFloatingBorder.RenderTransform = tt;
        // Manually set start values to break prior HoldEnd and ensure visible
        // / 手动设起始值断开之前HoldEnd并确保可见
        CollapseFloatingBorder.Visibility = Visibility.Visible;
        CollapseFloatingBorder.Opacity = 1;
        tt.X = 0; tt.Y = 0;

        // Flight ease: EaseOut (fast start, decelerate to target) / 飞行曲线: EaseOut(快速出发,减速到位)
        var flightEase = new CircleEase { EasingMode = EasingMode.EaseOut };
        // Fade ease: EaseIn (gradual disappear) / 淡出曲线: EaseIn(逐渐消失)
        var fadeEase = new CubicEase { EasingMode = EasingMode.EaseIn };
        var sb = new Storyboard();
        var dur = TimeSpan.FromMilliseconds(180);
        var xAnim = new DoubleAnimation { To = offsetX, Duration = dur, EasingFunction = flightEase };
        Storyboard.SetTarget(xAnim, tt);
        Storyboard.SetTargetProperty(xAnim, "X");
        sb.Children.Add(xAnim);
        var yAnim = new DoubleAnimation { To = offsetY, Duration = dur, EasingFunction = flightEase };
        Storyboard.SetTarget(yAnim, tt);
        Storyboard.SetTargetProperty(yAnim, "Y");
        sb.Children.Add(yAnim);
        // Fade starts AFTER flight completes (BeginTime = flight duration)
        // / 淡出在飞行完成后开始(BeginTime=飞行时长)
        var fadeDur = TimeSpan.FromMilliseconds(100);
        var opAnim = new DoubleAnimation { To = 0, BeginTime = dur, Duration = fadeDur, EasingFunction = fadeEase };
        Storyboard.SetTarget(opAnim, CollapseFloatingBorder);
        Storyboard.SetTargetProperty(opAnim, "Opacity");
        sb.Children.Add(opAnim);
        sb.Completed += (_, _) =>
        {
            CollapseFloatingBorder.Visibility = Visibility.Collapsed;
            CollapseFloatingBorder.Opacity = 0;
            tt.X = 0; tt.Y = 0;
            _flightStoryboard = null;
        };
        _flightStoryboard = sb;
        sb.Begin();
    }

    // Independent fade-in animation for expand button (separate from flight storyboard)
    // / 展开按钮独立淡入动画(与飞行动画分离)
    private void PlayFadeIn(FrameworkElement target)
    {
        _fadeInStoryboard?.Stop();
        target.Opacity = 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var sb = new Storyboard();
        var anim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(100),
            EasingFunction = ease
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, "Opacity");
        sb.Children.Add(anim);
        sb.Completed += (_, _) => { target.Opacity = 1; _fadeInStoryboard = null; };
        _fadeInStoryboard = sb;
        sb.Begin();
    }

    // Flight-in animation: floating button flies FROM expand button position TO fixed bottom position
    // / 飞入动画:浮动按钮从展开按钮位置飞到固定底部位置
    // offsetX/Y = expand button center - floating button fixed center (initial TranslateTransform)
    // / 偏移=展开按钮中心-浮动按钮固定中心(初始TranslateTransform)
    private void PlayFlightIn(double offsetX, double offsetY)
    {
        _flightStoryboard?.Stop();
        var tt = CollapseFloatingBorder.RenderTransform as TranslateTransform ?? new TranslateTransform();
        CollapseFloatingBorder.RenderTransform = tt;
        // Start at expand button position (offset), invisible / 从展开按钮位置(偏移)开始,不可见
        CollapseFloatingBorder.Visibility = Visibility.Visible;
        CollapseFloatingBorder.Opacity = 0;
        tt.X = offsetX; tt.Y = offsetY;

        var flightEase = new CircleEase { EasingMode = EasingMode.EaseOut };
        var fadeEase = new CubicEase { EasingMode = EasingMode.EaseOut };
        var sb = new Storyboard();
        var dur = TimeSpan.FromMilliseconds(180);
        // Fly to fixed position (0, 0) / 飞到固定位置(0,0)
        var xAnim = new DoubleAnimation { To = 0, Duration = dur, EasingFunction = flightEase };
        Storyboard.SetTarget(xAnim, tt);
        Storyboard.SetTargetProperty(xAnim, "X");
        sb.Children.Add(xAnim);
        var yAnim = new DoubleAnimation { To = 0, Duration = dur, EasingFunction = flightEase };
        Storyboard.SetTarget(yAnim, tt);
        Storyboard.SetTargetProperty(yAnim, "Y");
        sb.Children.Add(yAnim);
        // Fade in at flight START (visible during flight, not after)
        // / 飞行开始时淡入(飞行中可见,不是飞行后)
        var fadeDur = TimeSpan.FromMilliseconds(100);
        var opAnim = new DoubleAnimation { To = 1, Duration = fadeDur, EasingFunction = fadeEase };
        Storyboard.SetTarget(opAnim, CollapseFloatingBorder);
        Storyboard.SetTargetProperty(opAnim, "Opacity");
        sb.Children.Add(opAnim);
        sb.Completed += (_, _) =>
        {
            CollapseFloatingBorder.Opacity = 1;
            tt.X = 0; tt.Y = 0;
            _flightStoryboard = null;
        };
        _flightStoryboard = sb;
        sb.Begin();
    }

    // Find expand button in card by matching entry tag / 通过entry匹配查找卡片内展开按钮
    private static Button? FindExpandButton(DependencyObject root, ChangelogEntry entry)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button b && ReferenceEquals(b.Tag, entry)) return b;
            var found = FindExpandButton(child, entry);
            if (found is not null) return found;
        }
        return null;
    }
}
