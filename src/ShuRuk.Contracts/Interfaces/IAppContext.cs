namespace ShuRuk.Contracts.Interfaces;

// Application context interface for decoupling UI from App / 应用上下文接口，解耦UI与App层
public interface IAppContext
{
    IServiceProvider Services { get; }
    string AppTitle { get; }
    string AppSubtitle { get; }
    string AppVersion { get; }
    void ApplyTheme(string theme);
    void Restart();
    void CloseMainWindow();
}
