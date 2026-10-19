namespace ShuRuk.Runtime.Abstractions.Interfaces;

public interface ITestModeService
{
    bool IsEnabled { get; }
    bool IsFillDataEnabled { get; }

    event EventHandler<bool>? TestModeChanged;
    event EventHandler<bool>? FillDataChanged;

    Task InitializeAsync();
    Task SetEnabledAsync(bool enabled);
    Task SetFillDataEnabledAsync(bool enabled);
}