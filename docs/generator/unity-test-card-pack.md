# Unity Test Card Pack Export

The Unity test card pack exporter creates a client-facing baseline pack under `artifacts/unity-test-card-pack/<timestamp>/`.

Run:

```bash
bash scripts/local-models/run-unity-test-card-pack.sh
```

The exporter has two explicit modes:

- `curated` - default Unity baseline export. It calls the real local providers for provenance and assets, but uses the repository's curated Unity scenario DSL for stable client coverage.
- `production` - production-native evidence mode. It sends scenario requirements to the structured provider and publishes `providerStructured.Output` directly. No curated `UnityCardPackScenarioCatalog` DSL is substituted in this mode.

Run production-native evidence mode:

```bash
bash scripts/local-models/run-unity-test-card-pack.sh --mode production
```

The curated DSL mode remains intentional for Unity baseline work: Unity needs stable coverage for minions, spells, weapons, locations, choice UI, random resolution, prevention, aura display, healing/draw, and graveyard value flows. It must not be used as proof that the structured LLM provider can autonomously author all complex DSL families.

Current production-native evidence:

- final smoke pack: `artifacts/unity-test-card-pack/20260703-final-generator-smoke-12c/`
- final 100-card stress pack: `artifacts/unity-test-card-pack/20260703-final-generator-stress-100c/`
- final 50-card rerun pack: `artifacts/unity-test-card-pack/20260703-final-generator-rerun-50/`
- roll-based creative direction 20-card run: `artifacts/unity-test-card-pack/20260704-roll-reference-20-accepted/`
- final roll-quality 20-card run: `artifacts/unity-test-card-pack/20260704-roll-reference-20-final-quality/`
- final generator readiness 100-card run: `artifacts/unity-test-card-pack/20260704-roll-reference-100-final-readiness/`
- requested: 12
- generated: 12
- raw LLM outputs valid immediately: 10
- repaired by provider repair loop: 2
- validation accepted: 12
- publication accepted: 12
- complete PNG/GIF/WAV asset sets: 12
- rejected: 0
- semantic warnings: 0
- full per-card gameplay proof entries: 12

Stress evidence:

- final 20-card roll-quality requested/generated: 20/20
- final 20-card roll-quality raw LLM outputs valid immediately: 20
- final 20-card roll-quality repaired by provider repair loop: 0
- final 20-card roll-quality validation/publication/full assets/gameplay proof/replay: 20/20/20/20/20
- final 20-card scenario metadata mismatches: 0
- final 20-card duplicate/similar violations: 0
- final 20-card diversity violations: 0
- final 20-card unjustified tooWeak/tooStrong findings: 0
- final 20-card location ability families: `DAMAGE_DISTRIBUTION`, `HEAL`, `PREVENT_DAMAGE`
- final 20-card roll conformance: 19 `strong`, 1 `adapted`, 0 `ignored`, 0 `invalid_roll`
- final 20-card roll hard ignored directions: 0
- final 100-card roll-readiness requested/generated: 100/100
- final 100-card roll-readiness raw LLM outputs valid immediately: 99
- final 100-card roll-readiness repaired by provider repair loop: 1
- final 100-card roll-readiness rejected/semantic warnings: 0/0
- final 100-card roll-readiness validation/publication/full assets/gameplay proof/replay: 100/100/100/100/100
- final 100-card scenario metadata mismatches: 0
- final 100-card duplicate/similar violations: 0
- final 100-card diversity violations: 0
- final 100-card unjustified tooWeak/tooStrong findings: 0
- final 100-card location ability families: `DAMAGE_DISTRIBUTION`, `DISCOVER`, `DRAW_CARD`, `PREVENT_DAMAGE`
- final 100-card roll conformance: 82 `strong`, 18 `adapted`, 0 `ignored`, 0 `invalid_roll`
- final 100-card roll hard ignored directions: 0
- 100-card requested/generated: 100/100
- 100-card raw LLM outputs valid immediately: 86
- 100-card repaired by provider repair loop: 14
- 50-card rerun requested/generated: 50/50
- 50-card rerun raw LLM outputs valid immediately: 44
- 50-card rerun repaired by provider repair loop: 6
- rejected: 0
- semantic warnings: 0
- validation failures: 0
- publication failures: 0
- asset failures: 0
- duplicate/similar violations: 0
- effect coverage: `APPLY_AURA`, `DAMAGE_DISTRIBUTION`, `DEAL_DAMAGE`, `DISCOVER`, `DRAW_CARD`, `HEAL`, `PREVENT_DAMAGE`, `RESURRECT`
- card type coverage: `MINION_CARD`, `SPELL_CARD`, `WEAPON_CARD`, `LOCATION_CARD`

