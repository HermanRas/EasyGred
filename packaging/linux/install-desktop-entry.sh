#!/bin/sh
# OPTIONAL: adds EasyGred to your application menu (and "Open With") for the current user.
# EasyGred is portable and runs fine without this. Writes only one file:
#   ~/.local/share/applications/easygred.desktop
# Remove it again with:  rm ~/.local/share/applications/easygred.desktop
HERE="$(cd "$(dirname "$0")" && pwd)"
DEST="$HOME/.local/share/applications"
mkdir -p "$DEST"
cat > "$DEST/easygred.desktop" <<EOD
[Desktop Entry]
Type=Application
Name=EasyGred
GenericName=Image Editor
Comment=Easy Graphix Edit - simple portable picture editor
Exec="$HERE/EasyGred" %f
Icon=$HERE/EasyGred.png
Terminal=false
Categories=Graphics;2DGraphics;RasterGraphics;
MimeType=image/png;image/bmp;image/jpeg;image/webp;
EOD
echo "Added $DEST/easygred.desktop"
