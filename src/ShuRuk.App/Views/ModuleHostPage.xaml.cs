using Microsoft.UI.Xaml.Controls;

namespace ShuRuk.App.Views;

// Module host page: container for module function pages / 模块功能页承载容器
public sealed partial class ModuleHostPage : Page
{
    public ModuleHostPage()
    {
        InitializeComponent();
    }

    // Embed a module page into the host frame / 将模块页面嵌入承载Frame
    public void HostContent(object? content)
    {
        HostFrame.Content = content;
    }

    // Navigate to a module page type / 导航到模块页面类型
    public bool HostNavigate(Type pageType, object? parameter = null)
    {
        return HostFrame.Navigate(pageType, parameter);
    }
}