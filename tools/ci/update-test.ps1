# End-to-end test of the updater on a real Windows machine (run by CI after the app is published to dist/publish):
#   1. a copy of the app (1.0.0) is "installed" in a scratch folder
#   2. a local web server pretends to be GitHub, serving a signed 1.0.1 release
#   3. the installed copy is started with its feed pointed at the local server; it must update itself in place
#   4. the same again with a bad signature: it must refuse and stay at 1.0.0
$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$work = Join-Path $root 'update-test'
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
$feed = New-Item -ItemType Directory "$work/feed"
$port = 8765

function Fail($message) { Write-Host "FAILED: $message"; Get-Content "$env:APPDATA/Bismillah/bismillah.log" -ErrorAction SilentlyContinue; exit 1 }
function Version($path) { (Get-Item $path).VersionInfo.FileVersion }

# the new release
dotnet publish src/Bismillah.App -c Release -o "$work/v101" -p:Version=1.0.1 | Out-Null
if ((Version "$work/v101/Bismillah.exe") -notlike '1.0.1*') { Fail "the 1.0.1 build has version $(Version "$work/v101/Bismillah.exe")" }
Compress-Archive -Path "$work/v101/Bismillah.exe" -DestinationPath "$feed/Bismillah-1.0.1-win-x64.zip"

# the keys: the real one signs, a different one forges
$pub = (dotnet run --project tools/SignTool -- keygen "$work/key.txt").Trim()
$forgedPub = (dotnet run --project tools/SignTool -- keygen "$work/forger.txt").Trim()
$good = (dotnet run --project tools/SignTool -- sign "$work/key.txt" "$feed/Bismillah-1.0.1-win-x64.zip").Trim()
$forged = (dotnet run --project tools/SignTool -- sign "$work/forger.txt" "$feed/Bismillah-1.0.1-win-x64.zip").Trim()

function WriteFeed($signature) {
    Set-Content "$feed/Bismillah-1.0.1-win-x64.zip.sig" $signature -NoNewline
    $json = @{
        tag_name = 'v1.0.1'; html_url = "http://localhost:$port/"; draft = $false; prerelease = $false
        assets = @(
            @{ name = 'Bismillah-1.0.1-win-x64.zip'; browser_download_url = "http://localhost:$port/Bismillah-1.0.1-win-x64.zip" },
            @{ name = 'Bismillah-1.0.1-win-x64.zip.sig'; browser_download_url = "http://localhost:$port/Bismillah-1.0.1-win-x64.zip.sig" })
    } | ConvertTo-Json -Depth 4
    Set-Content "$feed/latest.json" $json
}

$server = Start-Process python -ArgumentList '-m', 'http.server', $port, '--directory', "$feed" -PassThru -WindowStyle Hidden
Start-Sleep 2
try {
    function RunInstalled($name) {
        $dir = New-Item -ItemType Directory "$work/$name"
        Copy-Item dist/publish/Bismillah.exe $dir
        if ((Version "$dir/Bismillah.exe") -notlike '1.0.0*') { Fail "the installed build has version $(Version "$dir/Bismillah.exe")" }
        $env:BISMILLAH_FEED_URL = "http://localhost:$port/latest.json"
        $env:BISMILLAH_UPDATE_KEY = $pub
        $env:BISMILLAH_TEST_UPDATE_NOW = '1'
        Start-Process "$dir/Bismillah.exe" | Out-Null
        return "$dir/Bismillah.exe"
    }

    # --- a forged release is refused
    WriteFeed $forged
    $exe = RunInstalled 'forged'
    Start-Sleep 25
    if ((Version $exe) -notlike '1.0.0*') { Fail "a release with a forged signature was installed" }
    Write-Host "forged release refused, still $(Version $exe)"
    Get-Content "$env:APPDATA/Bismillah/bismillah.log" -ErrorAction SilentlyContinue
    Get-Process Bismillah -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep 2

    # --- the genuine release installs itself
    WriteFeed $good
    $exe = RunInstalled 'installed'
    $updated = $false
    foreach ($i in 1..60) {
        Start-Sleep 1
        if ((Version $exe) -like '1.0.1*') { $updated = $true; break }
    }
    if (-not $updated) { Fail "the installed copy never updated (still $(Version $exe))" }
    Write-Host "updated in place: now $(Version $exe) after $i s"

    # it must be running again, from the installed place, and have cleaned up after itself
    Start-Sleep 8
    $running = Get-Process Bismillah -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Resolve-Path $exe).Path }
    if (-not $running) { Fail "the updated app isn't running" }
    if (Test-Path "$exe.old") { Fail "the old copy was left behind" }
    $leftover = Get-ChildItem $env:TEMP -Directory -Filter 'bismillah-update-*' -ErrorAction SilentlyContinue
    if ($leftover) { Fail "the update's scratch folder was left behind: $($leftover.Name)" }
    Write-Host "the updated app is running, nothing left behind"
}
finally {
    Get-Process Bismillah -ErrorAction SilentlyContinue | Stop-Process -Force
    Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
}
Write-Host "update test passed"
