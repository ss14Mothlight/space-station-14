#!/usr/bin/env python3
"""
Mothlight: git merge driver that auto-resolves conflicts caused by renames we keep on top
of upstream (see upstream_renames.py: Assistant -> Passenger, NTCredit -> Speso).

Before doing a normal three-way merge, this rewrites the *base* and *theirs* versions of
the file with the same renames, so the only differences git sees are real upstream changes.
Our side is never rewritten.

Git only calls merge drivers for files both sides changed; lines that come in from files
only upstream touched are handled by rename_fix_merge.py (pre-commit hook).

Setup (once per clone): Tools/_Mothlight/setup_merge_drivers.sh
Wired up for file types in .gitattributes.

Usage (from git): rename_merge_driver.py %O %A %B %L %P
"""

import os
import shlex
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from upstream_renames import describe, rename  # noqa: E402


def _read(p: str) -> str:
    with open(p, "rb") as f:
        return f.read().decode("utf-8", errors="surrogateescape")


def _write(p: str, s: str) -> None:
    with open(p, "wb") as f:
        f.write(s.encode("utf-8", errors="surrogateescape"))


def _map_driver() -> str | None:
    r = subprocess.run(["git", "config", "--get", "merge.mapping-merge-driver.driver"],
                       capture_output=True, text=True)
    return r.stdout.strip() or None


def main() -> int:
    base_p, ours_p, theirs_p = sys.argv[1:4]
    marker = sys.argv[4] if len(sys.argv) > 4 else "7"
    path = sys.argv[5] if len(sys.argv) > 5 else ours_p

    base, _ = rename(_read(base_p), path)
    theirs, counts = rename(_read(theirs_p), path)

    with tempfile.TemporaryDirectory() as tmp:
        base_t = os.path.join(tmp, "base")
        theirs_t = os.path.join(tmp, "theirs")
        _write(base_t, base)
        _write(theirs_t, theirs)

        map_driver = _map_driver() if path.startswith("Resources/Maps/") else None
        if map_driver:
            # Chain into the map-aware driver if it's configured (%A %O %B).
            cmd = map_driver.replace("%A", shlex.quote(ours_p)) \
                            .replace("%O", shlex.quote(base_t)) \
                            .replace("%B", shlex.quote(theirs_t))
            result = subprocess.run(cmd, shell=True).returncode
        else:
            result = subprocess.run([
                "git", "merge-file", f"--marker-size={marker}",
                "-L", "ours", "-L", "base", "-L", "theirs (Mothlight renames)",
                ours_p, base_t, theirs_t,
            ]).returncode

    if counts:
        status = "clean" if result == 0 else "CONFLICT"
        print(f"mothlight-rename: {path}: renamed upstream {describe(counts)} ({status})", file=sys.stderr)

    return 0 if result == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
