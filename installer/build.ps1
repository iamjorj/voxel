# Builds the Voxel installer: publishes the app (self-contained, so users need no .NET) into
# artifacts/publish, then compiles installer/Voxel.iss into artifacts/VoxelSetup-<version>.exe.
# Needs the .NET 10 SDK and Inno Setup 6 (winget install --id JRSoftware.InnoSetup -e).
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
[xml]$proj = Get-Content (Join-Path $root "Voxel\Voxel.csproj")
$env:VOXEL_VERSION = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

$publish = Join-Path $root "artifacts\publish"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root "Voxel") -c Release -r win-x64 --self-contained -o $publish -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
Get-ChildItem $publish -Filter *.pdb | Remove-Item

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
          "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it: winget install --id JRSoftware.InnoSetup -e" }
& $iscc /Q (Join-Path $PSScriptRoot "Voxel.iss")
if ($LASTEXITCODE -ne 0) { throw "installer build failed" }
Get-Item (Join-Path $root "artifacts\VoxelSetup-$env:VOXEL_VERSION.exe")