Verdict: `production-ready-with-known-limitations` for Unity Client Track baseline use. The pack publishes providerStructured.Output directly with no curated DSL substitution, passes 100-card and 50-card stress checks, covers the required supported effect families, and reports zero semantic warnings, validation failures, publication failures, asset failures, and duplicate/similar violations. Remaining limitation: representative bot-vs-bot proof is recorded as oracle-bot-compatible scripted/headless proof, not a full bot-vs-bot run inside the exporter.

Output:

- `manifest.json` - pack index, card ids, scenario coverage, validation/balance/publication status, and asset refs.
- `summary-report.json` - requested/generated/validated/published/full-asset counts, rejected cards, and risks.
- `cards/<slug>.json` - per-card DSL, provider provenance, raw structured trace ref, validation, balance, publication, repair governance trace, scripted gameplay proof, hashes, and Unity usage notes.
- `assets/images/*.png` - static card art copied from the local image provider output.
- `assets/animations/*.gif` - idle animations copied from the local animation provider output.
- `assets/audio/*.wav` - audio cues copied from the local audio provider output.
- `contact-sheet.html` - quick visual index for client developers.
- `<timestamp>.zip` - zip archive next to the export folder.

Default provider endpoints can be overridden with:

- `CCG_LOCAL_STRUCTURED_ENDPOINT`
- `CCG_LOCAL_STRUCTURED_MODEL`
- `CCG_LOCAL_IMAGE_ENDPOINT`
- `CCG_LOCAL_IMAGE_MODEL`
- `CCG_LOCAL_ANIMATION_ENDPOINT`
- `CCG_LOCAL_ANIMATION_MODEL`
- `CCG_LOCAL_AUDIO_ENDPOINT`
- `CCG_LOCAL_AUDIO_MODEL`

## Repair Governance Trace

Production-native structured traces are written under `artifacts/local-provider-gateways/structured/` and linked from each card's `providerStructuredResult.rawStructuredOutputRef`.

Each trace records:

- `rawAttempts`: raw LLM JSON responses in order.
- `rawLlmOutputValidImmediately`: true when the first raw attempt passed provider-side schema/preflight validation.
- `preflightNormalized` and `preflightDiff`: deterministic schema normalization applied before validation, such as enum casing, class tag correction to the active ruleset, or scenario-required card type/stat shape.
- `repaired`, `repairCount`, and `repairDiff`: changes between the first parsed output and the final accepted output after a failed validation attempt.
- `validationErrors`: provider-side errors that caused repair or final rejection.
- `finalPayload`: the structured payload returned to the exporter and then validated/published.

Preflight normalization is not counted as repair. Repair is reserved for a failed validation attempt followed by a new LLM attempt. The gateway prompt instructs repair to preserve card intent and not simplify complex scenario cards unless the reported validation errors require it.

## Production Readiness Verdicts

The exporter now reports readiness as separate verdicts instead of a single broad green status:

- `rawLlmOutputReadiness`
- `repairConvergedPipelineReadiness`
- `gameplayContractReadiness`
- `botVsBotReadiness`
- `mediaInclusiveReadiness`
- `unityBaselineReadiness`
- `reportingReadiness`

Production-ready requires raw valid immediately >= 80%, provider repair <= 20%, material/effects repair <= 5%, deterministic fallback 0, material preflight rewrite 0, technical leakage 0, rulesText/DSL mismatch 0, full exporter-run bot-vs-bot representative proof, and a real media-inclusive run unless the run is explicitly scoped as LLM-only. LLM-only runs with skipped assets must report media as `not_applicable_llm_only` and cannot produce a final `production-ready` verdict.

Final player-facing rules text is canonicalized from the final DSL effects before reporting and publication evidence. The generator must not expose Unity/test-pool/fixed-pool/provider/schema/validation terms, `No effect.`, vanilla/stat-only labels, raw statlines, unsupported pseudo-keywords such as `Mark`, or discover wording that implies the wrong pool source.

## Bounded Smoke And Failure Evidence

The exporter accepts bounded execution options for local provider work:

