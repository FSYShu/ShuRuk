using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ShuRuk.App.Models;

namespace ShuRuk.App.Controls;

// Module navigation view: flat sub-module entries / 模块导航栏：平铺子模块条目
public sealed partial class ModuleNavView : UserControl
{
    public ModuleNavView()
    {
        InitializeComponent();
    }

    // Sub-modules to display / 要显示的子模块列表
    public IReadOnlyList<SubModuleNavModel> SubModules
    {
        get => (IReadOnlyList<SubModuleNavModel>)GetValue(SubModulesProperty);
        set => SetValue(SubModulesProperty, value);
    }

    public static readonly DependencyProperty SubModulesProperty =
        DependencyProperty.Register(nameof(SubModules), typeof(IReadOnlyList<SubModuleNavModel>), typeof(ModuleNavView), new PropertyMetadata(Array.Empty<SubModuleNavModel>(), OnSubModulesChanged));

    private static void OnSubModulesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ModuleNavView view)
            view.SubModuleList.ItemsSource = e.NewValue as System.Collections.IEnumerable;
    }

    // Raised when a sub-module is selected / 子模块被选中时触发
    public event EventHandler<SubModuleNavModel>? SubModuleSelected;

    private void SubModuleList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SubModuleNavModel model)
            SubModuleSelected?.Invoke(this, model);
    }
}