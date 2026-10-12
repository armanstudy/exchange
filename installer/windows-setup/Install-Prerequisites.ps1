<#
.SYNOPSIS
    Installs LENJ Exchange build prerequisites (Git, .NET 8 SDK, Inno Setup 6) on Windows 10/11
    and optionally builds, tests, publishes and packages the desktop app.

.PARAMETER RepoUrl
    Git URL to clone into %USERPROFILE%\Documents\LENJ-Exchange when the script is not run from a clone.

.PARAMETER Branch
    Branch to clone / check out. Default: cursor/lenj-exchange-foundation-e31a

.PARAMETER Build
    Build, test, publish and package after installing prerequisites (default on). Use -Build:$false to skip.

.PARAMETER NoBuild
    Shortcut for -Build:$false.

.NOTES
    Must stay PowerShell 5.1 compatible and be saved as UTF-8 with BOM (Persian messages).
#>
[CmdletBinding()]
param(
    [string]$RepoUrl = '',
    [string]$Branch = 'cursor/lenj-exchange-foundation-e31a',
    [switch]$Build = $true,
    [switch]$NoBuild,
    [switch]$Elevated
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
try { $OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
} catch { }

if ($NoBuild) { $Build = [switch]$false }

$script:LogFile = Join-Path $env:TEMP 'lenj-setup.log'
$script:Installed = New-Object System.Collections.Generic.List[string]
$script:Skipped = New-Object System.Collections.Generic.List[string]
$script:Failed = New-Object System.Collections.Generic.List[string]
$script:BuildSteps = New-Object System.Collections.Generic.List[string]
$script:PublishDir = $null
$script:ExePath = $null
$script:SetupPath = $null

# ---------------------------------------------------------------- helpers

function Write-Log {
    param([string]$Message)
    try {
        $line = '{0:yyyy-MM-dd HH:mm:ss}  {1}' -f (Get-Date), $Message
        Add-Content -Path $script:LogFile -Value $line -Encoding UTF8
    } catch { }
}

function Write-Step {
    param([string]$Title)
    Write-Host ''
    Write-Host ('=' * 60) -ForegroundColor Cyan
    Write-Host ("  $Title") -ForegroundColor Cyan
    Write-Host ('=' * 60) -ForegroundColor Cyan
    Write-Log "== $Title"
}

function Write-Info { param([string]$m) Write-Host "  $m" -ForegroundColor Gray; Write-Log "INFO  $m" }
function Write-Ok { param([string]$m) Write-Host "  [✓] $m" -ForegroundColor Green; Write-Log "OK    $m" }
function Write-Warn2 { param([string]$m) Write-Host "  [!] $m" -ForegroundColor Yellow; Write-Log "WARN  $m" }
function Write-Err { param([string]$m) Write-Host "  [✗] $m" -ForegroundColor Red; Write-Log "ERROR $m" }

function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Update-SessionPath {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $parts = @()
    foreach ($p in (($machine, $user, $env:Path) -join ';').Split(';')) {
        if ($p -and ($parts -notcontains $p)) { $parts += $p }
    }
    $env:Path = $parts -join ';'
}

function Add-ToPersistentPath {
    param([string]$Dir, [ValidateSet('Machine', 'User')][string]$Scope)
    $current = [Environment]::GetEnvironmentVariable('Path', $Scope)
    if (-not $current) { $current = '' }
    $items = $current.Split(';') | Where-Object { $_ }
    if ($items -notcontains $Dir) {
        [Environment]::SetEnvironmentVariable('Path', (($items + $Dir) -join ';'), $Scope)
        Write-Log "Added $Dir to $Scope PATH"
    }
    Update-SessionPath
}

# Runs a native command, streams output to console and log, returns exit code.
function Invoke-Native {
    param([string]$FilePath, [string[]]$Arguments, [string]$WorkingDirectory)
    Write-Log ("RUN   {0} {1}" -f $FilePath, ($Arguments -join ' '))
    $oldPref = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $pushed = $false
    try {
        if ($WorkingDirectory) { Push-Location $WorkingDirectory; $pushed = $true }
        & $FilePath @Arguments 2>&1 | ForEach-Object {
            $line = "$_"
            Write-Host "    $line"
            try { Add-Content -Path $script:LogFile -Value "    $line" -Encoding UTF8 } catch { }
        }
        $code = $LASTEXITCODE
    } finally {
        if ($pushed) { Pop-Location }
        $ErrorActionPreference = $oldPref
    }
    if ($null -eq $code) { $code = 0 }
    Write-Log "EXIT  $code"
    return $code
}

function Get-FileFromWeb {
    param([string]$Url, [string]$OutFile)
    Write-Info "دریافت فایل از: $Url"
    Invoke-WebRequest -Uri $Url -OutFile $OutFile -UseBasicParsing
    if (-not (Test-Path $OutFile) -or (Get-Item $OutFile).Length -eq 0) {
        throw "دانلود ناموفق بود: $Url"
    }
}

function Test-Winget {
    return [bool](Get-Command winget.exe -ErrorAction SilentlyContinue)
}

function Install-WithWinget {
    param([string]$Id, [string]$Name)
    if (-not (Test-Winget)) { return $false }
    Write-Info "نصب $Name با winget ..."
    $code = Invoke-Native -FilePath 'winget.exe' -Arguments @(
        'install', '--id', $Id, '-e', '--silent',
        '--accept-package-agreements', '--accept-source-agreements')
    Update-SessionPath
    # 0x8A15002B = already installed / no applicable upgrade
    if ($code -eq 0 -or $code -eq -1978335189) { return $true }
    Write-Warn2 "winget برای $Name با کد $code خاتمه یافت. روش دانلود مستقیم امتحان می‌شود."
    return $false
}

# ---------------------------------------------------------------- detection

function Find-Git {
    $cmd = Get-Command git.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($p in @("$env:ProgramFiles\Git\cmd\git.exe", "${env:ProgramFiles(x86)}\Git\cmd\git.exe",
                     "$env:LOCALAPPDATA\Programs\Git\cmd\git.exe")) {
        if ($p -and (Test-Path $p)) { return $p }
    }
    return $null
}

function Find-Dotnet {
    $candidates = @()
    $cmd = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($cmd) { $candidates += $cmd.Source }
    $candidates += "$env:ProgramFiles\dotnet\dotnet.exe"
    $candidates += "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    return $null
}

function Test-Dotnet8Sdk {
    $dotnet = Find-Dotnet
    if (-not $dotnet) { return $false }
    $oldPref = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $sdks = & $dotnet --list-sdks 2>$null } catch { $sdks = @() } finally { $ErrorActionPreference = $oldPref }
    foreach ($s in @($sdks)) { if ("$s" -match '^8\.') { return $true } }
    return $false
}

