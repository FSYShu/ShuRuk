namespace ShuRuk.Runtime.Contracts;

public interface IModuleContext
{
    T? GetService<T>() where T : class;

    string DataPath { get; }

    IConfigurationService Configuration { get; }

    void RegisterSettingsPage(object page);
}
