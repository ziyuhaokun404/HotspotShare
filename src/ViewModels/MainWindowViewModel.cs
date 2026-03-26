using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotspotShare.Models;
using HotspotShare.Services;
using Wpf.Ui.Controls;

namespace HotspotShare.ViewModels;

internal partial class MainWindowViewModel : ObservableObject
{
    private readonly TetheringService _tetheringService = new();
    private string? _activeAdapterId;

    public static string[] BandOptions { get; } = ["自动", "2.4 GHz", "5 GHz"];
    private static readonly string[] BandValues = ["Auto", "TwoPointFourGigahertz", "FiveGigahertz"];

    public ObservableCollection<TetheringConnectionProfile> Profiles { get; } = [];

    [ObservableProperty]
    private TetheringConnectionProfile? _selectedProfile;

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
    private string _logText = string.Empty;

    [ObservableProperty]
    private bool _isStateOn;

    [ObservableProperty]
    private bool _isHeroStateOn;

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

    public async Task InitializeAsync(PendingPrivilegedAction? pendingAction)
    {
        UpdateAdministratorHint();
        EnsureDefaultInputs();
        await RefreshProfilesAsync(preserveSelection: false, pendingAdapterId: pendingAction?.AdapterId);
        await ExecutePendingActionAsync(pendingAction);
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
        AppendLog("已生成新的热点密码。");
    }

    [RelayCommand]
    private async Task StartSharingAsync()
    {
        if (SelectedProfile is null)
        {
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
            RequestAlert?.Invoke("无法停止热点", "当前没有可用于停止的共享连接。");
            return;
        }

        if (!IsRunningAsAdministrator())
        {
            RequestElevation?.Invoke("停止热点共享", PendingPrivilegedAction.CreateStop(adapterId));
            return;
        }

        await StopSharingCoreAsync(adapterId);
    }

    private async Task StartSharingCoreAsync(TetheringConnectionProfile profile, string ssid, string passphrase, string band)
    {
        try
        {
            SetBusy(true, "正在启动 Windows 移动热点并共享所选连接...");

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
            AppendLog("系统中没有发现可用于热点共享的连接。");
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

            var status = await _tetheringService.GetStatusAsync(adapterId);
            ApplyStatus(status, syncInputFields);
            SetResultState("状态已同步", "热点状态已更新。", InfoBarSeverity.Informational);
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
            AppendLog("检测到待执行操作，但当前进程仍不是管理员身份。");
            return;
        }

        SetResultState("已恢复操作", $"程序已重新以管理员身份启动，正在继续{pendingAction.ActionName}。", InfoBarSeverity.Informational);
        AppendLog($"程序已重新以管理员身份启动，正在继续{pendingAction.ActionName}。");

        if (pendingAction.Kind == PendingPrivilegedActionKind.Start)
        {
            var profile = Profiles.FirstOrDefault(p => p.AdapterId.Equals(pendingAction.AdapterId, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
            {
                SetResultState("无法恢复操作", "重新启动后未找到之前选择的共享连接。", InfoBarSeverity.Error);
                AppendLog("重新启动后未找到之前选择的共享连接。");
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
            AppendLog("重新启动后没有找到可停止的共享连接。");
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
        AppendLog(result.Message);

        if (!result.Success)
        {
            RequestAlert?.Invoke("操作未完成", result.Message);
        }
    }

    private void HandleException(string context, Exception exception, bool showAlert = true)
    {
        var message = $"{context}：{exception.Message}";
        SetResultState("发生错误", message, InfoBarSeverity.Error);
        AppendLog(message);

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

    private void AppendLog(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        if (string.IsNullOrWhiteSpace(LogText))
        {
            LogText = line;
            return;
        }

        var allLines = new List<string> { line };
        allLines.AddRange(LogText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Take(19));
        LogText = string.Join(Environment.NewLine, allLines);
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
    }

    private void UpdateAdministratorHint()
    {
        var isAdmin = IsRunningAsAdministrator();
        AdminTitle = isAdmin ? "管理员权限已启用" : "建议以管理员身份运行";
        AdminMessage = isAdmin
            ? "当前进程已具备管理员权限，可以直接修改热点共享与系统网络配置。"
            : "当前不是管理员身份。程序可以正常打开和查看状态，但在启动或停止热点时会提示你重新以管理员身份启动。";
        AdminSeverity = isAdmin ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
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
            RequestAlert?.Invoke("输入不完整", "热点名称不能为空。");
            return false;
        }

        if (ssid.Length > 32)
        {
            RequestAlert?.Invoke("输入不合法", "热点名称不能超过 32 个字符。");
            return false;
        }

        if (passphrase.Length is < 8 or > 63)
        {
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
}