function Find-ISCC {
    $paths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($p in $paths) { if ($p -and (Test-Path $p)) { return $p } }
    $keys = @(
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'
    )
    foreach ($k in $keys) {
        try {
            $loc = (Get-ItemProperty -Path $k -ErrorAction Stop).InstallLocation
            if ($loc) {
                $p = Join-Path $loc 'ISCC.exe'
                if (Test-Path $p) { return $p }
            }
        } catch { }
    }
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

# ---------------------------------------------------------------- installers

function Install-Git {
    Write-Step 'مرحله ۱ از ۳: Git for Windows'
    $existing = Find-Git
    if ($existing) {
        Write-Ok "Git از قبل نصب است: $existing"
        $script:Skipped.Add('Git (از قبل نصب بود)')
        return
    }
    try {
        $ok = Install-WithWinget -Id 'Git.Git' -Name 'Git'
        if (-not $ok -or -not (Find-Git)) {
            Write-Info 'دریافت آخرین نسخه Git از GitHub ...'
            $rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/git-for-windows/git/releases/latest' -UseBasicParsing -Headers @{ 'User-Agent' = 'lenj-setup' }
            $asset = $rel.assets | Where-Object { $_.name -match '^Git-.*-64-bit\.exe$' } | Select-Object -First 1
            if (-not $asset) { throw 'فایل نصب Git در صفحه انتشار پیدا نشد.' }
            $file = Join-Path $env:TEMP $asset.name
            Get-FileFromWeb -Url $asset.browser_download_url -OutFile $file
            Write-Info 'اجرای نصب‌کننده Git (بی‌صدا) ...'
            $p = Start-Process -FilePath $file -ArgumentList '/VERYSILENT', '/NORESTART', '/NOCANCEL', '/SP-', '/SUPPRESSMSGBOXES' -Wait -PassThru
            if ($p.ExitCode -ne 0) { throw "نصب‌کننده Git با کد $($p.ExitCode) خاتمه یافت." }
        }
        Update-SessionPath
        $git = Find-Git
        if (-not $git) { throw 'Git نصب شد اما پیدا نشد.' }
        $gitDir = Split-Path $git -Parent
        if (($env:Path.Split(';')) -notcontains $gitDir) { $env:Path = "$env:Path;$gitDir" }
        Write-Ok "Git نصب شد: $git"
        $script:Installed.Add('Git for Windows')
    } catch {
        Write-Err "نصب Git ناموفق بود: $($_.Exception.Message)"
        $script:Failed.Add('Git')
    }
}

function Install-DotnetSdk {
    Write-Step 'مرحله ۲ از ۳: .NET 8 SDK'
    if (Test-Dotnet8Sdk) {
        Write-Ok ".NET 8 SDK از قبل نصب است: $(Find-Dotnet)"
        $script:Skipped.Add('.NET 8 SDK (از قبل نصب بود)')
        return
    }
    try {
        $ok = Install-WithWinget -Id 'Microsoft.DotNet.SDK.8' -Name '.NET 8 SDK'
        if (-not $ok -or -not (Test-Dotnet8Sdk)) {
            $installScript = Join-Path $env:TEMP 'dotnet-install.ps1'
            Get-FileFromWeb -Url 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installScript
            if (Test-IsAdmin) {
                $dir = Join-Path $env:ProgramFiles 'dotnet'
                $scope = 'Machine'
            } else {
                $dir = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
                $scope = 'User'
            }
            Write-Info "نصب .NET 8 SDK در: $dir"
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installScript -Channel 8.0 -InstallDir $dir -NoPath
            if ($LASTEXITCODE -ne 0) { throw "dotnet-install.ps1 با کد $LASTEXITCODE خاتمه یافت." }
            Add-ToPersistentPath -Dir $dir -Scope $scope
            [Environment]::SetEnvironmentVariable('DOTNET_ROOT', $dir, $scope)
            $env:DOTNET_ROOT = $dir
        }
        Update-SessionPath
        if (-not (Test-Dotnet8Sdk)) { throw '.NET 8 SDK نصب شد اما پیدا نشد.' }
        Write-Ok ".NET 8 SDK نصب شد: $(Find-Dotnet)"
        $script:Installed.Add('.NET 8 SDK')
    } catch {
        Write-Err "نصب .NET 8 SDK ناموفق بود: $($_.Exception.Message)"
        $script:Failed.Add('.NET 8 SDK')
    }
}

function Install-InnoSetup {
    Write-Step 'مرحله ۳ از ۳: Inno Setup 6'
    $existing = Find-ISCC
    if ($existing) {
        Write-Ok "Inno Setup از قبل نصب است: $existing"
        $script:Skipped.Add('Inno Setup 6 (از قبل نصب بود)')
        return
    }
    try {
        $ok = Install-WithWinget -Id 'JRSoftware.InnoSetup' -Name 'Inno Setup 6'
        if (-not $ok -or -not (Find-ISCC)) {
            $file = Join-Path $env:TEMP 'innosetup-setup.exe'
            Get-FileFromWeb -Url 'https://jrsoftware.org/download.php/is.exe' -OutFile $file
            Write-Info 'اجرای نصب‌کننده Inno Setup (بی‌صدا) ...'
            $p = Start-Process -FilePath $file -ArgumentList '/VERYSILENT', '/NORESTART', '/SUPPRESSMSGBOXES', '/SP-', '/ALLUSERS' -Wait -PassThru
            if ($p.ExitCode -ne 0) { throw "نصب‌کننده Inno Setup با کد $($p.ExitCode) خاتمه یافت." }
        }
        $iscc = Find-ISCC
        if (-not $iscc) { throw 'Inno Setup نصب شد اما ISCC.exe پیدا نشد.' }
        Write-Ok "Inno Setup نصب شد: $iscc"
        $script:Installed.Add('Inno Setup 6')
    } catch {
        Write-Err "نصب Inno Setup ناموفق بود: $($_.Exception.Message)"
        $script:Failed.Add('Inno Setup 6')
    }
}

# ---------------------------------------------------------------- repository

function Test-RepoRoot {
    param([string]$Path)
    if (-not $Path -or -not (Test-Path $Path)) { return $false }
    if (Test-Path (Join-Path $Path 'LenjExchange.sln')) { return $true }
    if (Get-ChildItem -Path $Path -Filter '*.sln' -File -ErrorAction SilentlyContinue) { return $true }
    return $false
}

function Resolve-Repo {
    Write-Step 'آماده‌سازی مخزن کد (Repository)'
    $scriptDir = $PSScriptRoot
    if (-not $scriptDir) { $scriptDir = Split-Path -Parent $MyInvocation.ScriptName }
    $candidate = $null
    try { $candidate = (Resolve-Path (Join-Path $scriptDir '..\..')).Path } catch { }

    if (Test-RepoRoot $candidate) {
        Write-Ok "اسکریپت از داخل مخزن اجرا شده است: $candidate"
        return $candidate
    }

    $target = Join-Path $env:USERPROFILE 'Documents\LENJ-Exchange'
    if (-not $RepoUrl) {
        if (Test-RepoRoot $target) {
            Write-Ok "مخزن در مسیر پیش‌فرض پیدا شد: $target"
            return $target
        }
        Write-Warn2 'مخزن کد پیدا نشد و پارامتر -RepoUrl هم داده نشده است؛ مرحله ساخت انجام نمی‌شود.'
        return $null
    }

    $git = Find-Git
    if (-not $git) {
        Write-Err 'Git در دسترس نیست؛ دریافت مخزن ممکن نیست.'
        return $null
    }

    if (Test-Path (Join-Path $target '.git')) {
        Write-Info "مخزن از قبل در $target وجود دارد؛ به‌روزرسانی شاخه $Branch ..."
        $null = Invoke-Native -FilePath $git -Arguments @('fetch', 'origin', $Branch) -WorkingDirectory $target
        $null = Invoke-Native -FilePath $git -Arguments @('checkout', $Branch) -WorkingDirectory $target
        $code = Invoke-Native -FilePath $git -Arguments @('pull', '--ff-only', 'origin', $Branch) -WorkingDirectory $target
        if ($code -ne 0) { Write-Warn2 'به‌روزرسانی مخزن ناموفق بود؛ با نسخه فعلی ادامه می‌دهیم.' }
    } else {
        if ((Test-Path $target) -and (Get-ChildItem -Path $target -Force -ErrorAction SilentlyContinue)) {
            Write-Err "پوشه $target وجود دارد و خالی نیست؛ لطفاً آن را جابه‌جا یا حذف کنید."
            return $null
        }
        Write-Info "دریافت مخزن در $target ..."
        $code = Invoke-Native -FilePath $git -Arguments @('clone', '--branch', $Branch, $RepoUrl, $target)
        if ($code -ne 0) {
            Write-Err 'دریافت مخزن (git clone) ناموفق بود.'
            return $null
        }
    }
    if (Test-RepoRoot $target) {
        Write-Ok "مخزن آماده است: $target"
        return $target
    }
    Write-Err "در $target فایل راه‌حل (.sln) پیدا نشد."
    return $null
}

# ---------------------------------------------------------------- build

function Invoke-Build {
    param([string]$RepoRoot)
    Write-Step 'ساخت، تست و انتشار برنامه'
    $dotnet = Find-Dotnet
    if (-not $dotnet -or -not (Test-Dotnet8Sdk)) {
        Write-Err '.NET 8 SDK در دسترس نیست؛ ساخت انجام نمی‌شود.'
        $script:Failed.Add('Build')
        return
    }
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

    $sln = Join-Path $RepoRoot 'LenjExchange.sln'
    if (-not (Test-Path $sln)) {
        $found = Get-ChildItem -Path $RepoRoot -Filter '*.sln' -File | Select-Object -First 1
        if ($found) { $sln = $found.FullName } else { $sln = $null }
    }
    $target = @()
    if ($sln) { $target = @($sln) }

    Write-Info 'dotnet restore ...'
    if ((Invoke-Native -FilePath $dotnet -Arguments (@('restore') + $target) -WorkingDirectory $RepoRoot) -ne 0) {
        Write-Err 'بازیابی بسته‌ها (restore) ناموفق بود. اتصال اینترنت یا پروکسی را بررسی کنید.'
        $script:Failed.Add('dotnet restore')
        return
    }
    $script:BuildSteps.Add('restore')

    Write-Info 'dotnet build -c Release ...'
    if ((Invoke-Native -FilePath $dotnet -Arguments (@('build') + $target + @('-c', 'Release', '--no-restore')) -WorkingDirectory $RepoRoot) -ne 0) {
        Write-Err 'ساخت (build) ناموفق بود. جزئیات در فایل گزارش است.'
        $script:Failed.Add('dotnet build')
        return
    }
    $script:BuildSteps.Add('build')

    Write-Info 'dotnet test ...'
    if ((Invoke-Native -FilePath $dotnet -Arguments (@('test') + $target + @('-c', 'Release', '--no-build')) -WorkingDirectory $RepoRoot) -ne 0) {
        Write-Warn2 'برخی تست‌ها ناموفق بودند؛ انتشار با این حال ادامه می‌یابد.'
        $script:Failed.Add('dotnet test')
    } else {
        $script:BuildSteps.Add('test')
    }

    $project = Join-Path $RepoRoot 'src\LenjExchange.Desktop'
    if (-not (Test-Path $project)) {
        Write-Err "پروژه دسکتاپ پیدا نشد: $project"
        $script:Failed.Add('dotnet publish')
        return
    }
    $publishDir = Join-Path $RepoRoot 'artifacts\publish'
    Write-Info 'dotnet publish (win-x64, self-contained, single file) ...'
    $code = Invoke-Native -FilePath $dotnet -Arguments @(
        'publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-p:PublishSingleFile=true', '-o', $publishDir) -WorkingDirectory $RepoRoot
    if ($code -ne 0) {
        Write-Err 'انتشار (publish) ناموفق بود.'
        $script:Failed.Add('dotnet publish')
        return
    }
    $script:BuildSteps.Add('publish')
    $script:PublishDir = $publishDir
    $exe = Get-ChildItem -Path $publishDir -Filter '*.exe' -File -ErrorAction SilentlyContinue |
        Sort-Object Length -Descending | Select-Object -First 1
    if ($exe) { $script:ExePath = $exe.FullName }
    Write-Ok "خروجی برنامه: $publishDir"

    Invoke-Package -RepoRoot $RepoRoot
}

function Invoke-Package {
    param([string]$RepoRoot)
    Write-Step 'ساخت فایل نصب (Setup.exe) با Inno Setup'
    $installerDir = Join-Path $RepoRoot 'installer'
    $iss = Get-ChildItem -Path $installerDir -Filter '*.iss' -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notlike '*\windows-setup\*' } | Select-Object -First 1
    if (-not $iss) {
        Write-Warn2 'فایل اسکریپت Inno Setup (*.iss) در پوشه installer پیدا نشد؛ این مرحله رد شد.'
        return
    }
    $iscc = Find-ISCC
    if (-not $iscc) {
        Write-Warn2 'ISCC.exe پیدا نشد؛ ساخت فایل نصب رد شد.'
        $script:Failed.Add('Inno Setup compile')
        return
    }
    $start = (Get-Date).AddSeconds(-5)
    Write-Info "کامپایل $($iss.FullName) ..."
    $code = Invoke-Native -FilePath $iscc -Arguments @($iss.FullName) -WorkingDirectory $iss.DirectoryName
    if ($code -ne 0) {
        Write-Err 'کامپایل اسکریپت Inno Setup ناموفق بود.'
        $script:Failed.Add('Inno Setup compile')
        return
    }
    $script:BuildSteps.Add('installer')
    $publishFull = if ($script:PublishDir) { [IO.Path]::GetFullPath($script:PublishDir) } else { '|' }
    $setup = Get-ChildItem -Path $RepoRoot -Filter '*.exe' -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object {
            $_.LastWriteTime -ge $start -and
            -not $_.FullName.StartsWith($publishFull, [StringComparison]::OrdinalIgnoreCase) -and
            $_.FullName -notmatch '\\(bin|obj)\\'
        } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($setup) {
        $script:SetupPath = $setup.FullName
        Write-Ok "فایل نصب ساخته شد: $($setup.FullName)"
    } else {
        Write-Warn2 'کامپایل موفق بود اما فایل Setup.exe جدید پیدا نشد؛ پوشه Output کنار فایل .iss را بررسی کنید.'
    }
}

