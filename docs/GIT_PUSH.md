# Before first push — quick checklist

## 1. Secrets

- [ ] `backend/appsettings.json` — `ApiKey`, `Jwt:SecretKey`, passwords **empty**
- [ ] No `.env` committed (only `.env.example`)
- [ ] No `appsettings.Local.json` committed
- [ ] `dotnet user-secrets` stays on machine only

## 2. Ignored artifacts

- [ ] `backend/Data/*.db` — not in git
- [ ] `backend/Storage/Files/` — not in git
- [ ] `backend/wwwroot/dist/` — rebuild with `npm run build` in ClientApp
- [ ] `node_modules/`, `bin/`, `obj/` — not in git

## 3. First commit (example)

```bash
cd /path/to/AutomationVNI

git status                    # review — no secrets, no .db
git add .
git status                    # double-check staged files

git commit -m "$(cat <<'EOF'
Initial MVP: VNI Automation backend + frontend.

Mock/AI/Facebook provider pipelines, scheduler, dev seed, docs and smoke tests.
EOF
)"

git push -u origin main
# hoặc: git push -u origin master  (tùy branch default)
```

## 4. Clone & run (for teammates)

```bash
git clone https://github.com/thang-dev-aptech/AutomationVNI.git
cd AutomationVNI

# Backend
cd backend
dotnet ef database update
dotnet user-secrets set "Jwt:SecretKey" "DEV_ONLY_SECRET_KEY_MIN_32_CHARS_LONG!!"
dotnet run

# Frontend (terminal khác)
cd ClientApp
cp .env.example .env
npm install
npm run dev
```

## 5. Remote

Current remote: `https://github.com/thang-dev-aptech/AutomationVNI.git`

```bash
git branch -M main    # nếu cần đổi tên branch
git remote -v
```
