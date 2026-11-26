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
        // Disabled: Enabled triggers "Element is already the child" COMException in WinUI 3
        // Disabled:Enabled在WinUI3触发"Element is already the child"异常
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
        InitializeComponent();
    }
}
