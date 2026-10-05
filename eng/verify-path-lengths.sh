#!/usr/bin/env bash
# Keeps every tracked file path short enough to check out and build on Windows. Build-free:
#
#   eng/verify-path-lengths.sh
#
# Build output no longer counts: Directory.Build.props sends it to artifacts/{bin,obj}/{project}/,
# whose length depends on the project name only. What still has to fit under Windows' 260-character
# MAX_PATH is the source tree itself, measured at the shortest supported clone root below. The
# budget leaves headroom for editor and tool temp files written next to a source file.
#
# Fails when any tracked path, prefixed with the reference root, exceeds the budget.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

reference_root='C:\Github\platform-shared-kernel\'
budget=250

too_long="$(git ls-files | awk -v root_length="${#reference_root}" -v budget="$budget" '
  { length_at_root = root_length + length($0)
    if (length_at_root > budget) printf "  %d  %s\n", length_at_root, $0 }')"

if [[ -n "$too_long" ]]; then
  echo "::error::These paths exceed $budget characters when cloned at $reference_root. Shorten a folder or project name."
  echo "$too_long"
  exit 1
fi

longest="$(git ls-files | awk -v root_length="${#reference_root}" '
  { n = root_length + length($0); if (n > max) { max = n; path = $0 } }
  END { printf "%d (%s)", max, path }')"
echo "OK: every tracked path fits in $budget characters at $reference_root. Longest: $longest."
