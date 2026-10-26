# asset_generation_spec.md
## Asset Generation Specification
### Draft 1 — Visual, Animation and Audio Assets

---

## 0. Назначение

Документ описывает asset pipeline для карт: изображения, idle-анимации и аудио.

Главный принцип:

```text
Assets are presentation layer.
Gameplay engine does not depend on asset loading, playback or generation.
```

Генератор может создавать asset prompts и asset manifests, но игровой движок исполняет только DSL-механику карты.

---

## 1. Scope

Asset pipeline покрывает:

```text
- placeholder image;
- static card image;
- idle animation / GIF / WebP / video preview;
- sound effects;
- voice lines;
- inspect/hover audio;
- event-triggered audio cues;
- asset moderation;
- asset fallback;
- asset versioning;
- presentation replay support.
```

Asset pipeline не покрывает:

```text
- gameplay legality;
- target selection;
- trigger resolution;
- damage/heal/draw/summon logic;
- state mutations;
- balance validation mechanics, кроме presentation-cost метрик;
- LLM/audio generation во время матча.
```

---

## 2. Asset modes

Визуальные режимы:

```text
VisualAssetMode:
  NONE
  STATIC_IMAGE
  IDLE_ANIMATION
```

Аудио-режимы:

```text
AudioAssetMode:
  NONE
  SFX_ONLY
  VOICE_LINE_ONLY
  SFX_AND_VOICE
  FULL_AUDIO_SET
```

Полная asset policy карты задается отдельно:

```text
CardAssetPolicy:
  visualAssetMode
  audioAssetMode
  requireModeration
  allowFallback
  maxAssetCostTier
```

---

## 3. AssetManifest

```text
AssetManifest:
  assetManifestId
  assetManifestVersion
  cardId
  cardVersion
  visualManifest | null
  animationManifest | null
  audioManifest | null
  fallbackPolicy
  moderationStatus
  createdAt
  contentHash
```

Правило:

```text
CardVersion describes gameplay.
AssetManifestVersion describes presentation.
```

Если меняется только audio/image, обычно меняется `AssetManifestVersion`, а не `CardVersion`.

---

## 4. AudioAssetManifest

```text
AudioAssetManifest:
  audioManifestId
  audioManifestVersion
  cardId
  cardVersion
  audioAssetMode
  cues[]
  fallbackCueRefs[]
  localizationPolicy
  volumeGroup
  moderationStatus
  contentHash
```

---

## 5. AudioCueDefinition

```text
AudioCueDefinition:
  cueId
  cueType
  assetRef
  durationMs
  language | null
  subtitleKey | null
  volumeGroup
  playbackPolicy
  cooldownPolicy
  fallbackCueId | null
  contentHash
```

Минимальные cue types:

```text
ON_PLAY
ON_SUMMON
ON_ATTACK
ON_DEATH
ON_TRIGGER
ON_SECRET_REVEAL
ON_QUEST_COMPLETE
ON_HOVER
ON_INSPECT
ON_CRAFTED
```

---

## 6. Playback policy

```text
PlaybackPolicy:
  playOnce
  interruptPolicy
  overlapPolicy
  priority
  maxConcurrentInstances
  clientMaySkip
```

Аудио не должно блокировать gameplay.

Если cue не может быть воспроизведен:

```text
- gameplay продолжается;
- client использует fallback или silence;
- server state не меняется;
- replay gameplay не ломается.
```

---

## 7. Audio generation pipeline

```text
CardGenerationIntent
↓
AssetRequest
↓
AudioPromptGeneration
↓
AudioGenerationJob
↓
Moderation
↓
AudioAssetManifest
↓
AssetManifest
↓
Catalog publication
```

Важно:

```text
Audio generation runs outside match.
No LLM/audio generation during match.
No external asset call during gameplay resolution.
```

---

## 8. Structured Outputs integration

Генератор карты может вернуть asset request:

```text
AssetRequest:
  visualAssetMode
  audioAssetMode
  visualPrompt | null
  animationPrompt | null
  audioPrompt | null
  requestedCueTypes[]
  styleGuideRef
  safetyPolicyRef
```

Но asset request не является опубликованным asset.

Публикуется только:

```text
AssetManifest
```

---

## 9. Engine integration

Engine sees:

```text
assetManifestId
assetManifestVersion
presentationCueRefs
```

Engine does not see:

```text
raw audio bytes
audio generation prompt
provider-specific asset IDs
client playback state
volume/mute state
loaded audio buffers
```

Gameplay events can be mapped to audio cues:

```text
CommittedEvent
↓
PresentationCueResolver
↓
AudioCueEvent
↓
Client playback
```

AudioCueEvent is not a gameplay event.

---

## 10. Hashing and replay

Gameplay replay:

```text
seed + command log + catalog snapshot + ruleset profile = gameplay replay
```

Audio does not affect gameplay replay.

Optional presentation replay:

```text
DerivedEventLog
+ AssetManifestVersion
+ PresentationCatalogHash
= presentation timeline
```

Hash split:

```text
GameplayCatalogHash:
  excludes audio/visual content.

PresentationCatalogHash:
  includes audio/visual asset refs and content hashes.
```

---

## 11. Validation

Asset validation checks:

```text
- required cues exist for requested audio mode;
- asset refs are valid;
- content hashes present;
- moderation passed;
- duration within limits;
- file format allowed;
- fallback exists when required;
- localization rules satisfied;
- no gameplay dependency on audio.
```

---

## 12. TDD tests

```text
[ ] card with AudioAssetMode.NONE publishes without audio.
[ ] card with SFX_ONLY requires at least one SFX cue.
[ ] card with VOICE_LINE_ONLY requires voice cue or fallback.
[ ] missing audio asset falls back to silence/fallback.
[ ] audio cue does not affect GameState.
[ ] audio cue does not affect gameplay state hash.
[ ] changing audio changes PresentationCatalogHash only.
[ ] no audio generation call occurs during match.
[ ] AudioCueEvent cannot trigger gameplay effect.
```

---

## 13. MVP scope

MVP:

```text
- AudioAssetMode.NONE;
- optional SFX_ONLY;
- AssetManifestVersion;
- fallback to silence;
- AudioCueEvent as presentation-only event;
- gameplay hash excludes audio.
```

Post-MVP:

```text
- voice lines;
- localized audio;
- full audio sets;
- dynamic volume groups;
- presentation replay timeline;
- generated voice packs;
- advanced moderation workflow.
```

---

## 14. Definition of Done

```text
[ ] AssetManifest model exists.
[ ] AudioAssetManifest model exists.
[ ] AudioCueDefinition model exists.
[ ] AudioAssetMode supported by generator context.
[ ] Audio cues are presentation-only.
[ ] Engine never depends on audio playback.
[ ] Canonical serialization excludes audio playback from gameplay hash.
[ ] PresentationCatalogHash includes audio content hashes.
[ ] Tests prove audio cannot affect gameplay.
```
