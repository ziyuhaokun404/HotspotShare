param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [ValidateSet("x64", "arm64")]
    [string]$Architecture = "x64",
    [string]$Runtime,
    [string]$Configuration = "Release",
    [string]$Version,
    [switch]$KeepSymbols
)

function Get-ReleaseVersion {
    param(
        [string]$RepositoryRoot,
        [string]$ExplicitVersion
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitVersion)) {
        return $ExplicitVersion.Trim()
    }

    $tag = git -C $RepositoryRoot describe --tags --abbrev=0 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($tag)) {
        return $tag.Trim()
    }

    return "0.0.0-dev.$(Get-Date -Format 'yyyyMMdd-HHmmss')"
}

if ([string]::IsNullOrWhiteSpace($Runtime)) {
    $Runtime = "win-$Architecture"
}

$publishScript = Join-Path $ProjectRoot "script\publish-singlefile.ps1"
$resolvedVersion = Get-ReleaseVersion -RepositoryRoot $ProjectRoot -ExplicitVersion $Version
$publishDir = Join-Path $ProjectRoot (Join-Path "artifacts" "publish-singlefile\$Runtime")
$releaseDir = Join-Path $ProjectRoot (Join-Path "artifacts" "release\$resolvedVersion")
$stagingDir = Join-Path $releaseDir $Runtime
$exeName = "HotspotShare-$Runtime.exe"
$zipName = "HotspotShare-$resolvedVersion-$Runtime.zip"
$stagedExePath = Join-Path $stagingDir $exeName
$zipPath = Join-Path $releaseDir $zipName

if (-not (Test-Path $publishScript)) {
    throw "Publish script not found: $publishScript"
}

if (Test-Path $stagingDir) {
    Remove-Item -LiteralPath $stagingDir -Recurse -Force
}

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

$publishArgs = @{
    ProjectRoot = $ProjectRoot
    Architecture = $Architecture
    Configuration = $Configuration
    Runtime = $Runtime
}

if ($KeepSymbols) {
    $publishArgs["KeepSymbols"] = $true
}

Write-Host "Preparing release package..."
Write-Host "Version: $resolvedVersion"
Write-Host "Runtime: $Runtime"
Write-Host "Stage:   $stagingDir"
Write-Host "Zip:     $zipPath"

& $publishScript @publishArgs

if ($LASTEXITCODE -ne 0) {
    throw "Publish script failed with exit code $LASTEXITCODE"
}

$publishedExe = Join-Path $publishDir "HotspotShare.exe"
if (-not (Test-Path $publishedExe)) {
    throw "Published executable not found: $publishedExe"
}

New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

Copy-Item -LiteralPath $publishedExe -Destination $stagedExePath -Force

if ($KeepSymbols) {
    $publishedPdb = Join-Path $publishDir "HotspotShare.pdb"
    if (Test-Path $publishedPdb) {
        Copy-Item -LiteralPath $publishedPdb -Destination (Join-Path $stagingDir "HotspotShare-$Runtime.pdb") -Force
    }
}

Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath -CompressionLevel Optimal -Force

Write-Host ""
Write-Host "Release completed."
Get-ChildItem -Path $releaseDir -Recurse -Force | Select-Object FullName, Length | Format-Table -AutoSize
