#!/bin/sh
# Builds one release archive: scripts/package.sh <rid> [output-dir]
#   rid: linux-x64 linux-arm64 osx-x64 osx-arm64 win-x64 win-arm64
# Output (fixed names, so ".../releases/latest/download/<name>" always works):
#   linux:   ocnm-<rid>.tar.gz  (ocnm, ocnm.desktop, ocnm.svg, LICENSE)
#   macOS:   ocnm-<rid>.tar.gz  (ocnm, LICENSE)
#   windows: ocnm-<rid>.zip     (ocnm.exe, LICENSE)
set -eu

RID="${1:?usage: scripts/package.sh <rid> [output-dir]}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${2:-$ROOT/dist}"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

cd "$ROOT"
dotnet publish src/OCnM.App -c Release -r "$RID" --self-contained -p:PublishSingleFile=true -o "$WORK/pub" >/dev/null
mkdir -p "$WORK/pkg"
cp LICENSE "$WORK/pkg/"

case "$RID" in
  win-*)
    cp "$WORK/pub/ocnm.exe" "$WORK/pkg/"
    ARCHIVE="$OUT/ocnm-$RID.zip"
    rm -f "$ARCHIVE"
    if command -v zip >/dev/null 2>&1; then
      (cd "$WORK/pkg" && zip -q "$ARCHIVE" ocnm.exe LICENSE)
    else
      python3 - "$WORK/pkg" "$ARCHIVE" <<'PY'
import sys, zipfile
src, dst = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as z:
    for name in ("ocnm.exe", "LICENSE"):
        z.write(f"{src}/{name}", name)
PY
    fi
    ;;
  *)
    cp "$WORK/pub/ocnm" "$WORK/pkg/"
    chmod 755 "$WORK/pkg/ocnm"
    case "$RID" in
      linux-*) cp packaging/linux/ocnm.desktop packaging/ocnm.svg "$WORK/pkg/" ;;
      osx-*)
        # Apple Silicon refuses unsigned code; an ad-hoc signature is enough for a locally run binary.
        if [ "$(uname -s)" = "Darwin" ]; then
          codesign --force --sign - "$WORK/pkg/ocnm"
        fi
        ;;
    esac
    ARCHIVE="$OUT/ocnm-$RID.tar.gz"
    tar -C "$WORK/pkg" -czf "$ARCHIVE" $(cd "$WORK/pkg" && ls)
    ;;
esac

echo "$ARCHIVE"
