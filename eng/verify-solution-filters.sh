#!/usr/bin/env bash
# Checks Platform.SharedKernel.slnx against its two test lanes (WO-086 / P-572). Build-free, so it
# runs first in CI and locally in a second:
#
#   eng/verify-solution-filters.sh
#
# Fails when:
#   - a test project (*.Tests) is in neither lane, so its tests never run, or in both,
#     so they run twice;
#   - a production project in the solution is missing from the Unit lane;
#   - a lane lists a project the solution does not contain;
#   - a solution folder is not a capability folder on disk, or holds a project that lives elsewhere.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

slnx=Platform.SharedKernel.slnx
unit=Platform.SharedKernel.Unit.slnf
integration=Platform.SharedKernel.Integration.slnf

projects() { grep -oE "$1" "$2" | sed -E 's/^(Path=)?"//; s/"$//' | tr '\134' '/' | sort; }
projects 'Path="[^"]*\.csproj"' "$slnx" > "$work/solution.txt"
projects '"[^"]*\.csproj"' "$unit" > "$work/unit.txt"
projects '"[^"]*\.csproj"' "$integration" > "$work/integration.txt"

grep -E '\.Tests\.csproj$' "$work/solution.txt" > "$work/tests.txt" || true
grep -vE '\.Tests\.csproj$' "$work/solution.txt" > "$work/production.txt" || true
sort -u "$work/unit.txt" "$work/integration.txt" > "$work/either.txt"
comm -12 "$work/unit.txt" "$work/integration.txt" > "$work/both.txt"

failed=0
report() {
  echo "::error::$1"
  sed 's/^/  /' "$2"
  failed=1
}

comm -23 "$work/tests.txt" "$work/either.txt" > "$work/no-lane.txt"
[ -s "$work/no-lane.txt" ] && report "Test project(s) in $slnx but in neither $unit nor $integration (their tests never run; add each to the lane matching whether it needs Docker):" "$work/no-lane.txt"
[ -s "$work/both.txt" ] && report "Project(s) listed in both $unit and $integration (a test project belongs to exactly one lane):" "$work/both.txt"
comm -23 "$work/production.txt" "$work/unit.txt" > "$work/prod-missing.txt"
[ -s "$work/prod-missing.txt" ] && report "Production project(s) in $slnx missing from $unit:" "$work/prod-missing.txt"
comm -13 "$work/solution.txt" "$work/either.txt" > "$work/unknown.txt"
[ -s "$work/unknown.txt" ] && report "Project(s) listed in a solution filter but not in $slnx:" "$work/unknown.txt"
for lane in unit integration; do
  uniq -d "$work/$lane.txt" > "$work/$lane-dup.txt"
  [ -s "$work/$lane-dup.txt" ] && report "Project(s) listed twice in the $lane lane:" "$work/$lane-dup.txt"
done

# A solution folder is a capability folder on disk (e.g. "/src/Infrastructure/Caching/", the folder
# holding that capability's CLAUDE.md), and every project inside it lives under that folder.
awk -F'"' '/<Folder Name=/ { folder = $2; sub("^/", "", folder); sub("/$", "", folder); print "FOLDER " folder }
           /<Project Path=/ { path = $2; gsub("\\\\", "/", path);
                              if (index(path, folder "/") != 1) print "MISPLACED " path " is in solution folder /" folder "/" }' \
  "$slnx" > "$work/folders.txt"
grep '^MISPLACED ' "$work/folders.txt" | sed 's/^MISPLACED //' > "$work/misplaced.txt" || true
[ -s "$work/misplaced.txt" ] && report "Project(s) in the wrong solution folder:" "$work/misplaced.txt"
grep '^FOLDER ' "$work/folders.txt" | sed 's/^FOLDER //' | while read -r folder; do
  [ -f "$folder/CLAUDE.md" ] || echo "/$folder/"
done > "$work/not-capability.txt"
[ -s "$work/not-capability.txt" ] && report "Solution folder(s) that are not a capability folder (no CLAUDE.md on disk):" "$work/not-capability.txt"

if [ "$failed" -ne 0 ]; then
  exit 1
fi
echo "OK: $(wc -l < "$work/tests.txt") test projects in exactly one lane ($(wc -l < "$work/unit.txt") unit-lane entries, $(wc -l < "$work/integration.txt") integration), all $(wc -l < "$work/production.txt") production projects in $unit, every project in its folder."
