namespace HotspotShare.Models;

internal sealed class AppLogEntry
{
    public DateTime Timestamp { get; set; }

    public string Level { get; set; } = "Information";

    public string Category { get; set; } = "General";

    public string? EventId { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? Details { get; set; }

    public string? Exception { get; set; }

    public string? Source { get; set; }

    public string TimestampDisplay => Timestamp.ToString("MM-dd HH:mm:ss");

    public string DetailsDisplay => string.IsNullOrWhiteSpace(Details) ? "-" : Details;

    public string ExceptionDisplay => string.IsNullOrWhiteSpace(Exception) ? "-" : Exception;

    public string EventIdDisplay => string.IsNullOrWhiteSpace(EventId) ? "-" : EventId;

    public string SourceDisplay => string.IsNullOrWhiteSpace(Source) ? "-" : Source;

    public string SummaryLine =>
        $"{Timestamp:HH:mm:ss}  [{Level}] [{Category}] {Message}";
}
