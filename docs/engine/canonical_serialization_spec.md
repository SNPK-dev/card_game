# canonical_serialization_spec.md
## Canonical Serialization Specification
### Draft 2 — Audio / Presentation Hashing

---

## 0. Назначение

Canonical serialization нужна для deterministic replay, state hashing, golden tests и compatibility checks.

Главный принцип:

```text
Same gameplay state must always produce same canonical bytes.
Same canonical bytes must always produce same state hash.
```

Нельзя полагаться на обычную JSON-сериализацию без строгих правил порядка, null handling, number formatting и versioning.

---

## 1. Scope

Canonical serialization используется для:

```text
- GameState hashing;
- command payload hashing;
- replay verification;
- CatalogSnapshot hashing;
- RulesetProfile resolvedHash;
- EngineCapabilityRegistry hash;
- optional PresentationCatalogHash;
- golden tests;
- deterministic diffing.
```

Не используется для:

```text
- player-facing API DTO;
- debug pretty print;
- logs for humans;
- database persistence format, если он не заявлен canonical.
```

---

## 2. Canonical serialization invariants

```text
- deterministic field order;
- deterministic collection order;
- explicit null handling;
- stable enum representation;
- stable number representation;
- stable string encoding;
- no transient fields;
- no infrastructure metadata;
- no transport envelope metadata;
- versioned format;
- cross-platform stable output.
```

---

## 3. Canonical format

MVP recommended canonical format:

```text
Canonical JSON UTF-8
```

Rules:

```text
- UTF-8 without BOM;
- object fields sorted by canonical field order;
- arrays ordered by canonical domain order;
- enums serialized as uppercase string IDs;
- booleans as true/false;
- integers as base-10 without leading zeros;
- decimals avoided in core gameplay if possible;
- null included only where field is part of canonical model;
- no whitespace;
- no comments;
- no trailing commas.
```

Later optimization may introduce binary canonical format, but only after replay compatibility review.

---

## 4. Field order

Field order must be explicit.

Options:

```text
1. schema-defined field order;
2. attribute-defined field order;
3. generated canonical serializer order.
```

Alphabetical order is acceptable only if fixed by spec and tested.

Preferred:

```text
schema-defined field order
```

Example:

```text
GameState canonical order:
  gameId
  engineVersion
  engineCapabilityRegistryVersion
  rulesetProfileId
  resolvedRulesetProfileHash
  catalogSnapshotId
  primitiveRegistryVersion
  seed
  rngState
  rngStreams
  activePlayerId
  turnNumber
  phase
  players
  eventQueue
  historyStore
  modifierState
  auraState
  pendingChoices
  terminalState
```

Excluded from state hash unless explicitly configured:

```text
derivedEventLog
canonicalCommandLog
diagnostic logs
transport metadata
client timestamps
receivedAt
correlationId
```

---

## 5. Collection ordering

All collections must have canonical ordering.

### 5.1 Lists

Lists preserve domain order.

Examples:

```text
deck:
  order is gameplay-significant.

hand:
  order is gameplay-significant.

board:
  order is gameplay-significant.

eventQueue:
  queue order is gameplay-significant.
```

### 5.2 Sets

Sets must be serialized as sorted arrays.

Sort order:

```text
1. canonical ID;
2. enum ordinal only if frozen by spec;
3. string ordinal UTF-8 comparison.
```

### 5.3 Dictionaries / maps

Maps serialized as arrays of key-value entries sorted by canonical key.

```text
[
  { "key": "...", "value": ... },
  { "key": "...", "value": ... }
]
```

Never rely on runtime dictionary order.

---

## 6. Stable IDs

IDs must be canonical strings.

Examples:

```text
gameId
playerId
cardId
cardVersion
cardInstanceId
entityInstanceId
effectId
actionId
mutationId
eventId
choiceId
```

For deterministic runtime-created IDs, use deterministic ID provider.

Forbidden in Domain:

```text
Guid.NewGuid()
random UUID
timestamp-based ID
database auto-increment ID for gameplay object
```

Allowed:

```text
DeterministicIdProvider:
  nextId(scope, commandSeq, eventSeq, localCounter)
```

---

## 7. Number formatting

### 7.1 Integers

```text
- base-10;
- no leading zeros;
- negative sign only for negative values;
- no plus sign.
```

### 7.2 Floating point

Avoid floating point in gameplay state.

If unavoidable:

```text
- use fixed decimal type;
- define scale;
- serialize as string or scaled integer;
- never serialize binary floating point directly.
```

Preferred:

```text
scaled integer
```

Example:

```text
winrateDeltaBasisPoints: 350
```

instead of:

```text
0.035
```

---

## 8. String normalization

All strings included in canonical state must be normalized.

```text
Unicode normalization: NFC
Encoding: UTF-8
Case: preserve exact case unless field-specific rule says otherwise
Line endings: LF
```

Player-facing text should generally not be part of gameplay state hash unless it affects gameplay.

---

## 9. Enum representation

