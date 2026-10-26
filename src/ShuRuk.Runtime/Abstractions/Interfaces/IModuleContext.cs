namespace ShuRuk.Runtime.Abstractions.Interfaces;

public interface IModuleContext
{
    T? GetService<T>() where T : class;

    string DataPath { get; }

    IConfigurationService Configuration { get; }

    void RegisterSettingsPage(object page);
}