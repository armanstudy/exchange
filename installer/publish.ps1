<#
.SYNOPSIS
    Tests, publishes (self-contained win-x64 single-file EXE) and optionally packages LENJ Exchange Manager.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\publish.ps1
    powershell -ExecutionPolicy Bypass -File installer\publish.ps1 -SkipTests -NoInstaller

.NOTES
    Output: artifacts\publish\LenjExchange.exe and, when Inno Setup 6 is installed,
    installer\Output\LenjExchange-Setup-<version>.exe.
    installer\windows-setup\Install-Prerequisites.cmd installs the required tools on a clean machine.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$NoInstaller
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root 'artifacts\publish'
$project = Join-Path $root 'src\LenjExchange.Desktop\LenjExchange.Desktop.csproj'

function Invoke-Step([string]$title, [scriptblock]$body) {
    Write-Host "==> $title" -ForegroundColor Cyan
    & $body
    if ($LASTEXITCODE -ne 0) { throw "$title failed (exit code $LASTEXITCODE)" }
}

$propsPath = Join-Path $root 'Directory.Build.props'
$version = ([xml](Get-Content $propsPath -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { $version = '0.1.0' }

if (-not $SkipTests) {
    Invoke-Step 'dotnet test' { dotnet test (Join-Path $root 'LenjExchange.sln') -c $Configuration }
}

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Invoke-Step 'dotnet publish (win-x64, self-contained, single file)' {
    dotnet publish $project -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir
}
Write-Host "EXE: $(Join-Path $publishDir 'LenjExchange.exe')" -ForegroundColor Green

if ($NoInstaller) { return }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) {
    Write-Warning 'Inno Setup 6 (ISCC.exe) not found; skipping Setup.exe. Install it or run installer\windows-setup\Install-Prerequisites.cmd.'
    return
}
Invoke-Step 'Inno Setup compile' { & $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot 'LenjExchange.iss') }
Write-Host "Setup: $(Join-Path $PSScriptRoot "Output\LenjExchange-Setup-$version.exe")" -ForegroundColor Green
