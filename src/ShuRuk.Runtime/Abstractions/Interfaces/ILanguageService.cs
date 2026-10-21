namespace ShuRuk.Runtime.Abstractions.Interfaces;

// Language service interface for decoupling UI from App / 语言服务接口，解耦UI与App层
public interface ILanguageService
{
    string CurrentLanguage { get; }
    string ActiveLanguage { get; }
    bool IsRestartPending { get; }
    List<(string Code, string DisplayName)> AvailableLanguages { get; }
    event Action? LanguageChanged;

    Task SetLanguageAsync(string code);
    Task SetLanguageForRestartAsync(string code);
    Task RevertPendingLanguageChangeAsync();
    string ResolveEffectiveLanguage(string code);
}