# ---------------------------------------------------------------- summary

function Write-Summary {
    Write-Step 'خلاصه'
    if ($script:Installed.Count) { Write-Host '  نصب شد:' -ForegroundColor Green; $script:Installed | ForEach-Object { Write-Host "    - $_" -ForegroundColor Green } }
    if ($script:Skipped.Count) { Write-Host '  رد شد (از قبل موجود):' -ForegroundColor Gray; $script:Skipped | ForEach-Object { Write-Host "    - $_" -ForegroundColor Gray } }
    if ($script:BuildSteps.Count) { Write-Host ('  مراحل ساخت انجام‌شده: ' + ($script:BuildSteps -join '، ')) -ForegroundColor Green }
    if ($script:Failed.Count) { Write-Host '  ناموفق:' -ForegroundColor Red; $script:Failed | ForEach-Object { Write-Host "    - $_" -ForegroundColor Red } }
    if ($script:ExePath) { Write-Host "  فایل اجرایی برنامه (EXE): $($script:ExePath)" -ForegroundColor Cyan }
    if ($script:SetupPath) { Write-Host "  فایل نصب (Setup.exe): $($script:SetupPath)" -ForegroundColor Cyan }
    Write-Host "  فایل گزارش: $script:LogFile" -ForegroundColor Gray
    Write-Log ("SUMMARY installed=[{0}] skipped=[{1}] build=[{2}] failed=[{3}] exe={4} setup={5}" -f
        ($script:Installed -join ', '), ($script:Skipped -join ', '), ($script:BuildSteps -join ', '),
        ($script:Failed -join ', '), $script:ExePath, $script:SetupPath)
}

