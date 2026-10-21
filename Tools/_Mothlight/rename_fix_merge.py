#!/usr/bin/env python3
"""
Mothlight: applies our upstream renames (upstream_renames.py) to lines a merge is bringing in.

Git doesn't run merge drivers for files only upstream changed, so new upstream files
(maps, partials, ...) arrive with upstream names. This rewrites only lines that are added
relative to HEAD in the index, so anything we deliberately kept is untouched.
Covers the same files as rename_merge_driver.py (.gitattributes merge=mothlight-rename).

Usage: rename_fix_merge.py [--check]
  Runs as a pre-commit hook during merges (see setup_merge_drivers.sh); safe to run by hand.
  --check only reports, exit 1 if anything would change.
"""

import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from upstream_renames import describe, rename  # noqa: E402

_HUNK = re.compile(r"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@")


def git(*args: str) -> str:
    return subprocess.run(["git", *args], capture_output=True, text=True, check=True).stdout


def driver_files(paths: list[str]) -> list[str]:
    out = []
    # check-attr output: "<path>: merge: <value>"
    for chunk in (paths[i:i + 500] for i in range(0, len(paths), 500)):
        for line in git("check-attr", "merge", "--", *chunk).splitlines():
            path, _, value = line.rsplit(": ", 2)
            if value == "mothlight-rename":
                out.append(path)
    return out


def added_lines(path: str) -> set[int]:
    lines = set()
    for line in git("diff", "--cached", "-U0", "--no-color", "HEAD", "--", path).splitlines():
        m = _HUNK.match(line)
        if m:
            start, count = int(m.group(1)), int(m.group(2) or 1)
            lines.update(range(start, start + count))
    return lines


def main() -> int:
    check = "--check" in sys.argv
    os.chdir(git("rev-parse", "--show-toplevel").strip())

    # Only resolved/staged paths; anything still conflicted is left alone.
    staged = git("diff", "--cached", "--name-only", "--diff-filter=AMR", "HEAD").splitlines()
    unmerged = set(git("diff", "--name-only", "--diff-filter=U").splitlines())
    changed = []

    for path in driver_files([p for p in staged if p not in unmerged]):
        targets = added_lines(path)
        if not targets or not os.path.isfile(path):
            continue
        with open(path, "rb") as f:
            text = f.read().decode("utf-8", errors="surrogateescape")
        new, counts = rename(text, path, targets)
        if not counts:
            continue
        changed.append(path)
        print(f"mothlight-rename: {path}: renamed {describe(counts)}", file=sys.stderr)
        if not check:
            with open(path, "wb") as f:
                f.write(new.encode("utf-8", errors="surrogateescape"))

    if changed and not check:
        git("add", "--", *changed)
    return 1 if changed and check else 0


if __name__ == "__main__":
    sys.exit(main())
