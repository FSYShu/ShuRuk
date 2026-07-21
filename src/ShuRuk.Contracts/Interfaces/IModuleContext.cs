namespace ShuRuk.Contracts.Interfaces;

public interface IModuleContext
{
    /// <summary>
/// Retrieves a service instance of the specified type from the module context.
/// </summary>
/// <typeparam name="T">The service type to retrieve.</typeparam>
/// <returns>The requested service instance, or <c>null</c> if it is unavailable.</returns>
T? GetService<T>() where T : class;

    string DataPath { get; }

    IConfigurationService Configuration { get; }

    /// <summary>
/// Registers a settings page with the module context.
/// </summary>
/// <param name="page">The settings page to register.</param>
void RegisterSettingsPage(object page);
}