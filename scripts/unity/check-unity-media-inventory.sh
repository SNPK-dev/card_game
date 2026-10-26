#!/usr/bin/env bash
set -euo pipefail

project_root="clients/unity/CardGame.Unity"
inventory_doc="docs/unity/client-media-inventory.md"
policy_doc="docs/unity/client-media-license-policy.md"

echo "Unity media inventory gate"

if [[ ! -f "$inventory_doc" ]]; then
  echo "FAILED: missing media inventory doc: $inventory_doc"
  exit 1
fi

if [[ ! -f "$policy_doc" ]]; then
  echo "FAILED: missing media license policy doc: $policy_doc"
  exit 1
fi

for required in repo-generated unity-native not-applicable placeholder; do
  if ! grep -q "$required" "$inventory_doc" "$policy_doc"; then
    echo "FAILED: media docs do not mention required source/status token: $required"
    exit 1
  fi
done

allowed_prefixes=(
  "$project_root/Assets/CardGame/Art/Generated/"
  "$project_root/Assets/CardGame/Art/UI/"
  "$project_root/Assets/CardGame/Art/Cards/"
  "$project_root/Assets/CardGame/Audio/Generated/"
  "$project_root/Assets/CardGame/Audio/UI/"
  "$project_root/Assets/CardGame/Audio/Game/"
  "$project_root/Assets/CardGame/Resources/AppShell/"
  "$project_root/Assets/CardGame/Resources/Audio/Presentation/"
  "$project_root/Assets/CardGame/Resources/CardArt/"
  "$project_root/Assets/CardGame/Resources/CardFrames/"
  "$project_root/Assets/CardGame/Resources/HeroArt/"
  "$project_root/Assets/CardGame/Resources/PresentationVfx/"
  "$project_root/Assets/CardGame/Resources/ThemeBoards/"
)

# Full-screen boards, shell backgrounds, portrait/card art, and authored frame
# sources are documented 1024px presentation textures. Unity applies the
# platform import settings in the build; keep the checked-in source below 3 MB.
large_texture_prefixes=(
  "$project_root/Assets/CardGame/Resources/AppShell/"
  "$project_root/Assets/CardGame/Resources/CardArt/"
  "$project_root/Assets/CardGame/Resources/CardFrames/"
  "$project_root/Assets/CardGame/Resources/HeroArt/"
  "$project_root/Assets/CardGame/Resources/PresentationVfx/"
  "$project_root/Assets/CardGame/Resources/ThemeBoards/"
)

media_patterns=(-iname '*.png' -o -iname '*.jpg' -o -iname '*.jpeg' -o -iname '*.wav' -o -iname '*.mp3' -o -iname '*.ogg' -o -iname '*.aiff' -o -iname '*.tga' -o -iname '*.psd')
media_files=()
while IFS= read -r media_file; do
  media_files+=("$media_file")
done < <(find "$project_root/Assets" -type f \( "${media_patterns[@]}" \) | sort)

for file in "${media_files[@]}"; do
  allowed=false
  for prefix in "${allowed_prefixes[@]}"; do
    if [[ "$file" == "$prefix"* ]]; then
      allowed=true
      break
    fi
  done

  if [[ "$allowed" != true ]]; then
    echo "FAILED: media file outside approved directories: $file"
    exit 1
  fi

  size="$(wc -c < "$file" | tr -d ' ')"
  if [[ "$size" -gt 1048576 ]]; then
    documented_large_texture=false
    for prefix in "${large_texture_prefixes[@]}"; do
      if [[ "$file" == "$prefix"* ]]; then
        documented_large_texture=true
        break
      fi
    done
    if [[ "$documented_large_texture" != true || "$size" -gt 3145728 ]]; then
      echo "FAILED: large media file requires explicit rationale: $file ($size bytes)"
      exit 1
    fi
  fi
done

if git diff --cached --name-only | grep -E '^(artifacts/|clients/unity/CardGame.Unity/(Library|Temp|Logs|Build|Builds)/)' >/dev/null; then
  echo "FAILED: staged build/cache artifact detected."
  exit 1
fi

echo "Media files checked: ${#media_files[@]}"
echo "Unity media inventory gate passed."
