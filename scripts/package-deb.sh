#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${VERSION:-0.1.0}"
PKG="$ROOT/artifacts/deb/vortex-ai-assistant_${VERSION}_amd64"
rm -rf "$PKG"
mkdir -p "$PKG/opt/vortex-ai-assistant" "$PKG/usr/share/applications" "$PKG/DEBIAN"
cp -R "$ROOT/artifacts/linux-x64/vortex-desktop/." "$PKG/opt/vortex-ai-assistant/"
cp "$ROOT/packaging/linux/vortex-ai-assistant.desktop" "$PKG/usr/share/applications/vortex-ai-assistant.desktop"
cat > "$PKG/DEBIAN/control" <<CONTROL
Package: vortex-ai-assistant
Version: $VERSION
Section: utils
Priority: optional
Architecture: amd64
Maintainer: Teknomer <support@example.local>
Description: Vortex AI Assistant desktop client for Pardus/Linux
CONTROL
dpkg-deb --build "$PKG"
