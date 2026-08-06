using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using ShuRuk.App.Helpers;
using System.Xml.Linq;

namespace ShuRuk.App;

public static class LocalizationHelper
{
    private static ResourceLoader? _loader;
    private static string? _languageCode;
    private static Dictionary<string, string>? _overrideStrings;

    public static string? LanguageCode => _languageCode;

    private static ResourceLoader GetLoader()
    {
        if (_loader is not null) return _loader;
        try { _loader = new ResourceLoader(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ResourceLoader init failed: {ex}"); }
        return _loader!;
    }

    public static void SetLanguage(string? languageCode)
    {
        _languageCode = languageCode;
        _overrideStrings = null;

        if (!string.IsNullOrEmpty(languageCode))
        {
            LoadOverrideStrings(languageCode);
        }
        else
        {
            var systemLang = System.Globalization.CultureInfo.CurrentUICulture.Name.ToLowerInvariant();
            var lang = systemLang.StartsWith(AppConstants.Languages.ZhPrefix) ? AppConstants.Languages.ZhCn : AppConstants.Languages.EnUs;
            LoadOverrideStrings(lang);
        }
    }

    public static void Refresh()
    {
        try { _loader = new ResourceLoader(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ResourceLoader refresh failed: {ex}"); _loader = null; }
    }

    private static void LoadOverrideStrings(string lang)
    {
        try
        {
            var basePath = AppContext.BaseDirectory;
            var reswPath = Path.Combine(basePath, "Strings", lang, "Resources.resw");
            if (!File.Exists(reswPath)) return;

            var doc = XDocument.Load(reswPath);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            _overrideStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var data in doc.Root!.Elements(ns + "data"))
            {
                var name = data.Attribute("name")?.Value;
                var value = data.Element(ns + "value")?.Value;
                if (name != null && value != null)
                    _overrideStrings[name] = value;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadOverrideStrings failed: {ex}");
            _overrideStrings = null;
        }
    }

    public static string GetString(string key)
    {
        if (_overrideStrings != null && _overrideStrings.TryGetValue(key, out var overrideValue))
            return overrideValue;

        try
        {
            var loader = GetLoader();
            return loader?.GetString(key) ?? key;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetString failed for '{key}': {ex}");
            return key;
        }
    }

    public static string GetStringFormatted(string key, params object[] args)
    {
        var template = GetString(key);
        return string.Format(template, args);
    }

    /// <summary>
    /// 根据资源键自动应用本地化到 UI 元素。
    /// .resw 中的键格式为 "Uid.Property"（如 "NavHome.Content"），
    /// 此方法会在可视树中查找 x:Name 匹配 Uid 部分的元素，并设置对应属性。
    /// </summary>
    public static void ApplyUidResources(FrameworkElement root)
    {
        if (_overrideStrings == null || _overrideStrings.Count == 0) return;

        // 按 Uid 分组：key="NavHome.Content" → Uid="NavHome", Property="Content"
        var grouped = new Dictionary<string, List<(string Property, string Value)>>();

        foreach (var kvp in _overrideStrings)
        {
            var dotIndex = kvp.Key.LastIndexOf('.');
            if (dotIndex <= 0) continue;

            var uid = kvp.Key.Substring(0, dotIndex);
            var prop = kvp.Key.Substring(dotIndex + 1);
            if (!grouped.ContainsKey(uid))
                grouped[uid] = new List<(string, string)>();
            grouped[uid].Add((prop, kvp.Value));
        }

        // Use a set to avoid processing the same element twice
        var visited = new HashSet<FrameworkElement>();

        // Walk logical tree (covers items not yet in visual tree, e.g. collapsed ComboBoxItems)
        ApplyToNamedElement(root, grouped);
        visited.Add(root);
        foreach (var child in UiTreeHelper.EnumerateLogicalChildren<FrameworkElement>(root))
        {
            if (visited.Add(child))
                ApplyToNamedElement(child, grouped);
        }

        // Walk visual tree (covers template-generated elements)
        foreach (var child in UiTreeHelper.EnumerateVisualChildren<FrameworkElement>(root))
        {
            if (visited.Add(child))
                ApplyToNamedElement(child, grouped);
        }
    }

    private static void ApplyToNamedElement(FrameworkElement element, Dictionary<string, List<(string Property, string Value)>> grouped)
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
