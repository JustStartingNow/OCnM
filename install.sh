#!/bin/sh
# OCnM installer for Linux and macOS.
#
#   curl -fsSL https://raw.githubusercontent.com/JustStartingNow/OCnM/master/install.sh | sh
#   ... | sh -s -- --version 1.1.0       a specific version
#   ... | sh -s -- --uninstall           remove it again (settings are kept)
#
# Downloads the right build from GitHub Releases, checks its SHA-256, and installs it for the current user
# (no root needed): ~/.local/bin/ocnm, plus a launcher entry and icon on Linux.
#
# Environment: OCNM_PREFIX (default ~/.local), OCNM_VERSION (default latest), OCNM_REPO (default JustStartingNow/OCnM),
# OCNM_BASE_URL (download from this URL instead of GitHub; used for testing).
set -eu

REPO="${OCNM_REPO:-JustStartingNow/OCnM}"
VERSION="${OCNM_VERSION:-latest}"
PREFIX="${OCNM_PREFIX:-$HOME/.local}"
BASE_URL="${OCNM_BASE_URL:-}"
UNINSTALL=0

say() { printf '%s\n' "$*"; }
die() { printf 'error: %s\n' "$*" >&2; exit 1; }

usage() {
  cat <<'USAGE'
OCnM installer

Usage: install.sh [--version X.Y.Z] [--prefix DIR] [--uninstall] [--help]

  --version X.Y.Z   install that release instead of the latest
  --prefix DIR      install under DIR (default: ~/.local)
  --uninstall       remove OCnM (your settings in ~/.config/ocnm are kept)
USAGE
}

while [ $# -gt 0 ]; do
  case "$1" in
    --version) [ $# -ge 2 ] || die "--version needs a value"; VERSION="${2#v}"; shift 2 ;;
    --prefix) [ $# -ge 2 ] || die "--prefix needs a value"; PREFIX="$2"; shift 2 ;;
    --uninstall) UNINSTALL=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) die "unknown option: $1 (try --help)" ;;
  esac
done

BIN_DIR="$PREFIX/bin"
APP_DIR="$PREFIX/share/applications"
ICON_DIR="$PREFIX/share/icons/hicolor/scalable/apps"

OS="$(uname -s)"
case "$OS" in
  Linux) OS_ID=linux ;;
  Darwin) OS_ID=osx ;;
  *) die "unsupported OS: $OS. On Windows, download the .zip from https://github.com/$REPO/releases/latest" ;;
esac

if [ "$UNINSTALL" -eq 1 ]; then
  if [ -x "$BIN_DIR/ocnm" ]; then
    "$BIN_DIR/ocnm" --quit >/dev/null 2>&1 || true
  fi
  rm -f "$BIN_DIR/ocnm" "$APP_DIR/ocnm.desktop" "$ICON_DIR/ocnm.svg"
  say "OCnM removed. Your settings are still in ~/.config/ocnm (delete that folder to remove them too)."
  say "Also remove any keybind or autostart line you added for it."
  exit 0
fi

case "$(uname -m)" in
  x86_64|amd64) ARCH=x64 ;;
  aarch64|arm64) ARCH=arm64 ;;
  *) die "unsupported CPU: $(uname -m)" ;;
esac

RID="$OS_ID-$ARCH"
ASSET="ocnm-$RID.tar.gz"

if [ -n "$BASE_URL" ]; then
  URL_BASE="$BASE_URL"
elif [ "$VERSION" = "latest" ]; then
  URL_BASE="https://github.com/$REPO/releases/latest/download"
else
  URL_BASE="https://github.com/$REPO/releases/download/v$VERSION"
fi

if command -v curl >/dev/null 2>&1; then
  fetch() { curl -fsSL "$1" -o "$2"; }
elif command -v wget >/dev/null 2>&1; then
  fetch() { wget -q "$1" -O "$2"; }
else
  die "curl or wget is required"
fi

if command -v sha256sum >/dev/null 2>&1; then
  sha256() { sha256sum "$1" | cut -d' ' -f1; }
elif command -v shasum >/dev/null 2>&1; then
  sha256() { shasum -a 256 "$1" | cut -d' ' -f1; }
else
  die "sha256sum or shasum is required to verify the download"
fi

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

say "Downloading OCnM ($RID, $VERSION)..."
fetch "$URL_BASE/$ASSET" "$TMP/$ASSET" || die "could not download $URL_BASE/$ASSET (is there a release for $RID?)"
fetch "$URL_BASE/SHA256SUMS" "$TMP/SHA256SUMS" || die "could not download the checksum file"

EXPECTED="$(grep "  $ASSET\$" "$TMP/SHA256SUMS" | cut -d' ' -f1 || true)"
[ -n "$EXPECTED" ] || die "no checksum listed for $ASSET"
ACTUAL="$(sha256 "$TMP/$ASSET")"
[ "$EXPECTED" = "$ACTUAL" ] || die "checksum mismatch for $ASSET (expected $EXPECTED, got $ACTUAL). Nothing was installed."

tar -xzf "$TMP/$ASSET" -C "$TMP"
[ -f "$TMP/ocnm" ] || die "the archive does not contain the ocnm binary"

# Stop a running copy first, so the binary can be replaced and the new one starts clean.
if [ -x "$BIN_DIR/ocnm" ]; then
  "$BIN_DIR/ocnm" --quit >/dev/null 2>&1 || true
fi

mkdir -p "$BIN_DIR"
install -m 755 "$TMP/ocnm" "$BIN_DIR/ocnm"

if [ "$OS_ID" = osx ]; then
  # Downloads via curl carry no quarantine flag, but Apple Silicon still needs a signature.
  xattr -d com.apple.quarantine "$BIN_DIR/ocnm" >/dev/null 2>&1 || true
  codesign --force --sign - "$BIN_DIR/ocnm" >/dev/null 2>&1 || say "note: could not sign the binary; if macOS refuses to run it, run: codesign --force --sign - $BIN_DIR/ocnm"
else
  mkdir -p "$APP_DIR" "$ICON_DIR"
  # Point the launcher at the installed binary, so it works even when ~/.local/bin is not on the desktop's PATH.
  sed "s|^Exec=ocnm |Exec=$BIN_DIR/ocnm |" "$TMP/ocnm.desktop" > "$APP_DIR/ocnm.desktop"
  install -m 644 "$TMP/ocnm.svg" "$ICON_DIR/ocnm.svg"
  command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APP_DIR" >/dev/null 2>&1 || true
fi

INSTALLED="$("$BIN_DIR/ocnm" --version 2>/dev/null || true)"
say ""
say "Installed: ${INSTALLED:-OCnM} -> $BIN_DIR/ocnm"

case ":$PATH:" in
  *":$BIN_DIR:"*) ;;
  *)
    say ""
    say "$BIN_DIR is not on your PATH. Add it:"
    say "  bash/zsh: echo 'export PATH=\"$BIN_DIR:\$PATH\"' >> ~/.profile"
    say "  fish:     fish_add_path $BIN_DIR"
    ;;
esac

say ""
say "Start it with:  ocnm --toggle"
say "On Wayland (Niri, Sway, Hyprland, ...) bind that command to a key in your compositor; see the README."
