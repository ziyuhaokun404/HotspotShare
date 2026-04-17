using HotspotShare.Models;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace HotspotShare.Services;

internal sealed class TetheringService
{
    private const string WindowsPowerShellPath = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string CommonScript = """
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime

function New-Response {
    param(
        [bool] $Success,
        [string] $Message,
        $Data = $null
    )

    [pscustomobject]@{
        Success = $Success
        Message = $Message
        Data = $Data
    }
}

function Resolve-ConnectionProfile {
    param([string] $AdapterId)

    $networkType = [Windows.Networking.Connectivity.NetworkInformation, Windows, ContentType=WindowsRuntime]
    $profiles = New-Object System.Collections.Generic.List[object]
    $internet = $networkType::GetInternetConnectionProfile()
    if ($internet) {
        $profiles.Add($internet)
    }

    foreach ($profile in $networkType::GetConnectionProfiles()) {
        $profiles.Add($profile)
    }

    foreach ($profile in $profiles) {
        if ($null -eq $profile -or $null -eq $profile.NetworkAdapter) {
            continue
        }

        if ($profile.NetworkAdapter.NetworkAdapterId.ToString().Equals($AdapterId, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $profile
        }
    }

    throw "找不到与适配器 ID [$AdapterId] 对应的共享连接。"
}

function Await-AsyncAction {
    param($Action)

    $method = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
        $_.Name -eq 'AsTask' -and
        -not $_.IsGenericMethod -and
        $_.GetParameters().Count -eq 1 -and
        $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction'
    } | Select-Object -First 1

    $task = $method.Invoke($null, @($Action))
    $task.GetAwaiter().GetResult() | Out-Null
}

function Await-AsyncOperation {
    param(
        $Operation,
        [Type] $ResultType
    )

    $method = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
        $_.Name -eq 'AsTask' -and
        $_.IsGenericMethodDefinition -and
        $_.GetParameters().Count -eq 1 -and
        $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
    } | Select-Object -First 1

    $closedMethod = $method.MakeGenericMethod(@($ResultType))
    $task = $closedMethod.Invoke($null, @($Operation))
    return $task.GetAwaiter().GetResult()
}

function Get-TetheringManager {
    param([string] $AdapterId)

    $managerType = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager, Windows, ContentType=WindowsRuntime]
    $profile = Resolve-ConnectionProfile $AdapterId
    $manager = $managerType::CreateFromConnectionProfile($profile)

    if ($null -eq $manager) {
        throw "系统未返回可用的移动热点管理器。"
    }

    return [pscustomobject]@{
        Profile = $profile
        Manager = $manager
    }
}

function Get-PropertyValue {
    param(
        $Object,
        [string] $Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Format-MacAddress {
    param([string] $MacAddress)

    if ([string]::IsNullOrWhiteSpace($MacAddress)) {
        return ''
    }

    $normalized = ($MacAddress -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
    if ($normalized.Length -ne 12) {
        return $MacAddress.ToUpperInvariant()
    }

    $parts = New-Object System.Collections.Generic.List[string]
    for ($index = 0; $index -lt $normalized.Length; $index += 2) {
        $parts.Add($normalized.Substring($index, 2))
    }

    return [string]::Join('-', $parts)
}

function Resolve-ClientIdentity {
    param($Client)

    $bestIpv4 = ''
    $bestIpv6 = ''
    $bestHostName = ''
    $hostNames = @(Get-PropertyValue $Client 'HostNames')

    foreach ($hostName in $hostNames) {
        if ($null -eq $hostName) {
            continue
        }

        $type = [string](Get-PropertyValue $hostName 'Type')
        $canonicalName = [string](Get-PropertyValue $hostName 'CanonicalName')
        $displayName = [string](Get-PropertyValue $hostName 'DisplayName')
        $value = if (-not [string]::IsNullOrWhiteSpace($canonicalName)) {
            $canonicalName
        }
        elseif (-not [string]::IsNullOrWhiteSpace($displayName)) {
            $displayName
        }
        else {
            [string]$hostName
        }

        if ([string]::IsNullOrWhiteSpace($value)) {
            continue
        }

        if ($type -match 'Ipv4') {
            if ([string]::IsNullOrWhiteSpace($bestIpv4)) {
                $bestIpv4 = $value
            }
            continue
        }

        if ($type -match 'Ipv6') {
            if ([string]::IsNullOrWhiteSpace($bestIpv6)) {
                $bestIpv6 = $value
            }
            continue
        }

        if ([string]::IsNullOrWhiteSpace($bestHostName)) {
            $bestHostName = $value
        }
    }

    $displayName = if (-not [string]::IsNullOrWhiteSpace($bestHostName)) {
        $bestHostName.Split('.')[0]
    }
    else {
        ''
    }

    [pscustomobject]@{
        DisplayName = $displayName
        IpAddress = if (-not [string]::IsNullOrWhiteSpace($bestIpv4)) { $bestIpv4 } else { $bestIpv6 }
        RawHostName = $bestHostName
    }
}

function New-Status {
    param(
        $Profile,
        $Manager,
        [string] $OperationStatus = '',
        [string] $AdditionalErrorMessage = ''
    )

    $config = $Manager.GetCurrentAccessPointConfiguration()
    $clients = @($Manager.GetTetheringClients())
    $clientDetails = foreach ($client in $clients) {
        if ($null -eq $client) {
            continue
        }

        $identity = Resolve-ClientIdentity $client
        [pscustomobject]@{
            DisplayName = if ([string]::IsNullOrWhiteSpace($identity.DisplayName)) { '未知设备' } else { $identity.DisplayName }
            MacAddress = Format-MacAddress ([string](Get-PropertyValue $client 'MacAddress'))
            IpAddress = [string]$identity.IpAddress
            RawHostName = [string]$identity.RawHostName
        }
    }

    [pscustomobject]@{
        ProfileName = $Profile.ProfileName
        AdapterId = $Profile.NetworkAdapter.NetworkAdapterId.ToString()
        State = $Manager.TetheringOperationalState.ToString()
        Ssid = $config.Ssid
        Passphrase = $config.Passphrase
        ClientCount = $clients.Count
        Clients = @($clientDetails)
        OperationStatus = $OperationStatus
        AdditionalErrorMessage = $AdditionalErrorMessage
    }
}
""";

