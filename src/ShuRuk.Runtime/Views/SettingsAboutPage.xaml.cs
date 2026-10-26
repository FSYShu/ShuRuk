using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ShuRuk.Runtime.Helpers;
using System.Collections.Generic;

namespace ShuRuk.Runtime.Views;

public sealed partial class SettingsAboutPage : Page
{
    public static readonly DependencyProperty ModuleNameProperty =
        DependencyProperty.Register(nameof(ModuleName), typeof(string), typeof(SettingsAboutPage), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ModuleDescriptionProperty =
        DependencyProperty.Register(nameof(ModuleDescription), typeof(string), typeof(SettingsAboutPage), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ModuleVersionProperty =
        DependencyProperty.Register(nameof(ModuleVersion), typeof(string), typeof(SettingsAboutPage), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ModuleAuthorProperty =
        DependencyProperty.Register(nameof(ModuleAuthor), typeof(string), typeof(SettingsAboutPage), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SettingsItemsProperty =
        DependencyProperty.Register(nameof(SettingsItems), typeof(IEnumerable<SettingsItem>), typeof(SettingsAboutPage), null);

    public string ModuleName { get => (string)GetValue(ModuleNameProperty); set => SetValue(ModuleNameProperty, value); }
    public string ModuleDescription { get => (string)GetValue(ModuleDescriptionProperty); set => SetValue(ModuleDescriptionProperty, value); }
    public string ModuleVersion { get => (string)GetValue(ModuleVersionProperty); set => SetValue(ModuleVersionProperty, value); }
    public string ModuleAuthor { get => (string)GetValue(ModuleAuthorProperty); set => SetValue(ModuleAuthorProperty, value); }
    public IEnumerable<SettingsItem> SettingsItems { get => (IEnumerable<SettingsItem>)GetValue(SettingsItemsProperty); set => SetValue(SettingsItemsProperty, value); }

    public SettingsAboutPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RuntimeLocalizationHelper.ApplyUidResources(this);
        RuntimeAboutVersion.Text = RuntimeLocalizationHelper.GetString("RuntimeAboutVersion.Text");
        RuntimeAboutAuthor.Text = RuntimeLocalizationHelper.GetString("RuntimeAboutAuthor.Text");
    }
}

/// <summary>
/// Settings item for SettingsAboutPage / 设置关于页的设置项
/// </summary>
public sealed class SettingsItem
{
    public string Header { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public object? Content { get; set; }
}