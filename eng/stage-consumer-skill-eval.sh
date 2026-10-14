#!/usr/bin/env bash
# Stages a consumer-skill evaluation run (consumer/README.md, "Evaluation"):
#
#   bash eng/stage-consumer-skill-eval.sh [shop-service] <out-dir>      shop-service defaults to Ordering
#
# <out-dir> must be outside the repository and empty or missing. It becomes a standalone service
# workspace that looks like a consuming service, not like this repository:
#
#   <out-dir>/<Service>/ ...                  the Shop service's projects (bin/obj excluded)
#   <out-dir>/Shop.Contracts/, Shop.TestSupport/   the Shop projects it references, if any
#   <out-dir>/packages/                       a local feed with the packed SharedKernel nupkgs
#   <out-dir>/Directory.Build.props|Packages.props, NuGet.Config, global.json, <Service>.slnx
#                                             consumer-shaped build files: one pinned SharedKernelVersion
#   <out-dir>/.claude/skills/sharedkernel/    the packed skill, stamped with that same version
#   <out-dir>/.git                            a baseline commit, so each prompt starts from `git reset --hard`
#
# The scoring sheet is written NEXT TO <out-dir> (<out-dir>-scoring.md), never inside it: it holds
# the expected answers, which the assistant under test must not be able to read.
#
# The SharedKernel packages come from the repository's nupkgs/ feed (samples/Shop/build.sh packs it).
# Repo-only material is removed from the copy: CLAUDE.md, state-map*.md, .claude/ folders, and
# Markdown lines that point into the repository's .claude/.
set -euo pipefail

usage() {
  echo "usage: bash eng/stage-consumer-skill-eval.sh [shop-service] <out-dir>   (shop-service defaults to Ordering)" >&2
  exit 2
}

case $# in
  1) service="Ordering"; out_arg="$1" ;;
  2) service="$1"; out_arg="$2" ;;
  *) usage ;;
esac

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
shop="$repo_root/samples/Shop"
if [ ! -d "$shop/$service" ]; then
  services="$(find "$shop" -mindepth 1 -maxdepth 1 -type d -not -name 'Shop.*' -exec basename {} \; | sort | tr '\n' ' ')"
  echo "::error::No Shop service '$service' (expected $shop/$service). Services: $services" >&2
  exit 1
fi

mkdir -p "$out_arg"
out_dir="$(cd "$out_arg" && pwd)"
case "$out_dir/" in
  "$repo_root"/*)
    echo "::error::$out_dir is inside the repository; stage outside it so the assistant cannot read maintainer files." >&2
    exit 1 ;;
esac
if [ -n "$(ls -A "$out_dir")" ]; then
  echo "::error::$out_dir is not empty." >&2
  exit 1
fi

# Python runs the small XML/zip steps; Windows ships python/python3 stubs that only open the Store.
python_bin=""
for candidate in "${PYTHON:-}" python3 python py; do
  if [ -n "$candidate" ] && "$candidate" -c 'import zipfile' >/dev/null 2>&1; then
    python_bin="$candidate"
    break
  fi
done
if [ -z "$python_bin" ]; then
  echo "::error::No working Python found (set PYTHON)." >&2
  exit 1
fi

# The kernel version: the one the local feed holds (newest if several).
feed="$repo_root/nupkgs"
version="$(find "$feed" -maxdepth 1 -name 'SharedKernel.Primitives.*.nupkg' 2>/dev/null \
  | sed -E 's#.*/SharedKernel\.Primitives\.(.+)\.nupkg$#\1#' | sort -V | tail -1)"
if [ -z "$version" ]; then
  echo "::error::No SharedKernel packages in $feed. Pack them first: bash samples/Shop/build.sh" >&2
  exit 1
fi

copy_project_tree() {
  # $1 = folder under samples/Shop; copies it without build output.
  (cd "$shop" && tar --exclude='bin' --exclude='obj' -cf - "$1") | (cd "$out_dir" && tar -xf -)
}

copy_project_tree "$service"
# Shop projects outside the service that it references (Shop.Contracts, Shop.TestSupport, ...).
# The patterns match literal backslashes in csproj paths (..\..\Shop.Contracts\), not quote escapes.
# shellcheck disable=SC1003
mapfile -t siblings < <(grep -rhoE 'Include="\.\.\\\.\.\\[^\\"]+\\' "$out_dir/$service" --include='*.csproj' \
  | sed -E 's#Include="\.\.\\\.\.\\([^\\"]+)\\#\1#' | sort -u)
for sibling in "${siblings[@]}"; do
  [ -n "$sibling" ] && copy_project_tree "$sibling"
done

# Repo-only material out.
find "$out_dir" \( -name 'CLAUDE.md' -o -name 'state-map*.md' \) -type f -delete
find "$out_dir" -name '.claude' -type d -prune -exec rm -rf {} +
while IFS= read -r -d '' md; do
  sed -i.bak '/\.claude\//d' "$md" && rm -f "$md.bak"
done < <(find "$out_dir" -name '*.md' -type f -print0)
leftovers="$(grep -rlE '\.claude/|CLAUDE\.md|state-map' "$out_dir" 2>/dev/null || true)"
if [ -n "$leftovers" ]; then
  echo "::warning::These staged files still mention repository-only material (left as is, review them):" >&2
  echo "$leftovers" >&2
fi

# Local feed with exactly this version's packages.
mkdir -p "$out_dir/packages"
find "$feed" -maxdepth 1 -name "SharedKernel.*.$version.nupkg" -exec cp {} "$out_dir/packages/" \;

