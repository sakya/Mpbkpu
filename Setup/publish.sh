#!/usr/bin/env bash
# Builds the Release version of the GUI and the CLI for Linux into Setup/publish-linux/<runtime> and packs them into
# Setup/Output/Mpbkpu-<version>-<runtime>.tar.gz (the Linux counterpart of build.bat)
# Usage: Setup/publish.sh [runtime]   (default linux-x64; e.g. linux-arm64)
set -euo pipefail

SETUP="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(dirname "$SETUP")"
RID="${1:-linux-x64}"
OUT="$SETUP/publish-linux/$RID"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/SolutionInfo.proj")"
ARCHIVE="$SETUP/Output/Mpbkpu-$VERSION-$RID.tar.gz"

rm -rf "$OUT"

# A clean build: a stale intermediate assembly without the compiled XAML of Avalonia makes the GUI crash at startup.
# The clean of a runtime needs its restore (a build without runtime replaces the assets file)
echo "Cleaning..."
for project in MpbkpuGUI/MpbkpuGUI.csproj Mpbkpu/Mpbkpu.csproj; do
    dotnet restore "$ROOT/$project" -r "$RID"
    dotnet clean "$ROOT/$project" -c Release -r "$RID" -v q
done

echo "Publishing GUI..."
dotnet publish "$ROOT/MpbkpuGUI/MpbkpuGUI.csproj" -c Release -r "$RID" --self-contained true -o "$OUT"

echo "Publishing CLI..."
dotnet publish "$ROOT/Mpbkpu/Mpbkpu.csproj" -c Release -r "$RID" --self-contained true -o "$OUT"

echo "Packing..."
mkdir -p "$SETUP/Output"
TAR="${ARCHIVE%.gz}"
rm -f "$TAR" "$ARCHIVE"
# The executables get mode 755 explicitly: the bit is lost when publishing from Windows (Git Bash)
TAR_OPTIONS="--owner=0 --group=0"
tar -cf "$TAR" $TAR_OPTIONS --exclude=./mpbkpu --exclude=./mpbkpugui -C "$OUT" .
tar -rf "$TAR" $TAR_OPTIONS --mode=755 -C "$OUT" ./mpbkpu ./mpbkpugui
gzip -9 "$TAR"
chmod +x "$OUT/mpbkpu" "$OUT/mpbkpugui"

echo
echo "Build completed: $OUT"
echo "Archive: $ARCHIVE"
