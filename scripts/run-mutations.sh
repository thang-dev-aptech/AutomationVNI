#!/usr/bin/env bash
# Chạy bảng mutation (tests/mutations/*.json) chống hồi quy cho tests/Backend.Tests.
#
#   scripts/run-mutations.sh                         # toàn bộ bảng, song song
#   scripts/run-mutations.sh --list                  # liệt kê mutation
#   scripts/run-mutations.sh --only T1,M3            # chỉ chạy các id này
#   scripts/run-mutations.sh --since main            # chỉ mutation có file nằm trong `git diff --name-only main`
#   scripts/run-mutations.sh --jobs 2 --report /tmp/mut.md   # (.md hoặc .json)
#
# Tuỳ chọn khác: --table <file.json> (lặp lại được; mặc định mọi tests/mutations/*.json),
#   --workdir <dir> (thư mục tạm, mặc định $MUTATION_TMPDIR hoặc ~/.cache/automationvni-mutations),
#   --skip-baseline (bỏ bước kiểm các bộ lọc test xanh trên HEAD chưa đột biến),
#   --keep (không xoá worktree khi xong, để debug), --timeout <giây> (mặc định 600/mutation),
#   --full-build (biên dịch cả backend/Migrations; mặc định bỏ qua qua tests/mutations/fast-build.targets,
#   tự bật lại khi có mutation nằm trong backend/Migrations/).
#
# Cách chạy: mỗi mutation có một git worktree RIÊNG tạo từ HEAD (cây làm việc chính không bao giờ bị
# sửa). Để không phải build nguội mỗi lần, mỗi job có một đường dẫn "khe" cố định: build mẫu một lần ở
# đó rồi cất obj/bin; mỗi mutation tạo worktree mới đúng đường dẫn khe đó, chép obj/bin mẫu (reflink
# nếu filesystem hỗ trợ), áp mutation và build tăng dần — chỉ backend biên dịch lại. Phân loại:
#   RED              đỏ, có ít nhất một test đỏ nằm trong expect_red
#   RED-UNEXPECTED   đỏ nhưng không test nào trong expect_red đỏ (lỗi nếu không có allow_unexpected)
#   GREEN            mọi test xanh — lỗ hổng test (lỗi)
#   BUILDERR         build lỗi (có "error CS") — KHÔNG tính là đỏ (lỗi)
#   PATTERN-NOT-FOUND / AMBIGUOUS   đoạn gốc không có / có nhiều chỗ mà không chỉ occurrence (lỗi)
#   TIMEOUT / ERROR  quá giờ / test host lỗi không có test đỏ (lỗi)
#   EQUIVALENT / STALE  đã đánh dấu trong bảng, không chạy
# Exit code 0 chỉ khi không có trạng thái lỗi nào.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
exec python3 -I -u - "$ROOT" "$@" <<'PY'
import argparse, concurrent.futures as cf, glob, json, os, re, shutil, signal, subprocess, sys, tempfile, threading, time
import xml.etree.ElementTree as ET

ROOT = sys.argv[1]
ap = argparse.ArgumentParser(prog="run-mutations.sh")
ap.add_argument("--table", action="append")
ap.add_argument("--only")
ap.add_argument("--since")
ap.add_argument("--list", action="store_true")
ap.add_argument("--report")
ap.add_argument("--jobs", type=int)
ap.add_argument("--workdir")
ap.add_argument("--skip-baseline", action="store_true")
ap.add_argument("--keep", action="store_true")
ap.add_argument("--timeout", type=int, default=600)
ap.add_argument("--full-build", action="store_true")
args = ap.parse_args(sys.argv[2:])

TEST_PROJ = "tests/Backend.Tests"
# RunAnalyzers=false chỉ tắt chẩn đoán (không đổi IL); source generator vẫn chạy.
BUILD_FLAGS = ["--no-restore", "-nologo", "-nodeReuse:false", "-p:RunAnalyzers=false", "-clp:ErrorsOnly"]
FAST_TARGETS = os.path.join(ROOT, "tests/mutations/fast-build.targets")
FAIL_STATES = {"GREEN", "BUILDERR", "PATTERN-NOT-FOUND", "AMBIGUOUS", "TIMEOUT", "ERROR", "RED-UNEXPECTED"}
ENV = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", MSBUILDDISABLENODEREUSE="1",
           DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1")

def git(*a, cwd=ROOT, check=True):
    return subprocess.run(["git", *a], cwd=cwd, check=check, capture_output=True, text=True).stdout.strip()

