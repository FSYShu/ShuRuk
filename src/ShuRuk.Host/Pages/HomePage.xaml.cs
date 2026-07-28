using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ShuRuk.Host.Pages;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationHelper.ApplyUidResources(this);
    }

}