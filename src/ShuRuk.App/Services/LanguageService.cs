using ShuRuk.App;
using ShuRuk.App.Helpers;
using ShuRuk.Contracts.Interfaces;

namespace ShuRuk.App.Services;

public class LanguageService
{
    public const string KeyLanguage = AppConstants.ConfigKeys.AppLanguage;

    public string CurrentLanguage { get; private set; } = AppConstants.Languages.System;

    public string ActiveLanguage { get; private set; } = AppConstants.Languages.System;

    public bool IsRestartPending { get; private set; }

    public readonly List<(string Code, string DisplayName)> AvailableLanguages =
    [
        (AppConstants.Languages.System, "Follow system"),
        (AppConstants.Languages.ZhCn, "中文"),
        (AppConstants.Languages.EnUs, "English")
    ];

    public event Action? LanguageChanged;

    private readonly IConfigurationService _config;

    public LanguageService(IConfigurationService config)
    {
        _config = config;
    }

    public async Task LoadAsync()
    {
        var saved = await _config.GetValueAsync<string>(KeyLanguage);
        if (!string.IsNullOrEmpty(saved) && AvailableLanguages.Any(l => l.Code == saved))
        {
            CurrentLanguage = saved;
        }

        ActiveLanguage = CurrentLanguage;

        var lang = CurrentLanguage == AppConstants.Languages.System ? null : CurrentLanguage;

        if (lang is null)
        {
            var systemLang = System.Globalization.CultureInfo.CurrentUICulture.Name.ToLowerInvariant();
            lang = systemLang.StartsWith(AppConstants.Languages.ZhPrefix)
                ? AppConstants.Languages.ZhCn
                : AppConstants.Languages.EnUs;
        }

        LocalizationHelper.SetLanguage(lang);
    }

    public async Task SetLanguageAsync(string code)
    {
        if (CurrentLanguage == code) return;
        CurrentLanguage = code;
        ActiveLanguage = code;
        await _config.SetValueAsync(KeyLanguage, code);
        var lang = code == AppConstants.Languages.System ? null : code;
        LocalizationHelper.SetLanguage(lang);
        LanguageChanged?.Invoke();
    }

    public async Task SetLanguageForRestartAsync(string code)
    {
        CurrentLanguage = code;
        IsRestartPending = true;
        await _config.SetValueAsync(KeyLanguage, code);
    }

    public async Task RevertPendingLanguageChangeAsync()
    {
        if (!IsRestartPending) return;
        CurrentLanguage = ActiveLanguage;
        IsRestartPending = false;
        await _config.SetValueAsync(KeyLanguage, ActiveLanguage);
    }

    public string ResolveEffectiveLanguage(string code)
    {
        if (code != AppConstants.Languages.System) return code;
        var systemLang = System.Globalization.CultureInfo.CurrentUICulture.Name.ToLowerInvariant();
        if (systemLang.StartsWith(AppConstants.Languages.ZhPrefix)) return AppConstants.Languages.ZhCn;
        return AppConstants.Languages.EnUs;
    }
}