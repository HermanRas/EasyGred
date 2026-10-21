#!/bin/sh
# Portable launcher: runs EasyGred from wherever this folder lives.
HERE="$(cd "$(dirname "$0")" && pwd)"
exec "$HERE/EasyGred" "$@"
