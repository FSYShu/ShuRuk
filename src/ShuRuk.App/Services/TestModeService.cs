using ShuRuk.App.Helpers;
using ShuRuk.Runtime.Abstractions.Interfaces;

namespace ShuRuk.App.Services;

public class TestModeService : ITestModeService
{
    private readonly IConfigurationService _configService;
    private bool _isEnabled;
    private bool _isFillDataEnabled;

    public bool IsEnabled => _isEnabled;
    public bool IsFillDataEnabled => _isFillDataEnabled;

    public event EventHandler<bool>? TestModeChanged;
    public event EventHandler<bool>? FillDataChanged;

    public TestModeService(IConfigurationService configService)
    {
        _configService = configService;
        _isEnabled = false;
        _isFillDataEnabled = false;
    }

    public async Task InitializeAsync()
    {
        // Read test mode from config / 从配置读取测试模式状态
        var saved = await _configService.GetValueAsync<bool>(
            AppConstants.ConfigKeys.TestModeEnabled,
            AppConstants.ConfigKeys.AppConfigModule);
        _isEnabled = saved;

        // Read fill data flag from config / 从配置读取填充数据标志
        var fillSaved = await _configService.GetValueAsync<bool>(
            AppConstants.ConfigKeys.TestModeFillData,
            AppConstants.ConfigKeys.AppConfigModule);
        _isFillDataEnabled = fillSaved;
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        _isEnabled = enabled;
        await SetValueAsync(enabled);
        TestModeChanged?.Invoke(this, enabled);
    }

    public async Task SetFillDataEnabledAsync(bool enabled)
    {
        _isFillDataEnabled = enabled;
        await _configService.SetValueAsync(
            AppConstants.ConfigKeys.TestModeFillData, enabled,
            AppConstants.ConfigKeys.AppConfigModule);
        FillDataChanged?.Invoke(this, enabled);
    }

    private async Task SetValueAsync(bool value)
    {
        await _configService.SetValueAsync(AppConstants.ConfigKeys.TestModeEnabled, value, AppConstants.ConfigKeys.AppConfigModule);
    }
}