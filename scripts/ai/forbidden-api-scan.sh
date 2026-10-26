#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
domain_dir="$root/src/Game.Domain"

if [[ ! -d "$domain_dir" ]]; then
  echo "missing domain source directory: src/Game.Domain" >&2
  exit 1
fi

patterns=(
  'DateTime.Now'
  'DateTime.UtcNow'
  'DateTimeOffset.Now'
  'DateTimeOffset.UtcNow'
  'Guid.NewGuid'
  'Random.Shared'
  'new Random('
  'System.IO.File'
  'System.IO.Directory'
  'File.'
  'Directory.'
  'HttpClient'
  'System.Net.Http'
)

status=0

while IFS= read -r -d '' file; do
  for pattern in "${patterns[@]}"; do
    if grep -nF "$pattern" "$file" >/tmp/forbidden-api-match.$$ 2>/dev/null; then
      while IFS= read -r match; do
        printf '%s: forbidden pattern "%s": %s\n' "${file#$root/}" "$pattern" "$match" >&2
      done </tmp/forbidden-api-match.$$
      status=1
    fi
  done
done < <(find "$domain_dir" -type f -name '*.cs' \
  -not -path '*/bin/*' \
  -not -path '*/obj/*' \
  -print0)

rm -f /tmp/forbidden-api-match.$$

if [[ "$status" -ne 0 ]]; then
  exit "$status"
fi

echo "Forbidden API scan passed."
