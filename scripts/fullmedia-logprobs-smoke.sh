#!/usr/bin/env bash
# FULLMEDIA t3: một request chữ-only tới AiProviders:DefaultProvider với logprobs=true.
# CHỈ chạy khi người dùng xác nhận trực tiếp. Không in khoá / không in toàn bộ body.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APPSETTINGS="$ROOT/backend/appsettings.json"
PROJECT="$ROOT/backend"

if [[ ! -f "$APPSETTINGS" ]]; then
  echo "error: missing $APPSETTINGS" >&2
  exit 1
fi

PROVIDER="$(python3 - <<'PY' "$APPSETTINGS"
import json, sys
with open(sys.argv[1]) as f:
    cfg = json.load(f)
print(cfg.get("AiProviders", {}).get("DefaultProvider", "") or "")
PY
)"

if [[ -z "$PROVIDER" ]]; then
  echo "error: AiProviders:DefaultProvider empty in appsettings.json" >&2
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
MODEL="$(python3 - <<'PY' "$APPSETTINGS" "$PROVIDER"
import json, sys
with open(sys.argv[1]) as f:
    cfg = json.load(f)
page = cfg.get("PageFit", {}) or {}
p = cfg["AiProviders"]["Providers"].get(sys.argv[2], {})
print((page.get("Model") or "").strip() or (p.get("DefaultVisionModel") or "").strip() or (p.get("DefaultTextModel") or "").strip())
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

if [[ -z "$BASE_URL" || -z "$MODEL" ]]; then
  echo "error: BaseUrl hoặc Model trống cho provider '$PROVIDER'" >&2
  exit 1
fi

URL="${BASE_URL}${PATH_COMP}"
TMP="$(mktemp)"
trap 'rm -f "$TMP"' EXIT

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
  "max_tokens": 1,
  "temperature": 0,
  "logprobs": True,
  "top_logprobs": 5
}))
PY
)")"

python3 - <<'PY' "$HTTP_CODE" "$TMP"
import json, math, sys
code, path = sys.argv[1], sys.argv[2]
print(f"http_status={code}")
try:
    data = json.load(open(path))
except Exception:
    print("has_logprobs=no")
    print("yes_no_tokens=")
    sys.exit(0)

choice = (data.get("choices") or [None])[0] or {}
logprobs = choice.get("logprobs")
content = (logprobs or {}).get("content") if isinstance(logprobs, dict) else None
has = bool(content)
print(f"has_logprobs={'yes' if has else 'no'}")
tokens = []
if has and isinstance(content, list) and content:
    top = content[0].get("top_logprobs") or []
    for e in top:
        tok = (e.get("token") or "").strip()
        if tok.upper() in ("YES", "NO"):
            try:
                p = math.exp(float(e.get("logprob")))
            except Exception:
                p = float("nan")
            tokens.append(f"{tok}:{p:.6g}")
print("yes_no_tokens=" + (",".join(tokens) if tokens else ""))
PY
