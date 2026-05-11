namespace HotspotShare.Models;

internal sealed class NetworkSharingResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public NetworkSharingStatus? Status { get; set; }
}
