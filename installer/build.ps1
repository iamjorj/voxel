# Builds artifacts/VoxelSetup-<version>.exe, Voxel's own installer:
#   1. publish the app self-contained (users need no .NET) into artifacts/publish
#   2. zip it
#   3. publish Voxel.Setup as one self-contained exe
#   4. append the zip + trailer to that exe (Voxel.Setup/Payload.cs reads it back)
# Needs the .NET 10 SDK. -SetupOnly reuses the last app publish + zip (steps 1-2) when only the setup changed.
param([switch]$SetupOnly)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root "artifacts"
[xml]$proj = Get-Content (Join-Path $root "Voxel\Voxel.csproj")
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

$publish = Join-Path $artifacts "publish"
$zip = Join-Path $artifacts "payload.zip"
if (-not $SetupOnly -or -not (Test-Path $zip)) {
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    dotnet publish (Join-Path $root "Voxel") -c Release -r win-x64 --self-contained -o $publish -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "app publish failed" }
    Get-ChildItem $publish -Filter *.pdb | Remove-Item

    if (Test-Path $zip) { Remove-Item $zip }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
}

$setupOut = Join-Path $artifacts "setup"
if (Test-Path $setupOut) { Remove-Item $setupOut -Recurse -Force }
dotnet publish (Join-Path $root "Voxel.Setup") -c Release -r win-x64 --self-contained -o $setupOut -nologo -v q `
    -p:Version=$version -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE -ne 0) { throw "setup publish failed" }

$final = Join-Path $artifacts "VoxelSetup-$version.exe"
Copy-Item (Join-Path $setupOut "VoxelSetup.exe") $final -Force
$out = [IO.File]::Open($final, [IO.FileMode]::Append)
try {
    $in = [IO.File]::OpenRead($zip)
    try { $in.CopyTo($out) } finally { $in.Dispose() }
    $out.Write([BitConverter]::GetBytes([long](Get-Item $zip).Length), 0, 8)
    $out.Write([Text.Encoding]::ASCII.GetBytes("VOXELPAY"), 0, 8)
} finally { $out.Dispose() }
Get-Item $final
