# Builds the single-file exe, its zip and the MSI for one platform into out/.
# Usage: ./installer/build.ps1 -Version 0.1.0 -Rid win-x64
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [ValidateSet('win-x64', 'win-arm64')] [string] $Rid
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publishDir = Join-Path $root "out/$Rid/"
$platform = $Rid.Substring(4)

dotnet publish (Join-Path $root 'src/Blindspot') -c Release -r $Rid --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None `
    -p:Version=$Version -o $publishDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Compress-Archive -Path "$publishDir*" -DestinationPath (Join-Path $root "out/Blindspot-$Rid.zip") -Force

dotnet build (Join-Path $root 'installer') -c Release -p:Version=$Version -p:PublishDir=$publishDir `
    -p:InstallerPlatform=$platform -o (Join-Path $root "out/msi-$Rid")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Move-Item (Join-Path $root "out/msi-$Rid/Blindspot.msi") (Join-Path $root "out/Blindspot-$Rid.msi") -Force
