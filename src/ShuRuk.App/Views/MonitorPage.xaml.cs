using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ShuRuk.App;
using ShuRuk.App.Helpers;

using ShuRuk.App.Controls;
using ShuRuk.App.Services;
using ShuRuk.Runtime.Abstractions.Models;
using ShuRuk.Runtime.Abstractions.Interfaces;
using ShuRuk.Runtime.Abstractions.Services;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace ShuRuk.App.Views;

public class MonitorEntryViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public required string ModuleName { get; init; }
    public required string RawModuleName { get; init; }
    public required int Pid { get; init; }
    public required string CpuText { get; init; }
    public required string MemoryText { get; init; }
    public required double CpuUsagePercent { get; init; }
    public required long MemoryUsageBytes { get; init; }
    public required ModuleState State { get; init; }
    public bool IsAltRow { get; set; }


    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}


public sealed partial class MonitorPage : Page
{
    private static readonly double TotalMemoryMB = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (AppConstants.ByteUnitBase * AppConstants.ByteUnitBase);
    private bool _isFirstLoad = true;
    // Track theme to detect change while page away / 跟踪主题以检测页面离开期间的变化
    private ElementTheme _lastTheme;
    private string _sortBy = AppConstants.SortColumns.Cpu;
    private bool _sortDescending = false;
    private string _prevSortBy = string.Empty;
    private bool _sortIndicatorsApplied;
    private DispatcherTimer? _refreshTimer;
    private DispatcherTimer? _stopDelayTimer;
    private StatusMonitor? _monitor;
    private readonly IConfigurationService _config;
    private ITestModeService? _testModeService;
    private readonly ObservableCollection<MonitorEntryViewModel> _entries = [];

    // Constructor: init localization, config, bindings, events / 构造函数:初始化本地化、配置、绑定、事件
    public MonitorPage()
    {
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        InitializeComponent();
        ApplyLocalization();
        _config = App.Services.GetRequiredService<IConfigurationService>();
        _testModeService = App.Services.GetService<ITestModeService>();
        MonitorListView.ItemsSource = _entries;


        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        ActualThemeChanged += OnActualThemeChanged;
    }

