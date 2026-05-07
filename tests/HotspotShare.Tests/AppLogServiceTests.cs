using HotspotShare.Services;
using System.Text.Json.Nodes;

namespace HotspotShare.Tests;

public sealed class AppLogServiceTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), "HotspotShare.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WriteInformationAsync_PersistsStructuredLogEntryToJsonFile()
    {
        var service = new AppLogService(_logDirectory, memoryLimit: 10, retentionDays: 7);

        await service.InitializeAsync();
        await service.WriteInformationAsync("Hotspot started", category: "Hotspot", source: "Tests");

        var logFile = service.GetCurrentLogFilePath();
        Assert.True(File.Exists(logFile));
        Assert.Single(service.Entries);

        var line = Assert.Single(await File.ReadAllLinesAsync(logFile));
        var json = JsonNode.Parse(line)?.AsObject();

        Assert.NotNull(json);
        Assert.Equal("Information", json!["Level"]?.GetValue<string>());
        Assert.Equal("Hotspot", json["Category"]?.GetValue<string>());
        Assert.Equal("Hotspot started", json["Message"]?.GetValue<string>());
        Assert.Equal("Tests", json["Source"]?.GetValue<string>());
    }

    [Fact]
    public async Task InitializeAsync_LoadsRecentEntriesAndKeepsOnlyConfiguredMemoryLimit()
    {
        var writer = new AppLogService(_logDirectory, memoryLimit: 2, retentionDays: 7);

        await writer.InitializeAsync();
        await writer.WriteInformationAsync("first", category: "Test");
        await writer.WriteWarningAsync("second", category: "Test");
        await writer.WriteErrorAsync("third", category: "Test");

        Assert.Collection(
            writer.Entries,
            entry => Assert.Equal("third", entry.Message),
            entry => Assert.Equal("second", entry.Message));

        var reloaded = new AppLogService(_logDirectory, memoryLimit: 2, retentionDays: 7);
        await reloaded.InitializeAsync();

        Assert.Collection(
            reloaded.Entries,
            entry => Assert.Equal("third", entry.Message),
            entry => Assert.Equal("second", entry.Message));
    }

    [Fact]
    public async Task WriteErrorAsync_PersistsEventIdDetailsAndException()
    {
        var service = new AppLogService(_logDirectory, memoryLimit: 10, retentionDays: 7);
        var exception = new InvalidOperationException("boom");

        await service.InitializeAsync();
        await service.WriteErrorAsync(
            "failed",
            category: "Hotspot",
            source: "Tests",
            exception: exception,
            details: "adapter=wifi0",
            eventId: "hotspot.start.failed");

        var line = Assert.Single(await File.ReadAllLinesAsync(service.GetCurrentLogFilePath()));
        var json = JsonNode.Parse(line)?.AsObject();

        Assert.NotNull(json);
        Assert.Equal("Error", json!["Level"]?.GetValue<string>());
        Assert.Equal("hotspot.start.failed", json["EventId"]?.GetValue<string>());
        Assert.Equal("adapter=wifi0", json["Details"]?.GetValue<string>());
        Assert.Contains("boom", json["Exception"]?.GetValue<string>());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_logDirectory))
            {
                Directory.Delete(_logDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }
}
