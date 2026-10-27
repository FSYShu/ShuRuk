using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ShuRuk.App;
using ShuRuk.App.Helpers;
using ShuRuk.App.ViewModels;
using ShuRuk.Runtime.Abstractions.Interfaces;
using ShuRuk.Runtime.Abstractions.Models;

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

            // Header / 头部

            ModuleDescription.Text = card.DisplayName;

            // Info card / 信息卡片
            ModuleId.Text = card.Name;
            ModuleAuthor.Text = LocalizationHelper.GetString("String_ModuleDetailAuthorUnknown");
            ModuleVersion.Text = string.IsNullOrEmpty(card.Version)
                ? LocalizationHelper.GetString("String_Unknown")
                : card.Version;
            ModuleSourceValue.Text = card.Source == ModuleSource.BuiltIn
                ? LocalizationHelper.GetString("String_ModuleDetailSourceBuiltIn")
                : LocalizationHelper.GetString("String_ModuleDetailSourceExternal");
            ModuleStateValue.Text = card.State.ToString();
            ModuleDescriptionText.Text = card.Description;

            // Pivot placeholders / 选择栏占位
            IntroPlaceholder.Text = LocalizationHelper.GetString("String_ModuleDetailIntroPlaceholder");
            ChangelogPlaceholder.Text = LocalizationHelper.GetString("String_ModuleDetailChangelogPlaceholder");

            // Action buttons visibility / 操作按钮可见性
            if (card.Source == ModuleSource.BuiltIn)
            {
                ModuleDetailUninstallButton.Content = LocalizationHelper.GetString("String_ModuleDetailUninstallLogical");
                ModuleDetailReinstallButton.Visibility = Visibility.Visible;
            }
            else
            {
                ModuleDetailUninstallButton.Content = LocalizationHelper.GetString("String_ModuleDetailUninstallPhysical");
                ModuleDetailReinstallButton.Visibility = Visibility.Collapsed;
            }

            // Show install button for discovered external modules / 外部未安装模块显示安装按钮
            if (card.Source == ModuleSource.External && card.State == ModuleState.Discovered)
            {
                ModuleDetailInstallButton.Visibility = Visibility.Visible;
            }
        }
    }

    // Switch content panel on SelectorBar change / 选择栏切换内容面板
    private void DetailSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var selectedIndex = sender.Items.IndexOf(sender.SelectedItem);
        if (selectedIndex == 0)
        {
            IntroPlaceholder.Visibility = Visibility.Visible;
            ChangelogPlaceholder.Visibility = Visibility.Collapsed;
        }
        else
        {
            IntroPlaceholder.Visibility = Visibility.Collapsed;
            ChangelogPlaceholder.Visibility = Visibility.Visible;
        }
    }

    // Placeholder: install current module / 占位：安装当前模块
    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowNotImplementedDialog();
    }

    // Placeholder: update current module / 占位：更新当前模块
    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowNotImplementedDialog();
    }

    // Confirm and uninstall current module / 确认并卸载当前模块
    private async void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_card is null) return;

        try
        {
            var manager = AppContextAccessor.Current.Services.GetRequiredService<IModuleManager>();

            var dialog = _card.Source == ModuleSource.BuiltIn
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

    // Show "not implemented" notice / 显示"功能开发中"提示
    private async Task ShowNotImplementedDialog()
    {
        var dialog = new ContentDialog
        {
            Title = LocalizationHelper.GetString("String_ModuleDetailNotImplementedTitle"),
            Content = LocalizationHelper.GetString("String_ModuleDetailNotImplementedContent"),
            CloseButtonText = LocalizationHelper.GetString("String_DiscoverDialogCancel"),
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
