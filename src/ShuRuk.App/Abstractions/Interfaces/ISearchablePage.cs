namespace ShuRuk.App.Abstractions.Interfaces;

/// <summary>
/// Implemented by pages that can be filtered by a search keyword from the
/// owning tab container (e.g. <see cref="ShuRuk.App.Views.ModulePage"/>).
/// </summary>
public interface ISearchablePage
{
    void ApplyFilter(string keyword);
}