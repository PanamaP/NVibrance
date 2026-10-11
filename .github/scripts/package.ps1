# Publishes NVibrance and packs the installer and update packages with Velopack.
# Shared by ci.yml (every PR, to catch packaging problems before merge) and release.yml,
# so the build CI checks is the build users get.
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $OutputDir = 'releases',
    # release.yml passes these to download the previous release, so this one can ship a delta
    [string] $PreviousReleaseRepo,
    [string] $Token
)

$ErrorActionPreference = 'Stop'

# Velopack package id. Permanent: changing it breaks updates for installed copies.
# Deliberately not "NVibrance", which would install into %LOCALAPPDATA%\NVibrance,
# the folder that already holds NVibrance's logs.
$packId = 'PanamaP.NVibrance'

function Invoke-Native([scriptblock] $command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code ${LASTEXITCODE}: $command" }
}

Invoke-Native {
    dotnet publish NVibrance/NVibrance.csproj -c Release -r win-x64 --self-contained true `
        /p:PublishSingleFile=true /p:DebugType=None /p:DebugSymbols=false /p:Version=$Version -o publish
}

# vpk must be the same version as the Velopack package the app references, so take it
# from the project file: one place to change, which Dependabot keeps up to date.
[xml] $proj = Get-Content NVibrance/NVibrance.csproj
$vpkVersion = $proj.Project.ItemGroup.PackageReference |
    Where-Object Include -eq 'Velopack' | Select-Object -ExpandProperty Version
if (-not $vpkVersion) { throw 'No Velopack PackageReference found in NVibrance.csproj' }

# a per-version tool folder: no PATH setup, nothing global, and reruns reuse it
$toolDir = Join-Path ([IO.Path]::GetTempPath()) "nvibrance-vpk-$vpkVersion"
$vpk = Join-Path $toolDir 'vpk.exe'
if (-not (Test-Path $vpk)) {
    Invoke-Native { dotnet tool install vpk --version $vpkVersion --tool-path $toolDir }
}

if ($PreviousReleaseRepo) {
    # the first installer release has no previous one to download; that's fine
    & $vpk download github --repoUrl $PreviousReleaseRepo --token $Token -o $OutputDir
    if ($LASTEXITCODE -ne 0) { Write-Host 'No previous release downloaded; packing without a delta.' }
}

Invoke-Native {
    & $vpk pack --packId $packId --packVersion $Version --packDir publish `
        --mainExe NVibrance.exe --packTitle NVibrance --packAuthors PanamaP `
        --icon NVibrance/UI/Assets/logo.ico --shortcuts StartMenuRoot -o $OutputDir
}

# Velopack names downloads after the package id; give people friendlier names.
# Updates never look at these two files, only at the .nupkg and releases.win.json.
Copy-Item "$OutputDir/$packId-win-Setup.exe" NVibrance-Setup.exe
Copy-Item "$OutputDir/$packId-win-Portable.zip" NVibrance-Portable.zip
