#!/bin/sh
# Installs EasyGred from this (unzipped) build folder and adds it to the application menu.
#
#   ./install.sh                     current user only - no sudo needed
#                                      app:   ~/.local/opt/easygred
#                                      cmd:   ~/.local/bin/easygred
#                                      menu:  ~/.local/share/applications/easygred.desktop
#   sudo ./install.sh --system       all users
#                                      app:   /opt/easygred
#                                      cmd:   /usr/local/bin/easygred
#                                      menu:  /usr/share/applications/easygred.desktop
#   ./install.sh --uninstall         remove (add --system for a system-wide install)
#
# EasyGred also runs straight from this folder without installing.
set -eu

HERE="$(cd "$(dirname "$0")" && pwd)"
MODE=user
ACTION=install
for arg in "$@"; do
  case "$arg" in
    --system) MODE=system ;;
    --uninstall) ACTION=uninstall ;;
    -h|--help) sed -n '2,14p' "$0"; exit 0 ;;
    *) echo "Unknown option: $arg" >&2; exit 1 ;;
  esac
done

if [ "$MODE" = system ]; then
  APP=/opt/easygred
  BIN=/usr/local/bin
  APPS=/usr/share/applications
  ICONS=/usr/share/icons/hicolor
else
  APP="$HOME/.local/opt/easygred"
  BIN="$HOME/.local/bin"
  APPS="$HOME/.local/share/applications"
  ICONS="$HOME/.local/share/icons/hicolor"
fi

refresh() {
  command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q "$APPS" 2>/dev/null || true
  command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -f -t "$ICONS" 2>/dev/null || true
}

if [ "$ACTION" = uninstall ]; then
  rm -rf "$APP"
  rm -f "$BIN/easygred" "$APPS/easygred.desktop"
  rm -f "$ICONS"/*/apps/easygred.png
  refresh
  echo "EasyGred removed ($MODE install)."
  exit 0
fi

# Copy the build (skip when running the installer from the installed copy).
if [ "$HERE" != "$APP" ]; then
  rm -rf "$APP"
  mkdir -p "$APP"
  cp -a "$HERE"/. "$APP"/
fi
chmod +x "$APP/EasyGred"

mkdir -p "$BIN"
ln -sf "$APP/EasyGred" "$BIN/easygred"

for f in "$APP"/icons/EasyGred-*x*.png; do
  [ -e "$f" ] || continue
  size=$(basename "$f" .png | sed 's/^EasyGred-//')
  mkdir -p "$ICONS/$size/apps"
  cp "$f" "$ICONS/$size/apps/easygred.png"
done

mkdir -p "$APPS"
cat > "$APPS/easygred.desktop" <<EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=EasyGred
GenericName=Image Editor
Comment=Easy Graphix Edit - simple picture editor (BMP, PNG, JPG, WebP)
Exec="$APP/EasyGred" %f
TryExec=$APP/EasyGred
Icon=easygred
Terminal=false
StartupNotify=true
StartupWMClass=EasyGred
Categories=Graphics;2DGraphics;RasterGraphics;
Keywords=paint;draw;image;picture;editor;pixel;
MimeType=image/png;image/bmp;image/jpeg;image/webp;image/gif;
EOF
chmod 644 "$APPS/easygred.desktop"
refresh

echo "EasyGred installed ($MODE):"
echo "  app   $APP"
echo "  cmd   $BIN/easygred"
echo "  menu  $APPS/easygred.desktop"
case ":$PATH:" in
  *":$BIN:"*) ;;
  *) echo "  note: $BIN is not on your PATH yet - log out and back in (or use the menu entry)." ;;
esac
