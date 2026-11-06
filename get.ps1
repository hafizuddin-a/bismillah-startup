# Installs the latest Bismillah release. No admin rights, no installer program.
#
#   irm https://raw.githubusercontent.com/hafizuddin-a/bismillah-startup/main/get.ps1 | iex
#
# It downloads the release zip from GitHub, checks it against the checksum in the release notes, puts Bismillah.exe in
# %LOCALAPPDATA%\Bismillah (your own folder, so updates never need permission), adds a Start menu shortcut and opens it.
# Files downloaded this way aren't marked as "from the internet", so Windows SmartScreen doesn't warn.
# Run it again any time to reinstall the latest version.
# $env:BISMILLAH_DIR installs somewhere else; $env:BISMILLAH_NO_OPEN=1 doesn't open the app afterwards.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'      # Invoke-WebRequest is much faster without its progress bar
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repo = 'hafizuddin-a/bismillah-startup'
function Fail($message) { Write-Host "Bismillah install failed: $message" -ForegroundColor Red; throw $message }

if ([Environment]::OSVersion.Version.Build -lt 17763) { Fail 'Bismillah needs Windows 10 version 1809 or later.' }
if (-not [Environment]::Is64BitOperatingSystem) { Fail 'Bismillah needs 64-bit Windows.' }

$dir = if ($env:BISMILLAH_DIR) { $env:BISMILLAH_DIR } else { Join-Path $env:LOCALAPPDATA 'Bismillah' }

Write-Host 'Looking up the latest release...'
try { $release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers @{ 'User-Agent' = 'Bismillah-installer' } }
catch { Fail "couldn't reach GitHub. $($_.Exception.Message)" }
$tag = $release.tag_name
$asset = $release.assets | Where-Object { $_.name -like 'Bismillah-*-win-x64.zip' } | Select-Object -First 1
if (-not $asset) { Fail "couldn't find the download in the latest release." }

$work = Join-Path ([IO.Path]::GetTempPath()) ("bismillah-install-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null
try {
    Write-Host "Downloading Bismillah $tag..."
    $zip = Join-Path $work 'Bismillah.zip'
    try { Invoke-WebRequest $asset.browser_download_url -OutFile $zip -Headers @{ 'User-Agent' = 'Bismillah-installer' } }
    catch { Fail "the download didn't finish. $($_.Exception.Message)" }

    # the release notes list the zip's SHA-256; check it when it's there
    if ($release.body -match 'SHA-256 of [^`]*`([0-9a-fA-F]{64})`') {
        $got = (Get-FileHash $zip -Algorithm SHA256).Hash
        if ($got -ne $Matches[1]) { Fail "the download doesn't match its published checksum. Nothing was installed." }
    }

    Expand-Archive $zip -DestinationPath (Join-Path $work 'unpacked')
    $exe = Join-Path $work 'unpacked\Bismillah.exe'
    if (-not (Test-Path $exe)) { Fail "Bismillah.exe isn't in the download." }

    Get-Process -Name Bismillah -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
    New-Item -ItemType Directory $dir -Force | Out-Null
    $target = Join-Path $dir 'Bismillah.exe'
    Copy-Item $exe $target -Force
    Unblock-File $target -ErrorAction SilentlyContinue

    # a Start menu shortcut, in the user's own Start menu
    $programs = [Environment]::GetFolderPath('Programs')
    if ($programs) {
        $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $programs 'Bismillah.lnk'))
        $shortcut.TargetPath = $target
        $shortcut.WorkingDirectory = $dir
        $shortcut.Save()
    }
}
finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host "Installed Bismillah $tag to $dir." -ForegroundColor Green
if (-not $env:BISMILLAH_NO_OPEN) {
    Start-Process (Join-Path $dir 'Bismillah.exe')
    Write-Host 'Its icon appears in the tray (click the ^ arrow if you do not see it). Choose Launch at Login from its menu to start it with your PC.'
}
