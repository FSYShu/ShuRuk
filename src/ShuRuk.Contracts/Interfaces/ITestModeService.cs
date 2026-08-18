namespace ShuRuk.Contracts.Interfaces;

public interface ITestModeService
{
    bool IsEnabled { get; }

    event EventHandler<bool>? TestModeChanged;

    Task InitializeAsync();

    Task SetEnabledAsync(bool enabled);
}