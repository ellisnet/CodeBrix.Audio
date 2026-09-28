#!/usr/bin/env python3
"""Install and run the Core-only decoder regression app on an explicitly selected Android device."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import shlex
import subprocess
import time

APP = "com.codebrix.audio.coretests"


def run(apk, adb, serial, result_path):
    prefix = [adb, "-s", serial]

    def shell(*args, check=True):
        return subprocess.run([*prefix, "shell", shlex.join(str(a) for a in args)],
                              capture_output=True, text=True, check=check, timeout=20)

    api = int(shell("getprop", "ro.build.version.sdk").stdout.strip())
    abi = shell("getprop", "ro.product.cpu.abi").stdout.strip()
    if api < 33 or abi not in ("arm64-v8a", "x86_64"):
        raise SystemExit(f"Requires API 33+ ARM64 or x64, got {api}/{abi}")
    subprocess.run([*prefix, "install", "--no-incremental", "-r", str(apk)], check=True, timeout=90)
    shell("am", "force-stop", APP)
    shell("run-as", APP, "rm", "-f", "files/results.txt")
    shell("am", "start", "-W", "-n", APP + "/" + APP + ".MainActivity")
    deadline = time.monotonic() + 180
    while time.monotonic() < deadline:
        result = shell("run-as", APP, "cat", "files/results.txt", check=False).stdout
        crashed = shell("pidof", APP, check=False).returncode != 0
        if "PASS ALL" in result or "FAIL " in result or crashed:
            passed = "PASS ALL" in result and "FAIL " not in result
            report = {"tested_utc": datetime.now(timezone.utc).isoformat(), "abi": abi, "api": api,
                      "model": shell("getprop", "ro.product.model").stdout.strip(),
                      "apk_sha256": hashlib.sha256(apk.read_bytes()).hexdigest(),
                      "passed": passed, "process_exited": crashed, "output": result}
            result_path.parent.mkdir(parents=True, exist_ok=True)
            result_path.write_text(json.dumps(report, indent=2) + "\n")
            print(json.dumps(report, indent=2), flush=True)
            if not passed:
                raise SystemExit(1)
            return
        time.sleep(1)
    raise SystemExit("Core decoder checks timed out; inspect this app's logcat.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("apk", type=Path)
    parser.add_argument("--adb", default="adb")
    parser.add_argument("--serial", required=True)
    parser.add_argument("--result", type=Path, required=True)
    args = parser.parse_args()
    run(args.apk, args.adb, args.serial, args.result)
