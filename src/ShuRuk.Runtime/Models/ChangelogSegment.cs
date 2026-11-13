using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ShuRuk.Runtime.Models;

/// <summary>
/// A segment of changelog content: either markdown text or an image / 更新日志内容段:markdown文字或图片
/// </summary>
public sealed class ChangelogSegment : INotifyPropertyChanged
{
    // Markdown text (empty for image segments) / markdown文字(图片段为空)
    public string MarkdownText { get; set; } = string.Empty;
    // Image URL (empty for text segments) / 图片URL(文字段为空)
    public string ImageUrl { get; set; } = string.Empty;
    // Image alt text / 图片替代文字
    public string AltText { get; set; } = string.Empty;
    public bool IsImage => !string.IsNullOrEmpty(ImageUrl);

    // Text segment visibility (always visible for text, collapsed for image)
    // / 文字段可见性(文字段可见,图片段隐藏)
    public Visibility TextVisibility => IsImage ? Visibility.Collapsed : Visibility.Visible;
    // Image segment visibility (controlled by toggle, collapsed for text)
    // / 图片段可见性(由toggle控制,文字段隐藏)
    private Visibility _imageVisibility = Visibility.Visible;
    public Visibility ImageVisibility
    {
        get => IsImage ? _imageVisibility : Visibility.Collapsed;
        set
        {
            if (_imageVisibility != value)
            {
                _imageVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageVisibility)));
                // Notify ImageSource to reload unloaded images when switching to visible
                // / 切换为可见时通知ImageSource重新加载未加载图片
                if (value == Visibility.Visible && IsImage)
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageSource)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Image loaded state for loading placeholder / 图片加载状态(用于加载占位)
    private bool _isImageLoaded;
    public bool IsImageLoaded
    {
        get => _isImageLoaded;
        set
        {
            if (_isImageLoaded != value)
            {
                _isImageLoaded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsImageLoaded)));
                // Update visibility directly instead of computed properties / 直接更新可见性而非计算属性
                LoadingVisibility = IsImage && !value ? Visibility.Visible : Visibility.Collapsed;
                ImageLoadedVisibility = IsImage && value ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
    // Loading placeholder visibility (backing field for reliable x:Bind)
    // / 加载占位可见性(backing field确保x:Bind可靠)
    private Visibility _loadingVisibility = Visibility.Visible;
    public Visibility LoadingVisibility
    {
        get => _loadingVisibility;
        private set
        {
            if (_loadingVisibility != value)
            {
                _loadingVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LoadingVisibility)));
            }
        }
    }
    // Loaded image visibility (backing field for reliable x:Bind)
    // / 已加载图片可见性(backing field确保x:Bind可靠)
    private Visibility _imageLoadedVisibility = Visibility.Collapsed;
    public Visibility ImageLoadedVisibility
    {
        get => _imageLoadedVisibility;
        private set
        {
            if (_imageLoadedVisibility != value)
            {
                _imageLoadedVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageLoadedVisibility)));
            }
        }
    }

    // Max image width: small images use natural width, large capped
    // / 图片最大宽度:小图用自然宽度,大图限制
    public const double MaxImageWidth = 800;
    private double _imageMaxWidth = MaxImageWidth;
    public double ImageMaxWidth
    {
        get => _imageMaxWidth;
        set
        {
            if (Math.Abs(_imageMaxWidth - value) > 0.5)
            {
                _imageMaxWidth = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageMaxWidth)));
            }
        }
    }

    // Cached BitmapImage for binding (avoids string-to-Uri conversion crash)
    // / 缓存的BitmapImage(避免string转Uri崩溃)
    private ImageSource? _imageSource;
    // Bitmap loaded flag set by BitmapImage.ImageOpened (data layer, survives recycle)
    // / 位图加载标记(数据层,回收后保留)
    private bool _bitmapLoaded;
    public bool BitmapLoaded => _bitmapLoaded;
    public ImageSource? ImageSource
    {
        get
        {
            if (_imageSource is not null) return _imageSource;
            if (string.IsNullOrEmpty(ImageUrl)) return null;
            var url = NormalizeImageUrl(ImageUrl);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                System.Diagnostics.Debug.WriteLine($"[Changelog] Invalid image URL: {url}");
                return null;
            }
            var bi = new BitmapImage(uri);
            bi.ImageOpened += (s, e) =>
            {
                _bitmapLoaded = true;
                // IsImageLoaded=true sets LoadingVisibility=Collapsed and ImageLoadedVisibility=Visible
                // IsImageLoaded=true会设置LoadingVisibility=Collapsed和ImageLoadedVisibility=Visible
                IsImageLoaded = true;
                // Cap MaxWidth at natural width for small images / 小图片MaxWidth设为自然宽度避免放大
                if (bi.PixelWidth > 0)
                    ImageMaxWidth = Math.Min(MaxImageWidth, bi.PixelWidth);
            };
            bi.ImageFailed += (s, e) =>
            {
                _bitmapLoaded = true;
                LoadingVisibility = Visibility.Collapsed;
                System.Diagnostics.Debug.WriteLine($"[Changelog] Image load failed: {url}");
            };
            _imageSource = bi;
            return _imageSource;
        }
    }

    // Normalize relative image URLs to absolute (GitHub user-attachments etc.) / 补全相对图片URL为绝对路径
    private static string NormalizeImageUrl(string url)
    {
        // Decode common HTML entities in URL / 解码URL中的常见HTML实体
        url = url.Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase);
        // Fix backslashes to forward slashes / 反斜杠转正斜杠
        url = url.Replace('\\', '/');
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;
        if (url.StartsWith('/'))
            return "https://github.com" + url;
        return "https://github.com/" + url;
    }

    // Regex to match markdown images: ![alt](url) / 匹配markdown图片的正则
    private static readonly Regex ImageRegex = new(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled);
    // Regex to match <img> tags: <img src="url"> / 匹配img标签的正则
    private static readonly Regex ImgTagRegex =
        new(@"<img\s[^>]*?src\s*=\s*[""']([^""']+)[""'][^>]*?/?>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // Regex to match invalid autolinks: <content> without valid URI scheme
    // / 匹配无效自动链接:无合法URI scheme的<内容>
    // Markdig parses <...> as AutolinkInline; invalid URI crashes MyAutolinkInline ctor via new Uri()
    // / Markdig将<...>解析为AutolinkInline;无效URI导致MyAutolinkInline构造函数new Uri()崩溃
    private static readonly Regex InvalidAutolinkRegex =
        new(@"<(?!https?://|ftp://|mailto:)([^>]+)>", RegexOptions.Compiled);

    // Escape invalid autolinks to prevent UriFormatException in MarkdownTextBlock
    // / 转义无效自动链接防止MarkdownTextBlock中UriFormatException
    private static string EscapeInvalidAutolinks(string markdown)
    {
        // Skip if no angle brackets / 无尖括号则跳过
        if (!markdown.Contains('<')) return markdown;
        return InvalidAutolinkRegex.Replace(markdown, "&lt;$1&gt;");
    }

    // Parse markdown into merged text segment + image segments / 将markdown解析为合并文字段+图片段
    // All text between images is merged into a single segment to reduce MTB count
    // / 图片间所有文字合并为单个段以减少MTB数量
    // Images are appended after text; for changelogs this preserves readability
    // / 图片追加在文字后;对changelog保持可读性
    // This also fixes reference-style link definitions being split across segments
    // / 同时修复参考式链接定义被分割到不同段的问题
    public static List<ChangelogSegment> Parse(string markdown)
    {
        var segments = new List<ChangelogSegment>();
        if (string.IsNullOrEmpty(markdown)) return segments;
        // Convert <img> tags to markdown image syntax for unified parsing
        // / 统一转换img标签为markdown图片语法
        markdown = ImgTagRegex.Replace(markdown, m => $"![]({m.Groups[1].Value})");
        // Escape <...> that aren't valid URIs to prevent AutolinkInline crash
        // / 转义非合法URI的<...>防止AutolinkInline崩溃
        markdown = EscapeInvalidAutolinks(markdown);

        // Collect all text parts and image URLs separately / 分别收集所有文字部分和图片URL
        var textBuilder = new StringBuilder();
        var images = new List<(string url, string alt)>();
        int lastEnd = 0;

        foreach (Match m in ImageRegex.Matches(markdown))
        {
            // Text before image / 图片前的文字
            if (m.Index > lastEnd)
            {
                var text = markdown[lastEnd..m.Index];
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (textBuilder.Length > 0)
                        textBuilder.Append("\n\n");
                    textBuilder.Append(text.Trim());
                }
            }
            // Image / 图片
            images.Add((m.Groups[2].Value.Trim(), m.Groups[1].Value.Trim()));
            lastEnd = m.Index + m.Length;
        }

        // Remaining text / 剩余文字
        if (lastEnd < markdown.Length)
        {
            var text = markdown[lastEnd..];
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (textBuilder.Length > 0)
                    textBuilder.Append("\n\n");
                textBuilder.Append(text.Trim());
            }
        }

        // Add merged text segment (if any) / 添加合并文字段(如有)
        if (textBuilder.Length > 0)
            segments.Add(new ChangelogSegment { MarkdownText = textBuilder.ToString() });

        // Add image segments after text / 在文字后添加图片段
        foreach (var (url, alt) in images)
            segments.Add(new ChangelogSegment { ImageUrl = url, AltText = alt });

        // Fallback: no segments parsed, use raw markdown / 无段时用原始markdown
        if (segments.Count == 0 && !string.IsNullOrWhiteSpace(markdown))
            segments.Add(new ChangelogSegment { MarkdownText = markdown.Trim() });
        return segments;
    }
}
