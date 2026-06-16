using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotspotShare.Models;
using HotspotShare.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace HotspotShare.ViewModels;

internal partial class MainWindowViewModel : ObservableObject
{
    private readonly AppLogService _logService =
        System.Windows.Application.Current is App
            ? App.Logs
            : new AppLogService(Path.Combine(Path.GetTempPath(), "HotspotShare", "design-logs"));
    private readonly DeviceAliasStore _deviceAliasStore;
    private readonly TetheringService _tetheringService;
    private readonly Dictionary<string, TetheringClientInfo> _clientCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _deviceAliases;
    private static readonly TimeSpan ClientDisconnectRetention = TimeSpan.FromSeconds(60);
    private string? _activeAdapterId;

    public static string[] BandOptions { get; } = ["自动", "2.4 GHz", "5 GHz"];
    private static readonly string[] BandValues = ["Auto", "TwoPointFourGigahertz", "FiveGigahertz"];

    public ObservableCollection<TetheringConnectionProfile> Profiles { get; } = [];
    public ObservableCollection<TetheringClientInfo> ConnectedClients { get; } = [];
    public ObservableCollection<AppLogEntry> RecentLogs => _logService.Entries;
    public ObservableCollection<AppNavigationItem> NavigationItems { get; } =
    [
        new() { Title = "控制台", Description = "热点控制", Symbol = SymbolRegular.Wifi320, PageTag = "dashboard" },
        new() { Title = "设备", Description = "连接设备", Symbol = SymbolRegular.People20, PageTag = "devices" },
        new() { Title = "日志", Description = "执行日志", Symbol = SymbolRegular.DocumentText20, PageTag = "logs" },
        new() { Title = "关于", Description = "软件信息", Symbol = SymbolRegular.Info20, PageTag = "about" }
    ];

    [ObservableProperty]
    private TetheringConnectionProfile? _selectedProfile;

    [ObservableProperty]
    private AppNavigationItem? _selectedNavigationItem;

    [ObservableProperty]
    private string _ssid = string.Empty;

    [ObservableProperty]
    private string _passphrase = string.Empty;

    [ObservableProperty]
    private int _selectedBandIndex;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyMessage = "就绪。";

    [ObservableProperty]
    private string _currentPageTag = "dashboard";

    [ObservableProperty]
    private string _heroProfileValue = "-";

    [ObservableProperty]
    private string _heroStateValue = "Unknown";

    [ObservableProperty]
    private string _heroClientCount = "0";

    [ObservableProperty]
    private string _selectedProfileValue = "-";

    [ObservableProperty]
    private string _stateValue = "-";

    [ObservableProperty]
    private string _currentSsidValue = "-";

    [ObservableProperty]
    private string _clientCountValue = "0";

    [ObservableProperty]
    private string _operationValue = "-";

    [ObservableProperty]
    private string _resultTitle = "准备就绪";

    [ObservableProperty]
    private string _resultMessage = "等待操作。";

    [ObservableProperty]
    private InfoBarSeverity _resultSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private string _adminTitle = string.Empty;

    [ObservableProperty]
    private string _adminMessage = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity _adminSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private string _profileHint = "从系统共享连接里选择一个来源连接。";

    [ObservableProperty]
    private AppLogEntry? _selectedLogEntry;

    [ObservableProperty]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private SymbolRegular _themeIcon = SymbolRegular.WeatherSunny20;

    [ObservableProperty]
    private bool _isStateOn;

    [ObservableProperty]
    private bool _isHeroStateOn;

    [ObservableProperty]
    private bool _isAutoRefreshEnabled;

    private CancellationTokenSource? _autoRefreshCts;

    /// <summary>
    /// View 层注册此回调处理 UAC 提权（需要 Window.Close 和 Process.Start）。
    /// </summary>
    public Action<string, PendingPrivilegedAction>? RequestElevation { get; set; }

    /// <summary>
    /// View 层注册此回调处理 MessageBox 弹窗。
    /// </summary>
    public Func<string, string, bool>? RequestConfirm { get; set; }

    /// <summary>
    /// View 层注册此回调显示提示消息。
    /// </summary>
    public Action<string, string>? RequestAlert { get; set; }

