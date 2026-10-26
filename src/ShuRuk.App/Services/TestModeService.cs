using ShuRuk.App.Helpers;
using ShuRuk.Runtime.Abstractions.Interfaces;

namespace ShuRuk.App.Services;

public class TestModeService : ITestModeService
{
    private readonly IConfigurationService _configService;
    private bool _isEnabled;
    private bool _isFillDataEnabled;
    private string _changelogRepoUrl = string.Empty;
    private string _changelogSectionFilter = string.Empty;

    public bool IsEnabled => _isEnabled;
    public bool IsFillDataEnabled => _isFillDataEnabled;
    public string ChangelogRepoUrl => _changelogRepoUrl;
    public string ChangelogSectionFilter => _changelogSectionFilter;

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

        // Read changelog params from config / 从配置读取更新日志参数
        _changelogRepoUrl = await _configService.GetValueAsync<string>(
            AppConstants.ConfigKeys.ChangelogRepoUrl,
            AppConstants.ConfigKeys.AppConfigModule) ?? string.Empty;
        _changelogSectionFilter = await _configService.GetValueAsync<string>(
            AppConstants.ConfigKeys.ChangelogSectionFilter,
            AppConstants.ConfigKeys.AppConfigModule) ?? string.Empty;
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

    public async Task SetChangelogRepoUrlAsync(string repoUrl)
    {
        _changelogRepoUrl = repoUrl ?? string.Empty;
        await _configService.SetValueAsync(
            AppConstants.ConfigKeys.ChangelogRepoUrl, _changelogRepoUrl,
            AppConstants.ConfigKeys.AppConfigModule);
    }

    public async Task SetChangelogSectionFilterAsync(string sectionFilter)
    {
        _changelogSectionFilter = sectionFilter ?? string.Empty;
        await _configService.SetValueAsync(
            AppConstants.ConfigKeys.ChangelogSectionFilter, _changelogSectionFilter,
            AppConstants.ConfigKeys.AppConfigModule);
    }


    private async Task SetValueAsync(bool value)
    {
        await _configService.SetValueAsync(AppConstants.ConfigKeys.TestModeEnabled, value, AppConstants.ConfigKeys.AppConfigModule);
    }
}