Enums serialized as stable string IDs.

Example:

```json
"phase":"TURN_END"
```

Not allowed:

```json
"phase":17
```

Reason:

```text
numeric enum order may change between versions.
```

Enum string IDs are part of compatibility contract.

---

## 10. Null handling

Canonical null rules:

```text
- include null if field is required by canonical schema;
- omit field only if schema says field is non-canonical or absent by version;
- do not alternate between null and missing;
- nullable fields must have explicit semantics.
```

Example:

```json
"winnerPlayerId":null
```

is valid if `winnerPlayerId` is part of `GameEndInfo`.

---

## 11. Transient field exclusion

Must be excluded from canonical gameplay state hash:

```text
- receivedAt;
- clientTimestamp;
- correlationId;
- authSessionId;
- transportMetadata;
- rawRequestRef;
- diagnostic log messages;
- pretty printed text;
- OpenTelemetry span IDs;
- database row version;
- cache metadata;
- UI animation state;
- asset loading state;
- audio playback state;
- loaded audio buffers;
- client volume;
- animation frame;
- GIF playback position;
- hover/inspect audio cues.
```

---

## 11.1 Asset and audio canonical rules

Gameplay canonical state must not depend on presentation assets.

Excluded from gameplay state hash:

```text
visual asset loading state
animation playback state
audio playback state
loaded audio buffer state
client volume/mute state
CDN URL resolution
hover/inspect cue state
missing asset fallback state
```

Card mechanics and asset presentation must be hashable separately.

Recommended split:

```text
GameplayCatalogHash
  includes card mechanics, DSL, ruleset compatibility, primitive versions.
  excludes visual/audio binary content and CDN URLs.

PresentationCatalogHash
  includes AssetManifest refs, VisualAssetManifest refs, AudioAssetManifest refs, content hashes, localization refs.
  does not affect gameplay replay.
```

If only audio is changed:

```text
Gameplay replay must remain valid.
Presentation replay may use a different PresentationCatalogHash.
CardVersion should not change unless gameplay-facing card definition changes.
AssetManifestVersion should change.
```

Audio variant selection must not use gameplay RNG stream.
If deterministic presentation playback is needed, use:

```text
HASH_BY_EVENT_SEQ
HASH_BY_CARD_VERSION
HASH_BY_PRESENTATION_SEED
```

---

Potentially included if gameplay-relevant:

```text
- seed;
- rngState;
- rngStreams;
- active player;
- turn number;
- phase;
- zones;
- cards/entities;
- modifiers;
- auras;
- pending choices;
- terminal state.
```

---

## 12. StateHash

### 12.1 StateHash input

```text
StateHashInput:
  canonical serialized AuthoritativeGameState
  hashAlgorithmVersion
```

### 12.2 Hash algorithm

MVP:

```text
SHA-256 over canonical UTF-8 bytes
```

Output:

```text
hex lowercase
```

Example:

```text
stateHash: "7f83b1657ff1fc53..."
```

### 12.3 Hash versioning

```text
StateHash:
  algorithm: SHA256
  canonicalSerializationVersion
  value
```

Changing canonical serialization or hash algorithm requires major compatibility review.

---

## 13. Command canonicalization

Replay uses only `DeterministicCommandPayload`.

Canonical command excludes:

```text
- receivedAt;
- clientTimestamp;
- correlationId;
- authSessionId;
- transportMetadata;
- rawRequestRef.
```

Canonical command includes:

```text
commandSeq
playerId
commandType
payload
expectedPreStateHash | null
```

Command payload fields must be ordered by schema-defined order.

---

## 14. Event canonicalization

DerivedEventLog is not replay source, but must be canonical for comparison.

Event canonical model includes:

```text
eventSeq
parentEventSeq | null
commandSeq | null
eventType
phase
sourceRef
targetRefs
payload
rngRollRefs
mutationBatchRef
preStateHash
postStateHash
```

Diagnostic fields may be excluded from event hash unless debug trace hash is requested.

PresentationEvent / AudioCueEvent must be excluded from gameplay event hash by default. If presentation replay hash is requested, it must be computed separately from gameplay event hash.

---

## 15. Mutation canonicalization

ProposedMutationBatch canonical fields:

```text
batchId
commandSeq
parentEventSeq | null
phase
sourceRef
reason
mutations
preStateHash
replacementPolicyRef | null
fizzlePolicyRef | null
```

AppliedMutationBatch canonical fields:

```text
batchId
commandSeq
parentEventSeq | null
phase
sourceRef
reason
appliedMutations
skippedMutations
preStateHash
postStateHash
derivedEventRefs
appliedAtStep
```

Mutation order follows mutation ordering policy.

---

## 16. CatalogSnapshot hashing

CatalogSnapshot gameplay hash must include:

```text
catalogSnapshotId
catalogVersion
cardVersions
rulesetProfileRef
primitiveRegistryVersion
schemaVersion
mechanicsContentHash
```

CatalogSnapshot presentation hash may include:

