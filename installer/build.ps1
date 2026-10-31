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

# Separate intermediate files per platform and no incremental build: otherwise
# the second platform's MSI reuses the first one's, wrong exe included.
dotnet build (Join-Path $root 'installer') -c Release --no-incremental -p:Version=$Version -p:PublishDir=$publishDir `
    -p:InstallerPlatform=$platform -p:IntermediateOutputPath="obj/$Rid/" -o (Join-Path $root "out/msi-$Rid")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$msi = Join-Path $root "out/Blindspot-$Rid.msi"
Move-Item (Join-Path $root "out/msi-$Rid/Blindspot.msi") $msi -Force

# Check the MSI carries this platform's exe: unpack it and compare with the published one.
$unpacked = Join-Path $root "out/check-$Rid"
Remove-Item $unpacked -Recurse -Force -ErrorAction SilentlyContinue
$p = Start-Process msiexec.exe -ArgumentList '/a', "`"$msi`"", '/qn', "TARGETDIR=`"$unpacked`"" -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "Failed to unpack $msi (msiexec exit code $($p.ExitCode))" }
$inMsi = Get-ChildItem $unpacked -Recurse -Filter Blindspot.exe | Select-Object -First 1
if (-not $inMsi) { throw "Blindspot.exe not found in $msi" }
if ((Get-FileHash $inMsi.FullName).Hash -ne (Get-FileHash (Join-Path $publishDir 'Blindspot.exe')).Hash) {
    throw "$msi does not contain the $Rid build of Blindspot.exe"
}
Write-Host "Checked: $msi contains the $Rid Blindspot.exe"