# ---------------------------------------------------------------- main

if (-not (Test-IsAdmin)) {
    Write-Host 'برای نصب ابزارها به دسترسی مدیر (Administrator) نیاز است. لطفاً در پنجره UAC روی «Yes» کلیک کنید...' -ForegroundColor Yellow
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Elevated')
    if ($RepoUrl) { $argList += @('-RepoUrl', "`"$RepoUrl`"") }
    if ($PSBoundParameters.ContainsKey('Branch')) { $argList += @('-Branch', "`"$Branch`"") }
    if (-not $Build) { $argList += '-NoBuild' }
    try {
        $p = Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Verb RunAs -Wait -PassThru
        Write-Host 'اجرای اسکریپت در پنجره مدیر به پایان رسید.' -ForegroundColor Cyan
        exit $p.ExitCode
    } catch {
        Write-Host "اجرا با دسترسی مدیر لغو شد یا ممکن نشد: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
}

$exitCode = 0
try {
    Write-Log '------------------------------------------------------------'
    Write-Log "Start: PS $($PSVersionTable.PSVersion) on $([Environment]::OSVersion.VersionString)"
    Write-Host ''
    Write-Host '  نصب‌کننده خودکار پیش‌نیازهای «LENJ Exchange»' -ForegroundColor Magenta
    Write-Host "  گزارش کامل در: $script:LogFile" -ForegroundColor Gray
    Update-SessionPath
    if (-not (Test-Winget)) {
        Write-Warn2 'winget پیدا نشد؛ ابزارها مستقیماً از سایت سازنده دانلود می‌شوند.'
    }

    Install-Git
    Install-DotnetSdk
    Install-InnoSetup

    if ($Build) {
        $repo = Resolve-Repo
        if ($repo) {
            Invoke-Build -RepoRoot $repo
        } else {
            $script:Failed.Add('Build (مخزن پیدا نشد)')
        }
    } else {
        Write-Info 'مرحله ساخت به درخواست کاربر رد شد (-NoBuild).'
    }

    Write-Summary

    $openDir = $null
    if ($script:SetupPath) { $openDir = Split-Path $script:SetupPath -Parent }
    elseif ($script:PublishDir -and (Test-Path $script:PublishDir)) { $openDir = $script:PublishDir }
    if ($openDir) {
        Write-Info "باز کردن پوشه خروجی: $openDir"
        Start-Process explorer.exe -ArgumentList "`"$openDir`""
    }

    if ($script:Failed.Count) {
        $exitCode = 2
        Write-Host ''
        Write-Host '  برخی مراحل ناموفق بودند. پس از رفع مشکل، دوباره اجرا کنید؛ اجرای دوباره بی‌خطر است.' -ForegroundColor Yellow
    } else {
        Write-Host ''
        Write-Host '  همه مراحل با موفقیت انجام شد.' -ForegroundColor Green
    }
} catch {
    $exitCode = 1
    Write-Err "خطای غیرمنتظره: $($_.Exception.Message)"
    Write-Log ($_ | Out-String)
    Write-Host "  جزئیات در فایل گزارش: $script:LogFile" -ForegroundColor Yellow
} finally {
    if ($Elevated) {
        Write-Host ''
        try { $null = Read-Host 'برای بستن این پنجره کلید Enter را بزنید' } catch { }
    }
}
exit $exitCode
