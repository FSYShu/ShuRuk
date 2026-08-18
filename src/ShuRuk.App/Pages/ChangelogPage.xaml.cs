using Microsoft.UI.Xaml.Controls;
using ShuRuk.App;

namespace ShuRuk.App.Pages;

public sealed partial class ChangelogPage : Page
{
    public ChangelogPage()
    {
        InitializeComponent();
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        if (ChangelogEmptyText is not null)
            ChangelogEmptyText.Text = LocalizationHelper.GetString("String_ChangelogEmpty");
    }
}