    public async Task<IReadOnlyList<TetheringConnectionProfile>> GetProfilesAsync(CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync<List<TetheringConnectionProfile>>(BuildListProfilesScript(), null, cancellationToken);
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Message);
        }

        return response.Data ?? [];
    }

    public async Task<TetheringStatus> GetStatusAsync(string adapterId, CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync<TetheringStatus>(
            BuildStatusScript(),
            new Dictionary<string, string?> { ["HOTSPOT_ADAPTER_ID"] = adapterId },
            cancellationToken);

        if (!response.Success || response.Data is null)
        {
            throw new InvalidOperationException(response.Message);
        }

        return response.Data;
    }

    public async Task<TetheringActionResult> StartHotspotAsync(string adapterId, string ssid, string passphrase, string band = "Auto", CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync<TetheringStatus>(
            BuildStartScript(),
            new Dictionary<string, string?>
            {
                ["HOTSPOT_ADAPTER_ID"] = adapterId,
                ["HOTSPOT_SSID"] = ssid,
                ["HOTSPOT_PASSPHRASE"] = passphrase,
                ["HOTSPOT_BAND"] = band
            },
            cancellationToken);

        return new TetheringActionResult
        {
            Success = response.Success,
            Message = response.Message,
            Status = response.Data
        };
    }

    public async Task<TetheringActionResult> StopHotspotAsync(string adapterId, CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync<TetheringStatus>(
            BuildStopScript(),
            new Dictionary<string, string?> { ["HOTSPOT_ADAPTER_ID"] = adapterId },
            cancellationToken);

        return new TetheringActionResult
        {
            Success = response.Success,
            Message = response.Message,
            Status = response.Data
        };
    }

    private static async Task<ScriptResponse<T>> ExecuteAsync<T>(
        string script,
        IReadOnlyDictionary<string, string?>? environment,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo.FileName = WindowsPowerShellPath;
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-EncodedCommand");
        process.StartInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                process.StartInfo.Environment[pair.Key] = pair.Value ?? string.Empty;
            }
        }

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            TryKillProcess(process);
            throw;
        }

        var stdout = (await stdoutTask).Trim();
        var stderr = (await stderrTask).Trim();

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            var response = JsonSerializer.Deserialize<ScriptResponse<T>>(stdout, JsonOptions);
            if (response is not null)
            {
                if (!response.Success && string.IsNullOrWhiteSpace(response.Message) && !string.IsNullOrWhiteSpace(stderr))
                {
                    response.Message = stderr;
                }

                return response;
            }
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "PowerShell 脚本执行失败。" : stderr);
        }

        throw new InvalidOperationException("PowerShell 脚本没有返回可解析的 JSON 结果。");
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static string BuildListProfilesScript()
    {
        return CommonScript + """

try {
    $networkType = [Windows.Networking.Connectivity.NetworkInformation, Windows, ContentType=WindowsRuntime]
    $profiles = New-Object System.Collections.Generic.List[object]
    $seen = @{}

    $internet = $networkType::GetInternetConnectionProfile()
    if ($internet -and $internet.NetworkAdapter) {
        $adapterId = $internet.NetworkAdapter.NetworkAdapterId.ToString()
        $seen[$adapterId] = $true
        $profiles.Add([pscustomobject]@{
            Name = if ([string]::IsNullOrWhiteSpace($internet.ProfileName)) { $adapterId } else { $internet.ProfileName }
            AdapterId = $adapterId
            ConnectivityLevel = $internet.GetNetworkConnectivityLevel().ToString()
            IsInternetProfile = $true
        })
    }

    foreach ($profile in $networkType::GetConnectionProfiles()) {
        if ($null -eq $profile -or $null -eq $profile.NetworkAdapter) {
            continue
        }

        $adapterId = $profile.NetworkAdapter.NetworkAdapterId.ToString()
        if ($seen.ContainsKey($adapterId)) {
            continue
        }

        $seen[$adapterId] = $true
        $profiles.Add([pscustomobject]@{
            Name = if ([string]::IsNullOrWhiteSpace($profile.ProfileName)) { $adapterId } else { $profile.ProfileName }
            AdapterId = $adapterId
            ConnectivityLevel = $profile.GetNetworkConnectivityLevel().ToString()
            IsInternetProfile = $false
        })
    }

    New-Response $true '已加载可共享连接。' $profiles | ConvertTo-Json -Depth 6 -Compress
}
catch {
    New-Response $false $_.Exception.Message $null | ConvertTo-Json -Depth 6 -Compress
    exit 1
}
""";
    }

    private static string BuildStatusScript()
    {
        return CommonScript + """

try {
    $payload = Get-TetheringManager $env:HOTSPOT_ADAPTER_ID
    $status = New-Status $payload.Profile $payload.Manager
    New-Response $true '已读取热点状态。' $status | ConvertTo-Json -Depth 6 -Compress
}
catch {
    New-Response $false $_.Exception.Message $null | ConvertTo-Json -Depth 6 -Compress
    exit 1
}
""";
    }

    private static string BuildStartScript()
    {
        return CommonScript + """

try {
    $adapterId = $env:HOTSPOT_ADAPTER_ID
    $ssid = $env:HOTSPOT_SSID
    $passphrase = $env:HOTSPOT_PASSPHRASE
    $band = $env:HOTSPOT_BAND

    if ([string]::IsNullOrWhiteSpace($ssid)) {
        throw '热点名称不能为空。'
    }

    if ($passphrase.Length -lt 8 -or $passphrase.Length -gt 63) {
        throw '热点密码长度必须在 8 到 63 个字符之间。'
    }

    $payload = Get-TetheringManager $adapterId
    $manager = $payload.Manager

    $configType = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringAccessPointConfiguration, Windows, ContentType=WindowsRuntime]
    $config = [System.Activator]::CreateInstance($configType)
    $config.Ssid = $ssid
    $config.Passphrase = $passphrase

    if (-not [string]::IsNullOrWhiteSpace($band) -and $band -ne 'Auto') {
        $bandEnum = [Windows.Networking.NetworkOperators.TetheringWiFiBand, Windows, ContentType=WindowsRuntime]
        $config.Band = [Enum]::Parse($bandEnum, $band)
    }

    Await-AsyncAction ($manager.ConfigureAccessPointAsync($config))

    $resultType = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringOperationResult, Windows, ContentType=WindowsRuntime]
    $operationResult = Await-AsyncOperation ($manager.StartTetheringAsync()) $resultType
    $status = New-Status $payload.Profile $manager $operationResult.Status.ToString() $operationResult.AdditionalErrorMessage

    $success = $status.State -eq 'On' -or $operationResult.Status.ToString() -eq 'Success'
    $message = if ($success) {
        '热点已启动，所选连接正在共享。'
    }
    elseif (-not [string]::IsNullOrWhiteSpace($operationResult.AdditionalErrorMessage)) {
        $operationResult.AdditionalErrorMessage
    }
    else {
        '热点启动失败。'
    }

    New-Response $success $message $status | ConvertTo-Json -Depth 6 -Compress
    if (-not $success) {
        exit 1
    }
}
catch {
    New-Response $false $_.Exception.Message $null | ConvertTo-Json -Depth 6 -Compress
    exit 1
}
""";
    }

    private static string BuildStopScript()
    {
        return CommonScript + """

try {
    $payload = Get-TetheringManager $env:HOTSPOT_ADAPTER_ID
    $manager = $payload.Manager

    $resultType = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringOperationResult, Windows, ContentType=WindowsRuntime]
    $operationResult = Await-AsyncOperation ($manager.StopTetheringAsync()) $resultType
    $status = New-Status $payload.Profile $manager $operationResult.Status.ToString() $operationResult.AdditionalErrorMessage

    $success = $status.State -eq 'Off' -or $operationResult.Status.ToString() -eq 'Success'
    $message = if ($success) {
        '热点已停止。'
    }
    elseif (-not [string]::IsNullOrWhiteSpace($operationResult.AdditionalErrorMessage)) {
        $operationResult.AdditionalErrorMessage
    }
    else {
        '热点停止失败。'
    }

    New-Response $success $message $status | ConvertTo-Json -Depth 6 -Compress
    if (-not $success) {
        exit 1
    }
}
catch {
    New-Response $false $_.Exception.Message $null | ConvertTo-Json -Depth 6 -Compress
    exit 1
}
""";
    }

    private sealed class ScriptResponse<T>
    {
        public bool Success { get; set; }

        public string Message { get; set; } = string.Empty;

        public T? Data { get; set; }
    }
}
