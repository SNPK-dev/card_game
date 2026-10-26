#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
source_file="$repo_root/src/Game.UnityClient.Contracts/UnityApiDtos.cs"
target_file="$repo_root/clients/unity/CardGame.Unity/Assets/CardGame/Runtime/Contracts/UnityApiDtos.cs"

if [[ ! -f "$source_file" ]]; then
  echo "Unity DTO source file not found: $source_file" >&2
  exit 1
fi

if [[ ! -f "$target_file" ]]; then
  echo "Unity DTO mirror file not found: $target_file" >&2
  exit 1
fi

tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT
expected="$tmp_dir/UnityApiDtos.cs"

python_command="${PYTHON:-}"
if [[ -z "$python_command" ]]; then
  for candidate in python py python3; do
    if command -v "$candidate" >/dev/null 2>&1 && "$candidate" --version >/dev/null 2>&1; then
      python_command="$candidate"
      break
    fi
  done
fi
if [[ -z "$python_command" ]]; then
  echo "A working Python interpreter was not found. Set PYTHON and rerun." >&2
  exit 127
fi

"$python_command" - "$source_file" "$expected" <<'PY'
from pathlib import Path
import re
import sys

source = Path(sys.argv[1])
target = Path(sys.argv[2])

text = source.read_text(encoding="utf-8")
text = re.sub(r"using System\.Text\.Json;\r?\n", "", text)
text = re.sub(r"using System\.Text\.Json\.Serialization;\r?\n", "", text)
text = re.sub(r"\s*\[JsonExtensionData\] public Dictionary<string, JsonElement>\? ExtensionData \{ get; set; \}", "", text)
text = text.replace("namespace Game.UnityClient.Contracts;", "namespace Game.UnityClient.Contracts\n{", 1)
text = text.rstrip() + "\n}\n"
target.write_text(text, encoding="utf-8", newline="\n")
PY

if ! diff -u "$expected" "$target_file"; then
  echo "Unity DTO mirror is stale. Run scripts/unity/sync-unity-dtos.sh from the repository root." >&2
  exit 1
fi

echo "Unity DTO mirror is current."