# Consumer-shaped build files.
cp "$repo_root/global.json" "$out_dir/global.json"

cat > "$out_dir/NuGet.Config" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="sharedkernel" value="packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="sharedkernel">
      <package pattern="SharedKernel.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
XML

cat > "$out_dir/Directory.Build.props" <<'XML'
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <IsPackable>false</IsPackable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <!-- A newly disclosed CVE in a transitive dependency is reported, not a build break. -->
    <WarningsNotAsErrors>$(WarningsNotAsErrors);NU1901;NU1902;NU1903;NU1904</WarningsNotAsErrors>
    <IsTestProject Condition="$(MSBuildProjectName.EndsWith('.Tests'))">true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="SharedKernel.Analyzers" PrivateAssets="all" />
    <PackageReference Include="SharedKernel.Linter" PrivateAssets="all" />
  </ItemGroup>
</Project>
XML

# Directory.Packages.props: one pinned SharedKernelVersion, plus the repository's version of every
# third-party package the staged projects reference.
"$python_bin" - "$repo_root/Directory.Packages.props" "$out_dir" "$version" <<'PY'
import pathlib, re, sys
root_props, out_dir, version = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]), sys.argv[3]
versions = dict(re.findall(r'<PackageVersion\s+Include="([^"]+)"\s+Version="([^"]+)"', root_props.read_text(encoding="utf-8")))
names = set()
for path in [*out_dir.rglob("*.csproj"), out_dir / "Directory.Build.props"]:
    names.update(re.findall(r'<PackageReference\s+Include="([^"]+)"', path.read_text(encoding="utf-8-sig")))
missing = sorted(n for n in names if not n.startswith("SharedKernel.") and n not in versions)
if missing:
    sys.exit("::error::No version in the repository's Directory.Packages.props for: " + ", ".join(missing))
lines = [
    "<Project>",
    "  <PropertyGroup>",
    "    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>",
    f"    <SharedKernelVersion>{version}</SharedKernelVersion>",
    "  </PropertyGroup>",
    "  <ItemGroup>",
]
for name in sorted(names, key=str.lower):
    value = "$(SharedKernelVersion)" if name.startswith("SharedKernel.") else versions[name]
    lines.append(f'    <PackageVersion Include="{name}" Version="{value}" />')
lines += ["  </ItemGroup>", "</Project>", ""]
(out_dir / "Directory.Packages.props").write_text("\n".join(lines), encoding="utf-8")
PY

# A solution over every staged project.
"$python_bin" - "$out_dir" "$service" <<'PY'
import pathlib, sys
out_dir, service = pathlib.Path(sys.argv[1]), sys.argv[2]
projects = sorted(p.relative_to(out_dir).as_posix() for p in out_dir.rglob("*.csproj"))
body = "\n".join(f'  <Project Path="{p}" />' for p in projects)
(out_dir / f"{service}.slnx").write_text(f"<Solution>\n{body}\n</Solution>\n", encoding="utf-8")
PY

# The skill, packed at the same version and unzipped where Claude Code looks for project skills.
skill_tmp="$(mktemp -d)"
trap 'rm -rf "$skill_tmp"' EXIT
PYTHON="$python_bin" bash "$repo_root/eng/pack-consumer-skill.sh" "$version" "$skill_tmp" >/dev/null
mkdir -p "$out_dir/.claude/skills"
"$python_bin" -m zipfile -e "$skill_tmp/sharedkernel-skill-v$version.zip" "$out_dir/.claude/skills/"

# The scoring sheet, outside the workspace: the task table plus empty result columns.
sheet="${out_dir%/}-scoring.md"
"$python_bin" - "$repo_root/consumer/eval/tasks.md" "$sheet" "$service" "$version" "$out_dir" <<'PY'
import pathlib, sys
tasks, sheet, service, version, out_dir = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]), *sys.argv[3:]
extra = ["Picked packages", "Projects", "Recipe opened", "Build clean", "Notes"]
out, in_table = [], False
for line in tasks.read_text(encoding="utf-8").splitlines():
    if line.startswith("## Runs"):
        break
    if line.startswith("| # |"):
        in_table = True
        line = line + " " + " | ".join(extra) + " |"
    elif in_table and line.startswith("| ---"):
        line = line + " --- |" * len(extra)
    elif in_table and line.startswith("|"):
        line = line + "  |" * len(extra)
    else:
        in_table = False
    out.append(line)
header = [
    f"<!-- Scoring sheet: {service} at SharedKernel {version}, workspace {out_dir}. -->",
    "<!-- Keep this file outside the workspace: it holds the expected answers. -->",
    "",
]
sheet.write_text("\n".join(header + out) + "\n", encoding="utf-8")
PY

# Baseline commit: score each prompt with `git diff`, reset with `git reset --hard && git clean -fdx`.
if command -v git >/dev/null 2>&1; then
  printf 'bin/\nobj/\n' > "$out_dir/.gitignore"
  git -C "$out_dir" init -q
  git -C "$out_dir" config core.autocrlf false
  git -C "$out_dir" add -A
  git -C "$out_dir" -c user.name=eval -c user.email=eval@localhost commit -qm "Baseline: $service on SharedKernel $version"
fi

echo "OK: staged $service at SharedKernel $version"
echo "  workspace:     $out_dir"
echo "  skill:         $out_dir/.claude/skills/sharedkernel/ ($(find "$out_dir/.claude/skills/sharedkernel" -name '*.md' | wc -l | tr -d ' ') files)"
echo "  scoring sheet: $sheet"
echo "  baseline:      cd \"$out_dir\" && dotnet build $service.slnx"