- `--provider-timeout-seconds`
- `--asset-provider-timeout-seconds`
- `--run-timeout-seconds`
- `--max-provider-retries`
- `--max-generation-attempts-per-card`
- `--max-repair-attempts-per-card`
- `--failure-root`
- `--run-name`

Use staged smoke scripts for operational checks:

```bash
bash scripts/local-models/run-generator-smoke-1.sh
bash scripts/local-models/run-generator-smoke-3.sh
bash scripts/local-models/run-generator-smoke-10.sh
bash scripts/local-models/run-generator-readiness-100.sh
bash scripts/local-models/run-generator-media-smoke.sh
bash scripts/local-models/run-generator-bot-smoke.sh
```

If a run is not accepted, it writes a separate failure artifact under `artifacts/generator-failures/<timestamp>-<run-name>/`. Failure artifacts are intentionally separate from accepted card-pack artifacts and include provider call traces, prompt metadata, timing, options, environment, last provider output, and markdown/JSON failure reports.

## Diversity And Similarity Gates

Production-native reports now include strict pack-level quality gates:

- `similarityReport`: duplicate/similar score plus field-level pairs for similar names, rules text, effect signatures, stat lines, and gameplay roles.
- `stressReport.duplicateSimilarScore`, `duplicateSimilarViolations`, and `similarCards`.
- `diversityReport.themeDistribution`: theme/faction prefix spread.
- `diversityReport.mechanicalArchetypes`: gameplay role spread.
- `diversityReport.effectPatterns`: effect-pattern spread.

The local structured gateway uses `scenarioIndex` and `diversitySeed` to rotate themes, factions, visual prompts, role nouns, and names across repeated stress scenarios. The final 100-card and 50-card reports both record `duplicateSimilarViolations: 0`.

## Public Showcase Quality Reports

The exporter now includes public-showcase quality reports in `summary-report.json`:

- `mechanicCoverageReport` maps engine capabilities to generator support, prompt support, canonical rulesText support, semantic validation, gameplay proof, replay proof, bot proof, Unity presentation hints, status, and blockers.
- `themeDiversityReport` uses a theme taxonomy instead of first-token grouping. It reports theme family assignments, overused token clusters, repeated visual motif skeletons, required family count, max family share, and violations.
- `balanceQualityReport.valueEstimates` estimates stat value, effect value, cost target, delta, repeatability, target flexibility, randomness, card advantage, board impact, survivability, risk category, reason, and repair suggestion.
- `showcaseReadinessReport` scores name quality, theme freshness, mechanical interest, rules clarity, balance confidence, visual identity, pack diversity contribution, and per-card showcase readiness.

The theme taxonomy includes cosmic, mundane military, wilderness/beasts, urban/rogues, religious/cult, nautical, desert nomads, forest/fae, machine/clockwork, undead/graveyard, volcanic/fire, storm/sky, dream/nightmare, royal/court, plague/alchemy, and folklore families. A generator-supported mechanic must be declared in the generator mechanic registry; engine-supported mechanics without complete generator support are reported as `generator-blocked` rather than silently ignored.

Run a bounded showcase attempt:

```bash
bash scripts/local-models/run-generator-showcase-36.sh
```

Showcase mode does not bypass production validation. If the local structured provider times out, the run writes failure evidence under `artifacts/generator-failures/` and must not be counted as accepted pack evidence.

## Roll-Based Creative Direction

Production mode now creates a procedural creative reference before the structured provider call. It uses independent production roll slots such as `production-roll-001`; production mode does not carry `UnityCardPackScenarioCatalog` scenario DSL or labels. The exporter rolls independent supported-direction groups and stores both the rolls and the resulting reference in each card report:

- effect presence: effect-driven or stat-oriented;
- effect complexity: simple, moderate, or compound;
- primary supported primitive: for example `DEAL_DAMAGE`, `DISCOVER`, `APPLY_AURA`, `RESURRECT`, `LOCATION_VALUE`, or `VANILLA_STATS`;
- optional secondary primitive;
- card type direction;
- cost band;
- target pattern;
- timing/window;
- gameplay role;
- flavor, mood, and visual direction;
- unusualness level.

The generation reference is passed in the structured provider request prompt and in `contextRefs.generationReference` plus the `roll.*` context refs. The prompt explicitly says to use the roll result as creative direction, not as a rigid template, and allows coherent adaptations when needed for supported mechanics or semantic validity.

The roll system does not read player decks, player collections, or the global card catalog. It is built from the exporter ruleset profile and currently supported primitive families. The first production stress slots use a deterministic coverage bag over supported primitive families so short acceptance runs cover the supported surface; subsequent slots continue through weighted procedural rolls.

