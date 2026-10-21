using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ShuRuk.App;
using ShuRuk.App.Helpers;
using ShuRuk.App.ViewModels;
using ShuRuk.Runtime.Abstractions.Interfaces;

namespace ShuRuk.App.Views;

public sealed partial class ModuleDetailPage : Page
{
    private ModuleCardViewModel? _card;

    // Constructor: init and subscribe to Loaded / 构造函数:初始化并订阅Loaded事件
    public ModuleDetailPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    // Apply localized resources on page load / 页面加载时应用本地化资源
    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationHelper.ApplyUidResources(this);
    }

    // Receive module card and populate detail fields / 接收模块卡片并填充详情字段
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is ModuleCardViewModel card)
        {
            _card = card;
            ModuleDescription.Text = card.DisplayName;
            ModuleVersion.Text = LocalizationHelper.GetString("String_Unknown");
            ModuleSource.Text = card.Source == Runtime.Abstractions.Models.ModuleSource.BuiltIn
                ? LocalizationHelper.GetString("String_ModuleDetailSourceBuiltIn")
                : LocalizationHelper.GetString("String_ModuleDetailSourceExternal");
            ModuleState.Text = card.State.ToString();

            if (card.Source == Runtime.Abstractions.Models.ModuleSource.BuiltIn)
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

    // Confirm and uninstall current module / 确认并卸载当前模块
    private async void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_card is null) return;

        try
        {
            var manager = AppContextAccessor.Current.Services.GetRequiredService<IModuleManager>();

            var dialog = _card.Source == Runtime.Abstractions.Models.ModuleSource.BuiltIn
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
            CrashLogger.LogCrash("UninstallButton_Click", ex);
        }
    }

    // Reinstall built-in module and go back / 重新安装内置模块并返回
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
            CrashLogger.LogCrash("ReinstallButton_Click", ex);
        }
    }
}
