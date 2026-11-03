"""Read-only source boundary check; does not inspect private runtime files."""
from pathlib import Path
import subprocess
import sys
import re
import json
import hashlib

ROOT = Path(__file__).resolve().parents[1]
BASELINE = "17b362ea16587fd630892c450fd784317fabab72"
BRANCH = "feature/desktop-app"
BOUNDARY_FILES = {"AGENTS.md", ".gitignore"}
DESKTOP_ADAPTERS = {"host/LocalDaemonFeed.cs", "host/NotificationInput.cs", "host/LocalImageTarget.cs"}


def git(*args):
    return subprocess.check_output(["git", "-C", str(ROOT), *args]).decode("utf-8")


def main():
    if git("branch", "--show-current").strip() != BRANCH:
        raise SystemExit(f"FAIL: desktop work must use {BRANCH}")
    subprocess.run(
        ["git", "-C", str(ROOT), "merge-base", "--is-ancestor", BASELINE, "HEAD"],
        check=True,
    )
    # Include committed, staged and unstaged changes; expose both sides of renames.
    changed = set(git("diff", "--name-only", "--no-renames", "-z", BASELINE, "--").split("\0"))
    changed.update(git("ls-files", "--others", "--exclude-standard", "-z").split("\0"))
    rejected = sorted(p for p in changed if p and not p.startswith("desktop/") and p not in BOUNDARY_FILES and p not in DESKTOP_ADAPTERS)
    for path in DESKTOP_ADAPTERS:
        original = git("show", f"{BASELINE}:{path}").replace("\r\n", "\n")
        current = (ROOT / path).read_text(encoding="utf-8").replace("\r\n", "\n")
        # The normal host build must be text-identical after desktop-only blocks are removed.
        current = re.sub(r"(?m)^#if DESKTOP\n(?:(?!^#if|^#endif).)*^#endif\n", "", current, flags=re.S)
        current = current.replace('   }\n   else if(kind=="request"){', '   }else if(kind=="request"){')
        if path == "host/LocalImageTarget.cs":
            current = current.replace('    if(!LocalDaemonFeed.CorroboratesPhoto(frame,thread)){\n     throw new IOException("현재 Codex 대화를 확인하지 못했습니다. 터미널의 최신 대화를 확인하세요.");\n    }', '    if(!LocalDaemonFeed.CorroboratesPhoto(frame,thread))throw new IOException("현재 Codex 대화를 확인하지 못했습니다. 터미널의 최신 대화를 확인하세요.");')
        if current != original:
            rejected.append(path + " (non-desktop behavior changed)")
    # Keep the independently released connector implementation unchanged here.
    manifest = json.loads((ROOT / "desktop/connector/source.json").read_text(encoding="utf-8"))
    for item in manifest["files"]:
        data = (ROOT / item["path"]).read_text(encoding="utf-8").replace("\r\n", "\n").encode("utf-8")
        if hashlib.sha256(data).hexdigest() != item["sha256"]:
            rejected.append(item["path"] + " (pinned connector source changed)")
    if rejected:
        for path in rejected:
            print(f"FAIL: outside desktop scope: {path}")
        return 1
    print("PASS: desktop boundary; normal connector behavior and Android match the baseline.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, subprocess.CalledProcessError):
        sys.exit("FAIL: could not verify desktop source boundary")
