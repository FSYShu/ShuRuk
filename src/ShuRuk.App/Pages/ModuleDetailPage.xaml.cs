using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ShuRuk.App;
using ShuRuk.Contracts.Interfaces;

namespace ShuRuk.App.Pages;

public sealed partial class ModuleDetailPage : Page
{
    private ModuleCardViewModel? _card;

    public ModuleDetailPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationHelper.ApplyUidResources(this);
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is ModuleCardViewModel card)
        {
            _card = card;
            ModuleDescription.Text = card.DisplayName;
            ModuleVersion.Text = LocalizationHelper.GetString("String_Unknown");
            ModuleSource.Text = card.Source == Contracts.Enums.ModuleSource.BuiltIn
                ? LocalizationHelper.GetString("String_ModuleDetailSourceBuiltIn")
                : LocalizationHelper.GetString("String_ModuleDetailSourceExternal");
            ModuleState.Text = card.State.ToString();

            if (card.Source == Contracts.Enums.ModuleSource.BuiltIn)
            {
                ModuleDetailUninstallButton.Content = LocalizationHelper.GetString("String_ModuleDetailUninstallLogical");
                ModuleDetailReinstallButton.Visibility = Visibility.Visible;
            }
            else
            {
                ModuleDetailUninstallButton.Content = LocalizationHelper.GetString("String_ModuleDetailUninstallPhysical");
                ModuleDetailReinstallButton.Visibility = Visibility.Collapsed;
            }
        }
    }

    private async void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_card is null) return;

        try
        {
            var manager = AppContextAccessor.Current.Services.GetRequiredService<IModuleManager>();

            var dialog = _card.Source == Contracts.Enums.ModuleSource.BuiltIn
                ? new ContentDialog
                {
                    Title = LocalizationHelper.GetString("String_ModuleDetailDialogTitle"),
                    Content = LocalizationHelper.GetStringFormatted(
                        "String_ModuleDetailDialogLogicalContentFormat",
                        _card.DisplayName),
                    PrimaryButtonText = LocalizationHelper.GetString("String_ModuleDetailUninstallLogical"),
                    CloseButtonText = LocalizationHelper.GetString("String_DiscoverDialogCancel"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot
                }
                : new ContentDialog
                {
                    Title = LocalizationHelper.GetString("String_ModuleDetailDialogTitle"),
                    Content = LocalizationHelper.GetStringFormatted(
                        "String_ModuleDetailDialogExternalContentFormat",
                        _card.DisplayName),
                    PrimaryButtonText = LocalizationHelper.GetString("String_ModuleDetailUninstallPhysical"),
                    CloseButtonText = LocalizationHelper.GetString("String_DiscoverDialogCancel"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot
                };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await manager.UninstallModuleAsync(_card.Name);
                Frame.GoBack();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UninstallButton_Click: {ex}");
        }
    }

    private async void ReinstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_card is null) return;

        try
        {
            var manager = AppContextAccessor.Current.Services.GetRequiredService<IModuleManager>();
            await manager.ReinstallBuiltInModuleAsync(_card.Name);
            Frame.GoBack();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ReinstallButton_Click: {ex}");
        }
    }
}
