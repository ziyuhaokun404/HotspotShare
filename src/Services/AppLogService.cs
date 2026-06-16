using HotspotShare.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace HotspotShare.Services;

internal enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error
}

internal sealed class AppLogService : IDisposable
{
    private readonly string _logDirectory;
    private readonly int _memoryLimit;
    private readonly int _retentionDays;
    private readonly object _syncRoot = new();

    public ObservableCollection<AppLogEntry> Entries { get; } = [];

    public AppLogService(string logDirectory, int memoryLimit = 200, int retentionDays = 7)
    {
        _logDirectory = logDirectory;
        _memoryLimit = memoryLimit;
        _retentionDays = retentionDays;
    }

    public void Initialize()
    {
        Directory.CreateDirectory(_logDirectory);
        CleanupExpiredLogs();
        LoadRecentEntries();
    }

    public string GetCurrentLogFilePath()
    {
        return Path.Combine(_logDirectory, $"hotspotshare-{DateTime.Now:yyyyMMdd}.log");
    }

    public void WriteInformation(string message, string category, string? source = null, string? details = null, string? eventId = null)
    {
        Write(LogLevel.Information, message, category, source, details, eventId, null);
    }

    public void WriteDebug(string message, string category, string? source = null, string? details = null, string? eventId = null)
    {
        Write(LogLevel.Debug, message, category, source, details, eventId, null);
    }

    public void WriteWarning(string message, string category, string? source = null, string? details = null, string? eventId = null)
    {
        Write(LogLevel.Warning, message, category, source, details, eventId, null);
    }

    public void WriteError(string message, string category, string? source = null, Exception? exception = null, string? details = null, string? eventId = null)
    {
        Write(LogLevel.Error, message, category, source, details, eventId, exception);
    }

    public void ClearCurrentLog()
    {
        lock (_syncRoot)
        {
            Entries.Clear();
            var currentFile = GetCurrentLogFilePath();
            if (File.Exists(currentFile))
            {
                File.Delete(currentFile);
            }
        }

        WriteInformation("日志已清空。", "Logging", nameof(AppLogService), eventId: "log.cleared");
    }

    public string BuildTextSnapshot()
    {
        return string.Join(Environment.NewLine, Entries.Select(entry => entry.SummaryLine));
    }

    public void Dispose()
    {
    }

    private void Write(LogLevel level, string message, string category, string? source, string? details, string? eventId, Exception? exception)
    {
        var timestamp = DateTime.Now;
        var entry = new AppLogEntry
        {
            Timestamp = timestamp,
            Level = level.ToString(),
            Category = category,
            EventId = eventId,
            Message = message,
            Details = details,
            Exception = exception?.ToString(),
            Source = source
        };

        var line = FormatLogLine(timestamp, level, category, source, eventId, message, details, exception);

        lock (_syncRoot)
        {
            Directory.CreateDirectory(_logDirectory);
            File.AppendAllText(GetCurrentLogFilePath(), line + Environment.NewLine);
        }

        AppendEntry(entry);
    }

    private static string FormatLogLine(DateTime timestamp, LogLevel level, string category, string? source, string? eventId, string message, string? details, Exception? exception)
    {
        // [2026-06-16 09:33:36] [INFO ] [Application] [app.startup] 应用程序启动。
        var sb = new StringBuilder();
        sb.Append('[');
        sb.Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.Append("] [");
        sb.Append(level.ToString().ToUpperInvariant().PadRight(5));
        sb.Append("] [");
        sb.Append(category);
        sb.Append(']');

        if (!string.IsNullOrWhiteSpace(source))
        {
            sb.Append(" [");
            sb.Append(source);
            sb.Append(']');
        }

        if (!string.IsNullOrWhiteSpace(eventId))
        {
            sb.Append(" {");
            sb.Append(eventId);
            sb.Append('}');
        }

        sb.Append(' ');
        sb.Append(message);

        if (!string.IsNullOrWhiteSpace(details))
        {
            sb.Append(" | ");
            sb.Append(details);
        }

        if (exception is not null)
        {
            sb.AppendLine();
            sb.Append(exception.ToString());
        }

        return sb.ToString();
    }

