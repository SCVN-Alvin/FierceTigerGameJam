#!/bin/zsh
set -u

cd "$(dirname "$0")" || exit 1
APP_URL="http://127.0.0.1:4173"

if curl --silent --fail --max-time 1 "$APP_URL" >/dev/null 2>&1; then
  open "$APP_URL"
  exit 0
fi

if ! command -v python3 >/dev/null 2>&1; then
  echo "Không tìm thấy Python 3. Cài Python 3 rồi chạy lại launcher này."
  read -r "?Nhấn Enter để đóng cửa sổ..."
  exit 1
fi

python3 serve.py &
SERVER_PID=$!
trap 'kill "$SERVER_PID" 2>/dev/null' INT TERM EXIT

for attempt in {1..30}; do
  if curl --silent --fail --max-time 1 "$APP_URL" >/dev/null 2>&1; then
    open "$APP_URL"
    echo "Smash Builder đang chạy tại $APP_URL"
    echo "Giữ cửa sổ này mở. Nhấn Ctrl+C khi muốn tắt tool."
    wait "$SERVER_PID"
    exit $?
  fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    break
  fi
  sleep 0.2
done

echo "Không khởi động được Smash Builder tại $APP_URL"
echo "Nếu port 4173 đang bị ứng dụng khác dùng, hãy đóng ứng dụng đó rồi chạy lại."
read -r "?Nhấn Enter để đóng cửa sổ..."
exit 1
