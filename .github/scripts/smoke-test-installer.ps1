# Installs NVibrance-Setup.exe silently, checks the install, uninstalls, and checks the
# cleanup. Catches what building alone can't: a crashing install hook, an app that dies on
# startup, a wrong exe name or shortcut, or an uninstall that leaves autostart behind.
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Setup = 'NVibrance-Setup.exe'
)

$ErrorActionPreference = 'Stop'
$Setup = (Resolve-Path $Setup).Path

$installDir = Join-Path $env:LOCALAPPDATA 'PanamaP.NVibrance'
$exe = Join-Path $installDir 'current\NVibrance.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\NVibrance.lnk'

function Assert([bool] $condition, [string] $message) {
    if (-not $condition) { throw "Installer smoke test failed: $message" }
    Write-Host "ok - $message"
}

# Start-Process -Wait would also wait for the app setup launches; wait on the process alone,
# with a timeout so a hang fails the job instead of stalling it
function Invoke-AndWait([string] $file, [string[]] $arguments, [int] $timeoutSeconds = 120) {
    $process = Start-Process $file -ArgumentList $arguments -PassThru
    if (-not $process.WaitForExit($timeoutSeconds * 1000)) {
        $process.Kill()
        throw "Installer smoke test failed: $file did not finish within $timeoutSeconds seconds"
    }
    $process.ExitCode
}

function Get-Autostart { (Get-ItemProperty $runKey -Name NVibrance -ErrorAction SilentlyContinue).NVibrance }

function Get-UninstallEntry {
    Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' |
        ForEach-Object { Get-ItemProperty $_.PSPath } |
        Where-Object DisplayName -eq 'NVibrance'
}

# -------- install --------
# no "-- --minimized" for the app: Velopack 1.2.158's setup crashes on pass-through arguments
$installExit = Invoke-AndWait $Setup @('--silent')
Assert ($installExit -eq 0) "setup exits with 0 (got $installExit)"
Assert (Test-Path $exe) 'app is installed under %LOCALAPPDATA%\PanamaP.NVibrance'
Assert ((Get-Item $exe).VersionInfo.ProductVersion -like "$Version*") "installed version is $Version"
Assert ((Get-Autostart) -like "*$exe*") 'Start with Windows is on and points at the installed app'
Assert (Test-Path $shortcut) 'Start menu shortcut exists'
Assert ($null -ne (Get-UninstallEntry)) 'Settings > Apps entry exists'

# a silent install doesn't start the app, so start it the way Windows does at login; it
# must still be running a few seconds later, i.e. not crash on startup (CI machines have no
# NVIDIA GPU, which the app is meant to survive)
Start-Process $exe -ArgumentList '--minimized'
Start-Sleep -Seconds 8
$running = Get-Process NVibrance -ErrorAction SilentlyContinue | Where-Object Path -eq $exe
Assert ($null -ne $running) 'app starts and keeps running'

# -------- uninstall --------
$uninstallExit = Invoke-AndWait (Join-Path $installDir 'Update.exe') @('--uninstall', '--silent')
Assert ($uninstallExit -eq 0) "uninstall exits with 0 (got $uninstallExit)"
Start-Sleep -Seconds 5  # the updater finishes removing its own folder after it exits
Assert ($null -eq (Get-Autostart)) 'autostart entry is removed'
Assert (-not (Test-Path $shortcut)) 'Start menu shortcut is removed'
Assert ($null -eq (Get-UninstallEntry)) 'Settings > Apps entry is removed'
Assert ($null -eq (Get-Process NVibrance -ErrorAction SilentlyContinue | Where-Object Path -eq $exe)) 'app is no longer running'
