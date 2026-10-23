"""Run the Unity PlayMode tests through the Unity CLI without ever waiting on a run that cannot finish.

Usage (Editor open on unity/):
  python -I tools/unity/playmode_tests.py [--filter <name part>] [--timeout <seconds>]

Steps, each failing loudly instead of hanging:
  1. Preflight: refresh assets (new files are only compiled after a refresh), recompile, wait for the compile, and stop
     with the compiler errors if compilation failed (Unity cannot enter Play mode then, and a test run waits forever).
  2. Turn on run-in-background (an unfocused Editor does not advance Play mode) and start the tests.
  3. Watchdog: cancel the run and say why if Play mode has not started within 60 s, or the run exceeds --timeout.
  4. Report: totals, every failure with its message, and the console error/warning counts of the run.

Exit code: 0 all green and no console errors; 1 test failures or console errors; 2 compile errors; 3 stuck run.
"""
import json
import subprocess
import sys
import time

PLAY_MODE_START_SECONDS = 60
POLL_SECONDS = 5


def cli(*args, timeout=120):
    """Runs `unity <args> --format json` and returns the parsed result (or {} when the output is not JSON)."""
    proc = subprocess.run(["unity", *args, "--format", "json"], capture_output=True, text=True, timeout=timeout,
                          encoding="utf-8", errors="replace")
    try:
        return json.loads(proc.stdout)
    except json.JSONDecodeError:
        return {"raw": proc.stdout + proc.stderr}


def result(response):
    return ((response.get("data") or {}).get("result")) or {}


def console_truth():
    return result(cli("command", "console", "status")).get("groundTruth") or {}


def console_entries():
    return result(cli("command", "console", "get")).get("entries") or []


def is_playing():
    r = result(cli("command", "script", "eval", "--code", "return UnityEngine.Application.isPlaying.ToString();", "--timeout", "20"))
    return isinstance(r, dict) and r.get("result") == "True" or r == "True"


def preflight():
    if result(cli("command", "test", "status")).get("status") == "running":
        print("[preflight] a previous test run is still marked running; cancelling it")
        cli("command", "test", "cancel")
    print("[preflight] refreshing assets (new files compile only after a refresh) and recompiling")
    cli("command", "script", "eval", "--code", "UnityEditor.AssetDatabase.Refresh(); return \"ok\";", "--timeout", "60")
    # `unity recompile` waits for the compile and reports the compiler's own errors. A successful compile reloads the
    # scripting domain, which drops the CLI's connection ("socket hang up"): wait for the Editor and ask again.
    compiled = {}
    for attempt in range(6):
        compiled = cli("recompile", timeout=300).get("data") or {}
        if compiled.get("status") in ("completed", "up_to_date"):
            break
        time.sleep(10)
    else:
        print("[preflight] the Editor did not report a finished compile after six tries")
        sys.exit(3)
    if compiled.get("compiling"):
        print("[preflight] still compiling after 5 minutes; giving up")
        sys.exit(3)
    if compiled.get("errors") or console_truth().get("compilationFailed"):
        errors = compiled.get("errors") or []
        print(f"[preflight] COMPILATION FAILED ({len(errors)} errors); PlayMode tests would never start:")
        for e in errors[:20]:
            print(f"  {e.get('file')}({e.get('line')}): {e.get('code')} {e.get('message')}")
        sys.exit(2)
    print("[preflight] compiled cleanly")


def run(filter_text, timeout):
    cli("command", "editor.playmode", "set_run_in_background", "--enable", "true")
    cli("command", "console", "clear")
    args = ["command", "test", "run", "--mode", "PlayMode", "--async_tests", "true"]
    # Always pass a filter: without one the Test Runner may reuse the previous run's filter.
    args += ["--filter", filter_text or "TableWorld.Tests"]
    # The status keeps the previous run's results until the new run starts: remember them so they are not taken as ours.
    previous = json.dumps(result(cli("command", "test", "status")), sort_keys=True)
    started = time.time()
    print(f"[run] starting PlayMode tests{' matching ' + filter_text if filter_text else ''}")
    cli(*args)
    played = False
    active = False
    while True:
        time.sleep(POLL_SECONDS)
        status = result(cli("command", "test", "status"))
        state = status.get("status")
        elapsed = time.time() - started
        active = active or played or state not in ("completed", None)
        if state == "completed" and (active or json.dumps(status, sort_keys=True) != previous):
            return status
        if not played:
            played = is_playing()
            if not played and elapsed > PLAY_MODE_START_SECONDS:
                cli("command", "test", "cancel")
                truth = console_truth()
                why = "compile errors" if truth.get("compilationFailed") else "the Editor never entered Play mode (a modal dialog, or the Editor is busy)"
                print(f"[watchdog] cancelled after {elapsed:.0f} s: {why}")
                sys.exit(3)
        if elapsed > timeout:
            cli("command", "test", "cancel")
            print(f"[watchdog] cancelled: the run exceeded {timeout} s (a test is stuck or waiting forever)")
            sys.exit(3)


def report(status):
    summary = status.get("summary") or {}
    results = status.get("results") or []
    failed = [r for r in results if r.get("Status") == "Failed"]
    print(f"[result] {summary.get('passed', '?')}/{summary.get('total', '?')} passed, {summary.get('failed', 0)} failed, "
          f"{summary.get('skipped', 0)} skipped, {status.get('duration', '?')} s")
    for r in failed:
        print(f"  FAIL {r['FullName'].split('.')[-1]}: {(r.get('Message') or '').strip().splitlines()[0][:300] if r.get('Message') else ''}")
    entries = console_entries()
    errors = [e for e in entries if e.get("level") == "error"]
    warnings = [e for e in entries if e.get("level") == "warn"]
    print(f"[console] {len(errors)} errors, {len(warnings)} warnings during the run")
    for e in (errors + warnings)[:10]:
        print(f"  {e['level']}: {e['message'].splitlines()[0][:200]}")
    return 0 if not failed and not errors else 1


def main():
    args = sys.argv[1:]
    filter_text = args[args.index("--filter") + 1] if "--filter" in args else None
    timeout = float(args[args.index("--timeout") + 1]) if "--timeout" in args else 600
    preflight()
    sys.exit(report(run(filter_text, timeout)))


if __name__ == "__main__":
    main()
