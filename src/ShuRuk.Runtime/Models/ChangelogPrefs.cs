namespace ShuRuk.Runtime.Models;

/// <summary>
/// Persisted UI preferences for changelog page / 更新日志页持久化UI偏好
/// </summary>
public sealed class ChangelogPrefs
{
    public bool ShowImages { get; set; } = true;
    public string FilterTag { get; set; } = "all";
}
