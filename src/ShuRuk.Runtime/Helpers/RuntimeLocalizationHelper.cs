using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Xml.Linq;

namespace ShuRuk.Runtime.Helpers;

public static class RuntimeLocalizationHelper
{
    private static string? _languageCode;
    private static Dictionary<string, string>? _strings;

    public static string? LanguageCode => _languageCode;

    public static void SetLanguage(string? languageCode)
    {
        _languageCode = languageCode;

        if (string.IsNullOrEmpty(languageCode))
        {
            var systemLang = System.Globalization.CultureInfo.CurrentUICulture.Name.ToLowerInvariant();
            languageCode = systemLang.StartsWith("zh") ? "zh-cn" : "en-us";
        }

        LoadStrings(languageCode);
    }

    private static void LoadStrings(string lang)
    {
        try
        {
            var basePath = AppContext.BaseDirectory;
            // Prefer Runtime-specific resw to avoid App resw shadowing / 优先使用Runtime专用resw，避免App resw遮蔽
            var reswPath = Path.Combine(basePath, "ShuRuk.Runtime", "Strings", lang, "Resources.resw");
            if (!File.Exists(reswPath))
            {
                reswPath = Path.Combine(basePath, "Strings", lang, "Resources.resw");
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
            System.Diagnostics.Debug.WriteLine($"RuntimeLocalizationHelper.LoadStrings failed: {ex}");
            _strings = null;
        }
    }

    public static string GetString(string key)
    {
        if (_strings != null && _strings.TryGetValue(key, out var value))
            return value;
        return key;
    }

    public static string GetStringFormatted(string key, params object[] args)
    {
        return string.Format(GetString(key), args);
    }

    public static void ApplyUidResources(FrameworkElement root)
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
        foreach (var child in EnumerateLogicalChildren<FrameworkElement>(root))
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
            switch (prop)
            {
                case "Content":
                    if (element is ContentControl ctrl) ctrl.Content = value;
                    else if (element is AppBarButton abb) abb.Label = value;
                    break;
                case "Text":
                    if (element is TextBlock tb) tb.Text = value;
                    else if (element is TextBox txb) txb.Text = value;
                    break;
                case "Title":
                    if (element is InfoBar ib) ib.Title = value;
                    else if (element is ContentDialog cd) cd.Title = value;
                    break;
                case "Message":
                    if (element is InfoBar ib2) ib2.Message = value;
                    break;
                case "Header":
                    if (element is ContentDialog cd2) cd2.Title = value;
                    break;
            }
        }
    }

    private static IEnumerable<T> EnumerateLogicalChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is T t) yield return t;
                foreach (var grandChild in EnumerateLogicalChildren<T>(child)) yield return grandChild;
            }
        }
        else if (parent is Border border && border.Child is DependencyObject borderChild)
        {
            if (borderChild is T t) yield return t;
            foreach (var grandChild in EnumerateLogicalChildren<T>(borderChild)) yield return grandChild;
        }
        else if (parent is ContentControl cc && cc.Content is DependencyObject content)
        {
            if (content is T t) yield return t;
            foreach (var grandChild in EnumerateLogicalChildren<T>(content)) yield return grandChild;
        }
        else if (parent is ScrollViewer sv && sv.Content is DependencyObject svContent)
        {
            if (svContent is T t) yield return t;
            foreach (var grandChild in EnumerateLogicalChildren<T>(svContent)) yield return grandChild;
        }
        else if (parent is ItemsControl ic)
        {
            foreach (var item in ic.Items)
            {
                if (item is DependencyObject dObj)
                {
                    if (dObj is T t) yield return t;
                    foreach (var grandChild in EnumerateLogicalChildren<T>(dObj)) yield return grandChild;
                }
            }
        }
    }
}