#!/bin/sh
# Installs VB-CABLE from VB-Audio's own zip, unchanged, then restarts Core Audio so the device
# appears without a reboot. Runs as root: Voxel starts it through the macOS password prompt.
# Usage: install-vbcable.sh <VBCable_MACDriver_Pack108.zip>
set -e
work=$(mktemp -d /tmp/voxel-vbcable.XXXXXX)
trap 'hdiutil detach "$work/mount" >/dev/null 2>&1 || true; rm -rf "$work"' EXIT

ditto -x -k "$1" "$work"
# The zip also holds Pack107, which is for Intel Macs only.
dmg=$(ls "$work"/*Pack108*.dmg)
hdiutil attach "$dmg" -nobrowse -readonly -mountpoint "$work/mount" >/dev/null
installer -pkg "$(ls "$work"/mount/*.pkg | head -n 1)" -target /
killall coreaudiod || true
