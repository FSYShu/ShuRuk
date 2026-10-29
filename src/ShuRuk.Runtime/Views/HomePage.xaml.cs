using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ShuRuk.Runtime.Helpers;

namespace ShuRuk.Runtime.Views;

public sealed partial class HomePage : Page
{
    public string WelcomeTitle => LocalizationHelper.Runtime.GetString("HomeWelcome.Title");
    public string WelcomeMessage => LocalizationHelper.Runtime.GetString("HomeWelcome.Message");

    public HomePage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        InitializeComponent();
    }
}