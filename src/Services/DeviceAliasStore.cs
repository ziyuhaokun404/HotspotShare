using System.IO;
using System.Text.Json;

namespace HotspotShare.Services;

internal sealed class DeviceAliasStore
{
    private const string Category = "DeviceAlias";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly AppLogService _log;

    public DeviceAliasStore(AppLogService log)
    {
        _log = log;
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
                _log.WriteDebug("设备备注名文件不存在，使用空集合。", Category, eventId: "alias.load.not-found");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(_filePath);
            var aliases = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            var result = aliases is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(aliases, StringComparer.OrdinalIgnoreCase);
            _log.WriteDebug($"已加载 {result.Count} 个设备备注名。", Category, eventId: "alias.load.success");
            return result;
        }
        catch (Exception ex)
        {
            _log.WriteWarning($"加载设备备注名失败：{ex.Message}", Category, eventId: "alias.load.error");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Save(IReadOnlyDictionary<string, string> aliases)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var json = JsonSerializer.Serialize(aliases, JsonOptions);
            File.WriteAllText(_filePath, json);
            _log.WriteDebug($"已保存 {aliases.Count} 个设备备注名。", Category, eventId: "alias.save.success");
        }
        catch (Exception ex)
        {
            _log.WriteError("保存设备备注名失败。", Category, exception: ex, eventId: "alias.save.error");
            throw;
        }
    }
}
