using System.ComponentModel;
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
    private const int SlideInAnimMs = 150;
    private const int StateAnimMs = 300;
    private const double CardSlideDistance = 16;
    private const double PanelSlideDistance = 20;

    // Last expanded entry for floating collapse button / 最近展开条目供浮动收起
    private ChangelogEntry? _lastExpandedEntry;
    private int _lastExpandedIndex = -1;

    // Floating collapse button state / 浮动收起按钮状态
    private bool _floatingCollapseVisible;
    private bool _isCollapsing;
    private Storyboard? _flightStoryboard;
    private Storyboard? _heightStoryboard;
    private Storyboard? _fadeInStoryboard;
    private readonly List<(Storyboard sb, TranslateTransform tt)> _belowCardsAnims = [];

    // Toggle expand/collapse; handles both featured and history cards
    // / 切换展开折叠;处理 featured 和 history 卡片
    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var entry = btn.DataContext as ChangelogEntry ?? btn.Tag as ChangelogEntry;
        if (entry is null) return;

        // History cards: simple collapse (Expander handles expand, no flight animation)
        // / history 卡片:简单收起(Expander处理展开,无飞行动画)
        if (entry.CardMode == CardMode.HistoryVersion && entry.IsExpanded)
        {
            int collapsedIdx = _displayItems.IndexOf(entry);
            entry.IsExpanded = false;
            EnsureCollapsedCardVisible(collapsedIdx);
            return;
        }

        PlayButtonPress(btn);

        if (!entry.IsExpanded)
        {
            // EXPANDING / 展开
            // For history cards: collapse ALL expanded history cards first (mutual exclusivity)
            // / history 卡片:先收起所有已展开的 history 卡片(互斥)
            if (entry.CardMode == CardMode.HistoryVersion)
            {
                foreach (var item in _displayItems)
                {
                    if (item.CardMode == CardMode.HistoryVersion && item.IsExpanded && !ReferenceEquals(item, entry))
                        item.IsExpanded = false;
                }
            }

            _lastExpandedEntry = entry;
            _lastExpandedIndex = _displayItems.IndexOf(entry);

            if (entry.CardMode == CardMode.HistoryVersion)
                _expandedHistoryEntry = entry;
            _isExpandingHistory = true;

            // Stop prior storyboards / 停止之前动画
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
                CollapseFloatingBorder.Visibility = Visibility.Visible;
                CollapseFloatingBorder.Opacity = 0;
                CollapseFloatingBorder.UpdateLayout();
                if (_lastExpandedIndex >= 0 && _lastExpandedIndex < _displayItems.Count
                    && ChangelogRepeater.GetOrCreateElement(_lastExpandedIndex) is FrameworkElement root
                    && FindExpandButton(root, entry) is Button eb)
                {
                    fadeTarget = eb.Parent as FrameworkElement
                        ?? VisualTreeHelper.GetParent(eb) as FrameworkElement ?? eb;
                    fadeTarget.Opacity = 0;
                    var relPos = eb.TransformToVisual(CollapseFloatingBorder).TransformPoint(new Point(0, 0));
                    offsetX = relPos.X + eb.ActualWidth / 2 - CollapseFloatingBorder.ActualWidth / 2;
                    offsetY = relPos.Y + eb.ActualHeight / 2 - CollapseFloatingBorder.ActualHeight / 2;
                }
            }
            catch { }

            double expandCardHeightBefore = 0;
            double cardTopBefore = double.NaN;
            if (_lastExpandedIndex >= 0 && _lastExpandedIndex < _displayItems.Count)
            {
                try
                {
                    if (ChangelogRepeater.GetOrCreateElement(_lastExpandedIndex) is FrameworkElement rootExp)
                    {
                        expandCardHeightBefore = rootExp.ActualHeight;
                        var tBefore = rootExp.TransformToVisual(ChangelogScrollViewer);
                        cardTopBefore = tBefore.TransformPoint(new Point(0, 0)).Y;
                    }
                }
                catch { }
            }

            entry.IsExpanded = true;
            _floatingCollapseVisible = true;

            if (expandCardHeightBefore > 0 && _lastExpandedIndex >= 0 && _lastExpandedIndex < _displayItems.Count)
            {
                try
                {
                    ChangelogRepeater.UpdateLayout();
                    if (ChangelogRepeater.GetOrCreateElement(_lastExpandedIndex) is FrameworkElement rootExp)
                    {
                        // Restore card top position relative to viewport / 恢复卡片顶部相对视口位置
                        if (!double.IsNaN(cardTopBefore))
                        {
                            var tAfter = rootExp.TransformToVisual(ChangelogScrollViewer);
                            double cardTopAfter = tAfter.TransformPoint(new Point(0, 0)).Y;
                            double scrollDelta = cardTopAfter - cardTopBefore;
                            if (Math.Abs(scrollDelta) > 1)
                            {
                                double newOffset = ChangelogScrollViewer.VerticalOffset + scrollDelta;
                                newOffset = Math.Clamp(newOffset, 0, ChangelogScrollViewer.ScrollableHeight);
                                ChangelogScrollViewer.ChangeView(null, newOffset, null, true);
                            }
                        }
                        double deltaY = rootExp.ActualHeight - expandCardHeightBefore;
                        PlayBelowCardsFlyOut(_lastExpandedIndex, deltaY);
                    }
                }
                catch { }
            }

            // Check if card's bottom collapse button is in viewport / 检查卡片底部收起按钮是否在视口内
            bool bottomButtonInViewport = false;
            try
            {
                if (_lastExpandedIndex >= 0 && _lastExpandedIndex < _displayItems.Count
                    && ChangelogRepeater.GetOrCreateElement(_lastExpandedIndex) is FrameworkElement rootCheck
                    && UiTreeHelper.FindDescendantByName(rootCheck, "BottomButtonBorder") is FrameworkElement bottomBorder
                    && bottomBorder.Visibility == Visibility.Visible)
                {
                    var borderPos = bottomBorder.TransformToVisual(ChangelogScrollViewer)
                        .TransformPoint(new Point(0, 0));
                    double borderBottom = borderPos.Y + bottomBorder.ActualHeight;
                    bottomButtonInViewport = borderBottom <= ChangelogScrollViewer.ViewportHeight && borderPos.Y >= 0;
                }
            }
            catch { }

            if (bottomButtonInViewport)
            {
                _floatingCollapseVisible = false;
                CollapseFloatingBorder.Visibility = Visibility.Collapsed;
                CollapseFloatingBorder.Opacity = 0;
                if (fadeTarget is not null) PlayFadeIn(fadeTarget);
            }
            else if (offsetX != 0 || offsetY != 0)
            {
                PlayFlightIn(offsetX, offsetY);
                if (fadeTarget is not null)
                {
                    var fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                    fadeTimer.Tick += (_, _) => { fadeTimer.Stop(); PlayFadeIn(fadeTarget); };
                    fadeTimer.Start();
                }
            }
            else
            {
                SetFloatingCollapseVisible(true, animate: false);
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (_lastExpandedIndex >= 0 && _lastExpandedIndex < _displayItems.Count
                    && ChangelogRepeater.GetOrCreateElement(_lastExpandedIndex) is FrameworkElement root
                    && UiTreeHelper.FindDescendant<ItemsRepeater>(root) is ItemsRepeater content)
                    PlayContentFadeIn(content);
            });

            // Re-evaluate floating collapse visibility after expand animation settles
            // / 展开动画稳定后重新评估浮动收起按钮可见性
            var reevalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            reevalTimer.Tick += (_, _) => { reevalTimer.Stop(); _isExpandingHistory = false; UpdateCollapseButton(); };
            reevalTimer.Start();
        }

        else
        {
            // COLLAPSING / 收起
            _isCollapsing = true;
            _flightStoryboard?.Stop();
            _flightStoryboard = null;
            _fadeInStoryboard?.Stop();
            _fadeInStoryboard = null;
            _heightStoryboard?.Stop();
            _heightStoryboard = null;
            StopBelowCardsAnimations();

            int collapseIdx = _displayItems.IndexOf(entry);
            Action completeCardCollapse = () =>
            {
                entry.IsExpanded = false;
                if (entry.CardMode == CardMode.HistoryVersion)
                    _expandedHistoryEntry = null;
                _lastExpandedEntry = null;
                _lastExpandedIndex = -1;
                SetFloatingCollapseVisible(false);
                EnsureNextCardTopVisible(collapseIdx);
                var tc = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
                tc.Tick += (_, _) => { tc.Stop(); _isCollapsing = false; };
                tc.Start();
            };

            // Height animation only for featured cards (history cards have no height limit)
            // / 仅 featured 卡片有高度动画(history 卡片无高度限制)
            bool cardHeightAnimStarted = false;
            if (entry.CardMode == CardMode.Featured && collapseIdx >= 0 && collapseIdx < _displayItems.Count)
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

    // Fade in content opacity / 淡入内容透明度
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

    private void StopBelowCardsAnimations()
    {
        foreach (var (sb, tt) in _belowCardsAnims)
        {
            sb.Stop();
            tt.Y = 0;
        }
        _belowCardsAnims.Clear();
    }

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

    // Show floating collapse button when card's bottom collapse button is off-screen
    // / 卡片底部收起按钮不可见时显示浮动收起按钮
    private void UpdateCollapseButton()
    {
        if (_isCollapsing) return;
        if (_isExpandingHistory) return;
        if (_lastExpandedEntry is null || !_lastExpandedEntry.IsExpanded
            || _lastExpandedIndex < 0 || _lastExpandedIndex >= _displayItems.Count)
        {
            SetFloatingCollapseVisible(false);
            return;
        }
        if (_lastExpandedEntry.BottomButtonVisibility == Visibility.Collapsed)
        {
            SetFloatingCollapseVisible(false);
            return;
        }
        var top = GetItemTop(_lastExpandedIndex);
        if (top is null) return;
        try
        {
            var el = ChangelogRepeater.GetOrCreateElement(_lastExpandedIndex);
            if (el is not FrameworkElement fe) { SetFloatingCollapseVisible(false); return; }
            if (_lastExpandedEntry.CardMode == CardMode.HistoryVersion)
            {
                if (UiTreeHelper.FindDescendantByName(fe, "BottomButtonBorder") is FrameworkElement otherBottomBorder
                    && otherBottomBorder.Visibility == Visibility.Visible)
                {
                    var otherBorderPos = otherBottomBorder.TransformToVisual(ChangelogScrollViewer)
                        .TransformPoint(new Point(0, 0));
                    double otherBorderBottom = otherBorderPos.Y + otherBottomBorder.ActualHeight;
                    double vh = ChangelogScrollViewer.ViewportHeight;
                    SetFloatingCollapseVisible(otherBorderBottom > vh, animate: false);
                }
                else
                {
                    double cardBottom = top.Value + fe.ActualHeight;
                    double vh = ChangelogScrollViewer.ViewportHeight;
                    SetFloatingCollapseVisible(cardBottom > vh, animate: false);
                }
                return;
            }
            // Find BottomButtonBorder by name for reliable position check
            // / 通过名称查找BottomButtonBorder以可靠地检查位置
            if (UiTreeHelper.FindDescendantByName(fe, "BottomButtonBorder") is FrameworkElement bottomBorder
                && bottomBorder.Visibility == Visibility.Visible)
            {
                var borderPos = bottomBorder.TransformToVisual(ChangelogScrollViewer)
                    .TransformPoint(new Point(0, 0));
                double borderBottom = borderPos.Y + bottomBorder.ActualHeight;
                SetFloatingCollapseVisible(borderBottom > ChangelogScrollViewer.ViewportHeight, animate: false);
            }
            else if (FindExpandButton(fe, _lastExpandedEntry) is Button expandBtn)
            {
                var btnPos = expandBtn.TransformToVisual(ChangelogScrollViewer)
                    .TransformPoint(new Point(0, 0));
                double btnBottom = btnPos.Y + expandBtn.ActualHeight;
                SetFloatingCollapseVisible(btnBottom > ChangelogScrollViewer.ViewportHeight, animate: false);
            }
            else
            {
                // Fallback: estimate button bottom as cardBottom - 8 (button margin)
                // / 回退:估算按钮底部为卡片底部-8(按钮边距)
                double estimatedBtnBottom = top.Value + fe.ActualHeight - 8;
                SetFloatingCollapseVisible(estimatedBtnBottom > ChangelogScrollViewer.ViewportHeight, animate: false);
            }
        }
        catch { }
    }

    private void SetFloatingCollapseVisible(bool visible, bool animate = true)
    {

        if (visible == _floatingCollapseVisible)
        {
            // Ensure visual state matches even when skipping
            // / 跳过时也确保视觉状态匹配
            if (!visible && CollapseFloatingBorder.Visibility != Visibility.Collapsed)
            {
                CollapseFloatingBorder.Visibility = Visibility.Collapsed;
                CollapseFloatingBorder.Opacity = 0;
            }
            // Field says visible but border is actually collapsed or transparent: force show
            // / 字段表示可见但border实际折叠或透明:强制显示
            if (visible && (CollapseFloatingBorder.Visibility != Visibility.Visible || CollapseFloatingBorder.Opacity < 1))
            {
                CollapseFloatingBorder.Visibility = Visibility.Visible;
                CollapseFloatingBorder.Opacity = 1;
                var ttForce = CollapseFloatingBorder.RenderTransform as TranslateTransform ?? new TranslateTransform();
                CollapseFloatingBorder.RenderTransform = ttForce;
                ttForce.X = 0; ttForce.Y = 0;
            }
            return;
        }
        _floatingCollapseVisible = visible;
        var tt = CollapseFloatingBorder.RenderTransform as TranslateTransform ?? new TranslateTransform();
        CollapseFloatingBorder.RenderTransform = tt;
        if (visible)
        {
            CollapseFloatingBorder.Visibility = Visibility.Visible;
            if (animate)
            {
                PlaySlideFade(tt, CollapseFloatingBorder, fromY: -40, toY: 0, fromOp: 0, toOp: 1, () =>
                {
                    CollapseFloatingBorder.Opacity = 1;
                    tt.Y = 0;
                });
            }
            else
            {
                CollapseFloatingBorder.Opacity = 1;
                tt.X = 0; tt.Y = 0;
            }
        }
        else
        {
            CollapseFloatingBorder.Visibility = Visibility.Collapsed;
            CollapseFloatingBorder.Opacity = 0;
            tt.Y = 0;
        }
    }

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
                if (top.Value > viewportH - 40)
                {
                    double targetOffset = ChangelogScrollViewer.VerticalOffset + top.Value - viewportH / 2;
                    targetOffset = Math.Clamp(targetOffset, 0, ChangelogScrollViewer.ScrollableHeight);
                    ChangelogScrollViewer.ChangeView(null, targetOffset, null, false);
                }
            }
            catch { }
        });
    }

    // Ensure the collapsed card itself is visible in viewport (not the next card)
    // / 确保收起的卡片本身在视口内可见(非下一个卡片)
    private void EnsureCollapsedCardVisible(int collapsedIdx)
    {
        if (collapsedIdx < 0 || collapsedIdx >= _displayItems.Count) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                ChangelogRepeater.UpdateLayout();
                double? top = GetItemTop(collapsedIdx);
                if (top is null) return;
                double viewportH = ChangelogScrollViewer.ViewportHeight;
                if (top.Value < 0)
                {
                    double targetOffset = ChangelogScrollViewer.VerticalOffset + top.Value - 8;
                    targetOffset = Math.Clamp(targetOffset, 0, ChangelogScrollViewer.ScrollableHeight);
                    ChangelogScrollViewer.ChangeView(null, targetOffset, null, false);
                }
                else if (top.Value > viewportH - 40)
                {
                    double targetOffset = ChangelogScrollViewer.VerticalOffset + top.Value - viewportH / 2;
                    targetOffset = Math.Clamp(targetOffset, 0, ChangelogScrollViewer.ScrollableHeight);
                    ChangelogScrollViewer.ChangeView(null, targetOffset, null, false);
                }
            }
            catch { }
        };
        timer.Start();
    }

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

    private void PlayAppearAnimation() => PlaySlideInAnimation(ContentPanel, PanelSlideDistance, SlideInAnimMs);

    // Collapse all history cards (e.g. when navigating back to featured)
    // / 收起所有 history 卡片(如导航回 featured 时)
    private void CollapseAllOtherCards()
    {
        if (_expandedHistoryEntry is null) return;
        _expandedHistoryEntry.IsExpanded = false;
        _expandedHistoryEntry = null;
        _lastExpandedEntry = null;
        _lastExpandedIndex = -1;
        SetFloatingCollapseVisible(false);
    }

    private void CollapseFloatingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastExpandedEntry is null) return;
        var entry = _lastExpandedEntry;
        var idx = _lastExpandedIndex;

        // History cards: simple collapse (Expander handles animation, no flight)
        // / history 卡片:简单收起(Expander处理动画,无飞行)
        if (entry.CardMode == CardMode.HistoryVersion)
        {
            int collapsedIdx = _displayItems.IndexOf(entry);
            entry.IsExpanded = false;
            EnsureCollapsedCardVisible(collapsedIdx);
            return;
        }

        _isCollapsing = true;

        _flightStoryboard?.Stop();
        _flightStoryboard = null;
        _fadeInStoryboard?.Stop();
        _fadeInStoryboard = null;
        _heightStoryboard?.Stop();
        _heightStoryboard = null;
        StopBelowCardsAnimations();

        Button? expandBtn = null;
        FrameworkElement? fadeTarget = null;
        double offsetX = 0, offsetY = 0;
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
                    if (UiTreeHelper.FindDescendantByName(root, "CardContentGrid") is FrameworkElement cardContent)
                        cardShrinkDelta = Math.Max(0, cardContent.ActualHeight - ChangelogEntry.CollapsedMaxHeight);
                }
            }
            catch { }
        }

        double flightOffsetY = offsetY - cardShrinkDelta;

        Action completeCollapse = () =>
        {
            entry.IsExpanded = false;
            if (entry.CardMode == CardMode.HistoryVersion)
                _expandedHistoryEntry = null;
            _lastExpandedEntry = null;
            _lastExpandedIndex = -1;
            _floatingCollapseVisible = false;
            EnsureNextCardTopVisible(idx);

            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            t.Tick += (_, _) => { t.Stop(); _isCollapsing = false; };
            t.Start();
        };

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

        if (expandBtn is not null)
        {
            PlayFlightFade(offsetX, flightOffsetY);
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

        // Height animation only for featured cards / 仅featured卡片有高度动画
        bool heightAnimStarted = false;
        if (entry.CardMode == CardMode.Featured && idx >= 0 && idx < _displayItems.Count)
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

    private void PlayFlightFade(double offsetX, double offsetY)
    {
        _flightStoryboard?.Stop();
        var tt = CollapseFloatingBorder.RenderTransform as TranslateTransform ?? new TranslateTransform();
        CollapseFloatingBorder.RenderTransform = tt;
        CollapseFloatingBorder.Visibility = Visibility.Visible;
        CollapseFloatingBorder.Opacity = 1;
        tt.X = 0; tt.Y = 0;

        var flightEase = new CircleEase { EasingMode = EasingMode.EaseOut };
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

    private void PlayFlightIn(double offsetX, double offsetY)
    {
        _flightStoryboard?.Stop();
        var tt = CollapseFloatingBorder.RenderTransform as TranslateTransform ?? new TranslateTransform();
        CollapseFloatingBorder.RenderTransform = tt;
        CollapseFloatingBorder.Visibility = Visibility.Visible;
        CollapseFloatingBorder.Opacity = 0;
        tt.X = offsetX; tt.Y = offsetY;

        var flightEase = new CircleEase { EasingMode = EasingMode.EaseOut };
        var fadeEase = new CubicEase { EasingMode = EasingMode.EaseOut };
        var sb = new Storyboard();
        var dur = TimeSpan.FromMilliseconds(180);
        var xAnim = new DoubleAnimation { To = 0, Duration = dur, EasingFunction = flightEase };
        Storyboard.SetTarget(xAnim, tt);
        Storyboard.SetTargetProperty(xAnim, "X");
        sb.Children.Add(xAnim);
        var yAnim = new DoubleAnimation { To = 0, Duration = dur, EasingFunction = flightEase };
        Storyboard.SetTarget(yAnim, tt);
        Storyboard.SetTargetProperty(yAnim, "Y");
        sb.Children.Add(yAnim);
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

    // Find the visible expand/collapse button for an entry, skipping buttons inside collapsed containers
    // / 查找条目的可见展开/收起按钮,跳过折叠容器内的按钮
    private static Button? FindExpandButton(DependencyObject root, ChangelogEntry entry)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button b && ReferenceEquals(b.Tag, entry) && b.Visibility == Visibility.Visible
                && IsAncestorChainVisible(b))
                return b;
            var found = FindExpandButton(child, entry);
            if (found is not null) return found;
        }
        return null;
    }

    // Check if all ancestors up to the visual tree root are Visible
    // / 检查所有祖先元素是否可见(无 Collapsed 容器)
    private static bool IsAncestorChainVisible(FrameworkElement element)
    {
        DependencyObject? node = VisualTreeHelper.GetParent(element);
        while (node is not null)
        {
            if (node is FrameworkElement fe && fe.Visibility == Visibility.Collapsed)
                return false;
            node = VisualTreeHelper.GetParent(node);
        }
        return true;
    }

    // Handle IsExpanded change via PropertyChanged (fallback for when Expander
    // Expanding/Collapsed events don't fire due to TwoWay binding)
    // / 通过PropertyChanged处理IsExpanded变化(Expander的Expanding/Collapsed事件
    // 在TwoWay绑定下可能不触发)
    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ChangelogEntry entry) return;
        if (e.PropertyName != nameof(ChangelogEntry.IsExpanded)) return;


        if (entry.CardMode != CardMode.HistoryVersion) return;

        if (entry.IsExpanded)
        {
            // History card expanding via chevron: Expanding event may not fire
            // with TwoWay binding, so handle it here
            // / history 卡片通过 chevron 展开:TwoWay 绑定下 Expanding 事件可能不触发,
            // 在此处理
            _isExpandingHistory = true;

            // Record card position BEFORE layout updates (mutual exclusivity
            // collapse may shift this card's position upward)
            // / 布局更新前记录卡片位置(互斥收起可能使此卡片位置上移)
            double cardTopBefore = double.NaN;
            int expandIdx = _displayItems.IndexOf(entry);
            if (expandIdx >= 0 && expandIdx < _displayItems.Count)
            {
                try
                {
                    if (ChangelogRepeater.GetOrCreateElement(expandIdx) is FrameworkElement rootExp)
                    {
                        var tBefore = rootExp.TransformToVisual(ChangelogScrollViewer);
                        cardTopBefore = tBefore.TransformPoint(new Point(0, 0)).Y;
                    }
                }
                catch { }
            }

            // Mutual exclusivity: collapse ALL expanded history cards except this one
            // / 互斥:收起所有已展开的 history 卡片(除当前卡片外)
            foreach (var item in _displayItems)
            {
                if (item.CardMode == CardMode.HistoryVersion && item.IsExpanded && !ReferenceEquals(item, entry))
                    item.IsExpanded = false;
            }

            _expandedHistoryEntry = entry;
            _lastExpandedEntry = entry;
            _lastExpandedIndex = expandIdx;

            SetFloatingCollapseVisible(true, animate: false);

            // Restore card top position: mutual exclusivity collapse may have
            // shifted this card upward; adjust scroll offset to compensate.
            // / 恢复卡片顶部位置:互斥收起可能使此卡片上移,调整滚动偏移补偿
            RestoreExpandedCardPosition(cardTopBefore, expandIdx);

            // Fade in version detail panel inside the card (transition from
            // "History versions" label to version number/date/prerelease)
            // / 淡入卡片内版本详情面板(从"历史版本"标签过渡到版本号/日期/预发布)
            FadeInVersionDetailPanel(expandIdx);

            // Update sticky overlays immediately after expand state change
            // / 展开状态变化后立即更新sticky覆盖层
            UpdateStickyVersionLayers();

            // Re-evaluate after expansion animation completes
            // / 展开动画完成后重新评估
            var reevalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            reevalTimer.Tick += (_, _) =>
            {
                reevalTimer.Stop();
                _isExpandingHistory = false;
                UpdateCollapseButton();
                UpdateStickyVersionLayers();
                var reevalTimer2 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                reevalTimer2.Tick += (_, _) => { reevalTimer2.Stop(); UpdateCollapseButton(); UpdateStickyVersionLayers(); };
                reevalTimer2.Start();
            };
            reevalTimer.Start();
        }
        else
        {
            // History card collapsing via chevron: Collapsed event may not fire
            // with TwoWay binding, so handle it here
            // / history 卡片通过 chevron 收起:TwoWay 绑定下 Collapsed 事件可能不触发,
            // 在此处理
            if (_isExpandingHistory) return;

            // Record card position before collapse for scroll position restoration
            // / 收起前记录卡片位置用于滚动位置恢复
            double cardTopBeforeCollapse = double.NaN;
            int collapseIdx = _displayItems.IndexOf(entry);
            if (collapseIdx >= 0 && collapseIdx < _displayItems.Count)
            {
                try
                {
                    if (ChangelogRepeater.GetOrCreateElement(collapseIdx) is FrameworkElement rootCol)
                    {
                        var tBefore = rootCol.TransformToVisual(ChangelogScrollViewer);
                        cardTopBeforeCollapse = tBefore.TransformPoint(new Point(0, 0)).Y;
                    }
                }
                catch { }
            }

            if (ReferenceEquals(_expandedHistoryEntry, entry))
                _expandedHistoryEntry = null;
            if (ReferenceEquals(_lastExpandedEntry, entry))
            {
                _lastExpandedEntry = null;
                _lastExpandedIndex = -1;
            }
            SetFloatingCollapseVisible(false);

            // Restore card top position after collapse: card height shrinks,
            // adjust scroll offset to keep the card in view.
            // / 收起后恢复卡片顶部位置:卡片高度缩小,调整滚动偏移保持卡片可见
            RestoreExpandedCardPosition(cardTopBeforeCollapse, collapseIdx);

            // Update sticky overlays after collapse state change
            // / 收起状态变化后更新sticky覆盖层
            UpdateStickyVersionLayers();
        }
    }

    // Restore card top position after expand: prevents content above from
    // shifting when mutual exclusivity collapses another expanded card.
    // / 展开后恢复卡片顶部位置:防止互斥收起上方卡片时当前位置偏移
    private void RestoreExpandedCardPosition(double cardTopBefore, int expandIdx)
    {
        if (double.IsNaN(cardTopBefore) || expandIdx < 0 || expandIdx >= _displayItems.Count) return;
        // Defer UpdateLayout to next frame to avoid LayoutCycleException
        // / 将UpdateLayout延迟到下一帧避免LayoutCycleException
        double savedTopBefore = cardTopBefore;
        int savedIdx = expandIdx;
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                ChangelogRepeater.UpdateLayout();
                if (ChangelogRepeater.GetOrCreateElement(savedIdx) is FrameworkElement rootExp)
                {
                    var tAfter = rootExp.TransformToVisual(ChangelogScrollViewer);
                    double cardTopAfter = tAfter.TransformPoint(new Point(0, 0)).Y;
                    double scrollDelta = cardTopAfter - savedTopBefore;
                    if (Math.Abs(scrollDelta) > 1)
                    {
                        double newOffset = ChangelogScrollViewer.VerticalOffset + scrollDelta;
                        newOffset = Math.Clamp(newOffset, 0, ChangelogScrollViewer.ScrollableHeight);
                        ChangelogScrollViewer.ChangeView(null, newOffset, null, false);
                    }
                }
            }
            catch { }
        });
    }

    // Fade in the version detail panel inside a card after expand
    // / 展开后淡入卡片内版本详情面板
    private void FadeInVersionDetailPanel(int cardIdx)
    {
        if (cardIdx < 0 || cardIdx >= _displayItems.Count) return;
        try
        {
            if (ChangelogRepeater.GetOrCreateElement(cardIdx) is FrameworkElement root)
            {
                var panel = FindDescendantByName(root, "VersionDetailPanel");
                if (panel is not null)
                {
                    panel.Opacity = 0;
                    var fadeAnim = new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(StateAnimMs),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    };
                    Storyboard.SetTarget(fadeAnim, panel);
                    Storyboard.SetTargetProperty(fadeAnim, "Opacity");
                    var sb = new Storyboard();
                    sb.Children.Add(fadeAnim);
                    sb.Begin();
                }
            }
        }
        catch { }
    }

    // Find a descendant element by name in the visual tree
    // / 在可视树中按名称查找后代元素
    private static FrameworkElement? FindDescendantByName(DependencyObject root, string name)
    {
        if (root is FrameworkElement fe && fe.Name == name) return fe;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var result = FindDescendantByName(child, name);
            if (result is not null) return result;
        }
        return null;
    }

    // Expander expanding: handle mutual exclusivity for history cards
    // / Expander 展开:处理 history 卡片互斥
    private void HistoryExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        var entry = sender.DataContext as ChangelogEntry;
        if (entry is null) return;

        // Set flag BEFORE mutual exclusivity: collapsing history cards triggers
        // HistoryExpander_Collapsed which must not hide the floating button
        // / 互斥操作前设置标志:收起其他 history 卡片会触发 HistoryExpander_Collapsed,
        // 不能让它隐藏浮动按钮
        _isExpandingHistory = true;

        // Mutual exclusivity: collapse ALL expanded history cards except this one
        // / 互斥:收起所有已展开的 history 卡片(除当前卡片外)
        foreach (var item in _displayItems)
        {
            if (item.CardMode == CardMode.HistoryVersion && item.IsExpanded && !ReferenceEquals(item, entry))
                item.IsExpanded = false;
        }

        _expandedHistoryEntry = entry;
        _lastExpandedEntry = entry;
        _lastExpandedIndex = _displayItems.IndexOf(entry);

        // Record card position before layout changes / 布局变化前记录卡片位置
        double cardTopBefore = double.NaN;
        if (_lastExpandedIndex >= 0 && _lastExpandedIndex < _displayItems.Count)
        {
            try
            {
                if (ChangelogRepeater.GetOrCreateElement(_lastExpandedIndex) is FrameworkElement rootExp)
                {
                    var tBefore = rootExp.TransformToVisual(ChangelogScrollViewer);
                    cardTopBefore = tBefore.TransformPoint(new Point(0, 0)).Y;
                }
            }
            catch { }
        }


        SetFloatingCollapseVisible(true, animate: false);

        // Restore card top position after mutual exclusivity collapse
        // / 互斥收起后恢复卡片顶部位置
        RestoreExpandedCardPosition(cardTopBefore, _lastExpandedIndex);

        // Fade in version detail panel inside the card
        // / 淡入卡片内版本详情面板
        FadeInVersionDetailPanel(_lastExpandedIndex);

        // Update sticky overlays immediately after expand state change
        // / 展开状态变化后立即更新sticky覆盖层
        UpdateStickyVersionLayers();

        // Re-evaluate after expansion animation completes (card height may still be changing)
        // / 展开动画完成后重新评估(卡片高度可能仍在变化)
        var reevalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        reevalTimer.Tick += (_, _) =>
        {
            reevalTimer.Stop();
            _isExpandingHistory = false;
            UpdateCollapseButton();
            UpdateStickyVersionLayers();
            // Secondary re-evaluation: markdown content renders asynchronously and
            // may not be complete at 500ms, causing height misjudgment.
            // Re-check after additional delay when BottomButtonBorder should be ready.
            // / 二次评估:markdown内容异步渲染,500ms时可能未完成导致高度误判,
            // 额外延迟后BottomButtonBorder应已就绪,再次检查
            var reevalTimer2 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            reevalTimer2.Tick += (_, _) => { reevalTimer2.Stop(); UpdateCollapseButton(); UpdateStickyVersionLayers(); };
            reevalTimer2.Start();
        };
        reevalTimer.Start();
    }

    // Expander collapsed: clear state for history cards
    // / Expander 收起:清除 history 卡片状态
    private void HistoryExpander_Collapsed(Expander sender, ExpanderCollapsedEventArgs args)
    {
        var entry = sender.DataContext as ChangelogEntry;

        if (entry is null) return;

        // Skip during mutual exclusivity collapse: another card is expanding
        // and will manage the floating button. This prevents Collapsed from
        // overriding SetFloatingCollapseVisible(true) in HistoryExpander_Expanding.
        // / 互斥收起时跳过:另一个卡片正在展开,会管理浮动按钮
        // 防止Collapsed覆盖HistoryExpander_Expanding中的SetFloatingCollapseVisible(true)
        if (_isExpandingHistory) return;

        if (ReferenceEquals(_expandedHistoryEntry, entry))
            _expandedHistoryEntry = null;
        if (ReferenceEquals(_lastExpandedEntry, entry))
        {
            _lastExpandedEntry = null;
            _lastExpandedIndex = -1;
        }
        SetFloatingCollapseVisible(false);
    }
}