Exporter output roots are cleaned before each run so repeated exports to the same artifact path cannot leave stale per-card reports or assets beside a fresh manifest.

Each card report includes `rollReference`, `generationReference`, and `rollConformance`. The pack summary includes `rollReferenceReport` with conformance distribution, primitive distributions, target/timing/cost distributions, hard ignored directions, and ignored/invalid slugs.

Production roll-based export derives scenario metadata after structured validation from the final production-native card DSL, not from the curated Unity scenario catalog. Per-card reports now include `derivedMetadata` and `scenarioMetadataConsistency`; summary reports include `scenarioMetadataReport` with actual card type, action/effect family, timing/window, target pattern, gameplay role, complexity, roll conformance, location ability family distribution, and mismatch count. Ready verdicts are blocked by scenario metadata mismatches.

Balance reporting distinguishes surfaced candidates from accepted explanations. `balanceQualityReport.findings` records tooWeak/tooStrong diagnostics plus `acceptedWithJustification`; `tooWeakCandidates`, `tooStrongCandidates`, and `unjustifiedFindingCount` only contain unresolved quality issues. Ready verdicts are blocked by unjustified balance findings, but intentionally simple or low-stat value cards can remain when the report explains the supported compensating effect.

Conformance statuses are diagnostic:

- `strong`: the card follows the core direction and most secondary details;
- `adapted`: the card preserves the core intent while changing details;
- `ignored`: the card mostly missed the direction;
- `invalid_roll`: the direction was incompatible with supported production mechanics.

The conformance status does not reject otherwise valid useful cards by itself. The ready verdict blocks only hard ignored directions where an effect roll degenerates into an unrelated simple/no-effect card.

## Creative Diversity Gates

Production-native roll-based reports now include `creativeQualityReport` for pack-level content richness. The report is intentionally run-local only: it compares cards generated in the current pack and does not read player decks, player collections, or the global card catalog.

The report covers:

- unique name count, repeated name tokens, name structures, top prefixes/suffixes, and formulaic `Theme + Modifier + Noun` percentage;
- unique rulesText count, exact repeated rulesText, top repeated normalized text patterns, effect signature repetition, and mechanically identical cards;
- flavor distribution across roll mood, naming style, text style, mechanical twist, and unique visual prompts.

Ready verdicts are blocked when duplicate names, excessive formulaic names, low rulesText richness, rulesText flavor leakage, over-repeated normalized rules patterns, or mechanically identical generated cards remain. Exact repeated rulesText is still reported for review, but it is not an automatic failure when the repeated wording is mechanically appropriate and the pack has no exact mechanical clones.

Final creative-diversity evidence:

- pack: `artifacts/unity-test-card-pack/20260705-roll-creative-diversity-100-final-v4/`
- zip: `artifacts/unity-test-card-pack/20260705-roll-creative-diversity-100-final-v4.zip`
- result: 100 requested, 100 generated, 96 raw-valid immediately, 4 repaired, 0 rejected, 0 semantic warnings, 100 validation accepted, 100 publication accepted, 100 full PNG/GIF/WAV asset sets, 100 gameplay proof entries, and 100 replay verifications
- creative quality: 100 unique names, 0 formulaic `Theme + Modifier + Noun` names, 100 unique rulesText, 0 exact repeated rulesText, 0 mechanically identical cards, 100 unique visual prompts, 0 creative quality violations
- quality gates: 0 scenario metadata mismatches, 0 duplicate/similar violations, 0 diversity violations, 0 hard ignored roll directions, 0 unjustified tooWeak/tooStrong findings, and 0 provider failures

Final naturalness evidence:

- pack: `artifacts/unity-test-card-pack/20260705-roll-naturalness-100-final-v7/`
- zip: `artifacts/unity-test-card-pack/20260705-roll-naturalness-100-final-v7.zip`
- report: `artifacts/unity-test-card-pack/20260705-roll-naturalness-100-final-v7/generator-naturalness-report.md`
- result: 100 requested, 100 generated, 88 raw-valid immediately, 12 repaired by provider repair loop, 0 rejected, 0 semantic warnings, 100 validation accepted, 100 publication accepted, 100 gameplay proof entries, and 100 replay verifications
- asset note: this was an LLM-side naturalness run with `--skip-assets`; no image, animation, or audio provider calls were made by request
- naming quality: 100 unique names, average 4.23 words, max 6 words, 0 formulaic `Theme + Modifier + Noun` names, 0 long names, and 0 name naturalness violations
- rules quality: 78 unique exact rulesText strings, 51 normalized rulesText patterns, 0 rules flavor leakage, and 0 mechanically identical cards
- quality gates: 0 scenario metadata mismatches, 0 duplicate/similar violations, 0 diversity violations, 0 hard ignored roll directions, 0 unjustified tooWeak/tooStrong findings, 0 creative quality violations, and 0 provider failures
- final verdict: `production-ready-with-known-limitations`

