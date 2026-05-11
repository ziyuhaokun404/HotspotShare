namespace HotspotShare.Models;

internal sealed class NetworkSharingStatus
{
    public string NatName { get; set; } = string.Empty;

    public string SourceAdapterId { get; set; } = string.Empty;

    public string SourceInterfaceAlias { get; set; } = string.Empty;

    public string SourceGateway { get; set; } = string.Empty;

    public string InternalInterfaceAlias { get; set; } = string.Empty;

    public string InternalPrefix { get; set; } = string.Empty;

    public int? SourceInterfaceMetric { get; set; }
}
