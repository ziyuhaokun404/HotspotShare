namespace MetaHotspotShare.Models;

internal sealed class TetheringConnectionProfile
{
    public string Name { get; set; } = string.Empty;

    public string AdapterId { get; set; } = string.Empty;

    public string ConnectivityLevel { get; set; } = string.Empty;

    public bool IsInternetProfile { get; set; }

    public bool IsMetaSuggested { get; set; }

    public string DisplayName => IsInternetProfile ? $"{Name}  当前系统上网连接" : Name;
}
