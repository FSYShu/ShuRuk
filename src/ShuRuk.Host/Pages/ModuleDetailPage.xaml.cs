using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ShuRuk.Contracts.Interfaces;

namespace ShuRuk.Host.Pages;

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
            ModuleVersion.Text = "—";
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

        var manager = App.Services.GetRequiredService<IModuleManager>();

        var dialog = _card.Source == Contracts.Enums.ModuleSource.BuiltIn
            ? new ContentDialog
            {
                Title = LocalizationHelper.GetString("String_ModuleDetailDialogTitle"),
                Content = LocalizationHelper.GetStringFormatted(
                    "String_ModuleDetailDialogLogicalContentFormat",
                    _card.DisplayName),
                PrimaryButtonText = LocalizationHelper.GetString("String_ModuleDetailUninstallLogical"),
                CloseButtonText = LocalizationHelper.GetString("String_DiscoverDialogCancel"),
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
                XamlRoot = XamlRoot
            };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await manager.UninstallModuleAsync(_card.Name);
            Frame.GoBack();
        }
    }

    private async void ReinstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_card is null) return;

        var manager = App.Services.GetRequiredService<IModuleManager>();
        await manager.ReinstallBuiltInModuleAsync(_card.Name);
        Frame.GoBack();
    }
}
