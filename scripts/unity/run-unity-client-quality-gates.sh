#!/usr/bin/env bash
set -euo pipefail

echo "Unity client quality gates"

required_docs=(
  docs/unity/client-performance-baseline.md
  docs/unity/client-readability-accessibility-baseline.md
  docs/unity/client-authority-and-hidden-info-final-gate.md
  docs/unity/client-production-readiness-audit.md
)

for doc in "${required_docs[@]}"; do
  if [[ ! -f "$doc" ]]; then
    echo "FAILED: missing quality gate doc: $doc"
    exit 1
  fi
done

scene="clients/unity/CardGame.Unity/Assets/Scenes/CardGameLocalMatch.unity"
for token in CardGameClientRoot Background Header OpponentPanel OpponentHand Board PlayerPanel PlayerHand InteractionPanel PresentationOverlay ChoiceModal TargetOverlay EventFeed DiagnosticsPanel AudioControls; do
  if ! grep -q "$token" "$scene"; then
    echo "FAILED: scene is missing required UI token: $token"
    exit 1
  fi
done

for token in "1920x1080" "1366x768" "1280x720" "mute" "master volume" "event feed bounded" "presentation queue bounded" "Diagnostics"; do
  if ! grep -Riq "$token" docs/unity/client-performance-baseline.md docs/unity/client-readability-accessibility-baseline.md docs/unity/client-production-readiness-audit.md; then
    echo "FAILED: quality docs do not cover token: $token"
    exit 1
  fi
done

if ! grep -REq "MaxEventFeedLines|MaxFeedLines|EventFeed" clients/unity/CardGame.Unity/Assets/CardGame/Runtime; then
  echo "FAILED: event feed bound was not detectable in runtime code."
  exit 1
fi

if ! grep -REq "PresentationPlaybackRunner|MaxQueue|SkippedAnimations|PresentationIntentsPlayed" clients/unity/CardGame.Unity/Assets/CardGame/Runtime; then
  echo "FAILED: presentation queue/playback bound evidence was not detectable."
  exit 1
fi

if ! grep -REq "Mute|MasterVolume|AudioVolumeSettings" clients/unity/CardGame.Unity/Assets/CardGame/Runtime; then
  echo "FAILED: audio mute/master volume controls were not detectable."
  exit 1
fi

echo "Unity client quality gates passed."