# ---------- bảng mutation ----------
tables = args.table or sorted(glob.glob(os.path.join(ROOT, "tests/mutations/*.json")))
muts, seen = [], set()
REQUIRED = ("id", "description", "file", "original", "replacement", "filter", "expect_red")
for t in tables:
    with open(t, encoding="utf-8") as f:
        data = json.load(f)
    for m in data["mutations"]:
        missing = [k for k in REQUIRED if k not in m]
        if missing:
            sys.exit(f"{t}: mutation {m.get('id')} thiếu trường {missing}")
        if m["id"] in seen:
            sys.exit(f"{t}: id trùng {m['id']}")
        if not m["expect_red"]:
            sys.exit(f"{t}: {m['id']} cần ít nhất một test trong expect_red")
        if m.get("equivalent") and not m.get("reason"):
            sys.exit(f"{t}: {m['id']} equivalent=true phải có reason")
        seen.add(m["id"])
        m["_table"] = os.path.relpath(t, ROOT)
        muts.append(m)

if args.only:
    want = [x.strip() for x in args.only.split(",") if x.strip()]
    unknown = [w for w in want if w not in seen]
    if unknown:
        sys.exit(f"--only: không có id {unknown}")
    muts = [m for m in muts if m["id"] in want]
if args.since:
    changed = set(git("diff", "--name-only", args.since).splitlines())
    muts = [m for m in muts if {m["file"], *[e["file"] for e in m.get("extra_edits", [])]} & changed]

if args.list:
    for m in muts:
        flag = " [equivalent]" if m.get("equivalent") else " [stale]" if m.get("stale") else ""
        print(f"{m['id']:<8} {m['file']}{flag}\n         {m['description']}")
    print(f"{len(muts)} mutation")
    sys.exit(0)
if not muts:
    print("Không có mutation nào để chạy.")
    sys.exit(0)

head = git("rev-parse", "HEAD")
dirty = [l for l in git("status", "--porcelain", "--untracked-files=no").splitlines()]
if dirty:
    print("CẢNH BÁO: cây làm việc có thay đổi chưa commit — runner chỉ dùng nội dung HEAD:", file=sys.stderr)
    for l in dirty[:10]:
        print("   ", l, file=sys.stderr)

runnable = [m for m in muts if not m.get("equivalent") and not m.get("stale")]
touches_migrations = any(f.startswith("backend/Migrations/")
                         for m in runnable for f in [m["file"], *[e["file"] for e in m.get("extra_edits", [])]])
full_build = args.full_build or touches_migrations
if not full_build:
    BUILD_FLAGS.append(f"-p:CustomAfterMicrosoftCommonTargets={FAST_TARGETS}")
