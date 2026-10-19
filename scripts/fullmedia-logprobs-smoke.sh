#!/usr/bin/env bash
# Đã chuyển sang smoke bỏ phiếu (vietai không trả logprobs — 2026-10-19).
# Dùng: scripts/fullmedia-vote-smoke.sh
# CHỈ chạy khi người dùng xác nhận trực tiếp.
set -euo pipefail
echo "fullmedia-logprobs-smoke.sh đã được thay bằng scripts/fullmedia-vote-smoke.sh" >&2
echo "Chạy: $(cd "$(dirname "$0")" && pwd)/fullmedia-vote-smoke.sh" >&2
echo "CHỈ chạy sau khi người dùng xác nhận trực tiếp. Không tự chạy từ agent." >&2
exit 3
