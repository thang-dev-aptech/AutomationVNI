#!/usr/bin/env bash
# Đo thời gian từng test của tests/Backend.Tests: chạy dotnet test với logger trx vào thư mục
# tạm (không bao giờ ghi trx vào repo), rồi in N test và M lớp chậm nhất.
#
#   scripts/test-profile.sh                      # build + chạy toàn bộ suite
#   scripts/test-profile.sh --no-build           # dùng bản build sẵn
#   scripts/test-profile.sh --top 30 --classes 15 --filter "FullyQualifiedName~Crm"
#   scripts/test-profile.sh --trx path/to.trx    # chỉ phân tích file trx có sẵn
#   scripts/test-profile.sh --json out.json      # thêm bản json (tổng, theo lớp, theo test)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TOP=30
CLASSES=15
TRX=""
JSON_OUT=""
DOTNET_ARGS=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --top) TOP="$2"; shift 2 ;;
    --classes) CLASSES="$2"; shift 2 ;;
    --trx) TRX="$2"; shift 2 ;;
    --json) JSON_OUT="$2"; shift 2 ;;
    --filter) DOTNET_ARGS+=(--filter "$2"); shift 2 ;;
    --no-build|--no-restore) DOTNET_ARGS+=("$1"); shift ;;
    -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
    *) echo "unknown arg: $1" >&2; exit 2 ;;
  esac
done

if [[ -z "$TRX" ]]; then
  OUT_DIR="$(mktemp -d "${TMPDIR:-/tmp}/test-profile.XXXXXX")"
  trap 'rm -rf "$OUT_DIR"' EXIT
  start=$EPOCHREALTIME
  set +e
  dotnet test "$ROOT/tests/Backend.Tests" "${DOTNET_ARGS[@]}" \
    --logger "trx;LogFileName=profile.trx" --results-directory "$OUT_DIR" | tail -n 5
  rc=${PIPESTATUS[0]}
  set -e
  end=$EPOCHREALTIME
  python3 -c "print(f'wall time dotnet test: {$end-$start:.1f}s (exit {$rc})')"
  TRX="$(find "$OUT_DIR" -name 'profile.trx' | head -n1)"
  [[ -n "$TRX" ]] || { echo "error: no trx produced" >&2; exit 1; }
fi

python3 -I - "$TRX" "$TOP" "$CLASSES" "$JSON_OUT" <<'PY'
import json, re, sys, xml.etree.ElementTree as ET
from collections import defaultdict

trx, top, classes, json_out = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), sys.argv[4]
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
root = ET.parse(trx).getroot()

def secs(d):
    h, m, s = d.split(":")
    return int(h) * 3600 + int(m) * 60 + float(s)

# testId -> lớp (className) từ TestDefinitions
cls_of = {}
for ut in root.iterfind(".//t:UnitTest", ns):
    tm = ut.find("t:TestMethod", ns)
    cls_of[ut.get("id")] = tm.get("className") if tm is not None else "?"

rows, outcomes = [], defaultdict(int)
for r in root.iterfind(".//t:UnitTestResult", ns):
    d = secs(r.get("duration", "0:0:0"))
    outcomes[r.get("outcome")] += 1
    rows.append((d, r.get("testName"), cls_of.get(r.get("testId"), "?"), r.get("outcome")))

by_cls = defaultdict(lambda: [0.0, 0])
for d, _, c, _ in rows:
    by_cls[c][0] += d
    by_cls[c][1] += 1

total = sum(r[0] for r in rows)
print(f"\n{len(rows)} results, outcomes={dict(outcomes)}, sum of test durations={total:.1f}s")
print(f"\nTop {top} slowest tests:")
for d, name, c, o in sorted(rows, reverse=True)[:top]:
    short = re.sub(r"^.*\.", "", c)
    print(f"  {d:7.2f}s  {short}  ::  {name[len(c)+1:] if name.startswith(c) else name}  [{o}]")
print(f"\nTop {classes} slowest classes (sum, count, avg):")
for c, (s, n) in sorted(by_cls.items(), key=lambda kv: -kv[1][0])[:classes]:
    print(f"  {s:7.2f}s  {n:4d}  {s/n:6.3f}s  {c}")

if json_out:
    with open(json_out, "w") as f:
        json.dump({
            "results": len(rows), "outcomes": outcomes, "sum_seconds": total,
            "classes": {c: {"seconds": s, "count": n} for c, (s, n) in by_cls.items()},
            "tests": [{"name": n, "class": c, "seconds": d, "outcome": o} for d, n, c, o in rows],
        }, f, indent=1, ensure_ascii=False)
PY
