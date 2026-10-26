using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ShuRuk.Runtime.Controls;

public sealed partial class SubModuleOverviewCard : UserControl
{
    public static readonly DependencyProperty ModuleNameProperty =
        DependencyProperty.Register(nameof(ModuleName), typeof(string), typeof(SubModuleOverviewCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SubModuleOverviewCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(SubModuleOverviewCard), new PropertyMetadata("\uE7B3"));

    public string ModuleName { get => (string)GetValue(ModuleNameProperty); set => SetValue(ModuleNameProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }

    public SubModuleOverviewCard()
    {
        InitializeComponent();
    }
}