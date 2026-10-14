#!/usr/bin/env bash
# Packs the consumer skill (consumer/skill/sharedkernel/) as the release asset a service syncs into
# .claude/skills/sharedkernel/:
#
#   bash eng/pack-consumer-skill.sh <version> <output-directory>
#
# Writes <output-directory>/sharedkernel-skill-v<version>.zip with `sharedkernel/` at the archive root,
# {{SharedKernelVersion}} replaced by <version>, and the maintainer-only recipe frontmatter (proven-by,
# unproven: repo paths that mean nothing inside a service) removed. The asset name, the archive layout
# and the stamped version are the contract with consuming services (consumer/README.md, "How it ships").
set -euo pipefail

if [ $# -ne 2 ]; then
  echo "usage: bash eng/pack-consumer-skill.sh <version> <output-directory>" >&2
  exit 2
fi

version="${1#v}"
out_dir="$2"
if ! printf '%s' "$version" | grep -qE '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$'; then
  echo "::error::'$version' is not a SemVer version." >&2
  exit 1
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_dir="$repo_root/consumer/skill/sharedkernel"
if [ ! -f "$source_dir/SKILL.md" ]; then
  echo "::error::$source_dir/SKILL.md is missing." >&2
  exit 1
fi

staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT
cp -R "$source_dir" "$staging/sharedkernel"

# Stamp the version everywhere; strip CR so the asset is LF-only whatever the checkout's line endings.
while IFS= read -r -d '' file; do
  sed -i.bak -e 's/\r$//' -e "s/{{SharedKernelVersion}}/$version/g" "$file"
  rm -f "$file.bak"
done < <(find "$staging/sharedkernel" -type f -name '*.md' -print0)

# Recipes only: SKILL.md's own frontmatter (name, description) is what Claude Code reads.
if [ -d "$staging/sharedkernel/recipes" ]; then
  while IFS= read -r -d '' recipe; do
    awk 'NR == 1 && $0 == "---" { skipping = 1; next } skipping && $0 == "---" { skipping = 0; dropped = 1; next } !skipping { if (dropped && !started && $0 == "") next; started = 1; print }' \
      "$recipe" > "$recipe.tmp"
    mv "$recipe.tmp" "$recipe"
  done < <(find "$staging/sharedkernel/recipes" -type f -name '*.md' -print0)
fi

if grep -rn '{{' "$staging/sharedkernel"; then
  echo "::error::Unreplaced placeholders remain in the skill (above)." >&2
  exit 1
fi

mkdir -p "$out_dir"
out_dir="$(cd "$out_dir" && pwd)"
asset="$out_dir/sharedkernel-skill-v$version.zip"
rm -f "$asset"
if command -v zip >/dev/null 2>&1; then
  (cd "$staging" && zip -qrX "$asset" sharedkernel)
else
  # Windows ships python/python3 stubs that only open the Store; take the first interpreter that runs.
  python_bin=""
  for candidate in "${PYTHON:-}" python3 python py; do
    if [ -n "$candidate" ] && "$candidate" -c 'import zipfile' >/dev/null 2>&1; then
      python_bin="$candidate"
      break
    fi
  done
  if [ -z "$python_bin" ]; then
    echo "::error::Neither zip nor python is available to build the archive." >&2
    exit 1
  fi
  (cd "$staging" && "$python_bin" -m zipfile -c "$asset" sharedkernel)
fi

echo "OK: $asset ($(find "$staging/sharedkernel" -type f | wc -l | tr -d ' ') files, version $version)."
if [ -n "${GITHUB_OUTPUT:-}" ]; then
  echo "asset=$asset" >> "$GITHUB_OUTPUT"
fi
