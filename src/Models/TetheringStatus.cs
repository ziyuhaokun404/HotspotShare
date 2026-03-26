namespace MetaHotspotShare.Models;

internal sealed class TetheringStatus
{
    public string ProfileName { get; set; } = string.Empty;

    public string AdapterId { get; set; } = string.Empty;

    public string State { get; set; } = "Unknown";

    public string Ssid { get; set; } = string.Empty;

    public string Passphrase { get; set; } = string.Empty;

    public int ClientCount { get; set; }

    public string OperationStatus { get; set; } = string.Empty;

    public string AdditionalErrorMessage { get; set; } = string.Empty;
}
