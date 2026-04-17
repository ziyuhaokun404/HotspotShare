using System.IO;
using System.Text.Json;

namespace HotspotShare.Services;

internal sealed class DeviceAliasStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public DeviceAliasStore()
    {
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HotspotShare",
            "device-aliases.json");
    }

    public Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(_filePath);
            var aliases = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            return aliases is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(aliases, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Save(IReadOnlyDictionary<string, string> aliases)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var json = JsonSerializer.Serialize(aliases, JsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
