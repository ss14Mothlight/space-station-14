#!/usr/bin/env bash
# Mothlight: registers the local merge driver referenced by .gitattributes and a pre-commit
# hook that applies our upstream renames (Tools/_Mothlight/upstream_renames.py) to lines a
# merge brings in. Run once per clone.
set -e
cd "$(git rev-parse --show-toplevel)"

git config merge.mothlight-rename.name "Mothlight upstream-rename-aware merge"
git config merge.mothlight-rename.driver "python3 Tools/_Mothlight/rename_merge_driver.py %O %A %B %L %P"

hook="$(git rev-parse --git-path hooks)/pre-commit"
marker="# mothlight-rename-fix"
if ! grep -qs "$marker" "$hook"; then
    [ -f "$hook" ] || printf '#!/usr/bin/env bash\n' > "$hook"
    cat >> "$hook" <<'HOOK'

# mothlight-rename-fix
if [ -f "$(git rev-parse --git-path MERGE_HEAD)" ]; then
    python3 "$(git rev-parse --show-toplevel)/Tools/_Mothlight/rename_fix_merge.py" || exit 1
fi
HOOK
    chmod +x "$hook"
fi

echo "Registered merge.mothlight-rename driver and pre-commit hook."
