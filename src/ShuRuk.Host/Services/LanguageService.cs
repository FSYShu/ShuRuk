using ShuRuk.Contracts.Interfaces;

namespace ShuRuk.Host.Services;

public class LanguageService
{
    public const string KeyLanguage = "app.language";

    public string CurrentLanguage { get; private set; } = "system";

    public string ActiveLanguage { get; private set; } = "system";

    public bool IsRestartPending { get; private set; }

    public readonly List<(string Code, string DisplayName)> AvailableLanguages =
    [
        ("system", "Follow system"),
        ("zh-cn", "中文"),
        ("en-us", "English")
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

        var lang = CurrentLanguage == "system" ? null : CurrentLanguage;

        if (lang is null)
        {
            var systemLang = System.Globalization.CultureInfo.CurrentUICulture.Name.ToLowerInvariant();
            if (systemLang.StartsWith("zh"))
                lang = "zh-cn";
            else
                lang = "en-us";
        }

        LocalizationHelper.SetLanguage(lang);
    }

    public async Task SetLanguageAsync(string code)
    {
        if (CurrentLanguage == code) return;
        CurrentLanguage = code;
        ActiveLanguage = code;
        await _config.SetValueAsync(KeyLanguage, code);
        var lang = code == "system" ? null : code;
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
        if (code != "system") return code;
        var systemLang = System.Globalization.CultureInfo.CurrentUICulture.Name.ToLowerInvariant();
        if (systemLang.StartsWith("zh")) return "zh-cn";
        return "en-us";
    }
}