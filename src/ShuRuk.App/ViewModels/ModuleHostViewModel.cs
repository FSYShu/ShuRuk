using System.ComponentModel;

namespace ShuRuk.App.ViewModels;

// View model for module host page / 模块承载页视图模型
public sealed class ModuleHostViewModel : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _moduleId = string.Empty;

    // Module display title / 模块显示标题
    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(nameof(Title)); }
    }

    // Active module identifier / 当前模块标识
    public string ModuleId
    {
        get => _moduleId;
        set { _moduleId = value; OnPropertyChanged(nameof(ModuleId)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}