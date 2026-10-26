namespace ShuRuk.Runtime.Abstractions.Interfaces;

public interface ITestModeService
{
    bool IsEnabled { get; }
    bool IsFillDataEnabled { get; }
    string ChangelogRepoUrl { get; }
    string ChangelogSectionFilter { get; }

    event EventHandler<bool>? TestModeChanged;
    event EventHandler<bool>? FillDataChanged;

    Task InitializeAsync();
    Task SetEnabledAsync(bool enabled);
    Task SetFillDataEnabledAsync(bool enabled);
    Task SetChangelogRepoUrlAsync(string repoUrl);
    Task SetChangelogSectionFilterAsync(string sectionFilter);
}