    public MainWindowViewModel()
    {
        _deviceAliasStore = new DeviceAliasStore(_logService);
        _tetheringService = new TetheringService(_logService);
        _deviceAliases = _deviceAliasStore.Load();
        SelectedNavigationItem = NavigationItems.FirstOrDefault();
        CurrentPageTag = SelectedNavigationItem?.PageTag ?? "dashboard";
        RecentLogs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(LogText));
            OnPropertyChanged(nameof(LogCountValue));
            OnPropertyChanged(nameof(ErrorLogCountValue));
            OnPropertyChanged(nameof(WarningLogCountValue));
            OnPropertyChanged(nameof(LatestLogTimeValue));
            if (RecentLogs.Count == 0)
            {
                SelectedLogEntry = null;
                return;
            }

            if (SelectedLogEntry is null || !RecentLogs.Contains(SelectedLogEntry))
            {
                SelectedLogEntry = RecentLogs[0];
            }
        };

        if (RecentLogs.Count > 0)
        {
            SelectedLogEntry = RecentLogs[0];
        }
    }

    public string LogText => _logService.BuildTextSnapshot();
    public string LogCountValue => RecentLogs.Count.ToString();
    public string ErrorLogCountValue => RecentLogs.Count(entry => string.Equals(entry.Level, "Error", StringComparison.OrdinalIgnoreCase)).ToString();
    public string WarningLogCountValue => RecentLogs.Count(entry => string.Equals(entry.Level, "Warning", StringComparison.OrdinalIgnoreCase)).ToString();
    public string LatestLogTimeValue => RecentLogs.Count == 0 ? "-" : RecentLogs.Max(entry => entry.Timestamp).ToString("HH:mm:ss");

    public async Task InitializeAsync(PendingPrivilegedAction? pendingAction)
    {
        WriteInformationLog("应用程序正在初始化。", category: "Application", eventId: "app.init.begin",
            details: pendingAction is not null ? $"PendingAction={pendingAction.ActionName}" : null);
        UpdateAdministratorHint();
        EnsureDefaultInputs();
        await RefreshProfilesAsync(preserveSelection: false, pendingAdapterId: pendingAction?.AdapterId);
        await ExecutePendingActionAsync(pendingAction);
        WriteInformationLog("应用程序初始化完成。", category: "Application", eventId: "app.init.complete");
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        ApplicationThemeManager.Apply(IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);
        ThemeIcon = IsDarkTheme ? SymbolRegular.WeatherSunny20 : SymbolRegular.WeatherMoon20;
        WriteDebugLog($"已切换主题为 {(IsDarkTheme ? "深色" : "浅色")} 模式。", category: "UI", eventId: "ui.theme.toggled");
    }

    partial void OnIsAutoRefreshEnabledChanged(bool value)
    {
        if (value)
        {
            StartAutoRefresh();
        }
        else
        {
            StopAutoRefresh();
        }
    }

    private void StartAutoRefresh()
    {
        StopAutoRefresh();
        _autoRefreshCts = new CancellationTokenSource();
        _ = AutoRefreshLoopAsync(_autoRefreshCts.Token);
        WriteDebugLog("已开启自动刷新（每 5 秒）。", category: "Status", eventId: "status.auto-refresh.started");
    }

    private void StopAutoRefresh()
    {
        _autoRefreshCts?.Cancel();
        _autoRefreshCts?.Dispose();
        _autoRefreshCts = null;
        WriteDebugLog("已停止自动刷新。", category: "Status", eventId: "status.auto-refresh.stopped");
    }

    private async Task AutoRefreshLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (IsBusy)
            {
                continue;
            }

            var adapterId = _activeAdapterId ?? SelectedProfile?.AdapterId;
            if (!string.IsNullOrWhiteSpace(adapterId))
            {
                await RefreshStatusAsync(adapterId, syncInputFields: false, announceProgress: false);
            }
        }
    }

    partial void OnSelectedProfileChanged(TetheringConnectionProfile? value)
    {
        if (IsBusy || value is null)
        {
            return;
        }

        UpdateProfileHint(value);
        _ = RefreshStatusAsync(value.AdapterId, syncInputFields: true, announceProgress: true);
    }

    partial void OnSelectedNavigationItemChanged(AppNavigationItem? value)
    {
        if (value is null)
        {
            return;
        }

        CurrentPageTag = value.PageTag;
    }

    [RelayCommand]
    private async Task RefreshProfilesAsync()
    {
        await RefreshProfilesAsync(preserveSelection: true, pendingAdapterId: null);
    }

    [RelayCommand]
    private void GeneratePassword()
    {
        Passphrase = CreatePassphrase(12);
        SetResultState("密码已更新", "已生成新的热点密码。", InfoBarSeverity.Informational);
        WriteInformationLog("已生成新的热点密码。", category: "Configuration", eventId: "hotspot.passphrase.generated");
    }

    [RelayCommand]
    private void ClearLog()
    {
        _logService.ClearCurrentLog();
        SetResultState("日志已清空", "已清空执行日志。", InfoBarSeverity.Informational);
        OnPropertyChanged(nameof(LogText));
    }

    [RelayCommand]
    private async Task StartSharingAsync()
    {
        if (SelectedProfile is null)
        {
            WriteWarningLog("启动热点前未选择共享连接。", category: "Hotspot", eventId: "hotspot.start.missing-profile");
            RequestAlert?.Invoke("缺少共享连接", "请先选择一个可共享的连接。");
            return;
        }

        var ssid = Ssid.Trim();
        var passphrase = Passphrase.Trim();
        var band = GetSelectedBand();

        if (!ValidateInputs(ssid, passphrase))
        {
            return;
        }

        if (!IsRunningAsAdministrator())
        {
            WriteWarningLog(
                "启动热点需要管理员权限，已请求提权。",
                category: "Hotspot",
                eventId: "hotspot.start.elevation-required",
                details: $"AdapterId={SelectedProfile.AdapterId}; SSID={ssid}; Band={band}");
            RequestElevation?.Invoke("启动热点共享",
                PendingPrivilegedAction.CreateStart(SelectedProfile.AdapterId, ssid, passphrase, band));
            return;
        }

        await StartSharingCoreAsync(SelectedProfile, ssid, passphrase, band);
    }

    [RelayCommand]
    private async Task StopSharingAsync()
    {
        var adapterId = _activeAdapterId ?? SelectedProfile?.AdapterId;
        if (string.IsNullOrWhiteSpace(adapterId))
        {
            WriteWarningLog("停止热点时未找到可用的共享连接。", category: "Hotspot", eventId: "hotspot.stop.missing-profile");
            RequestAlert?.Invoke("无法停止热点", "当前没有可用于停止的共享连接。");
            return;
        }

        if (!IsRunningAsAdministrator())
        {
            WriteWarningLog(
                "停止热点需要管理员权限，已请求提权。",
                category: "Hotspot",
                eventId: "hotspot.stop.elevation-required",
                details: $"AdapterId={adapterId}");
            RequestElevation?.Invoke("停止热点共享", PendingPrivilegedAction.CreateStop(adapterId));
            return;
        }

        await StopSharingCoreAsync(adapterId);
    }

    [RelayCommand]
    private void SaveClientAlias(TetheringClientInfo? client)
    {
        if (client is null || string.IsNullOrWhiteSpace(client.MacAddress))
        {
            return;
        }

        var alias = client.Alias.Trim();
        if (string.IsNullOrWhiteSpace(alias))
        {
            _deviceAliases.Remove(client.MacAddress);
            client.Alias = string.Empty;
        }
        else
        {
            _deviceAliases[client.MacAddress] = alias;
            client.Alias = alias;
        }

        client.DisplayName = ResolveDisplayName(client.Alias, client.DetectedName);
        PersistAliases();
        SetResultState("备注名已保存", $"已更新 {client.MacAddress} 的备注名。", InfoBarSeverity.Success);
        WriteInformationLog($"已更新设备备注名：{client.MacAddress} -> {client.DisplayName}", category: "Device", eventId: "device.alias.saved", details: $"MAC={client.MacAddress}; Alias={client.DisplayName}");
    }

    [RelayCommand]
    private void ClearClientAlias(TetheringClientInfo? client)
    {
        if (client is null || string.IsNullOrWhiteSpace(client.MacAddress))
        {
            return;
        }

        _deviceAliases.Remove(client.MacAddress);
        client.Alias = string.Empty;
        client.DisplayName = ResolveDisplayName(client.Alias, client.DetectedName);
        PersistAliases();
        SetResultState("备注名已清除", $"已恢复 {client.MacAddress} 的自动识别名称。", InfoBarSeverity.Informational);
        WriteInformationLog($"已清除设备备注名：{client.MacAddress}", category: "Device", eventId: "device.alias.cleared", details: $"MAC={client.MacAddress}");
    }

    private async Task StartSharingCoreAsync(TetheringConnectionProfile profile, string ssid, string passphrase, string band)
    {
        try
        {
            SetBusy(true, "正在启动 Windows 移动热点并共享所选连接...");
            WriteInformationLog(
                "开始启动移动热点。",
                category: "Hotspot",
                eventId: "hotspot.start.begin",
                details: $"Profile={profile.Name}; AdapterId={profile.AdapterId}; SSID={ssid}; Band={band}");

            var result = await _tetheringService.StartHotspotAsync(profile.AdapterId, ssid, passphrase, band);
            ApplyResult(result);

            if (result.Success)
            {
                _activeAdapterId = profile.AdapterId;
            }

            if (result.Status is not null)
            {
                ApplyStatus(result.Status, syncInputFields: false);
            }
            else
            {
                await RefreshStatusAsync(profile.AdapterId, syncInputFields: false, announceProgress: false);
            }
        }
        catch (Exception ex)
        {
            HandleException("启动热点失败", ex);
        }
        finally
        {
            SetBusy(false, "就绪。");
        }
    }

    private async Task StopSharingCoreAsync(string adapterId)
    {
        try
        {
            SetBusy(true, "正在停止移动热点...");
            WriteInformationLog(
                "开始停止移动热点。",
                category: "Hotspot",
                eventId: "hotspot.stop.begin",
                details: $"AdapterId={adapterId}");

            var result = await _tetheringService.StopHotspotAsync(adapterId);
            ApplyResult(result);

            if (result.Status is not null)
            {
                ApplyStatus(result.Status, syncInputFields: false);
            }
            else
            {
                await RefreshStatusAsync(adapterId, syncInputFields: false, announceProgress: false);
            }

            if (result.Success && result.Status?.State.Equals("Off", StringComparison.OrdinalIgnoreCase) == true)
            {
                _activeAdapterId = null;
            }
        }
        catch (Exception ex)
        {
            HandleException("停止热点失败", ex);
        }
        finally
        {
            SetBusy(false, "就绪。");
        }
    }

    private async Task RefreshProfilesAsync(bool preserveSelection, string? pendingAdapterId)
    {
        try
        {
            SetBusy(true, "正在读取系统可共享连接...");
            WriteDebugLog(
                "开始读取系统可共享连接。",
                category: "Profiles",
                eventId: "profiles.refresh.begin",
                details: $"PreserveSelection={preserveSelection}; PendingAdapterId={pendingAdapterId ?? "-"}");

            var previousAdapterId = preserveSelection
                ? SelectedProfile?.AdapterId ?? _activeAdapterId
                : pendingAdapterId;

            var profiles = (await _tetheringService.GetProfilesAsync())
                .OrderByDescending(profile => profile.IsInternetProfile)
                .ThenBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            Profiles.Clear();
            foreach (var profile in profiles)
            {
                Profiles.Add(profile);
            }

            SelectedProfile =
                Profiles.FirstOrDefault(p => p.AdapterId.Equals(previousAdapterId, StringComparison.OrdinalIgnoreCase)) ??
                Profiles.FirstOrDefault();

            WriteInformationLog(
                $"已加载 {Profiles.Count} 个可共享连接。",
                category: "Profiles",
                eventId: "profiles.refresh.success",
                details: $"SelectedAdapterId={SelectedProfile?.AdapterId ?? "-"}");
        }
        catch (Exception ex)
        {
            HandleException("读取共享连接失败", ex, showAlert: false);
            SetBusy(false, "就绪。");
            return;
        }

        if (SelectedProfile is null)
        {
            UpdateProfileHint(null);
            ClearStatus();
            SetResultState("未发现可共享连接", "系统中没有发现可用于热点共享的连接。", InfoBarSeverity.Warning);
            WriteWarningLog("系统中没有发现可用于热点共享的连接。", category: "Profiles", eventId: "profiles.refresh.empty");
            SetBusy(false, "就绪。");
            return;
        }

        UpdateProfileHint(SelectedProfile);
        await RefreshStatusAsync(SelectedProfile.AdapterId, syncInputFields: true, announceProgress: false);
        SetBusy(false, "就绪。");
    }

    private async Task RefreshStatusAsync(string adapterId, bool syncInputFields, bool announceProgress)
    {
        try
        {
            if (announceProgress)
            {
                SetBusy(true, "正在读取所选连接的热点状态...");
            }

            WriteDebugLog(
                "开始读取热点状态。",
                category: "Status",
                eventId: "status.refresh.begin",
                details: $"AdapterId={adapterId}; SyncInputs={syncInputFields}; Announce={announceProgress}");

            var status = await _tetheringService.GetStatusAsync(adapterId);
            ApplyStatus(status, syncInputFields);
            SetResultState("状态已同步", "热点状态已更新。", InfoBarSeverity.Informational);
            if (announceProgress)
            {
                WriteInformationLog(
                    "热点状态已更新。",
                    category: "Status",
                    eventId: "status.refresh.success",
                    details: $"AdapterId={status.AdapterId}; State={status.State}; ClientCount={status.ClientCount}; Ssid={status.Ssid}");
            }
            else
            {
                WriteDebugLog(
                    "热点状态已更新。",
                    category: "Status",
                    eventId: "status.refresh.success",
                    details: $"AdapterId={status.AdapterId}; State={status.State}; ClientCount={status.ClientCount}; Ssid={status.Ssid}");
            }
        }
        catch (Exception ex)
        {
            HandleException("读取热点状态失败", ex, showAlert: false);
        }
        finally
        {
            if (announceProgress)
            {
                SetBusy(false, "就绪。");
            }
        }
    }

    private async Task ExecutePendingActionAsync(PendingPrivilegedAction? pendingAction)
    {
        if (pendingAction is null)
        {
            return;
        }

        if (!IsRunningAsAdministrator())
        {
            SetResultState("管理员操作未恢复", "检测到待执行操作，但当前进程仍不是管理员身份。", InfoBarSeverity.Warning);
            WriteWarningLog("检测到待执行操作，但当前进程仍不是管理员身份。", category: "Elevation", eventId: "elevation.restore.not-admin");
            return;
        }

        SetResultState("已恢复操作", $"程序已重新以管理员身份启动，正在继续{pendingAction.ActionName}。", InfoBarSeverity.Informational);
        WriteInformationLog(
            $"程序已重新以管理员身份启动，正在继续{pendingAction.ActionName}。",
            category: "Elevation",
            eventId: "elevation.restore.success",
            details: $"Action={pendingAction.ActionName}; AdapterId={pendingAction.AdapterId ?? "-"}");

        if (pendingAction.Kind == PendingPrivilegedActionKind.Start)
        {
            var profile = Profiles.FirstOrDefault(p => p.AdapterId.Equals(pendingAction.AdapterId, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
            {
                SetResultState("无法恢复操作", "重新启动后未找到之前选择的共享连接。", InfoBarSeverity.Error);
                WriteErrorLog("重新启动后未找到之前选择的共享连接。", category: "Elevation", eventId: "elevation.restore.profile-missing");
                return;
            }

            SelectedProfile = profile;

            if (!string.IsNullOrWhiteSpace(pendingAction.Ssid))
            {
                Ssid = pendingAction.Ssid;
            }

            if (!string.IsNullOrWhiteSpace(pendingAction.Passphrase))
            {
                Passphrase = pendingAction.Passphrase;
            }

            if (!string.IsNullOrWhiteSpace(pendingAction.Band))
            {
                var bandIndex = Array.IndexOf(BandValues, pendingAction.Band);
                if (bandIndex >= 0)
                {
                    SelectedBandIndex = bandIndex;
                }
            }

            EnsureDefaultInputs();

            var ssid = Ssid.Trim();
            var passphrase = Passphrase.Trim();
            var band = GetSelectedBand();

            if (!ValidateInputs(ssid, passphrase))
            {
                return;
            }

            await StartSharingCoreAsync(profile, ssid, passphrase, band);
            return;
        }

        var adapterId = pendingAction.AdapterId ?? _activeAdapterId ?? SelectedProfile?.AdapterId;
        if (string.IsNullOrWhiteSpace(adapterId))
        {
            SetResultState("无法恢复操作", "重新启动后没有找到可停止的共享连接。", InfoBarSeverity.Error);
            WriteErrorLog("重新启动后没有找到可停止的共享连接。", category: "Elevation", eventId: "elevation.restore.stop-missing");
            return;
        }

        await StopSharingCoreAsync(adapterId);
    }

    private void ApplyStatus(TetheringStatus status, bool syncInputFields)
    {
        var profileText = string.IsNullOrWhiteSpace(status.ProfileName) ? status.AdapterId : status.ProfileName;
        var stateText = string.IsNullOrWhiteSpace(status.State) ? "-" : status.State;
        var isOn = status.State.Equals("On", StringComparison.OrdinalIgnoreCase);

        SelectedProfileValue = profileText;
        HeroProfileValue = profileText;
        StateValue = stateText;
        IsStateOn = isOn;
        HeroStateValue = stateText;
        IsHeroStateOn = isOn;
        CurrentSsidValue = string.IsNullOrWhiteSpace(status.Ssid) ? "-" : status.Ssid;
        ClientCountValue = status.ClientCount.ToString();
        HeroClientCount = status.ClientCount.ToString();
        OperationValue = string.IsNullOrWhiteSpace(status.OperationStatus) ? "-" : status.OperationStatus;
        UpdateConnectedClients(status.Clients, isOn);

        if (isOn)
        {
            _activeAdapterId = status.AdapterId;
        }
        else if (!string.IsNullOrWhiteSpace(_activeAdapterId) &&
                 _activeAdapterId.Equals(status.AdapterId, StringComparison.OrdinalIgnoreCase))
        {
            _activeAdapterId = null;
        }

        if (syncInputFields)
        {
            if (!string.IsNullOrWhiteSpace(status.Ssid))
            {
                Ssid = status.Ssid;
            }

            if (!string.IsNullOrWhiteSpace(status.Passphrase))
            {
                Passphrase = status.Passphrase;
            }

            EnsureDefaultInputs();
        }
    }

    private void ApplyResult(TetheringActionResult result)
    {
        SetResultState(
            result.Success ? "操作完成" : "操作未完成",
            result.Message,
            result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        WriteLog(
            result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error,
            result.Message,
            category: "Hotspot",
            eventId: result.Success ? "hotspot.operation.success" : "hotspot.operation.failed");

        if (!result.Success)
        {
            RequestAlert?.Invoke("操作未完成", result.Message);
        }
    }

    private void HandleException(string context, Exception exception, bool showAlert = true)
    {
        var message = $"{context}：{exception.Message}";
        SetResultState("发生错误", message, InfoBarSeverity.Error);
        WriteErrorLog(message, category: "Exception", exception: exception, eventId: "hotspot.exception");

        if (showAlert)
        {
            RequestAlert?.Invoke(context, message);
        }
    }

    private void SetResultState(string title, string message, InfoBarSeverity severity)
    {
        ResultTitle = title;
        ResultMessage = message;
        ResultSeverity = severity;
    }

    private void SetBusy(bool isBusy, string message)
    {
        IsBusy = isBusy;
        BusyMessage = message;
    }

    private void ClearStatus()
    {
        SelectedProfileValue = "-";
        HeroProfileValue = "-";
        StateValue = "-";
        IsStateOn = false;
        HeroStateValue = "-";
        IsHeroStateOn = false;
        CurrentSsidValue = "-";
        ClientCountValue = "0";
        HeroClientCount = "0";
        OperationValue = "-";
        _clientCache.Clear();
        ConnectedClients.Clear();
    }

    private void UpdateConnectedClients(IReadOnlyList<TetheringClientInfo>? clients, bool hotspotOn)
    {
        if (!hotspotOn)
        {
            _clientCache.Clear();
            ConnectedClients.Clear();
            return;
        }

        var now = DateTime.Now;
        var activeMacAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var client in clients ?? [])
        {
            var normalizedMac = NormalizeMacAddress(client.MacAddress);
            if (string.IsNullOrWhiteSpace(normalizedMac))
            {
                continue;
            }

            activeMacAddresses.Add(normalizedMac);

            if (!_clientCache.TryGetValue(normalizedMac, out var trackedClient))
            {
                trackedClient = new TetheringClientInfo
                {
                    MacAddress = normalizedMac,
                    FirstSeenAt = now
                };
                _clientCache[normalizedMac] = trackedClient;
            }

            var wasConnected = trackedClient.IsConnected;

            trackedClient.MacAddress = normalizedMac;
            trackedClient.RawHostName = client.RawHostName?.Trim() ?? string.Empty;
            trackedClient.DetectedName = NormalizeDisplayName(client.DisplayName, trackedClient.RawHostName);
            trackedClient.Alias = ResolveAlias(normalizedMac);
            trackedClient.DisplayName = ResolveDisplayName(trackedClient.Alias, trackedClient.DetectedName);
            trackedClient.IpAddress = client.IpAddress?.Trim() ?? string.Empty;
            trackedClient.IpAddressDisplay = FormatIpAddressDisplay(trackedClient.IpAddress);
            trackedClient.LastSeenAt = now;
            trackedClient.IsConnected = true;
            trackedClient.ConnectionState = string.IsNullOrWhiteSpace(trackedClient.IpAddress)
                ? "已连接（无 IP）"
                : "已连接";
            trackedClient.ConnectedDurationText = FormatDuration(now - trackedClient.FirstSeenAt);

            if (!wasConnected)
            {
                WriteInformationLog(
                    $"设备已连接：{trackedClient.DisplayName}",
                    category: "Device",
                    eventId: "device.connected",
                    details: $"MAC={trackedClient.MacAddress}; IP={trackedClient.IpAddressDisplay}; Host={trackedClient.RawHostName}");
            }
        }

        var removedMacAddresses = new List<string>();
        foreach (var trackedClient in _clientCache.Values)
        {
            if (activeMacAddresses.Contains(trackedClient.MacAddress))
            {
                continue;
            }

            var disconnectedFor = now - trackedClient.LastSeenAt;
            if (disconnectedFor > ClientDisconnectRetention)
            {
                removedMacAddresses.Add(trackedClient.MacAddress);
                continue;
            }

            if (trackedClient.IsConnected)
            {
                WriteWarningLog(
                    $"设备已断开：{trackedClient.DisplayName}",
                    category: "Device",
                    eventId: "device.disconnected",
                    details: $"MAC={trackedClient.MacAddress}; IP={trackedClient.IpAddressDisplay}");
            }

            trackedClient.IsConnected = false;
            trackedClient.ConnectionState = "刚断开";
            trackedClient.ConnectedDurationText = FormatDuration(trackedClient.LastSeenAt - trackedClient.FirstSeenAt);
            trackedClient.IpAddressDisplay = FormatIpAddressDisplay(trackedClient.IpAddress);
        }

        foreach (var macAddress in removedMacAddresses)
        {
            _clientCache.Remove(macAddress);
        }

        ConnectedClients.Clear();
        foreach (var client in _clientCache.Values.OrderBy(GetClientSortOrder).ThenByDescending(client => client.LastSeenAt))
        {
            ConnectedClients.Add(client);
        }
    }

    private static int GetClientSortOrder(TetheringClientInfo client)
    {
        if (client.IsConnected && !string.IsNullOrWhiteSpace(client.IpAddress))
        {
            return 0;
        }

        if (client.IsConnected)
        {
            return 1;
        }

        return 2;
    }

    private static string NormalizeDisplayName(string? displayName, string? rawHostName)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(rawHostName))
        {
            return rawHostName.Trim();
        }

        return "未知设备";
    }

    private string ResolveAlias(string macAddress)
    {
        return _deviceAliases.TryGetValue(macAddress, out var alias)
            ? alias
            : string.Empty;
    }

    private static string ResolveDisplayName(string? alias, string? detectedName)
    {
        if (!string.IsNullOrWhiteSpace(alias))
        {
            return alias.Trim();
        }

        return NormalizeDisplayName(detectedName, null);
    }

    private static string NormalizeMacAddress(string? macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress))
        {
            return string.Empty;
        }

        var normalized = new string(macAddress.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (normalized.Length != 12)
        {
            return macAddress.Trim().ToUpperInvariant();
        }

        return string.Join("-", Enumerable.Range(0, 6).Select(index => normalized.Substring(index * 2, 2)));
    }

    private static string FormatIpAddressDisplay(string? ipAddress)
    {
        return string.IsNullOrWhiteSpace(ipAddress) ? "未分配" : ipAddress.Trim();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss");
    }

    private void PersistAliases()
    {
        try
        {
            _deviceAliasStore.Save(_deviceAliases);
        }
        catch (Exception ex)
        {
            HandleException("保存设备备注名失败", ex, showAlert: false);
        }
    }

    private void UpdateAdministratorHint()
    {
        var isAdmin = IsRunningAsAdministrator();
        AdminTitle = isAdmin ? "管理员权限已启用" : "建议以管理员身份运行";
        AdminMessage = isAdmin
            ? "当前进程已具备管理员权限，可以直接修改热点共享与系统网络配置。"
            : "当前不是管理员身份。程序可以正常打开和查看状态，但在启动或停止热点时会提示你重新以管理员身份启动。";
        AdminSeverity = isAdmin ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        WriteDebugLog($"管理员权限状态：{(isAdmin ? "是" : "否")}", category: "Application", eventId: "app.admin-status");
    }

    private void UpdateProfileHint(TetheringConnectionProfile? profile)
    {
        if (profile is null)
        {
            ProfileHint = "从系统共享连接里选择一个来源连接。";
            return;
        }

        var flags = new List<string>();

        if (profile.IsInternetProfile)
        {
            flags.Add("当前系统正在联网");
        }

        if (!string.IsNullOrWhiteSpace(profile.ConnectivityLevel))
        {
            flags.Add($"连接状态：{profile.ConnectivityLevel}");
        }

        flags.Add($"适配器 ID：{profile.AdapterId}");
        ProfileHint = string.Join(" | ", flags);
    }

    private void EnsureDefaultInputs()
    {
        if (string.IsNullOrWhiteSpace(Ssid))
        {
            Ssid = BuildDefaultSsid();
        }

        if (string.IsNullOrWhiteSpace(Passphrase))
        {
            Passphrase = CreatePassphrase(12);
        }
    }

    private bool ValidateInputs(string ssid, string passphrase)
    {
        if (string.IsNullOrWhiteSpace(ssid))
        {
            WriteWarningLog("输入校验失败：热点名称为空。", category: "Validation", eventId: "validation.ssid-empty");
            RequestAlert?.Invoke("输入不完整", "热点名称不能为空。");
            return false;
        }

        if (ssid.Length > 32)
        {
            WriteWarningLog($"输入校验失败：热点名称过长（{ssid.Length} > 32）。", category: "Validation", eventId: "validation.ssid-too-long");
            RequestAlert?.Invoke("输入不合法", "热点名称不能超过 32 个字符。");
            return false;
        }

        if (passphrase.Length is < 8 or > 63)
        {
            WriteWarningLog($"输入校验失败：密码长度不合法（{passphrase.Length}，要求 8-63）。", category: "Validation", eventId: "validation.passphrase-invalid");
            RequestAlert?.Invoke("输入不合法", "热点密码长度必须在 8 到 63 个字符之间。");
            return false;
        }

        return true;
    }

    private string GetSelectedBand()
    {
        var index = SelectedBandIndex;
        return index >= 0 && index < BandValues.Length ? BandValues[index] : "Auto";
    }

    private static bool IsRunningAsAdministrator()
    {
        var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string BuildDefaultSsid()
    {
        var machineName = new string(Environment.MachineName.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        if (string.IsNullOrWhiteSpace(machineName))
        {
            machineName = "Host";
        }

        var ssid = $"Hotspot-{machineName}";
        return ssid.Length <= 32 ? ssid : ssid[..32];
    }

    private static string CreatePassphrase(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var buffer = new byte[length];
        RandomNumberGenerator.Fill(buffer);

        var builder = new StringBuilder(length);
        foreach (var value in buffer)
        {
            builder.Append(alphabet[value % alphabet.Length]);
        }

        return builder.ToString();
    }

    private void WriteInformationLog(string message, string category, string? eventId = null, string? details = null)
    {
        _logService.WriteInformation(message, category, nameof(MainWindowViewModel), details, eventId);
        OnPropertyChanged(nameof(LogText));
    }

    private void WriteDebugLog(string message, string category, string? eventId = null, string? details = null)
    {
        _logService.WriteDebug(message, category, nameof(MainWindowViewModel), details, eventId);
        OnPropertyChanged(nameof(LogText));
    }

    private void WriteWarningLog(string message, string category, string? eventId = null, string? details = null)
    {
        _logService.WriteWarning(message, category, nameof(MainWindowViewModel), details, eventId);
        OnPropertyChanged(nameof(LogText));
    }

    private void WriteErrorLog(string message, string category, Exception? exception = null, string? eventId = null, string? details = null)
    {
        _logService.WriteError(message, category, nameof(MainWindowViewModel), exception, details, eventId);
        OnPropertyChanged(nameof(LogText));
    }

    private void WriteLog(InfoBarSeverity severity, string message, string category, string? eventId = null, string? details = null)
    {
        switch (severity)
        {
            case InfoBarSeverity.Error:
                WriteErrorLog(message, category, eventId: eventId, details: details);
                break;
            case InfoBarSeverity.Warning:
                WriteWarningLog(message, category, eventId, details);
                break;
            default:
                WriteInformationLog(message, category, eventId, details);
                break;
        }
    }
}
