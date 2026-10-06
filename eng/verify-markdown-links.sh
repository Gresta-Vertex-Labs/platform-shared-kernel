#!/usr/bin/env bash
# Fails when a relative link in a tracked Markdown file points at a file or folder that does not
# exist. Build-free:
#
#   eng/verify-markdown-links.sh
#
# Only relative links are checked (http/https/mailto and in-page #anchors are skipped); an anchor
# or query on a relative link is ignored and the target path alone must exist. Placeholder links
# containing "{" (templates such as src/{Zone}/{Capability}/README.md) are skipped, and so is
# .claude/, whose agent files show link syntax as examples.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

broken="$(
  git ls-files '*.md' | grep -vE '^(\.claude|archive|graphify-out)/' | while IFS= read -r file; do
    dir="$(dirname "$file")"
    perl -ne 'while (/\]\(([^)\s]+)\)/g) {
                my $link = $1;
                next if $link =~ m{^(?:https?:|mailto:|#)} || $link =~ /\{/;
                $link =~ s/[#?].*//;
                print "$link\n" if length $link;
              }' "$file" | sort -u | while IFS= read -r link; do
      [ -e "$dir/$link" ] || echo "  $file -> $link"
    done
  done
)"

if [ -n "$broken" ]; then
  echo "::error::Relative Markdown links that point at nothing:"
  echo "$broken"
  exit 1
fi
echo "OK: every relative link in the tracked Markdown files resolves."