jobs = args.jobs or max(1, min(4, (os.cpu_count() or 2) // 2))
jobs = max(1, min(jobs, len(runnable) or 1))
base = args.workdir or os.environ.get("MUTATION_TMPDIR") or os.path.join(
    os.environ.get("XDG_CACHE_HOME", os.path.expanduser("~/.cache")), "automationvni-mutations")
os.makedirs(base, exist_ok=True)
run_dir = tempfile.mkdtemp(prefix=f"run-{head[:7]}-", dir=base)
print(f"HEAD {head[:12]}  mutation={len(muts)} (chạy {len(runnable)})  jobs={jobs}  "
      f"build={'full' if full_build else 'fast (bỏ Migrations)'}  workdir={run_dir}")

# ---------- tiến trình con có thể huỷ ----------
active, active_lock, stopping = set(), threading.Lock(), threading.Event()

def run(cmd, cwd, log, timeout=None):
    """Chạy lệnh trong session riêng (killpg được khi timeout/Ctrl-C). Trả (rc, output, timed_out)."""
    with open(log, "a", encoding="utf-8") as lf:
        lf.write(f"\n$ {' '.join(cmd)}\n")
        lf.flush()
        p = subprocess.Popen(cmd, cwd=cwd, env=ENV, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                             text=True, start_new_session=True)
        with active_lock:
            active.add(p)
        try:
            out, _ = p.communicate(timeout=timeout)
            timed_out = False
        except subprocess.TimeoutExpired:
            os.killpg(p.pid, signal.SIGKILL)
            out, _ = p.communicate()
            timed_out = True
        finally:
            with active_lock:
                active.discard(p)
        lf.write(out or "")
    return p.returncode, out or "", timed_out

def kill_all():
    with active_lock:
        for p in list(active):
            try:
                os.killpg(p.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass

worktrees, wt_lock = set(), threading.Lock()

def add_worktree(path):
    git("worktree", "add", "--detach", "--force", path, head)
    with wt_lock:
        worktrees.add(path)

def remove_worktree(path, keep_as=None):
    if args.keep and keep_as:
        # Giữ để debug: dời sang tên khác để đường dẫn khe trống cho mutation kế tiếp.
        dst = os.path.join(run_dir, f"kept-{keep_as}")
        git("worktree", "move", path, dst, check=False)
        with wt_lock:
            worktrees.discard(path)
        return
    git("worktree", "remove", "--force", path, check=False)
    shutil.rmtree(path, ignore_errors=True)
    with wt_lock:
        worktrees.discard(path)

OUT_DIRS = ["backend/obj", "backend/bin", f"{TEST_PROJ}/obj", f"{TEST_PROJ}/bin"]

OLD_MTIME = "@946684800"  # 2000-01-01: cũ hơn mọi output của build mẫu

def copy_tree(src, dst):
    # -a giữ mtime nên thứ tự thời gian giữa các file trong obj/bin còn nguyên như lúc build mẫu.
    # --reflink=auto: gần như miễn phí trên btrfs/xfs, chép thường ở filesystem khác.
    subprocess.run(["cp", "-a", "--reflink=auto", src, dst], check=True)

def age_sources(wt):
    # File vừa checkout có mtime "bây giờ", mới hơn output mẫu → MSBuild sẽ build lại tất cả. Đưa mọi
    # file đã track về một mốc cũ cố định (nội dung giống hệt lúc build mẫu, cùng HEAD); file đột biến
    # ghi sau đó sẽ là file duy nhất mới hơn output → chỉ backend biên dịch lại.
    files = subprocess.run(["git", "ls-files", "-z"], cwd=wt, check=True, capture_output=True).stdout
    subprocess.run(["xargs", "-0", "touch", "-h", "-d", OLD_MTIME], cwd=wt, input=files, check=True)

# ---------- khe: build mẫu một lần ----------
def prepare_slot(k):
    slot = os.path.join(run_dir, f"slot{k}")
    cache = os.path.join(run_dir, f"cache{k}")
    log = os.path.join(run_dir, f"slot{k}-template.log")
    add_worktree(slot)
    age_sources(slot)
    t0 = time.time()
    rc, out, _ = run(["dotnet", "restore", TEST_PROJ, "-nologo"], slot, log)
    if rc == 0:
        rc, out, _ = run(["dotnet", "build", TEST_PROJ, *BUILD_FLAGS], slot, log)
    if rc != 0:
        raise RuntimeError(f"build mẫu khe {k} lỗi (xem {log})")
    os.makedirs(cache)
    for d in OUT_DIRS:
        os.makedirs(os.path.dirname(os.path.join(cache, d)), exist_ok=True)
        shutil.move(os.path.join(slot, d), os.path.join(cache, d))
    # Luôn gỡ (kể cả --keep): đường dẫn khe phải trống cho worktree của mutation kế tiếp.
    git("worktree", "remove", "--force", slot, check=False)
    shutil.rmtree(slot, ignore_errors=True)
    with wt_lock:
        worktrees.discard(slot)
    return slot, cache, time.time() - t0

def fresh_worktree(slot, cache):
    add_worktree(slot)
    age_sources(slot)
    for d in OUT_DIRS:
        copy_tree(os.path.join(cache, d), os.path.join(slot, d))

# ---------- áp mutation ----------
def apply_edit(wt, edit):
    path = os.path.join(wt, edit["file"])
    if not os.path.exists(path):
        return "PATTERN-NOT-FOUND", f"không có file {edit['file']}"
    with open(path, encoding="utf-8") as f:
        text = f.read()
    n = text.count(edit["original"])
    if n == 0:
        return "PATTERN-NOT-FOUND", f"đoạn gốc không có trong {edit['file']}"
    occ = edit.get("occurrence")
    if n > 1 and not occ:
        return "AMBIGUOUS", f"đoạn gốc xuất hiện {n} lần trong {edit['file']} (thêm occurrence)"
    occ = occ or 1
    if occ > n:
        return "PATTERN-NOT-FOUND", f"occurrence {occ} > {n} trong {edit['file']}"
    idx = -1
    for _ in range(occ):
        idx = text.index(edit["original"], idx + 1)
    text = text[:idx] + edit["replacement"] + text[idx + len(edit["original"]):]
    with open(path, "w", encoding="utf-8") as f:
        f.write(text)
    return None, ""

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

def trx_results(results_dir):
    failed, total = [], 0
    for trx in glob.glob(os.path.join(results_dir, "**", "*.trx"), recursive=True):
        for r in ET.parse(trx).getroot().iterfind(".//t:UnitTestResult", NS):
            total += 1
            if r.get("outcome") == "Failed":
                failed.append(r.get("testName"))
    return failed, total

def matches(test_name, expected):
    # expected: "Method", "Class.Method" hoặc tên đầy đủ; tên theory có "(args)" phía sau.
    base_name = test_name.split("(", 1)[0]
    return base_name == expected or base_name.endswith("." + expected)

def run_tests(wt, flt, log, results_dir, timeout):
    return run(["dotnet", "test", TEST_PROJ, "--no-build", "--nologo", "--filter", flt,
                "--logger", "trx;LogFileName=r.trx", "--results-directory", results_dir], wt, log, timeout)

def run_mutation(m, slot, cache):
    t0 = time.time()
    log = os.path.join(run_dir, f"{m['id']}.log")
    res = {"id": m["id"], "description": m["description"], "file": m["file"], "failed": [], "note": ""}
    try:
        fresh_worktree(slot, cache)
        for edit in [m, *m.get("extra_edits", [])]:
            state, note = apply_edit(slot, edit)
            if state:
                res.update(status=state, note=note)
                return res
        rc, out, to = run(["dotnet", "build", TEST_PROJ, *BUILD_FLAGS], slot, log, args.timeout)
        res["build_s"] = round(time.time() - t0, 1)
        if to:
            res.update(status="TIMEOUT", note="build quá giờ")
            return res
        if "error CS" in out or rc != 0:
            errs = sorted(set(re.findall(r"error CS\d+: [^\[\r\n]*", out)))[:3]
            res.update(status="BUILDERR", note="; ".join(errs) or f"build rc={rc}")
            return res
        rdir = os.path.join(run_dir, f"{m['id']}-results")
        t1 = time.time()
        rc, out, to = run_tests(slot, m["filter"], log, rdir, m.get("timeout", args.timeout))
        res["test_s"] = round(time.time() - t1, 1)
        failed, total = trx_results(rdir)
        res["failed"], res["total"] = failed, total
        if to:
            res.update(status="TIMEOUT", note=f"test quá giờ ({m.get('timeout', args.timeout)}s)")
        elif total == 0:
            res.update(status="ERROR", note="bộ lọc không chọn test nào / test host lỗi")
        elif not failed:
            res.update(status="ERROR" if rc != 0 else "GREEN",
                       note=f"rc={rc} nhưng không test nào đỏ" if rc != 0 else f"{total} test xanh")
        else:
            hit = [f for f in failed if any(matches(f, e) for e in m["expect_red"])]
            if hit:
                res.update(status="RED", note=f"{len(failed)}/{total} đỏ")
            else:
                res.update(status="RED-UNEXPECTED",
                           note=f"đỏ ngoài kỳ vọng: {', '.join(f.split('.')[-1] for f in failed[:3])}")
        return res
    finally:
        res["seconds"] = round(time.time() - t0, 1)
        remove_worktree(slot, keep_as=m["id"])

def worker(k, queue, results, slot_times):
    slot, cache, secs = prepare_slot(k)
    slot_times[k] = secs
    print(f"  khe {k}: build mẫu {secs:.0f}s")
    if k == 0 and not args.skip_baseline:
        flt = "|".join(sorted({f"({m['filter']})" for m in runnable}))
        fresh_worktree(slot, cache)
        try:
            rdir = os.path.join(run_dir, "baseline-results")
            rc, out, to = run_tests(slot, flt, os.path.join(run_dir, "baseline.log"), rdir, args.timeout)
            failed, total = trx_results(rdir)
        finally:
            remove_worktree(slot)
        if to or rc != 0 or failed or total == 0:
            stopping.set()
            raise RuntimeError(f"baseline KHÔNG xanh trên HEAD (rc={rc}, {len(failed)}/{total} đỏ: "
                               f"{failed[:5]}) — mutation sẽ vô nghĩa, dừng.")
        print(f"  baseline HEAD: {total} test xanh trên các bộ lọc")
        baseline_ok.set()
    else:
        if not args.skip_baseline:
            while not baseline_ok.wait(1):
                if stopping.is_set():
                    return
    while not stopping.is_set():
        try:
            m = queue.pop(0)
        except IndexError:
            return
        r = run_mutation(m, slot, cache)
        results.append(r)
        extra = f" build {r.get('build_s', '-')}s test {r.get('test_s', '-')}s"
        print(f"  [{len(results):>3}/{len(runnable)}] {r['id']:<8} {r['status']:<17} {r['seconds']:>5.0f}s{extra}  {r['note']}")

baseline_ok = threading.Event()
results, slot_times = [], {}
queue = list(runnable)
t_start = time.time()
interrupted = False

def on_term(signum, frame):
    raise KeyboardInterrupt
signal.signal(signal.SIGTERM, on_term)

pool = cf.ThreadPoolExecutor(max_workers=jobs)
futures = [pool.submit(worker, k, queue, results, slot_times) for k in range(jobs)] if runnable else []
try:
    for f in futures:
        while True:
            try:
                f.result(timeout=0.5)
                break
            except cf.TimeoutError:
                continue
except KeyboardInterrupt:
    interrupted = True
    print("\nNgắt — dừng và dọn worktree...", file=sys.stderr)
except Exception as e:
    print(f"LỖI: {e}", file=sys.stderr)
    interrupted = True
finally:
    stopping.set()
    kill_all()
    pool.shutdown(wait=True, cancel_futures=True)
    with wt_lock:
        for w in list(worktrees):
            git("worktree", "remove", "--force", w, check=False)
    if not args.keep:
        shutil.rmtree(run_dir, ignore_errors=True)
    git("worktree", "prune", check=False)

for m in muts:
    if m.get("equivalent"):
        results.append({"id": m["id"], "description": m["description"], "file": m["file"],
                        "status": "EQUIVALENT", "note": m["reason"], "seconds": 0})
    elif m.get("stale"):
        results.append({"id": m["id"], "description": m["description"], "file": m["file"],
                        "status": "STALE", "note": m.get("reason", ""), "seconds": 0})

order = {m["id"]: i for i, m in enumerate(muts)}
results.sort(key=lambda r: order[r["id"]])
total_s = time.time() - t_start
counts = {}
for r in results:
    counts[r["status"]] = counts.get(r["status"], 0) + 1
allowed = {m["id"] for m in muts if m.get("allow_unexpected")}
bad = [r for r in results if r["status"] in FAIL_STATES
       and not (r["status"] == "RED-UNEXPECTED" and r["id"] in allowed)]
not_run = [m["id"] for m in runnable if m["id"] not in {r["id"] for r in results}]

print(f"\n{'id':<8} {'trạng thái':<17} {'giây':>5}  ghi chú")
for r in results:
    print(f"{r['id']:<8} {r['status']:<17} {r['seconds']:>5.0f}  {r['note']}")
print(f"\nTổng: {json.dumps(counts, ensure_ascii=False)}  thời gian {total_s:.0f}s (jobs={jobs}, "
      f"build mẫu {max(slot_times.values()) if slot_times else 0:.0f}s)")
if not_run:
    print(f"CHƯA CHẠY: {', '.join(not_run)}")

if args.report:
    meta = {"head": head, "jobs": jobs, "full_build": full_build, "seconds": round(total_s, 1), "counts": counts,
            "interrupted": interrupted, "not_run": not_run}
    if args.report.endswith(".json"):
        with open(args.report, "w", encoding="utf-8") as f:
            json.dump({**meta, "results": results}, f, indent=1, ensure_ascii=False)
    else:
        with open(args.report, "w", encoding="utf-8") as f:
            f.write(f"# Mutation report — {head[:12]}\n\n")
            f.write(f"jobs={jobs}, build={'full' if full_build else 'fast'}, {total_s:.0f}s, "
                    f"{json.dumps(counts, ensure_ascii=False)}\n\n")
            f.write("| id | trạng thái | giây | mô tả | ghi chú |\n|---|---|---|---|---|\n")
            for r in results:
                d = r["description"].replace("|", "\\|")
                n = r["note"].replace("|", "\\|")
                f.write(f"| {r['id']} | {r['status']} | {r['seconds']:.0f} | {d} | {n} |\n")
    print(f"Báo cáo: {args.report}")

if bad:
    print(f"THẤT BẠI: {', '.join(r['id'] + '=' + r['status'] for r in bad)}")
sys.exit(1 if bad or interrupted or not_run else 0)
PY
