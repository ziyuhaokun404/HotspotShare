using System.IO;
using System.Text;
using System.Text.Json;

namespace HotspotShare.Models;

internal enum PendingPrivilegedActionKind
{
    Start,
    Stop
}

internal sealed class PendingPrivilegedAction
{
    private const string PendingActionFileArgumentPrefix = "--pending-action-file=";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PendingPrivilegedActionKind Kind { get; init; }

    public string AdapterId { get; init; } = string.Empty;

    public string? Ssid { get; init; }

    public string? Passphrase { get; init; }

    public string? Band { get; init; }

    public string ActionName => Kind == PendingPrivilegedActionKind.Start ? "启动热点共享" : "停止热点共享";

    public static PendingPrivilegedAction CreateStart(string adapterId, string ssid, string passphrase, string band)
    {
        return new PendingPrivilegedAction
        {
            Kind = PendingPrivilegedActionKind.Start,
            AdapterId = adapterId,
            Ssid = ssid,
            Passphrase = passphrase,
            Band = band
        };
    }

    public static PendingPrivilegedAction CreateStop(string adapterId)
    {
        return new PendingPrivilegedAction
        {
            Kind = PendingPrivilegedActionKind.Stop,
            AdapterId = adapterId
        };
    }

    public static string WriteToTemporaryFile(PendingPrivilegedAction action)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HotspotShare",
            "pending");

        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, $"{DateTime.Now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json");
        var payload = JsonSerializer.Serialize(action, SerializerOptions);
        File.WriteAllText(filePath, payload, Encoding.UTF8);
        return filePath;
    }

    public static string BuildArguments(string filePath)
    {
        return PendingActionFileArgumentPrefix + Encode(filePath);
    }

    public static PendingPrivilegedAction? TryLoadFromCommandLine(IEnumerable<string> args)
    {
        var fileArgument = args.FirstOrDefault(argument => argument.StartsWith(PendingActionFileArgumentPrefix, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(fileArgument))
        {
            return null;
        }

        string? filePath = null;

        try
        {
            filePath = Decode(fileArgument[PendingActionFileArgumentPrefix.Length..]);
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            var payload = File.ReadAllText(filePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<PendingPrivilegedAction>(payload, SerializerOptions);
        }
        catch
        {
            return null;
        }
        finally
        {
            TryDelete(filePath);
        }
    }

    public static void TryDelete(string? filePath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
        }
    }

    private static string Encode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string Decode(string value)
    {
        var normalized = value
            .Replace('-', '+')
            .Replace('_', '/');

        var remainder = normalized.Length % 4;
        if (remainder > 0)
        {
            normalized = normalized.PadRight(normalized.Length + (4 - remainder), '=');
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
    }
}
