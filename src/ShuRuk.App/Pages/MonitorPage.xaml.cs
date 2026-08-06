using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using ShuRuk.App;
using ShuRuk.App.Helpers;
using ShuRuk.Contracts.Enums;
using ShuRuk.Contracts.Services;

namespace ShuRuk.App.Pages;

public class MonitorEntryViewModel
{
    public required string ModuleName { get; init; }
    public required string StateText { get; init; }
    public required string MemoryText { get; init; }
    public required double CpuUsagePercent { get; init; }
}

public sealed partial class MonitorPage : Page
{
    public MonitorPage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        InitializeComponent();
        LoadData();
    }

    private void LoadData()
    {
        var monitor = AppContextAccessor.Current.Services.GetService(typeof(StatusMonitor)) as StatusMonitor;
        if (monitor is null)
        {
            MonitorListView.ItemsSource = Array.Empty<MonitorEntryViewModel>();
            return;
        }

        var snapshot = monitor.GetLatestSnapshot();
        var entries = snapshot.Select(s => new MonitorEntryViewModel
        {
            ModuleName = s.ModuleName,
            StateText = s.State.ToString(),
            MemoryText = FormatBytes(s.MemoryUsageBytes),
            CpuUsagePercent = s.CpuUsagePercent
        }).ToList();

        MonitorListView.ItemsSource = entries;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes == 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB"];
        var order = (int)Math.Log(bytes, AppConstants.ByteUnitBase);
        if (order >= units.Length) order = units.Length - 1;
        var size = bytes / Math.Pow(AppConstants.ByteUnitBase, order);
        return $"{size:0.##} {units[order]}";
    }
}