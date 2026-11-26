using CommunityToolkit.Mvvm.ComponentModel;

namespace ShuRuk.App.ViewModels;

public sealed partial class ModuleHostViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ModuleId { get; set; } = string.Empty;
}
