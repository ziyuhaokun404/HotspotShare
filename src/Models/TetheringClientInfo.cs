using CommunityToolkit.Mvvm.ComponentModel;

namespace HotspotShare.Models;

internal partial class TetheringClientInfo : ObservableObject
{
    [ObservableProperty]
    private string _displayName = "未知设备";

    [ObservableProperty]
    private string _macAddress = string.Empty;

    [ObservableProperty]
    private string _ipAddress = string.Empty;

    [ObservableProperty]
    private string _ipAddressDisplay = "未分配";

    [ObservableProperty]
    private string _rawHostName = string.Empty;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionState = "未知";

    [ObservableProperty]
    private string _connectedDurationText = "-";

    [ObservableProperty]
    private DateTime _firstSeenAt;

    [ObservableProperty]
    private DateTime _lastSeenAt;

    [ObservableProperty]
    private string _alias = string.Empty;

    [ObservableProperty]
    private string _detectedName = "未知设备";
}
