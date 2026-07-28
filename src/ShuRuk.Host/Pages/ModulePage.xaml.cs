using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace ShuRuk.Host.Pages;

public sealed partial class ModulePage : Page
{
    private int _previousIndex = 0;

    public ModulePage()
    {
        InitializeComponent();
        Loaded += ModulePage_Loaded;
        ContentFrame.Navigated += ContentFrame_Navigated;
    }

    private void ModulePage_Loaded(object sender, RoutedEventArgs e)
    {
        LocalizationHelper.ApplyUidResources(this);

        SearchBox.PlaceholderText = LocalizationHelper.GetString("String_SearchModules");

        if (ContentFrame.Content is null && SubBar.SelectedItem is SelectorBarItem first)
        {
            var index = SubBar.Items.IndexOf(first);
            if (index >= 0) NavigateToIndex(index, animate: false);
        }
    }

    private void SubBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var selectedItem = sender.SelectedItem as SelectorBarItem;
        if (selectedItem is null) return;

        var currentIndex = sender.Items.IndexOf(selectedItem);
        if (currentIndex < 0) return;

        NavigateToIndex(currentIndex, animate: true);
    }

    // Pattern adapted from the official WinUI Gallery SelectorBarPage sample:
    // Frame.Navigate(pageType, null, new SlideNavigationTransitionInfo { Effect = ... })
    private void NavigateToIndex(int currentIndex, bool animate)
    {
        Type? pageType = currentIndex switch
        {
            0 => typeof(InstalledPage),
            1 => typeof(DiscoverPage),
            _ => null
        };
        if (pageType is null) return;
        if (ContentFrame.CurrentSourcePageType == pageType) return;

        SlideNavigationTransitionInfo? transition = null;
        if (animate)
        {
            var effect = currentIndex - _previousIndex > 0
                ? SlideNavigationTransitionEffect.FromRight
                : SlideNavigationTransitionEffect.FromLeft;
            transition = new SlideNavigationTransitionInfo { Effect = effect };
        }

        ContentFrame.Navigate(pageType, null, transition);
        _previousIndex = currentIndex;
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        // Re-apply the current search keyword to the page that just loaded.
        if (e.Content is ISearchablePage searchable)
        {
            searchable.ApplyFilter(SearchBox.Text);
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (ContentFrame.Content is ISearchablePage searchable)
        {
            searchable.ApplyFilter(sender.Text);
        }
    }

    public void SwitchToDiscoverTab()
    {
        var discoverItem = SubBar.Items.OfType<SelectorBarItem>().ElementAtOrDefault(1);
        if (discoverItem is not null)
        {
            SubBar.SelectedItem = discoverItem;
        }
    }
}
