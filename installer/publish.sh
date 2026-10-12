#!/usr/bin/env bash
# Cross-publishes the Windows EXE from Linux/macOS (CI). The installer itself must be compiled on Windows
# with Inno Setup (installer/publish.ps1), and the app only runs on Windows.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
out="$root/artifacts/publish"

if [[ "${SKIP_TESTS:-0}" != "1" ]]; then
  dotnet test "$root/LenjExchange.sln" -c Release
fi
rm -rf "$out"
dotnet publish "$root/src/LenjExchange.Desktop/LenjExchange.Desktop.csproj" -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$out"
echo "EXE: $out/LenjExchange.exe"
