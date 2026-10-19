#!/usr/bin/env bash
# FULLMEDIA: smoke bỏ phiếu (Mode=Vote) — mỗi model trong PageFit:Models đúng một request chữ-only.
# CHỈ chạy khi người dùng xác nhận trực tiếp. Không in khoá / không in toàn bộ body.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APPSETTINGS="$ROOT/backend/appsettings.json"
PROJECT="$ROOT/backend"

if [[ ! -f "$APPSETTINGS" ]]; then
  echo "error: missing $APPSETTINGS" >&2
  exit 1
fi

readarray -t MODELS < <(python3 - <<'PY' "$APPSETTINGS"
import json, sys
with open(sys.argv[1]) as f:
    cfg = json.load(f)
page = cfg.get("PageFit", {}) or {}
models = page.get("Models") or []
for m in models:
    name = (m or "").strip()
    if name:
        print(name)
PY
)

PROVIDER="$(python3 - <<'PY' "$APPSETTINGS"
import json, sys
with open(sys.argv[1]) as f:
    cfg = json.load(f)
page = cfg.get("PageFit", {}) or {}
override = (page.get("Provider") or "").strip()
if override:
    print(override)
else:
    print(cfg.get("AiProviders", {}).get("DefaultProvider", "") or "")
PY
)"

VOTE_MAX_TOKENS="$(python3 - <<'PY' "$APPSETTINGS"
import json, sys
with open(sys.argv[1]) as f:
    cfg = json.load(f)
page = cfg.get("PageFit", {}) or {}
print(int(page.get("VoteMaxTokens") or 16))
PY
)"

if [[ -z "$PROVIDER" ]]; then
  echo "error: PageFit:Provider / AiProviders:DefaultProvider empty" >&2
  exit 1
fi

if [[ ${#MODELS[@]} -eq 0 ]]; then
  echo "error: PageFit:Models empty in appsettings.json" >&2
  exit 1
fi

BASE_URL="$(python3 - <<'PY' "$APPSETTINGS" "$PROVIDER"
import json, sys
with open(sys.argv[1]) as f:
    cfg = json.load(f)
p = cfg["AiProviders"]["Providers"].get(sys.argv[2], {})
print((p.get("BaseUrl") or "").rstrip("/"))
PY
)"
PATH_COMP="$(python3 - <<'PY' "$APPSETTINGS" "$PROVIDER"
import json, sys
with open(sys.argv[1]) as f:
    cfg = json.load(f)
p = cfg["AiProviders"]["Providers"].get(sys.argv[2], {})
path = p.get("ChatCompletionsPath") or "/chat/completions"
print(path if path.startswith("/") else "/" + path)
PY
)"

ENV_KEY="AiProviders__Providers__${PROVIDER}__ApiKey"
API_KEY="${!ENV_KEY:-}"

if [[ -z "$API_KEY" ]]; then
  SECRET_LINE="$(dotnet user-secrets list --project "$PROJECT" 2>/dev/null | grep -F "AiProviders:Providers:${PROVIDER}:ApiKey" || true)"
  if [[ -n "$SECRET_LINE" ]]; then
    API_KEY="$(printf '%s\n' "$SECRET_LINE" | sed -E 's/^AiProviders:Providers:[^:]+:ApiKey *= *//')"
  fi
fi

if [[ -z "$API_KEY" ]]; then
  cat <<EOF
Không tìm thấy khoá API cho provider '$PROVIDER'.
Đặt một trong hai:
  export ${ENV_KEY}='<key>'
  hoặc: dotnet user-secrets set "AiProviders:Providers:${PROVIDER}:ApiKey" "<key>" --project backend
Không đọc appsettings.Production.json / .env.
EOF
  exit 2
fi

if [[ -z "$BASE_URL" ]]; then
  echo "error: BaseUrl trống cho provider '$PROVIDER'" >&2
  exit 1
fi

URL="${BASE_URL}${PATH_COMP}"
echo "provider=$PROVIDER base_url=$BASE_URL models=${#MODELS[@]} vote_max_tokens=$VOTE_MAX_TOKENS"
echo "---"

for MODEL in "${MODELS[@]}"; do
  TMP="$(mktemp)"
  HTTP_CODE="$(curl -sS -o "$TMP" -w '%{http_code}' \
    -X POST "$URL" \
    -H "Authorization: Bearer ${API_KEY}" \
    -H "Content-Type: application/json" \
    -d "$(python3 - <<PY
import json
print(json.dumps({
  "model": "$MODEL",
  "messages": [
    {"role": "system", "content": "Reply with exactly one word: YES or NO."},
    {"role": "user", "content": "Is 2+2 equal to 4? Answer YES or NO."}
  ],
  "max_tokens": int("$VOTE_MAX_TOKENS"),
  "temperature": 0
}))
PY
)")"

  python3 - <<'PY' "$MODEL" "$HTTP_CODE" "$TMP"
import json, re, sys
model, code, path = sys.argv[1], sys.argv[2], sys.argv[3]
accepts_params = "yes"
normalized = "unparseable"
try:
    data = json.load(open(path))
except Exception:
    data = None

if code == "400":
    body = open(path, errors="replace").read()[:400].lower()
    if "temperature" in body or "max_tokens" in body or "max tokens" in body:
        accepts_params = "no"

if data is not None:
    choice = (data.get("choices") or [None])[0] or {}
    msg = choice.get("message") or {}
    content = msg.get("content")
    if isinstance(content, list):
        parts = []
        for p in content:
            if isinstance(p, str):
                parts.append(p)
            elif isinstance(p, dict) and isinstance(p.get("text"), str):
                parts.append(p["text"])
        content = "".join(parts)
    if isinstance(content, str):
        s = content.strip()
        while s and s[-1] in '.!,;:"\')]}':
            s = s[:-1].rstrip()
        while s and s[0] in '"\'([{':
            s = s[1:].lstrip()
        u = s.upper()
        if u in ("YES", "NO"):
            normalized = u

print(f"model={model} http={code} answer={normalized} accepts_temperature_max_tokens={accepts_params}")
PY
  rm -f "$TMP"
done