```text
assetManifestVersions
visualAssetManifestVersions
audioAssetManifestVersions
assetContentHashes
localizationRefs
presentationFallbackPolicies
```

CardVersions sorted by:

```text
cardId
cardVersion
```

CatalogSnapshot hash must not depend on database row order.

GameplayCatalogHash must not change when only audio/visual assets change.
PresentationCatalogHash must change when asset refs or audio content hashes change.

---

## 17. RulesetProfile resolvedHash

ResolvedRulesetProfile hash includes:

```text
profileId
profileVersion
enabledMechanics
disabledMechanics
allowedActions
allowedEffectKinds
allowedKeywords
limits
validationPolicy
balancePolicy
simulationPolicy
publicationPolicy
```

Collections sorted canonically.

Do not hash unresolved parent references only.

Hash the resolved profile.

---

## 18. EngineCapabilityRegistry hash

EngineCapabilityRegistry hash includes:

```text
engineVersion
supportedSchemaVersions
supportedPrimitiveRegistryVersions
supportedRulesetProfileVersions
supportedActionTypes
supportedEffectKinds
supportedMechanics
supportedPhases
supportedSourceZones
supportedResolutionPolicies
supportedFizzlePolicies
```

Collections sorted canonically.

---

## 19. Canonical serializer implementation

Recommended design:

```text
ICanonicalSerializer<T>:
  SerializeToCanonicalBytes(T value) -> byte[]
  SerializeToCanonicalString(T value) -> string

IStateHasher:
  ComputeStateHash(GameState state) -> StateHash

ICommandHasher:
  ComputeCommandHash(DeterministicCommandPayload command) -> Hash

IEventLogHasher:
  ComputeEventLogHash(DerivedEventLog eventLog) -> Hash
```

Generated serializers preferred over reflection for stability.

Reflection-based serializer is allowed only for prototype and must be covered by snapshot tests.

---

## 20. Snapshot tests

Required snapshot tests:

```text
[ ] GameState canonical snapshot.
[ ] DeterministicCommandPayload canonical snapshot.
[ ] ProposedMutationBatch canonical snapshot.
[ ] AppliedMutationBatch canonical snapshot.
[ ] CommittedEvent canonical snapshot.
[ ] CatalogSnapshot canonical snapshot.
[ ] ResolvedRulesetProfile canonical snapshot.
[ ] EngineCapabilityRegistry canonical snapshot.
```

Any snapshot change requires review.

---

## 21. Property tests

```text
[ ] same state produces same canonical bytes.
[ ] same canonical bytes produce same hash.
[ ] dictionary insertion order does not affect canonical bytes.
[ ] set insertion order does not affect canonical bytes.
[ ] envelope metadata does not affect state hash.
[ ] audio playback state does not affect gameplay state hash.
[ ] changing only audio asset changes PresentationCatalogHash, not GameplayCatalogHash.
[ ] AudioCueEvent excluded from gameplay event hash.
[ ] different gameplay state changes hash.
[ ] different RNG state changes hash.
[ ] different hidden hand card changes authoritative state hash.
[ ] player view serialization does not expose hidden data.
```

---

## 22. Golden tests

Golden fixtures must be stored for:

```text
- initial game state;
- after draw;
- after play minion;
- after damage;
- after death;
- after secret reveal;
- after batch destroy;
- after replay full mini-match.
```

Each fixture should contain:

```text
canonical JSON
stateHash
engineVersion
canonicalSerializationVersion
schemaVersion
```

---

## 23. Versioning

Canonical serialization has explicit version:

```text
canonicalSerializationVersion: "1.0.0"
```

Breaking changes:

```text
- field order change;
- included/excluded field change;
- enum representation change;
- collection ordering change;
- number formatting change;
- hash algorithm change.
```

Breaking changes require:

```text
- migration plan;
- replay compatibility decision;
- old engine support or replay lock;
- major version bump.
```

---

## 24. MVP scope

MVP canonical serialization must support:

```text
- GameState;
- PlayerState;
- CardInstance;
- EntityInstance;
- DeterministicCommandPayload;
- ProposedMutationBatch;
- AppliedMutationBatch;
- CommittedEvent;
- CatalogSnapshotRef;
- RulesetProfileRef;
- EngineCapabilityRegistryRef;
```

Post-MVP:

```text
- full CatalogSnapshot;
- full ResolvedRulesetProfile;
- debug trace hash;
- binary canonical format exploration;
- cross-language canonical serialization.
```

---

## 25. Definition of Done

Canonical serialization is ready when:

```text
[ ] Canonical field order defined for core DTOs.
[ ] Collection ordering policy implemented.
[ ] Transient field exclusion tested.
[ ] StateHash implemented.
[ ] Command hash implemented.
[ ] Event log hash implemented.
[ ] Snapshot tests exist.
[ ] Property tests exist.
[ ] Replay uses canonical command payload.
[ ] StateHash stable across repeated runs.
[ ] Dictionary order cannot change hash.
[ ] Versioning policy documented.
```
