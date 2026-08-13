#!/bin/zsh
set -eu

TOOL_DIR="$(cd "$(dirname "$0")" && pwd)"
OUTPUT_ZIP="$(dirname "$TOOL_DIR")/SmashBuilder-Teammate.zip"
STAGING_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/smash-builder-package.XXXXXX")"
STAGING_TOOL="$STAGING_ROOT/Smash Builder"

cleanup() {
  rm -rf "$STAGING_ROOT"
}
trap cleanup EXIT INT TERM

mkdir -p "$STAGING_TOOL/assets"
cp "$TOOL_DIR/index.html" "$STAGING_TOOL/"
cp "$TOOL_DIR/styles.css" "$STAGING_TOOL/"
cp "$TOOL_DIR/app.js" "$STAGING_TOOL/"
cp "$TOOL_DIR/serve.py" "$STAGING_TOOL/"
cp "$TOOL_DIR/Start Smash Builder.command" "$STAGING_TOOL/"
cp "$TOOL_DIR/START HERE.txt" "$STAGING_TOOL/"
cp "$TOOL_DIR/README.md" "$STAGING_TOOL/"
cp "$TOOL_DIR/THIRD_PARTY_NOTICES.md" "$STAGING_TOOL/"
cp "$TOOL_DIR/assets/Brick.fbx" "$STAGING_TOOL/assets/"
cp "$TOOL_DIR/assets/concrete.fbx" "$STAGING_TOOL/assets/"
cp "$TOOL_DIR/assets/Glass.fbx" "$STAGING_TOOL/assets/"
if [[ -d "$TOOL_DIR/assets/Brick.fbm" ]]; then
  cp -R "$TOOL_DIR/assets/Brick.fbm" "$STAGING_TOOL/assets/"
fi

chmod 755 "$STAGING_TOOL/Start Smash Builder.command" "$STAGING_TOOL/serve.py"
ditto -c -k --keepParent --norsrc --noextattr --noqtn --noacl "$STAGING_TOOL" "$OUTPUT_ZIP"

echo "Đã tạo bản ZIP sạch:"
echo "$OUTPUT_ZIP"
echo "Gửi đúng file này cho teammate."
if [[ -t 0 ]]; then
  read -r "?Nhấn Enter để đóng cửa sổ..."
fi
