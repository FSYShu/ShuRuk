namespace ShuRuk.Host.Pages;

/// <summary>
/// Implemented by pages that can be filtered by a search keyword from the
/// owning tab container (e.g. <see cref="ModulePage"/>).
/// </summary>
public interface ISearchablePage
{
    void ApplyFilter(string keyword);
}
