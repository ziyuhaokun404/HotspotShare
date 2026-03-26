namespace HotspotShare.Models;

internal sealed class TetheringActionResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public TetheringStatus? Status { get; set; }
}
