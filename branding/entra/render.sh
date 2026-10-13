#!/bin/zsh
# Renders the Entra branding images on macOS with headless Google Chrome
# (fonts load from Google Fonts), then converts the background to JPEG.
set -euo pipefail
cd "$(dirname "$0")"
CHROME="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
[[ -x "$CHROME" ]] || { echo "Google Chrome not found at $CHROME" >&2; exit 1; }
mkdir -p out
render() { # name width height background
  "$CHROME" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 \
    --default-background-color="$4" --window-size="$2,$3" --virtual-time-budget=6000 \
    --screenshot="$PWD/out/$1.png" "file://$PWD/render/$1.html" >/dev/null 2>&1
}
render banner 245 36 00000000
render square 240 240 00000000
render favicon 32 32 00000000
render background 1920 1080 0B1426FF
sips -s format jpeg -s formatOptions 82 out/background.png --out out/background.jpg >/dev/null
rm out/background.png
ls -l out
