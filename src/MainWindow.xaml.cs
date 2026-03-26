using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HotspotShare.Models;
using HotspotShare.Services;

namespace HotspotShare;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly TetheringService _tetheringService = new();
    private readonly List<TetheringConnectionProfile> _profiles = [];
    private bool _isBusy;
    private bool _suppressSelectionChanged;
    private string? _activeAdapterId;
    private PendingPrivilegedAction? _pendingPrivilegedAction = PendingPrivilegedAction.TryLoadFromCommandLine(Environment.GetCommandLineArgs().Skip(1));
    private bool _pendingPrivilegedActionHandled;

    private static readonly string[] BandOptions = ["自动", "2.4 GHz", "5 GHz"];
    private static readonly string[] BandValues = ["Auto", "TwoPointFourGigahertz", "FiveGigahertz"];

    public MainWindow()
    {
        InitializeComponent();
        SourceProfileComboBox.ItemsSource = _profiles;
        BandComboBox.ItemsSource = BandOptions;
        BandComboBox.SelectedIndex = 0;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateAdministratorHint();
        EnsureDefaultInputs();
        await RefreshProfilesAsync(preserveSelection: false);
        await ExecutePendingPrivilegedActionAsync();
    }

    private async void RefreshProfilesButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshProfilesAsync(preserveSelection: true);
    }

    private async void SourceProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged || !IsLoaded || _isBusy)
        {
            return;
        }

        var selectedProfile = GetSelectedProfile();
        if (selectedProfile is null)
        {
            return;
        }

        UpdateProfileHint(selectedProfile);
        await RefreshStatusAsync(selectedProfile.AdapterId, syncInputFields: true, announceProgress: true);
    }

    private string GetSelectedBand()
    {
        var index = BandComboBox.SelectedIndex;
        return index >= 0 && index < BandValues.Length ? BandValues[index] : "Auto";
    }

    private async void StartSharingButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedProfile = GetSelectedProfile();
        if (selectedProfile is null)
        {
            MessageBox.Show(this, "请先选择一个可共享的连接。", "缺少共享连接", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var ssid = SsidTextBox.Text.Trim();
        var passphrase = PassphraseBox.Password.Trim();
        var band = GetSelectedBand();
        if (!ValidateInputs(ssid, passphrase))
        {
            return;
        }

        if (!EnsureAdministratorForPrivilegedAction(
                "启动热点共享",
                PendingPrivilegedAction.CreateStart(selectedProfile.AdapterId, ssid, passphrase, band)))
        {
            return;
        }

        await StartSharingAsync(selectedProfile, ssid, passphrase, band);
    }

    private async Task StartSharingAsync(TetheringConnectionProfile selectedProfile, string ssid, string passphrase, string band)
    {
        try
        {
            SetBusy(true, "正在启动 Windows 移动热点并共享所选连接...");

            var result = await _tetheringService.StartHotspotAsync(selectedProfile.AdapterId, ssid, passphrase, band);
            ApplyResult(result);

            if (result.Success)
            {
                _activeAdapterId = selectedProfile.AdapterId;
            }

            if (result.Status is not null)
            {
                ApplyStatus(result.Status, syncInputFields: false);
            }
            else
            {
                await RefreshStatusAsync(selectedProfile.AdapterId, syncInputFields: false, announceProgress: false);
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

    private async void StopSharingButton_Click(object sender, RoutedEventArgs e)
    {
        var adapterId = _activeAdapterId ?? GetSelectedProfile()?.AdapterId;
        if (string.IsNullOrWhiteSpace(adapterId))
        {
            MessageBox.Show(this, "当前没有可用于停止的共享连接。", "无法停止热点", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!EnsureAdministratorForPrivilegedAction("停止热点共享", PendingPrivilegedAction.CreateStop(adapterId)))
        {
            return;
        }

        await StopSharingAsync(adapterId);
    }

    private async Task StopSharingAsync(string adapterId)
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

    private void GeneratePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        PassphraseBox.Password = CreatePassphrase(12);
        SetResultState("密码已更新", "已生成新的热点密码。", Wpf.Ui.Controls.InfoBarSeverity.Informational);
        AppendLog("已生成新的热点密码。");
    }

    private async Task RefreshProfilesAsync(bool preserveSelection)
    {
        try
        {
            SetBusy(true, "正在读取系统可共享连接...");

            var previousAdapterId = preserveSelection
                ? GetSelectedProfile()?.AdapterId ?? _activeAdapterId
                : _pendingPrivilegedAction?.AdapterId;
            var profiles = (await _tetheringService.GetProfilesAsync())
                .OrderByDescending(profile => profile.IsInternetProfile)
                .ThenBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _suppressSelectionChanged = true;
            _profiles.Clear();
            _profiles.AddRange(profiles);
            SourceProfileComboBox.Items.Refresh();
            SourceProfileComboBox.SelectedItem =
                _profiles.FirstOrDefault(profile => profile.AdapterId.Equals(previousAdapterId, StringComparison.OrdinalIgnoreCase)) ??
                _profiles.FirstOrDefault();
        }
        catch (Exception ex)
        {
            HandleException("读取共享连接失败", ex);
            SetBusy(false, "就绪。");
            return;
        }
        finally
        {
            _suppressSelectionChanged = false;
        }

        var selectedProfile = GetSelectedProfile();
        if (selectedProfile is null)
        {
            UpdateProfileHint(null);
            ClearStatus();
            SetResultState("未发现可共享连接", "系统中没有发现可用于热点共享的连接。", Wpf.Ui.Controls.InfoBarSeverity.Warning);
            AppendLog("系统中没有发现可用于热点共享的连接。");
            SetBusy(false, "就绪。");
            return;
        }

        UpdateProfileHint(selectedProfile);
        await RefreshStatusAsync(selectedProfile.AdapterId, syncInputFields: true, announceProgress: false);
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
            SetResultState("状态已同步", "热点状态已更新。", Wpf.Ui.Controls.InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            HandleException("读取热点状态失败", ex, showDialog: false);
        }
        finally
        {
            if (announceProgress)
            {
                SetBusy(false, "就绪。");
            }
        }
    }

    private void ApplyStatus(TetheringStatus status, bool syncInputFields)
    {
        var profileText = string.IsNullOrWhiteSpace(status.ProfileName) ? status.AdapterId : status.ProfileName;
        var stateText = string.IsNullOrWhiteSpace(status.State) ? "-" : status.State;

        SelectedProfileValueTextBlock.Text = profileText;
        HeroProfileValueTextBlock.Text = profileText;

        StateValueTextBlock.Text = stateText;
        StateValueTextBlock.Foreground = status.State.Equals("On", StringComparison.OrdinalIgnoreCase)
            ? (Brush)FindResource("SystemFillColorSuccessBrush")
            : (Brush)FindResource("TextFillColorSecondaryBrush");
        HeroStateValueTextBlock.Text = stateText;
        HeroStateValueTextBlock.Foreground = status.State.Equals("On", StringComparison.OrdinalIgnoreCase)
            ? (Brush)FindResource("SystemFillColorSuccessBrush")
            : (Brush)FindResource("TextFillColorPrimaryBrush");

        CurrentSsidValueTextBlock.Text = string.IsNullOrWhiteSpace(status.Ssid) ? "-" : status.Ssid;
        ClientCountValueTextBlock.Text = status.ClientCount.ToString();
        HeroClientCountTextBlock.Text = status.ClientCount.ToString();
        OperationValueTextBlock.Text = string.IsNullOrWhiteSpace(status.OperationStatus) ? "-" : status.OperationStatus;

        if (status.State.Equals("On", StringComparison.OrdinalIgnoreCase))
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
                SsidTextBox.Text = status.Ssid;
            }

            if (!string.IsNullOrWhiteSpace(status.Passphrase))
            {
                PassphraseBox.Password = status.Passphrase;
            }

            EnsureDefaultInputs();
        }
    }

    private void ApplyResult(TetheringActionResult result)
    {
        SetResultState(
            result.Success ? "操作完成" : "操作未完成",
            result.Message,
            result.Success ? Wpf.Ui.Controls.InfoBarSeverity.Success : Wpf.Ui.Controls.InfoBarSeverity.Error);
        AppendLog(result.Message);

        if (!result.Success)
        {
            MessageBox.Show(this, result.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void HandleException(string context, Exception exception, bool showDialog = true)
    {
        var message = $"{context}：{exception.Message}";
        SetResultState("发生错误", message, Wpf.Ui.Controls.InfoBarSeverity.Error);
        AppendLog(message);

        if (showDialog)
        {
            MessageBox.Show(this, message, context, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetResultState(string title, string message, Wpf.Ui.Controls.InfoBarSeverity severity)
    {
        ResultInfoBar.Title = title;
        ResultInfoBar.Message = message;
        ResultInfoBar.Severity = severity;
        ResultInfoBar.IsOpen = true;
    }

    private void AppendLog(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        if (string.IsNullOrWhiteSpace(LogTextBox.Text))
        {
            LogTextBox.Text = line;
            return;
        }

        var allLines = new List<string> { line };
        allLines.AddRange(LogTextBox.Text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Take(19));
        LogTextBox.Text = string.Join(Environment.NewLine, allLines);
    }

    private void SetBusy(bool isBusy, string message)
    {
        _isBusy = isBusy;
        BusyStateTextBlock.Text = message;
        BusyStateTextBlock.Foreground = isBusy
            ? (Brush)FindResource("TextFillColorPrimaryBrush")
            : (Brush)FindResource("TextFillColorSecondaryBrush");

        BusyProgressRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        BusyStateIcon.Symbol = isBusy
            ? Wpf.Ui.Controls.SymbolRegular.ArrowClockwise20
            : Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle20;
        BusyStateIcon.Foreground = isBusy
            ? (Brush)FindResource("AccentTextFillColorPrimaryBrush")
            : (Brush)FindResource("SystemFillColorSuccessBrush");

        SourceProfileComboBox.IsEnabled = !isBusy;
        RefreshProfilesButton.IsEnabled = !isBusy;
        GeneratePasswordButton.IsEnabled = !isBusy;
        StartSharingButton.IsEnabled = !isBusy;
        StopSharingButton.IsEnabled = !isBusy;
    }

    private void UpdateAdministratorHint()
    {
        var isAdministrator = IsRunningAsAdministrator();

        AdminInfoBar.Title = isAdministrator ? "管理员权限已启用" : "建议以管理员身份运行";
        AdminInfoBar.Message = isAdministrator
            ? "当前进程已具备管理员权限，可以直接修改热点共享与系统网络配置。"
            : "当前不是管理员身份。程序可以正常打开和查看状态，但在启动或停止热点时会提示你重新以管理员身份启动。";
        AdminInfoBar.Severity = isAdministrator
            ? Wpf.Ui.Controls.InfoBarSeverity.Success
            : Wpf.Ui.Controls.InfoBarSeverity.Warning;
        AdminInfoBar.IsOpen = true;
    }

    private async Task ExecutePendingPrivilegedActionAsync()
    {
        if (_pendingPrivilegedActionHandled || _pendingPrivilegedAction is null)
        {
            return;
        }

        _pendingPrivilegedActionHandled = true;
        var pendingAction = _pendingPrivilegedAction;
        _pendingPrivilegedAction = null;

        if (!IsRunningAsAdministrator())
        {
            SetResultState("管理员操作未恢复", "检测到待执行操作，但当前进程仍不是管理员身份。", Wpf.Ui.Controls.InfoBarSeverity.Warning);
            AppendLog("检测到待执行操作，但当前进程仍不是管理员身份。");
            return;
        }

        SetResultState("已恢复操作", $"程序已重新以管理员身份启动，正在继续{pendingAction.ActionName}。", Wpf.Ui.Controls.InfoBarSeverity.Informational);
        AppendLog($"程序已重新以管理员身份启动，正在继续{pendingAction.ActionName}。");

        var selectedProfile = SelectProfileByAdapterId(pendingAction.AdapterId);

        if (pendingAction.Kind == PendingPrivilegedActionKind.Start)
        {
            if (selectedProfile is null)
            {
                SetResultState("无法恢复操作", "重新启动后未找到之前选择的共享连接。", Wpf.Ui.Controls.InfoBarSeverity.Error);
                AppendLog("重新启动后未找到之前选择的共享连接。");
                return;
            }

            if (!string.IsNullOrWhiteSpace(pendingAction.Ssid))
            {
                SsidTextBox.Text = pendingAction.Ssid;
            }

            if (!string.IsNullOrWhiteSpace(pendingAction.Passphrase))
            {
                PassphraseBox.Password = pendingAction.Passphrase;
            }

            if (!string.IsNullOrWhiteSpace(pendingAction.Band))
            {
                var bandIndex = Array.IndexOf(BandValues, pendingAction.Band);
                if (bandIndex >= 0)
                {
                    BandComboBox.SelectedIndex = bandIndex;
                }
            }

            EnsureDefaultInputs();

            var ssid = SsidTextBox.Text.Trim();
            var passphrase = PassphraseBox.Password.Trim();
            var band = GetSelectedBand();
            if (!ValidateInputs(ssid, passphrase))
            {
                return;
            }

            await StartSharingAsync(selectedProfile, ssid, passphrase, band);
            return;
        }

        var adapterId = pendingAction.AdapterId ?? _activeAdapterId ?? GetSelectedProfile()?.AdapterId;
        if (string.IsNullOrWhiteSpace(adapterId))
        {
            SetResultState("无法恢复操作", "重新启动后没有找到可停止的共享连接。", Wpf.Ui.Controls.InfoBarSeverity.Error);
            AppendLog("重新启动后没有找到可停止的共享连接。");
            return;
        }

        await StopSharingAsync(adapterId);
    }

    private bool EnsureAdministratorForPrivilegedAction(string actionName, PendingPrivilegedAction? pendingAction = null)
    {
        if (IsRunningAsAdministrator())
        {
            return true;
        }

        var result = MessageBox.Show(
            this,
            $"{actionName}需要管理员权限。是否现在重新以管理员身份启动程序？",
            "需要管理员权限",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            SetResultState("需要管理员权限", $"{actionName}前请先同意提权。", Wpf.Ui.Controls.InfoBarSeverity.Warning);
            return false;
        }

        string? pendingActionFile = null;

        try
        {
            var executablePath = ResolveExecutablePath();
            pendingActionFile = pendingAction is null ? null : PendingPrivilegedAction.WriteToTemporaryFile(pendingAction);
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = AppContext.BaseDirectory,
                Arguments = pendingActionFile is null ? string.Empty : PendingPrivilegedAction.BuildArguments(pendingActionFile),
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(startInfo);
            Close();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            PendingPrivilegedAction.TryDelete(pendingActionFile);
            SetResultState("已取消提权", "你取消了管理员权限请求。", Wpf.Ui.Controls.InfoBarSeverity.Warning);
            AppendLog("用户取消了管理员权限请求。");
        }
        catch (Exception ex)
        {
            PendingPrivilegedAction.TryDelete(pendingActionFile);
            HandleException("请求管理员权限失败", ex);
        }

        return false;
    }

    private static bool IsRunningAsAdministrator()
    {
        var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string ResolveExecutablePath()
    {
        var currentProcessPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(currentProcessPath) &&
            !Path.GetFileName(currentProcessPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return currentProcessPath;
        }

        var candidate = Path.Combine(AppContext.BaseDirectory, "HotspotShare.exe");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException("找不到可用于提权重启的程序文件。", candidate);
    }

    private TetheringConnectionProfile? SelectProfileByAdapterId(string? adapterId)
    {
        if (string.IsNullOrWhiteSpace(adapterId))
        {
            return null;
        }

        var profile = _profiles.FirstOrDefault(item => item.AdapterId.Equals(adapterId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return null;
        }

        _suppressSelectionChanged = true;
        try
        {
            SourceProfileComboBox.SelectedItem = profile;
        }
        finally
        {
            _suppressSelectionChanged = false;
        }

        UpdateProfileHint(profile);
        return profile;
    }

    private void UpdateProfileHint(TetheringConnectionProfile? profile)
    {
        if (profile is null)
        {
            ProfileHintTextBlock.Text = "从系统共享连接里选择一个来源连接。";
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
        ProfileHintTextBlock.Text = string.Join(" | ", flags);
    }

    private void ClearStatus()
    {
        SelectedProfileValueTextBlock.Text = "-";
        HeroProfileValueTextBlock.Text = "-";
        StateValueTextBlock.Text = "-";
        StateValueTextBlock.Foreground = (Brush)FindResource("TextFillColorSecondaryBrush");
        HeroStateValueTextBlock.Text = "-";
        HeroStateValueTextBlock.Foreground = (Brush)FindResource("TextFillColorPrimaryBrush");
        CurrentSsidValueTextBlock.Text = "-";
        ClientCountValueTextBlock.Text = "0";
        HeroClientCountTextBlock.Text = "0";
        OperationValueTextBlock.Text = "-";
    }

    private void EnsureDefaultInputs()
    {
        if (string.IsNullOrWhiteSpace(SsidTextBox.Text))
        {
            SsidTextBox.Text = BuildDefaultSsid();
        }

        if (string.IsNullOrWhiteSpace(PassphraseBox.Password))
        {
            PassphraseBox.Password = CreatePassphrase(12);
        }
    }

    private bool ValidateInputs(string ssid, string passphrase)
    {
        if (string.IsNullOrWhiteSpace(ssid))
        {
            MessageBox.Show(this, "热点名称不能为空。", "输入不完整", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (ssid.Length > 32)
        {
            MessageBox.Show(this, "热点名称不能超过 32 个字符。", "输入不合法", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (passphrase.Length is < 8 or > 63)
        {
            MessageBox.Show(this, "热点密码长度必须在 8 到 63 个字符之间。", "输入不合法", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        return true;
    }

    private TetheringConnectionProfile? GetSelectedProfile()
    {
        return SourceProfileComboBox.SelectedItem as TetheringConnectionProfile;
    }

    private static string BuildDefaultSsid()
    {
        var machineName = new string(Environment.MachineName.Where(character => char.IsLetterOrDigit(character) || character == '-').ToArray());
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