    // Theme changed: regenerate list containers for alt row brushes / 主题变更:重新生成列表容器以刷新交错行
    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        _lastTheme = sender.ActualTheme;
        // Force full container regeneration by re-binding / 清除并重新添加强制重新生成所有容器
        var snapshot = _entries.ToList();
        _entries.Clear();
        foreach (var item in snapshot) _entries.Add(item);
    }

    // Page loaded: start monitor, wait data, init refresh timer / 页面加载:启动监控、等待数据、初始化刷新计时器
    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _monitor = App.Services.GetService<StatusMonitor>();

        if (_testModeService is not null)
        {
            _testModeService.TestModeChanged += OnTestModeChanged;
            _testModeService.FillDataChanged += OnTestModeChanged;
        }

        if (_stopDelayTimer is not null)
        {
            _stopDelayTimer.Stop();
            _stopDelayTimer = null;
        }

        if (_monitor is not null && !_monitor.IsRunning)
        {
            _monitor.Start();
        }

        if (_isFirstLoad)
        {
            _isFirstLoad = false;
            await LoadSortPreference();
            LoadingText.Text = LocalizationHelper.GetString("String_MonitorLoading");

            for (int i = 0; i < AppConstants.MonitorDataWaitRetries; i++)
            {
                await Task.Delay(AppConstants.MonitorDataWaitDelayMs);
                if (_monitor is not null && _monitor.GetLatestSnapshot().Count > 0)
                    break;
            }

            // Hide overlay before LoadData so empty state can show / 先隐藏加载层再加载数据,使空状态可立即显示
            LoadingOverlay.Visibility = Visibility.Collapsed;
            LoadData();
            _lastTheme = ActualTheme;
        }
        else if (ActualTheme != _lastTheme)
        {
            // Theme changed while page was away, refresh alt row brushes / 离开期间主题变化,刷新交错行画刷
            _lastTheme = ActualTheme;
            MonitorListView.ItemsSource = null;
            MonitorListView.ItemsSource = _entries;
        }

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(AppConstants.MonitorRefreshIntervalSeconds)
        };
        _refreshTimer.Tick += OnRefreshTick;
        _refreshTimer.Start();
    }

    // Page unloaded: stop refresh, start stop-delay timer / 页面卸载:停止刷新、启动停止延迟计时器
    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (_testModeService is not null)
        {
            _testModeService.TestModeChanged -= OnTestModeChanged;
            _testModeService.FillDataChanged -= OnTestModeChanged;
        }

        if (_refreshTimer is not null)
        {
            _refreshTimer.Stop();
            _refreshTimer.Tick -= OnRefreshTick;
            _refreshTimer = null;
        }

        _stopDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AppConstants.MonitorStopDelaySeconds) };
        _stopDelayTimer.Tick += OnStopDelayTick;
        _stopDelayTimer.Start();
    }

    // Stop-delay tick: stop the status monitor / 停止延迟触发:停止状态监控
    private void OnStopDelayTick(object? sender, object e)
    {
        if (_stopDelayTimer is not null)
        {
            _stopDelayTimer.Stop();
            _stopDelayTimer = null;
        }

        if (_monitor is not null && _monitor.IsRunning)
        {
            _monitor.Stop();
        }
    }

    // Refresh tick: reload data if not loading / 刷新触发:非加载状态时重新加载数据
    private void OnRefreshTick(object? sender, object e)
    {
        if (LoadingOverlay.Visibility == Visibility.Visible) return;
        LoadData();
    }

    // Test mode changed: reload data on UI thread / 测试模式变更:在UI线程重新加载数据
    private void OnTestModeChanged(object? sender, bool _)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (LoadingOverlay.Visibility == Visibility.Visible) return;
            LoadData();
        });
    }

    // Load latest snapshot from monitor into list / 从监控加载最新快照到列表
    private void LoadData()
    {
        var monitor = App.Services.GetService<StatusMonitor>();
        if (monitor is null)
        {
            _entries.Clear();
            return;
        }

        var snapshot = monitor.GetLatestSnapshot();
        UpdateFromSnapshot(snapshot);
    }

    // Build entries from snapshot, sort, restore selection, update cards / 从快照构建条目、排序、恢复选中、更新卡片
    private void UpdateFromSnapshot(IReadOnlyList<ModuleResourceUsage> snapshot)
    {
        var selectedKey = (MonitorListView.SelectedItem as MonitorEntryViewModel) is { } sel
            ? $"{sel.ModuleName}:{sel.Pid}" : null;

        var entries = snapshot.Select(s => new MonitorEntryViewModel
        {
            ModuleName = s.ModuleName == StatusMonitor.HostProcessName
                ? LocalizationHelper.GetString("String_MonitorHostProcess")
                : s.ModuleName,
            RawModuleName = s.ModuleName,
            Pid = s.Pid,
            CpuText = $"{s.CpuUsagePercent:0.00}%",
            MemoryText = FormatBytes(s.MemoryUsageBytes),
            CpuUsagePercent = s.CpuUsagePercent,
            MemoryUsageBytes = s.MemoryUsageBytes,
            State = s.State,
            IsAltRow = false
        }).ToList();

        ApplySort(entries);

        for (int i = 0; i < entries.Count; i++)
            entries[i].IsAltRow = i % 2 == 1;

        for (int i = 0; i < entries.Count; i++)
        {
            if (i < _entries.Count)
                _entries[i] = entries[i];
            else
                _entries.Add(entries[i]);
        }
        while (_entries.Count > entries.Count)
            _entries.RemoveAt(_entries.Count - 1);

        if (selectedKey != null)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if ($"{_entries[i].ModuleName}:{_entries[i].Pid}" == selectedKey)
                {
                    MonitorListView.SelectedIndex = i;
                    break;
                }
            }
        }

        UpdateSummaryCards(snapshot);

        UpdateEmptyStateVisibility();

        if (!_sortIndicatorsApplied)
        {
            _sortIndicatorsApplied = true;
            UpdateSortIndicators();
        }
    }

    // Update empty state visibility with fade-in / 更新空状态可见性(含淡入动画)
    private void UpdateEmptyStateVisibility()
    {
        var shouldShow = _entries.Count == 0 && LoadingOverlay.Visibility == Visibility.Collapsed;
        if (shouldShow && EmptyState.Visibility != Visibility.Visible)
        {
            EmptyState.Opacity = 0;
            EmptyState.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase()
            };
            Storyboard.SetTarget(fadeIn, EmptyState);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            var sb = new Storyboard();
            sb.Children.Add(fadeIn);
            sb.Begin();
        }
        else if (!shouldShow)
        {
            EmptyState.Visibility = Visibility.Collapsed;
        }
    }

    // Sort entries by current column and direction / 按当前列和方向排序条目
    private void ApplySort(List<MonitorEntryViewModel> entries)
    {
        switch (_sortBy)
        {
            case AppConstants.SortColumns.Name:
                entries.Sort((a, b) => _sortDescending
                    ? StrCmpLogicalW(b.ModuleName, a.ModuleName)
                    : StrCmpLogicalW(a.ModuleName, b.ModuleName));
                break;
            case AppConstants.SortColumns.Pid:
                entries.Sort((a, b) => _sortDescending
                    ? b.Pid.CompareTo(a.Pid)
                    : a.Pid.CompareTo(b.Pid));
                break;
            case AppConstants.SortColumns.Cpu:
                entries.Sort((a, b) => _sortDescending
                    ? b.CpuUsagePercent.CompareTo(a.CpuUsagePercent)
                    : a.CpuUsagePercent.CompareTo(b.CpuUsagePercent));
                break;
            case AppConstants.SortColumns.Memory:
                entries.Sort((a, b) => _sortDescending
                    ? b.MemoryUsageBytes.CompareTo(a.MemoryUsageBytes)
                    : a.MemoryUsageBytes.CompareTo(b.MemoryUsageBytes));
                break;
            default:
                break;
        }
    }


    // Update sort arrow glyphs with show/hide animation / 更新排序箭头字形(含显示/隐藏动画)
    private void UpdateSortIndicators()
    {
        if (_prevSortBy == _sortBy)
        {
            var glyph = _sortDescending ? "\uE70D" : "\uE70E";
            switch (_sortBy)
            {
                case AppConstants.SortColumns.Name: SortArrowName.Glyph = glyph; break;
                case AppConstants.SortColumns.Pid: SortArrowPid.Glyph = glyph; break;
                case AppConstants.SortColumns.Cpu: SortArrowCpu.Glyph = glyph; break;
                case AppConstants.SortColumns.Memory: SortArrowMemory.Glyph = glyph; break;
            }
            return;
        }

        if (_prevSortBy != string.Empty)
        {
            var (oldArrow, oldTranslate) = GetArrowAndTranslate(_prevSortBy);
            AnimateArrow(oldArrow, false);
            if (oldTranslate is not null) AnimateTranslate(oldTranslate, 0);
        }

        var (newArrow, newTranslate) = GetArrowAndTranslate(_sortBy);
        var glyph2 = _sortDescending ? "\uE70D" : "\uE70E";
        newArrow.Glyph = glyph2;
        AnimateArrow(newArrow, true);
        if (newTranslate is not null) AnimateTranslate(newTranslate, -14);

        _prevSortBy = _sortBy;
    }

    // Map sort column to its arrow icon and translate transform / 将排序列映射到其箭头图标和位移变换
    private (FontIcon arrow, TranslateTransform? translate) GetArrowAndTranslate(string column) => column switch
    {
        AppConstants.SortColumns.Name => (SortArrowName, null),
        AppConstants.SortColumns.Pid => (SortArrowPid, HeaderPidTranslate),
        AppConstants.SortColumns.Cpu => (SortArrowCpu, HeaderCpuTranslate),
        AppConstants.SortColumns.Memory => (SortArrowMemory, HeaderMemoryTranslate),
        _ => (SortArrowCpu, null)
    };

    // Animate arrow opacity (show/hide) / 动画切换箭头透明度(显示/隐藏)
    private static void AnimateArrow(FontIcon arrow, bool show)
    {
        var anim = new DoubleAnimation
        {
            From = show ? 0 : 1,
            To = show ? 1 : 0,
            Duration = TimeSpan.FromMilliseconds(AppConstants.SortArrowAnimationMs),
            EasingFunction = new QuadraticEase()
        };
        Storyboard.SetTarget(anim, arrow);
        Storyboard.SetTargetProperty(anim, "Opacity");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }

    // Animate translate transform X to target / 动画移动变换X到目标值
    private static void AnimateTranslate(TranslateTransform transform, double toX)
    {
        var anim = new DoubleAnimation
        {
            From = transform.X,
            To = toX,
            Duration = TimeSpan.FromMilliseconds(AppConstants.SortArrowAnimationMs),
            EasingFunction = new QuadraticEase()
        };
        Storyboard.SetTarget(anim, transform);
        Storyboard.SetTargetProperty(anim, "X");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }

    private static string? _cpuName;
    // Get CPU name from registry (cached) / 从注册表获取CPU名称(缓存)
    private static string GetCpuName()
    {
        if (_cpuName is not null) return _cpuName;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            _cpuName = key?.GetValue("ProcessorNameString") as string ?? "CPU";
        }
        catch { _cpuName = "CPU"; }
        return _cpuName;
    }



    private static string? _memoryInfo;
    // DDR memory type lookup table / DDR内存类型查找表
    private static readonly Dictionary<int, string> _memoryTypeNames = new()
    {
        [20] = "DDR", [21] = "DDR2", [22] = "DDR2 FB-DIMM", [24] = "DDR3",
        [25] = "FBD2", [26] = "DDR4", [27] = "LPDDR", [28] = "LPDDR2",
        [29] = "LPDDR3", [30] = "LPDDR4", [32] = "HBM", [33] = "HBM2",
        [34] = "DDR5", [35] = "LPDDR5", [36] = "LPDDR5X", [37] = "HBM3",
    };

    // Get memory type and speed via WMI (cached) / 通过WMI获取内存类型和速度(缓存)
    private static string GetMemoryInfo()
    {
        if (_memoryInfo is not null) return _memoryInfo;
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT SMBIOSMemoryType, Speed FROM Win32_PhysicalMemory");
            int memoryType = 0;
            uint speed = 0;
            foreach (var obj in searcher.Get())
            {
                var mt = obj["SMBIOSMemoryType"];
                if (mt is IConvertible conv)
                {
                    var val = conv.ToInt32(System.Globalization.CultureInfo.InvariantCulture);
                    if (val > memoryType) memoryType = val;
                }
                var sp = obj["Speed"];
                if (sp is IConvertible sc)
                {
                    var val = sc.ToUInt32(System.Globalization.CultureInfo.InvariantCulture);
                    if (val > speed) speed = val;
                }
            }
            var typeName = _memoryTypeNames.TryGetValue(memoryType, out var known)
                ? known
                : memoryType > 0 ? $"Type {memoryType}" : null;

            _memoryInfo = typeName != null
                ? speed > 0 ? $"{typeName} · {speed} MT/s" : typeName
                : speed > 0 ? $"{speed} MT/s" : string.Empty;
        }
        catch { _memoryInfo = string.Empty; }
        return _memoryInfo;
    }

    private static readonly SolidColorBrush[] SegmentBrushes =
    [
        new(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x00, 0x78, 0xD4)),
        new(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x10, 0x7C, 0x10)),
        new(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xBF, 0x57, 0x00)),
        new(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x8B, 0x5A, 0xD2)),
        new(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xE7, 0x4C, 0x3C)),
        new(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x00, 0xB2, 0x94)),
        new(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xF0, 0xAD, 0x4E)),
        new(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x5B, 0x9B, 0xD5)),
    ];

    private static readonly SolidColorBrush _stoppedBrush = new(Microsoft.UI.ColorHelper.FromArgb(0x60, 0x80, 0x80, 0x80));
    private static readonly SolidColorBrush _nonShuRukBrush = new(Microsoft.UI.ColorHelper.FromArgb(0x80, 0x80, 0x80, 0x80));

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

    // Natural sort via Win32 StrCmpLogicalW / 自然排序(如1,2,10而非1,10,2)
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    private static long _prevIdleTime, _prevKernelTime, _prevUserTime;
    private static double _systemCpuPercent;
    private static bool _cpuSampleInitialized;

    // Sample system CPU usage via GetSystemTimes deltas / 通过GetSystemTimes差值采样系统CPU使用率
    private static double SampleSystemCpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return _systemCpuPercent;

        if (_cpuSampleInitialized)
        {
            var idleDelta = idle - _prevIdleTime;
            var kernelDelta = kernel - _prevKernelTime;
            var userDelta = user - _prevUserTime;
            var total = kernelDelta + userDelta;
            if (total > 0)
                _systemCpuPercent = Math.Clamp((1.0 - (double)idleDelta / total) * 100.0, 0, 100);
        }
        else
        {
            _cpuSampleInitialized = true;
        }

        _prevIdleTime = idle;
        _prevKernelTime = kernel;
        _prevUserTime = user;
        return _systemCpuPercent;
    }

    // Get system used memory via GlobalMemoryStatusEx / 通过GlobalMemoryStatusEx获取系统已用内存
    private static long GetSystemUsedMemoryBytes()
    {
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref memStatus))
            return (long)(memStatus.ullTotalPhys - memStatus.ullAvailPhys);
        return 0;
    }

    // Update CPU, memory, module summary cards / 更新CPU、内存、模块汇总卡片
    private void UpdateSummaryCards(IReadOnlyList<ModuleResourceUsage> snapshot)
    {
        // Single pass to aggregate all totals / 单次遍历聚合所有总计
        double totalCpu = 0;
        long totalMemory = 0;
        int installedCount = 0;
        int runningCount = 0;
        foreach (var s in snapshot)
        {
            totalCpu += s.CpuUsagePercent;
            totalMemory += s.MemoryUsageBytes;
            if (s.ModuleName != StatusMonitor.HostProcessName)
            {
                installedCount++;
                if (s.State == ModuleState.Running)
                    runningCount++;
            }
        }
        var isTestMode = _testModeService?.IsEnabled == true;

        UpdateCpuCard(snapshot, totalCpu, isTestMode);
        UpdateMemoryCard(snapshot, totalMemory, isTestMode);
        UpdateModuleCard(installedCount, runningCount);
    }

    // Update CPU card value, detail, and progress segments / 更新CPU卡片数值、详情、进度段
    private void UpdateCpuCard(IReadOnlyList<ModuleResourceUsage> snapshot, double totalCpu, bool isTestMode)
    {
        // Show dash for empty test mode / 空测试模式显示破折号
        if (snapshot.Count == 0 && isTestMode)
        {
            CpuCardValue.Text = "—";
            CpuCardDetail.Text = $"{GetCpuName()} · {Environment.ProcessorCount} cores";
            CpuProgressBar.Segments = [];
            CpuProgressBar.Maximum = 100;
            return;
        }

        CpuCardValue.Text = $"{Math.Min(totalCpu, 100):0.0}%";
        CpuCardDetail.Text = $"{GetCpuName()} · {Environment.ProcessorCount} cores";

        var cpuSegments = snapshot
            .Select((s, i) => new ProgressSegment { Value = s.CpuUsagePercent, Brush = SegmentBrushes[i % SegmentBrushes.Length] })
            .ToList();

        if (!isTestMode)
        {
            var systemCpu = SampleSystemCpuPercent();
            var nonShuRukCpu = Math.Max(0, systemCpu - totalCpu);
            if (nonShuRukCpu > 0.1)
                cpuSegments.Add(new ProgressSegment { Value = nonShuRukCpu, Brush = _nonShuRukBrush });
        }

        CpuProgressBar.Segments = cpuSegments.ToArray();
        CpuProgressBar.Maximum = 100;
    }

    // Update memory card value, detail, and progress segments / 更新内存卡片数值、详情、进度段
    private void UpdateMemoryCard(IReadOnlyList<ModuleResourceUsage> snapshot, long totalMemory, bool isTestMode)
    {
        // Show dash for empty test mode / 空测试模式显示破折号
        if (snapshot.Count == 0 && isTestMode)
        {
            MemoryCardValue.Text = "—";
            var memInfoEmpty = GetMemoryInfo();
            MemoryCardDetail.Text = FormatBytes((long)(TotalMemoryMB * AppConstants.ByteUnitBase * AppConstants.ByteUnitBase)) + (string.IsNullOrEmpty(memInfoEmpty) ? "" : $" · {memInfoEmpty}");
            MemoryProgressBar.Segments = [];
            MemoryProgressBar.Maximum = 100;
            return;
        }

        MemoryCardValue.Text = FormatBytes(totalMemory);
        var memInfo = GetMemoryInfo();
        MemoryCardDetail.Text = FormatBytes((long)(TotalMemoryMB * AppConstants.ByteUnitBase * AppConstants.ByteUnitBase)) + (string.IsNullOrEmpty(memInfo) ? "" : $" · {memInfo}");
        var memMax = TotalMemoryMB * AppConstants.ByteUnitBase * AppConstants.ByteUnitBase;

        var memSegments = snapshot
            .Select((s, i) => new ProgressSegment { Value = s.MemoryUsageBytes / memMax * 100, Brush = SegmentBrushes[i % SegmentBrushes.Length] })
            .ToList();

        if (!isTestMode)
        {
            var systemUsedMem = GetSystemUsedMemoryBytes();
            var nonShuRukMem = Math.Max(0, systemUsedMem - totalMemory);
            var nonShuRukMemPercent = nonShuRukMem / memMax * 100;
            if (nonShuRukMemPercent > 0.1)
                memSegments.Add(new ProgressSegment { Value = nonShuRukMemPercent, Brush = _nonShuRukBrush });
        }

        MemoryProgressBar.Segments = memSegments.ToArray();
        MemoryProgressBar.Maximum = 100;
    }

    // Update module card counts and progress segment / 更新模块卡片计数和进度段
    private void UpdateModuleCard(int installedCount, int runningCount)
    {
        // Show dash for empty data / 空数据显示破折号
        if (installedCount == 0 && _testModeService?.IsEnabled == true)
        {
            ModuleCardValue.Text = "—";
            ModuleCardDetail.Text = "—";
            ModuleProgressBar.Segments = [];
            ModuleProgressBar.Maximum = 1;
            return;
        }

        ModuleCardValue.Text = $"{runningCount} {LocalizationHelper.GetString("String_MonitorModuleRunning")}";
        ModuleCardDetail.Text = $"{installedCount} {LocalizationHelper.GetString("String_MonitorModuleInstalled")}";
        var moduleSegments = new List<ProgressSegment>();
        if (runningCount > 0)
            moduleSegments.Add(new ProgressSegment { Value = runningCount, Brush = SegmentBrushes[0] });

        ModuleProgressBar.Segments = moduleSegments.ToArray();
        ModuleProgressBar.Maximum = installedCount > 0 ? installedCount : 1;
    }

    // Toggle sort column/direction, save, animate transition / 切换排序列/方向、保存、动画过渡
    private async void ToggleSort(string column)
    {
        if (_sortBy == column)
            _sortDescending = !_sortDescending;
        else
        {
            _sortBy = column;
            _sortDescending = false;
        }
        await SaveSortPreference();
        AnimateSortTransition();
    }

    // Fade+slide out, reload, fade+slide in for sort transition / 淡出+滑出、重载、淡入+滑入排序过渡
    private void AnimateSortTransition()
    {
        var ease = new QuadraticEase();
        var fadeOut = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(AppConstants.SortFadeOutMs), EasingFunction = ease };
        var slideOut = new DoubleAnimation { From = 0, To = -AppConstants.SortSlideDistance, Duration = TimeSpan.FromMilliseconds(AppConstants.SortFadeOutMs), EasingFunction = ease };
        Storyboard.SetTarget(fadeOut, MonitorListView);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");
        Storyboard.SetTarget(slideOut, ListViewTranslate);
        Storyboard.SetTargetProperty(slideOut, "Y");
        var sbOut = new Storyboard();
        sbOut.Children.Add(fadeOut);
        sbOut.Children.Add(slideOut);
        sbOut.Completed += (_, _) =>
        {
            LoadData();
            UpdateSortIndicators();

            var fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(AppConstants.SortFadeInMs), EasingFunction = ease };
            var slideIn = new DoubleAnimation { From = AppConstants.SortSlideDistance, To = 0, Duration = TimeSpan.FromMilliseconds(AppConstants.SortFadeInMs), EasingFunction = ease };
            Storyboard.SetTarget(fadeIn, MonitorListView);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            Storyboard.SetTarget(slideIn, ListViewTranslate);
            Storyboard.SetTargetProperty(slideIn, "Y");
            var sbIn = new Storyboard();
            sbIn.Children.Add(fadeIn);
            sbIn.Children.Add(slideIn);
            sbIn.Begin();
        };
        sbOut.Begin();
    }

    // Valid sort columns for validation / 有效排序列(用于验证)
    private static readonly string[] _validSortColumns =
        [AppConstants.SortColumns.Name, AppConstants.SortColumns.Pid, AppConstants.SortColumns.Cpu, AppConstants.SortColumns.Memory];

    // Load sort preference from config / 从配置加载排序偏好
    private async Task LoadSortPreference()
    {
        var sortBy = await _config.GetValueAsync<string>(AppConstants.ConfigKeys.MonitorSortBy, AppConstants.ConfigKeys.AppConfigModule);
        var sortDesc = await _config.GetValueAsync<bool>(AppConstants.ConfigKeys.MonitorSortDescending, AppConstants.ConfigKeys.AppConfigModule);
        if (!string.IsNullOrEmpty(sortBy) && _validSortColumns.Contains(sortBy))
            _sortBy = sortBy;
        _sortDescending = sortDesc;
    }

    // Save current sort preference to config / 保存当前排序偏好到配置
    private async Task SaveSortPreference()
    {
        await _config.SetValueAsync(AppConstants.ConfigKeys.MonitorSortBy, _sortBy, AppConstants.ConfigKeys.AppConfigModule);
        await _config.SetValueAsync(AppConstants.ConfigKeys.MonitorSortDescending, _sortDescending, AppConstants.ConfigKeys.AppConfigModule);
    }

    // Apply localized strings to all headers and cards / 对所有表头和卡片应用本地化文本
    private void ApplyLocalization()
    {
        HeaderName.Text = LocalizationHelper.GetString("String_MonitorHeaderName");
        HeaderPid.Text = LocalizationHelper.GetString("String_MonitorHeaderPid");
        HeaderCpu.Text = LocalizationHelper.GetString("String_MonitorHeaderCpu");
        HeaderMemory.Text = LocalizationHelper.GetString("String_MonitorHeaderMemory");

        CpuCardIcon.Glyph = "\ueea1";
        CpuCardTitle.Text = LocalizationHelper.GetString("String_MonitorCpuCardTitle");
        MemoryCardIcon.Glyph = "\ueea0";
        MemoryCardTitle.Text = LocalizationHelper.GetString("String_MonitorMemoryCardTitle");
        ModuleCardIcon.Glyph = "\ue7b8";
        ModuleCardTitle.Text = LocalizationHelper.GetString("String_MonitorModuleCardTitle");

        LoadingText.Text = LocalizationHelper.GetString("String_MonitorLoading");

        // Empty state text / 空状态文本
        MonitorEmptyTitle.Text = LocalizationHelper.GetString("String_MonitorEmptyTitle");
        MonitorEmptyDescription.Text = LocalizationHelper.GetString("String_MonitorEmptyDescription");

        RestartProcessButton.Label = LocalizationHelper.GetString("String_MonitorRestartProcess");
        StopProcessButton.Label = LocalizationHelper.GetString("String_MonitorStopProcess");
    }

    // Header hover enter: set subtle background / 表头悬停进入:设置微妙背景色
    private void HeaderBorder_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
            border.Background = (Brush?)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
    }

    // Header hover exit: clear background / 表头悬停退出:清除背景
    private void HeaderBorder_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
            border.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    // Format bytes to human-readable string (B/KB/MB/GB) / 格式化字节为可读字符串
    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB"];
        var order = (int)Math.Log(bytes, AppConstants.ByteUnitBase);
        if (order >= units.Length) order = units.Length - 1;
        var size = bytes / Math.Pow(AppConstants.ByteUnitBase, order);
        return $"{size:0.##} {units[order]}";
    }

    // Header tapped: toggle sort by column (unified handler) / 表头点击:按列切换排序(统一处理)
    private void HeaderName_Tapped(object sender, TappedRoutedEventArgs e) => ToggleSort(AppConstants.SortColumns.Name);
    private void HeaderPid_Tapped(object sender, TappedRoutedEventArgs e) => ToggleSort(AppConstants.SortColumns.Pid);
    private void HeaderCpu_Tapped(object sender, TappedRoutedEventArgs e) => ToggleSort(AppConstants.SortColumns.Cpu);
    private void HeaderMemory_Tapped(object sender, TappedRoutedEventArgs e) => ToggleSort(AppConstants.SortColumns.Memory);

    // Selection changed: update IsSelected flags and command bar / 选择变更:更新IsSelected标志和命令栏
    private void MonitorListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonitorListView.ItemsSource is not System.Collections.IList items) return;
        var selected = MonitorListView.SelectedItem as MonitorEntryViewModel;
        foreach (var item in items)
        {
            if (item is MonitorEntryViewModel vm)
                vm.IsSelected = vm == selected;
        }

        UpdateCommandBarState(selected);
    }

    // Clear selection when tapping blank area / 点击空白区域清除选择
    private void RootGrid_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null)
        {
            if (element is ListViewItem) return;
            element = VisualTreeHelper.GetParent(element);
        }
        MonitorListView.SelectedItem = null;
    }

    // Enable/disable restart and stop buttons by selection state / 按选中状态启用/禁用重启和停止按钮
    private void UpdateCommandBarState(MonitorEntryViewModel? selected)
    {
        if (selected is null || selected.RawModuleName == StatusMonitor.HostProcessName)
        {
            RestartProcessButton.IsEnabled = false;
            StopProcessButton.IsEnabled = false;
            return;
        }

        // Enable restart for any installed module / 已安装模块均可重启(启动)
        RestartProcessButton.IsEnabled = selected.State is not (ModuleState.Discovered or ModuleState.Validated or ModuleState.Uninstalled);
        StopProcessButton.IsEnabled = selected.State is ModuleState.Running or ModuleState.Paused;
    }

    // Restart selected module: stop then start, show error on failure / 重启选中模块:停止再启动,失败显示错误
    private async void RestartProcessButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = MonitorListView.SelectedItem as MonitorEntryViewModel;
        if (selected is null || selected.RawModuleName == StatusMonitor.HostProcessName) return;

        var moduleManager = App.Services.GetService<IModuleManager>();
        if (moduleManager is null) return;

        try
        {
            if (selected.State is ModuleState.Running or ModuleState.Paused)
                await moduleManager.StopModuleAsync(selected.RawModuleName);

            await moduleManager.StartModuleAsync(selected.RawModuleName);
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = LocalizationHelper.GetString("String_MonitorRestartFailedTitle"),
                Content = $"{selected.ModuleName}: {ex.Message}",
                CloseButtonText = LocalizationHelper.GetString("String_MonitorCommandCancel"),
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
        finally
        {
            LoadData();
        }
    }

    // Stop selected module, show error on failure / 停止选中模块,失败显示错误
    private async void StopProcessButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = MonitorListView.SelectedItem as MonitorEntryViewModel;
        if (selected is null || selected.RawModuleName == StatusMonitor.HostProcessName) return;

        var moduleManager = App.Services.GetService<IModuleManager>();
        if (moduleManager is null) return;

        try
        {
            await moduleManager.StopModuleAsync(selected.RawModuleName);
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = LocalizationHelper.GetString("String_MonitorStopFailedTitle"),
                Content = $"{selected.ModuleName}: {ex.Message}",
                CloseButtonText = LocalizationHelper.GetString("String_MonitorCommandCancel"),
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
        finally
        {
            LoadData();
        }
    }
}

public class AltRowBrushConverter : IValueConverter
{
    // Light: #08000000 (~3% black) / Dark: #04FFFFFF (~1.6% white) / 浅色#08000000 深色#04FFFFFF
    private static readonly SolidColorBrush _lightBrush = new(Microsoft.UI.ColorHelper.FromArgb(0x06, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush _darkBrush = new(Microsoft.UI.ColorHelper.FromArgb(0x04, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush _transparent = new(Microsoft.UI.Colors.Transparent);

    // Convert IsAltRow bool to theme-aware alt row brush / 将IsAltRow布尔值转换为主题感知的交错行画刷
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isAlt && isAlt)
        {
            if (App.MainWindowStatic.Content is FrameworkElement root)
                return root.ActualTheme == ElementTheme.Dark ? _darkBrush : _lightBrush;
            return _lightBrush;
        }
        return _transparent;
    }

    // ConvertBack not supported (one-way binding) / 反向转换不支持(单向绑定)
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