    private void AppendEntry(AppLogEntry entry)
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => AppendEntryCore(entry));
            return;
        }

        AppendEntryCore(entry);
    }

    private void AppendEntryCore(AppLogEntry entry)
    {
        Entries.Insert(0, entry);
        while (Entries.Count > _memoryLimit)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }
    }

    private void CleanupExpiredLogs()
    {
        if (!Directory.Exists(_logDirectory))
        {
            return;
        }

        var threshold = DateTime.Today.AddDays(-_retentionDays);
        foreach (var path in Directory.EnumerateFiles(_logDirectory, "hotspotshare-*.log"))
        {
            try
            {
                var created = File.GetCreationTime(path);
                if (created < threshold)
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    private void LoadRecentEntries()
    {
        Entries.Clear();

        if (!Directory.Exists(_logDirectory))
        {
            return;
        }

        var recentEntries = Directory
            .EnumerateFiles(_logDirectory, "hotspotshare-*.log")
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .SelectMany(ReadEntries)
            .OrderByDescending(entry => entry.Timestamp)
            .Take(_memoryLimit)
            .ToList();

        foreach (var entry in recentEntries)
        {
            Entries.Add(entry);
        }
    }

    private static IEnumerable<AppLogEntry> ReadEntries(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var entry = ParseLogLine(line);
            if (entry is not null)
            {
                yield return entry;
            }
        }
    }

    private static AppLogEntry? ParseLogLine(string line)
    {
        // [2026-06-16 09:33:36] [INFO ] [Category] [Source] {eventId} Message | Details
        try
        {
            if (!line.StartsWith('['))
            {
                return null;
            }

            var pos = 0;

            // Timestamp: [2026-06-16 09:33:36]
            var tsEnd = line.IndexOf(']', pos);
            if (tsEnd < 0) return null;
            var tsStr = line.Substring(1, tsEnd - 1);
            if (!DateTime.TryParse(tsStr, out var timestamp)) return null;
            pos = tsEnd + 2; // skip "] "

            // Level: [INFO ]
            if (pos >= line.Length || line[pos] != '[') return null;
            var levelEnd = line.IndexOf(']', pos);
            if (levelEnd < 0) return null;
            var level = line.Substring(pos + 1, levelEnd - pos - 1).Trim();
            pos = levelEnd + 2;

            // Category: [Category]
            if (pos >= line.Length || line[pos] != '[') return null;
            var catEnd = line.IndexOf(']', pos);
            if (catEnd < 0) return null;
            var category = line.Substring(pos + 1, catEnd - pos - 1);
            pos = catEnd + 1;

            string? source = null;
            string? eventId = null;

            // Optional: [Source]
            if (pos < line.Length && line[pos] == ' ')
            {
                pos++;
                if (pos < line.Length && line[pos] == '[')
                {
                    var srcEnd = line.IndexOf(']', pos);
                    if (srcEnd > pos)
                    {
                        source = line.Substring(pos + 1, srcEnd - pos - 1);
                        pos = srcEnd + 1;
                    }
                }
            }

            // Optional: {eventId}
            if (pos < line.Length && line[pos] == ' ')
            {
                pos++;
                if (pos < line.Length && line[pos] == '{')
                {
                    var idEnd = line.IndexOf('}', pos);
                    if (idEnd > pos)
                    {
                        eventId = line.Substring(pos + 1, idEnd - pos - 1);
                        pos = idEnd + 1;
                    }
                }
            }

            // Message (rest of line, split by " | " for details)
            string message;
            string? details = null;
            string? exception = null;

            if (pos < line.Length && line[pos] == ' ')
            {
                pos++;
            }

            var remaining = line[pos..];
            var detailsSep = remaining.IndexOf(" | ");
            if (detailsSep >= 0)
            {
                message = remaining[..detailsSep];
                details = remaining[(detailsSep + 3)..];
            }
            else
            {
                message = remaining;
            }

            return new AppLogEntry
            {
                Timestamp = timestamp,
                Level = level,
                Category = category,
                EventId = eventId,
                Message = message,
                Details = details,
                Exception = exception,
                Source = source
            };
        }
        catch
        {
            return null;
        }
    }
}
