#!/bin/sh
# Builds artifacts/Voxel-<version>-mac.dmg, the Mac download. Runs on a Mac (needs codesign, hdiutil):
#   1. publish Voxel.Mac self-contained for Apple Silicon (the AI engine has no Intel Mac build)
#   2. lay it out as Voxel.app: program in Contents/MacOS, models and VB-CABLE in Contents/Resources
#   3. sign it ad hoc (Apple Silicon refuses unsigned apps; a Developer ID would also remove
#      the "unverified developer" step for users)
#   4. put it in a disk image next to a shortcut to Applications, for drag-to-install
# Needs the .NET 10 SDK. Usage: sh installer/build-mac.sh
set -e
root=$(cd "$(dirname "$0")/.." && pwd)
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Voxel.Mac/Voxel.Mac.csproj" | head -n 1)
work="$root/artifacts/mac"
rm -rf "${work:?}"
mkdir -p "$work"

dotnet publish "$root/Voxel.Mac" -c Release -r osx-arm64 --self-contained -o "$work/publish" -nologo -v q
rm -f "$work"/publish/*.pdb

app="$work/disk/Voxel.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
mv "$work/publish/Assets" "$app/Contents/Resources/Assets"
cp -R "$work/publish/." "$app/Contents/MacOS/"
cp "$root/Voxel.Mac/Assets/voxel.icns" "$app/Contents/Resources/"
sed "s/@VERSION@/$version/g" "$root/installer/Info.plist" > "$app/Contents/Info.plist"
chmod +x "$app/Contents/MacOS/Voxel"

codesign --force --deep --sign - "$app"
codesign --verify --deep --strict --verbose=2 "$app"

ln -s /Applications "$work/disk/Applications"
dmg="$root/artifacts/Voxel-$version-mac.dmg"
hdiutil create -volname "Voxel" -srcfolder "$work/disk" -ov -format UDZO "$dmg"
ls -la "$dmg"
shasum -a 256 "$dmg"
