#!/bin/bash
# Makes the README's two animations (assets/demo-wake.gif and assets/demo-dhikr.gif) from the frames that CI draws
# with the app's real renderer on a Windows machine (the "gif-frames" artifact of the latest successful CI run).
set -euo pipefail
cd "$(dirname "$0")/.."
TMP="$(mktemp -d)"
RUN=$(gh run list --workflow ci.yml --status success --limit 1 --json databaseId -q '.[0].databaseId')
gh run download "$RUN" -n gif-frames -D "$TMP/frames"
swiftc -O -framework ImageIO -framework CoreServices tools/png_to_gif.swift -o "$TMP/png_to_gif"
"$TMP/png_to_gif" "$TMP/frames" wake assets/demo-wake.gif
"$TMP/png_to_gif" "$TMP/frames" dhikr assets/demo-dhikr.gif
rm -rf "$TMP"
ls -la assets/demo-*.gif