The naturalness run supersedes the previous creative-diversity wording strategy for day-to-day generator acceptance. The earlier `20260705-roll-creative-diversity-100-final-v4` pack reached 100 unique rulesText strings, but many names averaged 7.08 words, 38 names were 8+ words, and some rulesText embedded card identity into gameplay text. The v7 naturalness pack accepts some repeated canonical gameplay wording while keeping rulesText clean, short, mechanically varied, and free of exact mechanical clones.

## Complex Effects Suite

`complexEffectsReport` records representative generated cards for:

- `APPLY_AURA`
- `DAMAGE_DISTRIBUTION`
- `PREVENT_DAMAGE`
- `DISCOVER`
- `RESURRECT`
- `DEATHRATTLE_EFFECT`
- `LOCATION_ABILITY_EFFECT`
- weapon equip/attack/durability
- heal + draw
- targeted damage
- random damage

Each proof row records production-native provider output, validation, publication, gameplay proof, and replay verification. The final 50-card rerun records all complex effect rows as passed.

## Provider Reliability

Before export, the exporter checks structured, image, animation, and audio provider health endpoints. Runtime provider failures are classified as `transient`, `permanent`, `schema`, `timeout`, or `unknown`; production mode uses retry/backoff for transient HTTP failures. The final 100-card and 50-card reports record all providers healthy before run and no runtime provider failures.

## Unity Mapping Notes

Weapon cards:

- `cardType: WEAPON_CARD`.
- `attack` maps to weapon attack.
- `health` maps to durability.
- Unity should render durability as the weapon durability counter, not minion health.
- Gameplay playback should consume public events for weapon equip, attack, durability loss, and destruction.

Location cards:

- `cardType: LOCATION_CARD`.
- `health` maps to location durability.
- The exported location scenario uses `LOCATION_ABILITY_EFFECT` and `ACTIVATE_LOCATION` proof refs.
- Unity should show durability, enabled/disabled activation state, and ability playback as location-specific UI, not minion combat UI.

Aura stat changes:

- Aura cards use `AURA_EFFECT` with `APPLY_AURA`.
- Current supported pack aura changes board minion attack via `BOARD_ATTACK_MODIFIER` while the source is active.
- Unity should derive displayed modified stats from state/events and refresh affected board entities when aura refresh playback occurs.

Discover choice UI:

- Discover cards use `DISCOVER` with `poolRef: UNITY_FIXED_POOL`, `offerCount: 3`, and `addToZone: HAND`.
- Unity should present a three-option choice UI for `CHOICE_CREATED`, submit the selected option through the public choice command flow, and animate `CHOICE_RESOLVED` plus card materialization into hand.

Resurrect playback:

- Resurrect cards use `RESURRECT` with bounded `OWN_GRAVEYARD_MINION` history query.
- Unity should show graveyard source context when available, then play the summoned entity from graveyard/history into board.
- Replay is command-log based; the event log is playback output, not replay source.

Prevention/replacement playback:

- Prevention cards use `PREVENT_DAMAGE`, represented at runtime through proposed damage and replacement resolver events.
- Unity should treat `DAMAGE_WOULD_BE_DEALT` as the proposed window, `REPLACEMENT_RESOLVED` as the resolver decision, and the later committed events as the resolved result.
- If damage is prevented or shielded, the resolved committed damage amount can be zero or replaced by keyword/modifier consumption.

Replay/event playback:

- Unity should consume public committed/proposed/resolver events in event sequence order.
- Reconnect must use state hash continuity from the public API.
- Replay verification is requested through the public replay endpoint; Unity must not reconstruct gameplay from event logs as a source of truth.

The pack is a Unity integration baseline, not final content. Generated media is local-provider baseline quality, and per-card proof records export-profile scripted/headless evidence rather than full production bot-vs-bot acceptance evidence.
