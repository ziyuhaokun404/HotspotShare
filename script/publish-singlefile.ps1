param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [ValidateSet("x64", "arm64")]
    [string]$Architecture = "x64",
    [string]$Runtime,
    [string]$Configuration = "Release",
    [switch]$KeepSymbols
)

$projectFile = Join-Path $ProjectRoot "src\HotspotShare.csproj"

if ([string]::IsNullOrWhiteSpace($Runtime)) {
    $Runtime = "win-$Architecture"
}

$outputDir = Join-Path $ProjectRoot (Join-Path "artifacts" "publish-singlefile\$Runtime")

if (-not (Test-Path $projectFile)) {
    throw "Project file not found: $projectFile"
}

if (Test-Path $outputDir) {
    Remove-Item -LiteralPath $outputDir -Recurse -Force
}

$publishArgs = @(
    "publish"
    $projectFile
    "-c"
    $Configuration
    "-r"
    $Runtime
    "--self-contained"
    "true"
    "-p:PublishSingleFile=true"
    "-p:IncludeNativeLibrariesForSelfExtract=true"
    "-o"
    $outputDir
)

Write-Host "Publishing single-file package..."
Write-Host "Project: $projectFile"
Write-Host "Output:  $outputDir"
Write-Host "Arch:    $Architecture"
Write-Host "Runtime: $Runtime"

& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (-not $KeepSymbols) {
    Get-ChildItem -Path $outputDir -Filter *.pdb -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

Write-Host ""
Write-Host "Publish completed."
Get-ChildItem -Path $outputDir -Force | Select-Object Name, Length | Format-Table -AutoSize
