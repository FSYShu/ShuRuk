using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Xml.Linq;

namespace ShuRuk.Runtime.Helpers;

/// <summary>
/// Unified localization helper supporting multiple resource domains.
/// / 统一的本地化辅助类，支持多资源域。
/// Each domain loads from its own resw file and maintains independent state.
/// </summary>
public sealed class LocalizationHelper
{
    private string? _languageCode;
    private Dictionary<string, string>? _strings;
    private ResourceLoader? _loader;
    private readonly string _resourceFileName;
    private readonly string? _resourceSubPath;
    private readonly bool _useResourceLoader;

    /// <summary>
    /// Runtime resource domain (RuntimeResources.resw under ShuRuk.Runtime/Strings).
    /// / Runtime 资源域。
    /// </summary>
    public static readonly LocalizationHelper Runtime = new(
        "RuntimeResources.resw", "ShuRuk.Runtime", useResourceLoader: false);

    /// <summary>
    /// App resource domain (Resources.resw under Strings, with PRI ResourceLoader fallback).
    /// / App 资源域，含 PRI ResourceLoader 回退。
    /// </summary>
    public static readonly LocalizationHelper App = new(
        "Resources.resw", null, useResourceLoader: true);

    public LocalizationHelper(string resourceFileName, string? resourceSubPath = null, bool useResourceLoader = false)
    {
        _resourceFileName = resourceFileName;
        _resourceSubPath = resourceSubPath;
        _useResourceLoader = useResourceLoader;
    }

    public string? LanguageCode => _languageCode;

    public void SetLanguage(string? languageCode)
    {
        _languageCode = languageCode;
        _strings = null;

        if (string.IsNullOrEmpty(languageCode))
        {
            var systemLang = System.Globalization.CultureInfo.CurrentUICulture.Name.ToLowerInvariant();
            languageCode = systemLang.StartsWith("zh") ? "zh-cn" : "en-us";
        }

        LoadStrings(languageCode);
    }

    /// <summary>
    /// Refresh PRI ResourceLoader (App domain only).
    /// / 刷新 PRI ResourceLoader（仅 App 域）。
    /// </summary>
    public void Refresh()
    {
        if (!_useResourceLoader) return;
        try { _loader = new ResourceLoader(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ResourceLoader refresh failed: {ex}");
            _loader = null;
        }
    }

    private void LoadStrings(string lang)
    {
        try
        {
            var basePath = AppContext.BaseDirectory;
            string reswPath;

            if (_resourceSubPath is not null)
            {
                // Prefer sub-path specific resw / 优先子路径专用 resw
                reswPath = Path.Combine(basePath, _resourceSubPath, "Strings", lang, _resourceFileName);
                if (!File.Exists(reswPath))
                {
                    reswPath = Path.Combine(basePath, "Strings", lang, _resourceFileName);
                    if (!File.Exists(reswPath)) return;
                }
            }
            else
            {
                reswPath = Path.Combine(basePath, "Strings", lang, _resourceFileName);
                if (!File.Exists(reswPath)) return;
            }

            var doc = XDocument.Load(reswPath);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            _strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var data in doc.Root!.Elements(ns + "data"))
            {
                var name = data.Attribute("name")?.Value;
                var value = data.Element(ns + "value")?.Value;
                if (name != null && value != null)
                    _strings[name] = value;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LocalizationHelper.LoadStrings failed: {ex}");
            _strings = null;
        }
    }

    public string GetString(string key)
    {
        if (_strings != null && _strings.TryGetValue(key, out var value))
            return value;

        if (_useResourceLoader)
        {
            try
            {
                _loader ??= new ResourceLoader();
                return _loader?.GetString(key) ?? key;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetString failed for '{key}': {ex}");
            }
        }

        return key;
    }

    public string GetStringFormatted(string key, params object[] args)
    {
        return string.Format(GetString(key), args);
    }

    /// <summary>
    /// Apply localized resources by Uid to UI elements.
    /// / 根据资源键自动应用本地化到 UI 元素。
    /// Walks both logical and visual trees to cover all elements.
    /// </summary>
    public void ApplyUidResources(FrameworkElement root)
    {
        if (_strings == null || _strings.Count == 0) return;

        var grouped = new Dictionary<string, List<(string Property, string Value)>>();
        foreach (var kvp in _strings)
        {
            var dotIndex = kvp.Key.LastIndexOf('.');
            if (dotIndex <= 0) continue;
            var uid = kvp.Key.Substring(0, dotIndex);
            var prop = kvp.Key.Substring(dotIndex + 1);
            if (!grouped.ContainsKey(uid))
                grouped[uid] = new List<(string, string)>();
            grouped[uid].Add((prop, kvp.Value));
        }

        var visited = new HashSet<FrameworkElement> { root };
        ApplyToNamedElement(root, grouped);

        // Walk logical tree / 遍历逻辑树
        foreach (var child in UiTreeHelper.EnumerateLogicalChildren<FrameworkElement>(root))
        {
            if (visited.Add(child))
                ApplyToNamedElement(child, grouped);
        }

        // Walk visual tree (covers template-generated elements) / 遍历可视树
        foreach (var child in UiTreeHelper.EnumerateVisualChildren<FrameworkElement>(root))
        {
            if (visited.Add(child))
                ApplyToNamedElement(child, grouped);
        }
    }

    private static void ApplyToNamedElement(FrameworkElement element,
        Dictionary<string, List<(string Property, string Value)>> grouped)
    {
        var name = element.Name;
        if (string.IsNullOrEmpty(name)) return;
        if (!grouped.TryGetValue(name, out var props)) return;

        foreach (var (prop, value) in props)
        {
            ApplyProperty(element, prop, value);
        }
    }

    private static void ApplyProperty(FrameworkElement element, string property, string value)
    {
        switch (property)
        {
            case "Content":
                if (element is ContentControl ctrl)
                    ctrl.Content = value;
                else if (element is AppBarButton abb)
                    abb.Label = value;
                break;
            case "Text":
                if (element is TextBlock tb)
                    tb.Text = value;
                else if (element is TextBox txb)
                    txb.Text = value;
                else if (element is AutoSuggestBox asb)
                    asb.Text = value;
                else if (element is SelectorBarItem sbi)
                    sbi.Text = value;
                break;
            case "PlaceholderText":
                if (element is AutoSuggestBox asb2)
                    asb2.PlaceholderText = value;
                else if (element is TextBox txb2)
                    txb2.PlaceholderText = value;
                break;
            case "Title":
                if (element is InfoBar ib)
                    ib.Title = value;
                break;
            case "Message":
                if (element is InfoBar ib2)
                    ib2.Message = value;
                break;
            case "Header":
                if (element is SelectorBarItem sbiHdr)
                    sbiHdr.Text = value;
                else if (element is ContentDialog cd)
                    cd.Title = value;
                break;
        }
    }
}
