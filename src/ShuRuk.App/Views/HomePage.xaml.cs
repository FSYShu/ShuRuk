using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ShuRuk.App;
using ShuRuk.App.Helpers;

namespace ShuRuk.App.Views;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationHelper.ApplyUidResources(this);
    }

}