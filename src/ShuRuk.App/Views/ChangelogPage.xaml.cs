using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ShuRuk.App;
using ShuRuk.App.Helpers;
using ShuRuk.Runtime.Abstractions.Interfaces;

namespace ShuRuk.App.Views;

// Changelog entry view model / 更新日志条目视图模型
public class ChangelogEntryViewModel
{
    public required string Version { get; init; }
    public required string DateText { get; init; }
    public required string Content { get; init; }
}

public sealed partial class ChangelogPage : Page
{
    private ITestModeService? _testModeService;

    public ChangelogPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLocalization();

        _testModeService = App.Services.GetService<ITestModeService>();
        if (_testModeService is not null)
        {
            _testModeService.TestModeChanged += OnTestModeChanged;
            _testModeService.FillDataChanged += OnTestModeChanged;
        }

        LoadData();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (_testModeService is not null)
        {
            _testModeService.TestModeChanged -= OnTestModeChanged;
            _testModeService.FillDataChanged -= OnTestModeChanged;
        }
    }

    private void OnTestModeChanged(object? sender, bool enabled)
    {
        DispatcherQueue.TryEnqueue(LoadData);
    }

    private void LoadData()
    {
        var entries = new List<ChangelogEntryViewModel>();

        // Fill test entries when test mode + fill enabled / 测试模式+填充开启时填测试条目
        if (_testModeService?.IsEnabled == true && _testModeService.IsFillDataEnabled)
        {
            for (int i = 1; i <= 50; i++)
            {
                entries.Add(new ChangelogEntryViewModel
                {
                    Version = $"1.0.{51 - i}",
                    DateText = DateTime.UtcNow.AddDays(-i).ToString("yyyy-MM-dd"),
                    Content = $"测试更新条目 {51 - i}，非实际存在的更新内容。"
                });
            }
        }

        ChangelogListView.ItemsSource = entries;
        EmptyState.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyLocalization()
    {
        if (ChangelogEmptyTitle is not null)
            ChangelogEmptyTitle.Text = LocalizationHelper.GetString("String_ChangelogEmptyTitle");
        if (ChangelogEmptyDescription is not null)
            ChangelogEmptyDescription.Text = LocalizationHelper.GetString("String_ChangelogEmptyDescription");
    }
}
