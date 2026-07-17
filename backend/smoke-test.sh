#!/usr/bin/env bash
# VNI Automation — MVP mock end-to-end smoke test (curl)
#
# Prerequisites:
#   - Backend running (default http://localhost:5068)
#   - ASPNETCORE_ENVIRONMENT=Development (dev seed + admin account)
#   - python3 for JSON parsing
#
# Usage:
#   chmod +x smoke-test.sh    # first time only
#   ./smoke-test.sh
#
# Optional env:
#   BASE_URL=http://localhost:5068
#   ADMIN_EMAIL=admin@vni.local
#   ADMIN_PASSWORD=Admin@123
#   DEFAULT_CHANNEL_ID=00000000-0000-0000-0000-000000000001
#   DEFAULT_CATEGORY_ID=00000000-0000-0000-0000-000000000003
#
# Security: does not print JWT tokens or passwords to stdout.
#
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:5068}"
ADMIN_EMAIL="${ADMIN_EMAIL:-admin@vni.local}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-Admin@123}"
DEFAULT_CHANNEL_ID="${DEFAULT_CHANNEL_ID:-00000000-0000-0000-0000-000000000001}"
DEFAULT_CATEGORY_ID="${DEFAULT_CATEGORY_ID:-00000000-0000-0000-0000-000000000003}"

json_field() {
  python3 -c "import sys,json; d=json.load(sys.stdin); print($1)" 2>/dev/null
}

echo "==> Smoke test against $BASE_URL"

echo "==> 1. Login"
TOKEN=$(curl -sf -X POST "$BASE_URL/api/auth/login" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" \
  | json_field "d['data']['accessToken']")
AUTH="Authorization: Bearer $TOKEN"

echo "==> 2. Verify dev seed SocialChannel"
curl -sf "$BASE_URL/api/socialchannel/$DEFAULT_CHANNEL_ID" -H "$AUTH" > /dev/null

echo "==> 3. Create post"
POST_ID=$(curl -sf -X POST "$BASE_URL/api/post" -H "$AUTH" -H "Content-Type: application/json" \
  -d "{\"title\":\"Smoke test $(date +%s)\",\"socialChannelId\":\"$DEFAULT_CHANNEL_ID\",\"categoryId\":\"$DEFAULT_CATEGORY_ID\",\"generationFlow\":1}" \
  | json_field "d['data']['id']")
echo "    postId=$POST_ID"

echo "==> 4. Text generation"
TEXT_JOB=$(curl -sf -X POST "$BASE_URL/api/post/$POST_ID/queue-text-generation" -H "$AUTH" \
  | json_field "d['data']['jobId']")
curl -sf -X POST "$BASE_URL/api/generationjob/$TEXT_JOB/process" -H "$AUTH" > /dev/null

echo "==> 5. Image generation"
IMAGE_JOB=$(curl -sf -X POST "$BASE_URL/api/post/$POST_ID/queue-image-generation" -H "$AUTH" \
  | json_field "d['data']['jobId']")
curl -sf -X POST "$BASE_URL/api/generationjob/$IMAGE_JOB/process" -H "$AUTH" > /dev/null

echo "==> 6. Image render"
RENDER_JOB=$(curl -sf -X POST "$BASE_URL/api/post/$POST_ID/queue-image-render" -H "$AUTH" \
  | json_field "d['data']['jobId']")
curl -sf -X POST "$BASE_URL/api/generationjob/$RENDER_JOB/process" -H "$AUTH" > /dev/null

echo "==> 7. Approve (post đã WaitingReview sau generation pipeline)"
curl -sf -X POST "$BASE_URL/api/post/$POST_ID/approve" -H "$AUTH" > /dev/null

echo "==> 8. Publish now + process"
curl -sf -X POST "$BASE_URL/api/post/$POST_ID/publish-now" -H "$AUTH" > /dev/null
PUBLISH_LOG_ID=$(curl -sf "$BASE_URL/api/publishlog/by-post/$POST_ID" -H "$AUTH" \
  | json_field "d['data'][0]['id']")
curl -sf -X POST "$BASE_URL/api/publishlog/$PUBLISH_LOG_ID/process" -H "$AUTH" > /dev/null

echo "==> 9. Verify Published"
POST_STATUS=$(curl -sf "$BASE_URL/api/post/$POST_ID" -H "$AUTH" | json_field "d['data']['status']")
PUBLISHED_URL=$(curl -sf "$BASE_URL/api/post/$POST_ID" -H "$AUTH" | json_field "d['data'].get('publishedUrl','')")
if [[ "$POST_STATUS" != "7" ]]; then
  echo "FAIL: expected post status=7 (Published), got $POST_STATUS"
  exit 1
fi
echo "    status=Published, publishedUrl=$PUBLISHED_URL"

echo "==> 10. Verify publish log"
LOG_STATUS=$(curl -sf "$BASE_URL/api/publishlog/by-post/$POST_ID" -H "$AUTH" | json_field "d['data'][0]['status']")
if [[ "$LOG_STATUS" != "1" ]]; then
  echo "FAIL: expected publish log status=1 (Success), got $LOG_STATUS"
  exit 1
fi

echo "==> 11. Verify media preview HTTP 200"
MEDIA_ID=$(curl -sf "$BASE_URL/api/postmedia/by-post/$POST_ID" -H "$AUTH" | json_field "d['data'][0]['mediaId']")
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" "$BASE_URL/api/mediaasset/$MEDIA_ID/preview")
if [[ "$HTTP_CODE" != "200" ]]; then
  echo "FAIL: media preview returned HTTP $HTTP_CODE"
  exit 1
fi
echo "    mediaId=$MEDIA_ID preview=200"

echo "==> Smoke test PASSED"
