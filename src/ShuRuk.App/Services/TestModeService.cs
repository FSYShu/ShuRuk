using ShuRuk.App.Helpers;
using ShuRuk.Contracts.Interfaces;

namespace ShuRuk.App.Services;

public class TestModeService : ITestModeService
{
    private readonly IConfigurationService _configService;
    private bool _isEnabled;

    public bool IsEnabled => _isEnabled;

    public event EventHandler<bool>? TestModeChanged;

    public TestModeService(IConfigurationService configService)
    {
        _configService = configService;
        _isEnabled = false;
    }

    public async Task InitializeAsync()
    {
        _isEnabled = false;
        await SetValueAsync(false);
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        _isEnabled = enabled;
        await SetValueAsync(enabled);
        TestModeChanged?.Invoke(this, enabled);
    }

    private async Task SetValueAsync(bool value)
    {
        await _configService.SetValueAsync(AppConstants.ConfigKeys.TestModeEnabled, value, AppConstants.ConfigKeys.AppConfigModule);
    }
}