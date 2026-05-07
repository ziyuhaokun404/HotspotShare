using HotspotShare.Models;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;

namespace HotspotShare.Services;

internal sealed class AppLogService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly string _logDirectory;
    private readonly int _memoryLimit;
    private readonly int _retentionDays;
    private readonly object _syncRoot = new();
    private Logger? _logger;

    public ObservableCollection<AppLogEntry> Entries { get; } = [];

    public AppLogService(string logDirectory, int memoryLimit = 200, int retentionDays = 7)
    {
        _logDirectory = logDirectory;
        _memoryLimit = memoryLimit;
        _retentionDays = retentionDays;
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_logDirectory);
        CleanupExpiredLogs();
        LoadRecentEntries();

        _logger?.Dispose();
        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(new AppLogSink(this))
            .CreateLogger();

        await Task.CompletedTask;
    }

    public string GetCurrentLogFilePath()
    {
        return Path.Combine(_logDirectory, $"hotspotshare-{DateTime.Now:yyyyMMdd}.jsonl");
    }

    public async Task WriteInformationAsync(string message, string category, string? source = null, string? details = null, string? eventId = null)
    {
        await WriteAsync(LogEventLevel.Information, message, category, source, details, eventId, null);
    }

    public async Task WriteDebugAsync(string message, string category, string? source = null, string? details = null, string? eventId = null)
    {
        await WriteAsync(LogEventLevel.Debug, message, category, source, details, eventId, null);
    }

    public async Task WriteWarningAsync(string message, string category, string? source = null, string? details = null, string? eventId = null)
    {
        await WriteAsync(LogEventLevel.Warning, message, category, source, details, eventId, null);
    }

    public async Task WriteErrorAsync(string message, string category, string? source = null, Exception? exception = null, string? details = null, string? eventId = null)
    {
        await WriteAsync(LogEventLevel.Error, message, category, source, details, eventId, exception);
    }

    public async Task ClearCurrentLogAsync()
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

        await WriteInformationAsync("日志已清空。", "Logging", nameof(AppLogService), eventId: "log.cleared");
    }

    public string BuildTextSnapshot()
    {
        return string.Join(Environment.NewLine, Entries.Select(entry => entry.SummaryLine));
    }

    public void Dispose()
    {
        _logger?.Dispose();
        _logger = null;
    }

    private Task WriteAsync(LogEventLevel level, string message, string category, string? source, string? details, string? eventId, Exception? exception)
    {
        EnsureLogger();

        _logger!
            .ForContext("Category", category)
            .ForContext("Source", source)
            .ForContext("Details", details)
            .ForContext("EventId", eventId)
            .Write(level, exception, "{LogMessage}", message);

        return Task.CompletedTask;
    }

    private void EnsureLogger()
    {
        if (_logger is not null)
        {
            return;
        }

        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(new AppLogSink(this))
            .CreateLogger();
    }

    private void Emit(LogEvent logEvent)
    {
        var entry = new AppLogEntry
        {
            Timestamp = logEvent.Timestamp.LocalDateTime,
            Level = logEvent.Level.ToString(),
            Category = GetScalarString(logEvent, "Category") ?? "General",
            EventId = GetScalarString(logEvent, "EventId"),
            Message = logEvent.Properties.TryGetValue("LogMessage", out var rawMessage)
                ? GetScalarValue(rawMessage) ?? logEvent.RenderMessage()
                : logEvent.RenderMessage(),
            Details = GetScalarString(logEvent, "Details"),
            Exception = logEvent.Exception?.ToString(),
            Source = GetScalarString(logEvent, "Source")
        };

        lock (_syncRoot)
        {
            Directory.CreateDirectory(_logDirectory);
            File.AppendAllText(GetCurrentLogFilePath(), JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine, Encoding.UTF8);
        }

        AppendEntry(entry);
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
        foreach (var path in Directory.EnumerateFiles(_logDirectory, "hotspotshare-*.jsonl"))
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
            .EnumerateFiles(_logDirectory, "hotspotshare-*.jsonl")
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

            AppLogEntry? entry = null;
            try
            {
                entry = JsonSerializer.Deserialize<AppLogEntry>(line, JsonOptions);
            }
            catch
            {
            }

            if (entry is not null)
            {
                yield return entry;
            }
        }
    }

    private static string? GetScalarString(LogEvent logEvent, string propertyName)
    {
        return logEvent.Properties.TryGetValue(propertyName, out var propertyValue)
            ? GetScalarValue(propertyValue)
            : null;
    }

    private static string? GetScalarValue(LogEventPropertyValue propertyValue)
    {
        return propertyValue switch
        {
            ScalarValue { Value: null } => null,
            ScalarValue { Value: string text } => text,
            ScalarValue scalar => scalar.Value?.ToString(),
            _ => propertyValue.ToString()
        };
    }

    private sealed class AppLogSink(AppLogService owner) : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            owner.Emit(logEvent);
        }
    }
}
