using Wpf.Ui.Controls;

namespace HotspotShare.Models;

internal sealed class AppNavigationItem
{
    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public SymbolRegular Symbol { get; init; }

    public string PageTag { get; init; } = string.Empty;
}
