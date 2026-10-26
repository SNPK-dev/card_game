# Техническое задание

## Система генерации карт и controlled event-driven CCG engine

### Draft 5 — Architecture Contract, Product Ruleset Strategy & Governance Model

---

# 0. Executive Summary

Мы строим не копию Hearthstone.

Мы строим собственную **controlled CCG platform**:

```text
deterministic event-driven engine
+
strict canonical DSL
+
LLM-assisted card generation
+
multi-layer validation
+
simulation gate
+
immutable card catalog
+
deterministic replay
+
controlled ruleset governance
```

Цель системы — позволить генерировать новые карты через LLM, но так, чтобы итоговая карта:

```text
1. была строго описана в DSL;
2. использовала только approved mechanics;
3. проходила schema/semantic/rule/static/balance/simulation/replay validation;
4. не ломала движок;
5. не требовала hardcoded exception;
6. была воспроизводима через replay;
7. могла быть опубликована как immutable CardVersion.
```

Ключевая продуктовая формула:

```text
Hearthstone-class architecture,
not Hearthstone-compatible ruleset.
```

Hearthstone-like карты используются как:

```text
- stress-tests;
- coverage examples;
- regression scenarios;
- проверка выразительности DSL;
- проверка устойчивости движка.
```

Но они не являются:

```text
- product compatibility target;
- требованием копировать все edge cases;
- основанием тащить исторический baggage;
- требованием реализовать все Hearthstone mechanics;
- оправданием расширять production ruleset без необходимости.
```

---

# 1. Статус документа

Этот документ является:

```text
Architecture Contract
Product Ruleset Strategy
Generator Governance Contract
Engine Capability Contract
Planning Baseline
```

Документ описывает:

```text
- какую платформу мы строим;
- какие архитектурные принципы обязательны;
- как LLM-генератор взаимодействует с DSL, валидаторами, симулятором, каталогом и движком;
- как controlled ruleset ограничивает разрешенные механики;
- как новые mechanics добавляются через RFC;
- как поддерживать расширяемость без переписывания core engine;
- как проверять архитектуру через Hearthstone-like stress cases;
- какие дочерние specs должны быть созданы.
```

Документ **не является**:

```text
- полным DSL Reference;
- финальным Primitive Registry;
- полным Action Registry;
- полным Keyword Registry;
- полной спецификацией игрового движка;
- полной спецификацией валидаторов;
- полной спецификацией баланса;
- полной спецификацией replay;
- полной спецификацией API;
- финальным списком всех enum, phases, policies и edge cases.
```

Этот документ отвечает на вопрос:

```text
Какую архитектурную платформу для генерации и исполнения карт мы строим?
```

Дочерние specs должны отвечать на вопросы:

```text
- какие конкретно enum существуют;
- какие exact fields есть у каждой модели;
- как работает каждый action;
- как работает каждая фаза;
- как валидируется каждая механика;
- как симулируется каждая механика;
- как логируется replay;
- какие карты покрывают этот блок;
- какие API-контракты связывают подсистемы.
```

---

# 2. Главная продуктовая позиция

Мы не строим Hearthstone clone.

Мы строим:

```text
собственный deterministic event-driven CCG engine
с контролируемым canonical DSL,
который способен архитектурно выражать Hearthstone-class mechanics,
но production card set использует только approved primitives.
```

Главное отличие от Hearthstone-like compatibility:

```text
Architecture может уметь многое.
Production ruleset должен разрешать мало, строго и осознанно.
```

Иначе проект быстро придет к проблемам:

```text
- слишком широкий DSL;
- слишком сложная генерация;
- слабая валидация;
- неконтролируемый баланс;
- сложный replay;
- engine spaghetti;
- постоянные исключения в core;
- рост количества багов при каждой новой механике.
```

Правильная цель:

```text
стабильное ядро
+
контролируемое расширение через approved primitives
+
строгий GenerationContext
+
формальный Extension RFC
+
тесты, симуляция и replay на каждую механику
```

---

# 3. Основной принцип доверия

LLM не является источником истины.

```text
LLM proposes.
DSL constrains.
Validators reject.
Simulator proves.
Catalog freezes.
Engine executes.
Replay verifies.
```

LLM не должна:

```text
- исполнять правила игры;
- работать во время матча;
- иметь доступ ко всему каталогу карт;
- создавать новые mechanics вне approved registry;
- принимать финальное решение о валидности карты;
- определять финальный баланс;
- менять опубликованную карту;
- обходить валидаторы;
- генерировать поведение, которого нет в движке.
```

LLM может:

```text
- создавать CardSkeleton;
- предлагать EffectSlotIntent;
- раскрывать effect slots в DSL effects;
- чинить конкретный невалидный участок;
- генерировать flavor text;
- генерировать asset prompt;
- предлагать design rationale;
- предлагать balance hints, которые потом проверяет код.
```

---

# 4. Главный pipeline генерации

Единый pipeline для всех карт:

```text
0. Generation Context Builder
1. Skeleton Generation
2. EffectKind-aware Expansion
3. Validation + Repair
4. Simulation Gate
5. Human/Auto Review
6. Catalog Publication
7. Replay Verification
```

Даже простая карта без эффекта проходит тот же pipeline:

```text
CardSkeleton
↓
effects = []
↓
validation
↓
simulation
↓
catalog
↓
replay check
```

Причина:

```text
- единые логи;
- единые тесты;
- единый UX;
- единая диагностика;
- единый механизм публикации;
- меньше special cases;
- проще масштабировать генерацию.
```

---

# 5. Non-goals для MVP

Для MVP явно не ставятся цели:

```text
- полная совместимость с Hearthstone;
- поддержка всех CCG archetypes;
- поддержка всех Hearthstone-like keywords;
- Zephrys-quality идеальный AdvisorEngine;
- полностью автоматический финальный баланс;
- LLM calls во время матча;
- произвольная генерация новых mechanics;
- full dynamic card crafting;
- production-grade animated cards;
- ranked matchmaking;
- economy / collection system;
- anti-cheat network architecture;
- live-service meta balancing.
```

MVP должен доказать цепочку:

```text
Generated card
↓
Validated card
↓
Simulated card
↓
Published immutable CardVersion
↓
Playable deck
↓
Full deterministic match
↓
Replay success
```

---

# 6. Product Ruleset Philosophy

Production ruleset должен быть уже, чище и стабильнее, чем полный Hearthstone-like набор механик.

Принципы:

```text
1. Не все, что архитектурно возможно, разрешено в production.
2. Production card set использует только approved Primitive Registry.
3. Новые primitives добавляются редко.
4. Новые primitives добавляются только через Extension RFC.
5. Предпочтение отдается композиции существующих primitives.
6. LLM выбирает только из mechanics, разрешенных GenerationContext.
7. Чем шире DSL, тем хуже генерация, валидация и баланс.
8. Движок должен быть расширяемым.
9. Production ruleset должен быть строгим.
10. Hearthstone-like coverage проверяет архитектуру, а не расширяет scope автоматически.
```

---

# 7. Card Design Constitution

Card Design Constitution фиксирует, какие карты система считает желательными, рискованными или запрещенными.

## 7.1 Предпочтительные паттерны

Предпочтительны карты, которые:

```text
- имеют понятный source zone;
- имеют явный timing;
- имеют ограниченное количество actions;
- используют controlled pools;
- имеют deterministic RNG;
- имеют понятный fizzle behavior;
- не создают скрытых exception rules;
- легко симулируются;
- легко объясняются игроку;
- имеют ограниченный event count;
- не требуют глобального анализа всей игры без необходимости;
- не создают unbounded history dependency;
- не требуют сложных replacement loops.
```

## 7.2 Допустимые, но рискованные паттерны

Допустимы только после review:

```text
- replay ранее сыгранных карт;
- повторение большого числа эффектов;
- random spell casting;
- persistent rest-of-game modifiers;
- replacement/prevention loops;
- start-of-game effects;
- extra turns;
- complex generated cards;
- hand/deck-wide cost or stat modifiers;
- effects using broad history queries;
- multi-stage questline rewards;
- mechanics affecting command availability.
```

## 7.3 Запрещенные или нецелевые паттерны для MVP

Для MVP запрещены:

```text
- unbounded loops;
- uncapped repeat effects;
- random effects без deterministic pool;
- LLM calls во время матча;
- карты, создающие новые mechanics;
- карты, требующие hardcoded exception в engine;
- бесконечный draw;
- бесконечная mana generation;
- бесконечная summon chain;
- неограниченный replay history;
- Zephrys-quality smart advisor;
- full dynamic card crafting без predefined recipe;
- mechanics без replay representation;
- mechanics без validation rules;
- mechanics без simulation scenarios.
```

---

# 8. MechanicStatus Model

Нужно явно разделить архитектурную поддержку и production-доступность.

Механика должна иметь статус.

```text
MechanicStatus:
  DISCOVERED
  ARCHITECTURE_SUPPORTED
  SPEC_DRAFTED
  SPEC_APPROVED
  IMPLEMENTED
  VALIDATED
  SIMULATION_VERIFIED
  REPLAY_VERIFIED
  BALANCE_REVIEWED
  APPROVED_FOR_RULESET
  ENABLED_IN_FORMAT
  DISABLED_IN_FORMAT
  DEPRECATED
  REJECTED
```

## 8.1 Значение статусов

```text
DISCOVERED
  Механика выявлена как потенциально полезная.

ARCHITECTURE_SUPPORTED
  Архитектура может выразить механику концептуально.

SPEC_DRAFTED
  Есть черновая спецификация.

SPEC_APPROVED
  Спецификация прошла review.

IMPLEMENTED
  Есть кодовая реализация.

VALIDATED
  Есть schema/semantic/rule/static validation.

SIMULATION_VERIFIED
  Механика прошла simulation scenarios.

REPLAY_VERIFIED
  Механика воспроизводится через deterministic replay.

BALANCE_REVIEWED
  Есть balance report и risk classification.

APPROVED_FOR_RULESET
  Механика может использоваться в production ruleset.

ENABLED_IN_FORMAT
  Механика разрешена в конкретном formatId/cardSetId.

DISABLED_IN_FORMAT
  Механика существует, но запрещена в конкретном формате.

DEPRECATED
  Механика не используется в новых картах, но нужна для старых replay.

REJECTED
  Механика запрещена как product pattern.
```

## 8.2 Почему это нужно

Без `MechanicStatus` команда будет путать:

```text
- "движок теоретически может";
- "DSL это описывает";
- "код уже реализован";
- "валидаторы готовы";
- "можно публиковать карты";
- "можно включить в формат".
```

Это разные уровни зрелости.

---

# 9. RulesetProfile / FormatProfile

Движок может архитектурно поддерживать широкий набор mechanics, но конкретный формат должен разрешать только безопасную часть.

Нужна модель `RulesetProfile`.

```text
RulesetProfile:
  id
  name
  description
  version
  formatId
  cardSetId
  enabledMechanics
  disabledMechanics
  allowedEffectKinds
  allowedActions
  allowedKeywords
  allowedSourceZones
  allowedPhases
  allowedPools
  allowedCardTypes
  maxComplexityScore
  maxRandomnessScore
  maxEventCountPerCard
  maxEventCountPerTurn
  maxRepeatCount
  maxGeneratedCardsPerEffect
  maxHistoryQueryScope
  validationPolicy
  balancePolicy
  simulationPolicy
  publicationPolicy
```

## 9.1 Зачем нужен RulesetProfile

Он решает проблему:

```text
Engine может.
Но формат не разрешает.
```

Пример:

```text
Engine поддерживает QUESTLINE_EFFECT.
MVP ruleset не разрешает QUESTLINE_EFFECT.
Experimental ruleset разрешает QUESTLINE_EFFECT.
Ranked ruleset запрещает QUESTLINE_EFFECT до balance review.
```

## 9.2 GenerationContext зависит от RulesetProfile

GenerationContext должен строиться на основании:

```text
RulesetProfile
+
Primitive Registry
+
Catalog Snapshot
+
Balance Policy
+
Generation Intent
```

LLM не должна видеть mechanics, которые выключены в текущем `RulesetProfile`.

---

# 10. Tier Model

Механики делятся по приоритету реализации.

## 10.1 Tier 0 — Required for MVP

Обязательно для production-ready MVP:

```text
- vanilla minion;
- simple spell;
- battlecry-like on-play effect;
- deathrattle-like death effect;
- triggered board effect;
- basic secret;
- basic quest;
- basic aura;
- basic modifier;
- basic discover;
- summon token;
- summon from fixed pool;
- basic resurrect from history;
- batch destroy;
- deterministic RNG;
- validation pipeline;
- simulation gate;
- replay validation.
```

## 10.2 Tier 1 — Post-MVP controlled compatibility

Нужно для controlled Hearthstone-class compatibility, но вводится постепенно после MVP:

```text
- START_OF_GAME_EFFECT;
- QUESTLINE_EFFECT;
- TAKE_EXTRA_TURN_ACTION;
- REEXECUTABLE_CARD_PLAY_HISTORY;
- MAGNETIC_PLAY_MODE;
- INFUSE_PROGRESS_TRACKER;
- PERSISTENT_ENCHANTMENT_CARRY_POLICY;
- DAMAGE_DISTRIBUTION_ACTION;
- PLAYERBOUND_REPLACEMENT_RULE;
- KEYWORD_CANONICAL_REGISTRY;
- BASIC_HERO_CARD_EFFECT;
- BASIC_CARD_CRAFTING;
- ADVANCED_SECRET_WINDOWS;
- ADVANCED_HISTORY_QUERIES.
```

## 10.3 Tier 2 — Advanced systems

```text
- Zephrys-like AdvisorEngine;
- full card crafting;
- complex generated minions;
- advanced hero powers;
- advanced questlines;
- large-scale balance simulation;
- meta-analysis;
- automated nerf suggestions;
- complex replacement loops;
- broad re-executable card play semantics.
```

## 10.4 Tier 3 — Experimental

```text
- LLM-assisted balance suggestions;
- AI deck meta simulation;
- auto-generated card sets;
- player-facing card designer;
- animated card variants;
- procedural PvE encounters;
- adaptive PvE card generation.
```

---

# 11. Architecture-supported vs Production-enabled

Каждая механика оценивается по двум осям.

```text
Architecture Coverage:
  Можно ли выразить механику в архитектуре?

Production Readiness:
  Готова ли механика к публикации карт?
```

## 11.1 Coverage statuses

```text
Architecture Coverage:
  ✅ supported
  🟨 partially supported
  ❌ not supported

Production Readiness:
  ✅ production-ready
  🟨 needs detailed spec / tests / validation
  ❌ not production-ready
```

## 11.2 Пример

```text
Zephrys-like SmartOffer:
  Architecture Coverage: ✅
  Production Readiness: 🟨 / ❌ for MVP

Ragnaros-like turn-end random damage:
  Architecture Coverage: ✅
  Production Readiness: ✅

Tess-like replay cards:
  Architecture Coverage: ✅
  Production Readiness: 🟨
```

---

# 12. Дочерние спецификации

Перед production-разработкой должны быть заведены:

```text
docs/
  architecture_contract.md
  product_ruleset_strategy.md
  card_design_constitution.md
  generator_pipeline_spec.md
  structured_outputs_schema_spec.md

  dsl_reference.md
  primitive_registry.md
  action_registry.md
  effect_kind_registry.md
  phase_timing_registry.md
  source_zone_registry.md
  keyword_canonical_registry.md

  validator_spec.md
  rule_validation_spec.md
  static_analysis_spec.md
  replay_spec.md
  history_spec.md
  simulation_spec.md
  balance_model_spec.md

  ruleset_profile_spec.md
  mechanic_status_spec.md
  extension_rfc_process.md
  catalog_publication_spec.md
  api_contracts_spec.md

  card_coverage_examples.md
  coverage_matrix.md

  asset_generation_spec.md
  prompt_registry.md
```

Каждый дочерний документ должен иметь:

```text
- status;
- owner;
- version;
- scope;
- non-goals;
- dependencies;
- examples;
- valid examples;
- invalid examples;
- validator implications;
- simulation implications;
- replay implications;
- balance implications;
- coverage links;
- test requirements;
- open questions.
```

---

# 13. Ownership / RACI Model

Для дочерних specs нужен owner.

Роли:

```text
Product Owner
  Отвечает за product ruleset, MVP scope, non-goals, card design constitution.

Game Systems Architect
  Отвечает за engine contract, phase model, event model, DSL architecture.

DSL Owner
  Отвечает за dsl_reference, primitive registry, action registry, schema.

Validation Owner
  Отвечает за schema, semantic, rule, static analysis validators.

Simulation Owner
  Отвечает за simulation scenarios, bot runners, batch testing.

Replay Owner
  Отвечает за deterministic replay, state hash, command/event logs.

Balance Owner
  Отвечает за balance model, score outputs, thresholds, review gates.

Generator Owner
  Отвечает за LLM pipeline, structured outputs, prompt registry, repair loop.

Catalog Owner
  Отвечает за card publication, catalog snapshots, versioning.

QA / Test Owner
  Отвечает за coverage matrix, regression tests, edge cases.
```

## 13.1 RACI shorthand

```text
R = Responsible
A = Accountable
C = Consulted
I = Informed
```

Пример:

```text
action_registry.md
  R: DSL Owner
  A: Game Systems Architect
  C: Validation Owner, Simulation Owner
  I: Product Owner

balance_model_spec.md
  R: Balance Owner
  A: Product Owner
  C: Simulation Owner, Game Systems Architect
  I: Generator Owner

extension_rfc_process.md
  R: Game Systems Architect
  A: Product Owner
  C: All subsystem owners
  I: Team
```

---

# 14. Extension Governance

Новая mechanic/primitive добавляется только через Extension RFC.

## 14.1 Extension RFC должен содержать

```text
ExtensionRFC:
  title
  motivation
  player-facing behavior
  mechanic description
  tier
  proposed MechanicStatus
  DSL model changes
  engine resolver changes
  validator changes
  static analysis changes
  simulation scenarios
  replay representation
  balance risk flags
  coverage examples
  edge cases
  migration impact
  catalog impact
  UI/API impact
  rejection alternatives
```

## 14.2 Approval gates

Новая механика может попасть в approved registry только если:

```text
[ ] описана в DSL;
[ ] имеет engine resolver;
[ ] имеет semantic validator;
[ ] имеет static analysis rules;
[ ] имеет simulation smoke tests;
[ ] имеет replay tests;
[ ] имеет at least 3 card examples;
[ ] имеет edge-case tests;
[ ] имеет balance risk classification;
[ ] имеет coverage matrix entry;
[ ] не требует hardcoded exceptions в core engine;
[ ] явно включается через RulesetProfile.
```

## 14.3 Предпочтение композиции

Перед добавлением нового primitive нужно доказать, что механику нельзя безопасно выразить существующими primitives.

Если можно выразить безопасной композицией, новый primitive не добавляется.

Если композиция возможна, но приводит к неоднозначности LLM-генерации, допускается canonical sugar primitive.

Пример:

```text
MAGNETIC можно частично выразить через AttachToEntityAction,
но как production mechanic лучше иметь first-class MAGNETIC_PLAY_MODE,
чтобы избежать несовместимых вариантов реализации.
```

---

# 15. Минимальные API-контракты

Дочерний `api_contracts_spec.md` должен описать минимум следующие контракты.

## 15.1 Validation Contract

```text
ValidateGeneratedCardRequest:
  candidate
  schemaVersion
  engineVersion
  primitiveRegistryVersion
  rulesetProfileId
  catalogSnapshotId
  validationMode

ValidateGeneratedCardResult:
  isValid
  schemaErrors
  deserializationErrors
  semanticErrors
  ruleErrors
  staticAnalysisErrors
  repairHints
  riskFlags
  blockedMechanics
```

## 15.2 Simulation Contract

```text
SimulationRequest:
  cardCandidate
  testScenarioSet
  catalogSnapshotId
  rulesetProfileId
  seed
  simulationMode
  maxTurns
  maxEventCount

SimulationResult:
  success
  scenarioResults
  stateHashes
  eventCount
  failureReason
  replayCandidate
  balanceSignals
  outlierScenarioIds
```

## 15.3 Replay Contract

```text
ReplayValidationRequest:
  seed
  commandLog
  catalogSnapshotId
  rulesetProfileId
  expectedFinalStateHash
  engineVersion

ReplayValidationResult:
  success
  finalStateHash
  mismatchStep
  mismatchReason
  rngRollLog
  replayTraceRef
```

## 15.4 Catalog Publication Contract

```text
PublishCardVersionCommand:
  cardCandidate
  validationReport
  simulationReport
  replayReport
  balanceReport
  approvalDecision
  reviewerId
  targetRulesetProfileId

PublishCardVersionResult:
  published
  cardId
  cardVersion
  catalogSnapshotId
  rejectionReason
  auditLogRef
```

---

# 16. Generation Context Builder

Generation Context Builder — backend-код, не LLM.

Он собирает безопасный и версионированный контекст генерации.

## 16.1 Контекст должен содержать

```text
schemaVersion
engineVersion
pipelineVersion
rulesetProfileId
cardSetId
formatId
catalogSnapshotId

allowedCardTypes
allowedClasses
allowedRarities
allowedTribes
allowedKeywords
allowedCanonicalMechanics
allowedEffectKinds
allowedActions
allowedTriggers
allowedPhases
allowedSourceZones
allowedTargets
allowedConditions
allowedResolutionPolicies
allowedFizzlePolicies

availableSummonRefs
availableDiscoverPools
availableRandomPools
availableCraftingRecipes
availableAdvisorCandidatePools
tokenTemplates

balanceLimits
complexityLimits
randomnessLimits
eventCountLimits
forbiddenCombos
assetModes
generationStyleRules
```

## 16.2 Запрещено передавать модели

```text
- весь CardCatalog;
- внутренние balance coefficients;
- disabled mechanics;
- deprecated mechanics без явного разрешения;
- mechanics вне RulesetProfile;
- raw game logs;
- неочищенные user prompts;
- runtime-only engine internals.
```

## 16.3 Модель получает только whitelist

```text
availableSummonRefs
availableDiscoverPools
availableRandomSpellPools
availableTokenTemplates
availableCraftingRecipes
availableAdvisorCandidatePools
allowedMechanics
allowedActionSchemas
allowedEffectKindSchemas
```

---

# 17. Skeleton Generation

Skeleton Generation создает верхний уровень карты.

Skeleton должен описывать:

```text
name
cardType
class/faction
rarity
manaCost
base stats
entity block
tribe/tags
keywords
design role
complexity target
power target
randomness target
effectSlots
assetMode
flavor direction
```

## 17.1 EffectSlotIntent

`EffectSlotIntent` — намерение эффекта, не финальный effect.

Он фиксирует:

```text
effectKind
trigger intent
source zone intent
action intent
target intent
randomness intent
history dependency
persistent modifier dependency
replacement/prevention behavior
power budget share
complexity
```

## 17.2 Skeleton не должен содержать

```text
- полные action payloads;
- случайно выбранные card refs вне whitelist;
- неразрешенные mechanics;
- произвольные новые triggers;
- произвольные новые keywords;
- engine-specific execution details.
```

---

# 18. EffectKind-aware Expansion

Effect Expansion превращает `EffectSlotIntent` в полноценный `EffectDefinition`.

Один action может иметь разную семантику.

Пример `SUMMON`:

```text
- battlecry summon token;
- deathrattle summon copy;
- summon from fixed pool;
- summon from filtered catalog pool;
- resurrect from history;
- summon self from deck;
- secret summon copy of opponent minion;
- summon generated crafted entity.
```

Поэтому expansion должен выбирать schema и контекст по `effectKind`.

## 18.1 EffectKind-классы

Архитектура должна предусматривать:

```text
SIMPLE_PLAY_EFFECT
TRIGGERED_BOARD_EFFECT
TRIGGERED_HAND_EFFECT
TRIGGERED_DECK_EFFECT
DEATHRATTLE_EFFECT
SECRET_EFFECT
QUEST_EFFECT
QUESTLINE_EFFECT
AURA_EFFECT
MODIFIER_EFFECT
HISTORY_BASED_EFFECT
REEXECUTABLE_HISTORY_EFFECT
POOL_BASED_EFFECT
BATCH_EFFECT
COMPOSITE_EFFECT
REPLACEMENT_EFFECT
PREVENTION_EFFECT
ALTERNATIVE_PLAY_EFFECT
CARD_CRAFTING_EFFECT
SMART_OFFER_EFFECT
HERO_CARD_EFFECT
HERO_POWER_EFFECT
LOCATION_ABILITY_EFFECT
WEAPON_EFFECT
START_OF_GAME_EFFECT
WIN_CONDITION_EFFECT
REST_OF_GAME_EFFECT
```

Финальный список — в `effect_kind_registry.md`.

---

# 19. Специализированные action-модели

Action не должен быть универсальным объектом с набором nullable-полей.

Используется:

```text
ActionDefinition = discriminated union / tagged union
```

Примеры:

```text
DealDamageAction
DamageDistributionAction
HealAction
FullyHealAction
DrawCardAction
DiscardCardAction
SummonAction
MoveSelfToBoardAction
DestroyAction
BatchDestroyAction
SilenceAction
FreezeAction
TransformAction
DiscoverAction
ApplyModifierAction
ApplyAuraAction
ApplyRestOfGameRuleAction
PreventDamageAction
RedirectDamageAction
ReplaceEventAction
ResurrectAction
ReplayCardPlayAction
ReplayEffectAction
CraftCustomCardAction
OfferSmartChoicesAction
TakeExtraTurnAction
AttachToEntityAction
ShuffleIntoDeckAction
WinGameAction
ReplaceHeroAction
ReplaceHeroPowerAction
```

Принцип:

```text
DEAL_DAMAGE не содержит summonSpec.
SUMMON не содержит damageSpec.
DISCOVER не содержит buffSpec.
TAKE_EXTRA_TURN не содержит targetDamageSpec.
```

Финальный список — в `action_registry.md`.

---

# 20. Canonical Keyword / Mechanic Registry

Нужен `Keyword Canonical Registry`.

Некоторые mechanics можно выразить через composition, но для production DSL они должны иметь каноническое представление.

Причина:

```text
LLM иначе будет генерировать одну механику разными несовместимыми способами.
```

Минимальный проверочный набор:

```text
TAUNT
RUSH
CHARGE
DIVINE_SHIELD
LIFESTEAL
POISONOUS
WINDFURY
STEALTH
IMMUNE
DORMANT
REBORN
DISCOVER
SECRET
QUEST
QUESTLINE
MAGNETIC
INFUSE
ENDLESSLY_INFUSE
MANATHIRST
TRADEABLE
FORGE
OUTCAST
COMBO
OVERLOAD
START_OF_GAME
BATTLECRY
DEATHRATTLE
```

Важно:

```text
Architecture registry может быть шире production registry.
RulesetProfile решает, какие mechanics включены.
```

---

# 21. Source Zones

Каждый effect должен явно описывать, из какой зоны он активен.

Минимальные зоны:

```text
BOARD
HAND
DECK
SECRET_ZONE
QUEST_ZONE
WEAPON_ZONE
LOCATION_ZONE
HERO_ZONE
GRAVEYARD
SET_ASIDE
GLOBAL
```

Без `sourceZones` невозможно корректно реализовать:

```text
- deck triggers;
- secrets;
- quests;
- weapons;
- locations;
- hand progress trackers;
- start-of-game effects;
- global rules;
- graveyard/history-dependent effects.
```

---

# 22. Phase / Timing Model

Trigger недостаточен. Нужна явная фазовая модель.

Минимальные фазы:

```text
DECK_SUBMITTED
MATCH_INITIALIZED
START_OF_GAME_PRE_MULLIGAN
START_OF_GAME_EFFECTS
MULLIGAN
TURN_0_SETUP

BEFORE_PLAY
PAY_COST
CARD_LEAVES_HAND
ON_PLAY
BATTLECRY_PHASE
ON_SUMMON
AFTER_SUMMON
SECRET_WINDOW
AFTER_PLAY

PRE_DAMAGE
DAMAGE_APPLIED
AFTER_DAMAGE
FATAL_DAMAGE_CHECK

DEATH_CHECK
DEATH_PROCESSING
DEATHRATTLE_PHASE
AFTER_DEATH

TURN_START
TURN_END
EXTRA_TURN_SCHEDULING
AURA_REFRESH
```

Start-of-game lifecycle:

```text
DECK_SUBMITTED
↓
MATCH_INITIALIZED
↓
START_OF_GAME_PRE_MULLIGAN
↓
START_OF_GAME_EFFECTS
↓
MULLIGAN
↓
TURN_0_SETUP
```

---

# 23. Resolution Policy

Каждый effect/action должен иметь стратегию разрешения.

Нужно поддерживать:

```text
SEQUENTIAL_RESOLUTION
SIMULTANEOUS_RESOLUTION
BATCH_RESOLUTION
RANDOM_SELECTION
REPEAT_UNTIL
FOR_EACH
CONDITIONAL_BRANCH
PARTIAL_RESOLUTION
EVENT_CHAIN_LIMITED_RESOLUTION
```

Пример batch destroy:

```text
1. Зафиксировать всех minions.
2. Выбрать survivor через RNG.
3. Одновременно уничтожить остальных.
4. Сформировать death events.
5. Обработать deathrattle в стабильном порядке.
```

---

# 24. Fizzle Policy

Каждый effect/action должен определить, что делать, если он не может выполниться.

Ситуации:

```text
- нет валидной цели;
- цель умерла;
- board full;
- hand full;
- deck empty;
- pool empty;
- карта исчезла из deck;
- random spell не имеет valid target;
- secret condition больше не валидна;
- history query ничего не вернул.
```

Политики:

```text
SKIP_ACTION
SKIP_INVALID_TARGETS
RESOLVE_PARTIAL
FIZZLE_WHOLE_EFFECT
RETRY_RANDOM_TARGET
FAIL_VALIDATION
```

---

# 25. Pool Resolver

Все эффекты, выбирающие карты/сущности из множества, должны работать через `PoolResolver`.

Виды пулов:

```text
FixedPool
CatalogQueryPool
FormatPool
ClassPool
TribePool
SpellSchoolPool
HistoryPool
TokenPool
GeneratedPool
ChoiceTreePool
AdvisorCandidatePool
```

Принцип:

```text
LLM задает pool query или выбирает poolId
↓
Validator проверяет pool
↓
Engine через CatalogSnapshot резолвит pool
↓
Deterministic RNG выбирает результат
↓
RngRollLog фиксирует выбор
```

---

# 26. History Store

Движку нужен полноценный `GameHistoryStore`.

Минимально:

```text
cardsPlayedThisGame
cardsPlayedFromHand
spellsCastThisGame
spellsPlayedFromHand
minionsDiedThisGame
minionsSummonedThisGame
battlecriesResolvedThisGame
deathrattlesResolvedThisGame
cardsGeneratedThisGame
damageEvents
healEvents
discardEvents
destroyEvents
shuffleEvents
heroPowerUsageEvents
cardOrigin
lastReturnedCard
lastSummonedEntity
lastDamagedEntity
lastDestroyedEntity
```

`graveyard` недостаточен, потому что карта могла быть:

```text
- умершей;
- resurrected;
- transformed;
- copied;
- shuffled;
- removed;
- silenced;
- killed multiple times.
```

---

# 27. ReExecutableCardPlayHistory

Для Shudderwock/Tess-like mechanics нужен общий `ReExecutableCardPlayHistory`.

Он должен хранить:

```text
originalCardSnapshot
cardType
cardClassAtPlayTime
controllerAtPlayTime
ownerAtPlayTime
playMode
sourceZoneAtPlayTime
replayableActions
battlecrySnapshot
spellEffectSnapshot
equipEffectSnapshot
heroCardEffectSnapshot
summonPolicy
randomRetargetPolicy
targetingPolicy
excludedSelfPolicy
classIdentitySnapshot
fizzlePolicy
```

Это Tier 1/Tier 2-зона, не MVP default.

---

# 28. Modifier System

Нужны:

```text
StatModifier
StatOverrideModifier
SetBaseStatsModifier
CostModifier
KeywordModifier
TriggerModifier
RuleModifier
ZoneModifier
TargetingModifier
DamageModifier
CardOriginModifier
```

Каждый modifier должен иметь:

```text
source
owner
scope
filter
duration
priority
stackingPolicy
expirationPolicy
crossZonePolicy
replayRepresentation
```

Для Caverns-like паттернов нужен:

```text
SetBaseStatsModifier / StatOverrideModifier

scope:
  HAND
  DECK
  BOARD
  FUTURE_SUMMONS

duration:
  REST_OF_GAME
```

---

# 29. Persistent Enchantment Carry Policy

Для Kingsbane-like effects нужен отдельный policy-слой:

```text
PersistentEnchantmentCarryPolicy:
  retainAcrossZones
  retainOnShuffle
  retainOnReturnToHand
  retainOnCopy
  retainOnTransform
  retainOnDestroy
  durationStillExpires
  sourceOwnershipPolicy
```

Это нужно для корректного replay/copy/transform semantics.

---

# 30. Aura System

Aura — modifier, зависящий от source lifecycle.

Aura должна:

```text
- появляться при появлении source;
- пересчитываться при изменении board;
- исчезать при исчезновении source;
- корректно стакаться;
- иметь priority;
- иметь stable recalculation order;
- логироваться;
- воспроизводиться через replay.
```

Aura нельзя моделировать как обычный permanent buff.

---

# 31. Replacement / Prevention / Playerbound Rules

Некоторые эффекты заменяют или предотвращают события до их применения.

Нужны:

```text
ReplacementWindow
PreDamageWindow
PreDeathWindow
PreDrawWindow
PlayerboundReplacementRule
RedirectOrReplaceDamageTakenAction
PreventDamageAction
```

Пример playerbound rule:

```text
owner: player
condition: damageTakenByOwner && ownerTurn
replacement: redirectToOpponent
duration: REST_OF_GAME
```

Replacement loops требуют отдельной спецификации.

---

# 32. Secret System

Secret — отдельная подсистема.

Secret:

```text
- играется как card;
- попадает в secret zone;
- скрыт от противника;
- имеет trigger/reveal condition;
- работает из secret zone;
- может использовать replacement/prevention window;
- раскрывается в строго определенной фазе;
- после срабатывания обычно consumed;
- может fizzled.
```

DSL должен поддерживать:

```text
SecretEffect
sourceZones: SECRET_ZONE
revealCondition
triggerWindow
timingPolicy
consumePolicy
fizzlePolicy
visibilityPolicy
replacementWindow
```

---

# 33. Quest и Questline

## 33.1 Quest

Quest — persistent progress tracker.

Нужны:

```text
QuestDefinition
ProgressTracker
ProgressRule
ProgressEventFilter
ProgressAggregation
RewardDefinition
CompletionPolicy
```

## 33.2 Questline

Questline — не просто Quest с большим счетчиком.

Нужен:

```text
QuestlineDefinition:
  stages[]
    progressRule
    requiredProgress
    reward
    nextStage
  finalReward
  stageTransitionPolicy
  visibilityPolicy
  replayRepresentation
```

Questline — Tier 1 после MVP.

---

# 34. Card Origin Tracking

Нужен обязательный `CardOrigin`.

Минимальные origin-состояния:

```text
STARTED_IN_DECK
GENERATED
DISCOVERED
COPIED
TOKEN
CREATED_BY_EFFECT
TRANSFORMED
RETURNED_FROM_BOARD
SHUFFLED_BY_EFFECT
CRAFTED_DURING_GAME
```

CardOrigin нужен для:

```text
- quests;
- generated-card conditions;
- discover;
- Tess-like replay;
- anti-abuse mechanics;
- balance analytics.
```

---

# 35. Damage Distribution

Для Denathrius-like effects нужен `DamageDistributionAction`.

Требования:

```text
DamageDistributionAction:
  totalAmount
  candidateTargets
  distributionMode
  perTargetInstancePolicy
  lifestealAggregationPolicy
  overkillPolicy
  fizzleIfNoTargets
  rngPolicy
```

Нужно отдельно специфицировать:

```text
- damage instances vs aggregated damage;
- Lifesteal interaction;
- Divine Shield interaction;
- death trigger timing;
- overkill-like effects;
- target disappearance mid-resolution.
```

---

# 36. Infuse / In-hand Progress Trackers

Для Infuse нужны эффекты, работающие из руки.

Нужен слой:

```text
InHandProgressTracker
InfuseProgressTracker
EndlesslyInfuseProgressTracker
```

Tracker должен иметь:

```text
sourceZones: HAND
progressEvent
progressCondition
progressAmount
threshold
onThresholdReached
upgradePolicy
endlessScalingPolicy
visibilityPolicy
```

Infuse нельзя моделировать как Battlecry.

---

# 37. Start-of-game Effects

Нужен отдельный `START_OF_GAME_EFFECT`.

Требования:

```text
sourceZone: DECK / GLOBAL
phase: START_OF_GAME_EFFECTS
deckValidationPredicate
priority
orderingPolicy
conflictPolicy
preMulliganPolicy
```

Нужна отдельная спецификация:

```text
start_of_game_ordering_spec.md
```

---

# 38. Take Extra Turn

Нужен отдельный `TakeExtraTurnAction`.

Требования:

```text
TakeExtraTurnAction:
  oncePerGame
  currentTurnEndsNormally
  extraTurnStartPolicy
  turnSchedulerPolicy
  commandButtonReplacement
  modifierDurationInteractionPolicy
```

Extra turn влияет на:

```text
- turn scheduler;
- end turn button;
- duration effects;
- start/end turn triggers;
- player priority;
- replay;
- UI command availability.
```

---

# 39. Alternative Play Modes и Magnetic

Нужен общий слой:

```text
AlternativePlayMode
PlayModeCondition
AlternativeTargetRequirement
AlternativeCostRule
AlternativeAction
```

`MAGNETIC` — first-class mechanic.

Нужны правила:

```text
MagneticPlayMode:
  targetRequirement: friendly Mech
  attachPolicy
  mergeStatsPolicy
  mergeKeywordsPolicy
  mergeEnchantmentsPolicy
  damageStatePolicy
  zoneTransitionPolicy
  silencePolicy
  copyPolicy
  entityIdentityPolicy
```

---

# 40. Card Crafting / Choice Tree

Card crafting делится на Basic и Full.

## 40.1 Basic Card Crafting — Tier 1

Покрывает Kazakus-like паттерны:

```text
predefined ChoiceTree
fixed choice steps
fixed recipe templates
generated spell snapshot
deterministic card compilation
generated card validation
no LLM during match
```

## 40.2 Full Card Crafting — Tier 2

Покрывает:

```text
generated minion compiler
generated weapon compiler
generated hero power compiler
multi-part composition
dynamic text assembly
complex generated-card validation
generated entity identity policy
advanced pool restrictions
```

---

# 41. Smart Offer / Advisor Engine

Zephrys-like карты требуют deterministic `AdvisorEngine`.

Требования:

```text
AdvisorEngine
GameStateEvaluator
CandidatePool
HeuristicScorer
ExplainableScoringModel
DeterministicTieBreaker
RegressionTestSuite
```

Минимальные scorer-классы:

```text
lethal detection
survival detection
board clear detection
mana curve scorer
taunt removal
weapon removal
silence value
card draw value
healing value
tempo swing
```

Это Tier 2. Для MVP не требуется.

---

# 42. Hero Cards и Hero Powers

## 42.1 Basic Hero Card — Tier 1

Поддерживает:

```text
ReplaceHeroAction
GainArmorAction
ReplaceHeroPowerAction
one-shot battlecry-like effect
asset/portrait replacement
replay representation
```

## 42.2 Advanced Hero Powers — Tier 2

Поддерживают:

```text
passive hero power
card-crafting hero power
upgradeable hero power
choice-based hero power
generated-card hero power
hero power with persistent rule changes
```

---

# 43. Weapons и Locations

## 43.1 Weapons

Weapon — отдельная entity.

Нужны:

```text
WeaponDefinition
WeaponZone
WeaponAttack
Durability
OnAttackEffect
AfterAttackEffect
OnBreakEffect
HeroAttackModifier
EnchantmentRetentionPolicy
```

Weapon нельзя моделировать как minion.

## 43.2 Locations

Location — persistent entity с activated ability.

Нужны:

```text
LocationDefinition
Durability
CooldownPolicy
ActivationCost
TargetRequirement
ActivatedAbility
ActivationLimit
LocationZone
```

Location не является minion.

## 43.3 MVP choice

Для MVP нужно выбрать:

```text
weapons OR locations
```

Не нужно реализовывать оба entity type одновременно, если это замедляет MVP.

---

# 44. Win Condition Effects

Нужны:

```text
WinGameAction
LoseGameAction
DestroyHeroAction
SetHeroHealthAction
AlternateWinCondition
```

Эти эффекты проходят отдельный high-risk balance gate.

---

# 45. Structured Outputs и JSON Schema strategy

Генератор должен использовать Structured Outputs.

JSON Schema отвечает за форму:

```text
required поля
типы
enum
union/discriminator
absence of additional properties
базовая структура
```

Semantic Validator отвечает за смысл:

```text
action совместим с effectKind
trigger совместим с sourceZones
phase корректна
questline имеет stages
secret имеет consume policy
modifier имеет duration
Magnetic имеет attach rules
Infuse работает из hand
TakeExtraTurn влияет на turn scheduler
pool не пуст
replayable history валиден
damage distribution имеет policy
```

На верхнем уровне карты допустимы взаимоисключающие блоки через `null`.

Пример:

```text
cardType = SPELL
minionBlock = null
spellBlock = present
weaponBlock = null
```

Внутри action-моделей не должно быть универсальных nullable-specs.

---

# 46. Validation Pipeline

Каждая карта после генерации проходит:

```text
1. Schema Validation
2. Deserialization Validation
3. Semantic Validation
4. Rule Validation
5. Static Analysis
6. Balance Validation
7. Simulation Validation
8. Replay Validation
```

## 46.1 Static Analysis проверяет

```text
- бесконечные trigger loops;
- бесконечный draw;
- бесконечный summon;
- бесконечную mana generation;
- repeat без cap;
- event storm;
- recursive token creation;
- invalid replacement loop;
- excessive random pool;
- non-deterministic behavior;
- cross-zone enchantment ambiguity.
```

## 46.2 Simulation Validation проверяет

```text
- isolated effect scenario;
- edge cases;
- generated deck scenario;
- random legal bot scenario;
- baseline deck scenario;
- full match completion;
- replay hash consistency.
```

---

# 47. Balance Validation

Balance validation должна иметь формальный выход.

## 47.1 BalanceReport

```text
BalanceReport:
  cardId
  cardVersion
  balanceModelVersion

  statScore
  tempoScore
  valueScore
  survivabilityScore
  manaCheatScore
  drawPowerScore
  removalScore
  boardImpactScore
  generationScore
  randomnessSwingScore
  comboRiskScore
  otkRiskScore
  snowballRiskScore
  complexityScore

  simulationWinrateDelta
  averageGameLengthDelta
  eventCountDelta
  outlierScenarioIds
  detectedRiskFlags

  recommendedAction:
    APPROVE
    REVIEW
    REJECT
    NERF
```

## 47.2 Risk flags

```text
CAN_GO_INFINITE
CAN_CHAIN_DRAW
CAN_CHAIN_MANA
CAN_GENERATE_UNBOUNDED_CARDS
CAN_CREATE_RECURSIVE_SUMMON
CAN_FORCE_OTK
HAS_EXTREME_RANDOM_SWING
HAS_UNBOUNDED_REPLAY_HISTORY
HAS_DANGEROUS_REPLACEMENT_LOOP
HAS_EXCESSIVE_EVENT_COUNT
```

## 47.3 Suggested thresholds v1

Точные числа должны быть вынесены в `balance_model_spec.md`, но базовый каркас должен быть таким:

```text
complexityScore:
  0-30   APPROVE range
  31-60  REVIEW range
  61-80  HIGH_RISK_REVIEW
  81+    REJECT for MVP

randomnessSwingScore:
  0-25   APPROVE range
  26-50  REVIEW range
  51-75  HIGH_RISK_REVIEW
  76+    REJECT for MVP

eventCountDelta:
  <= baseline + 20% APPROVE
  20-50%             REVIEW
  50-100%            HIGH_RISK_REVIEW
  >100%              REJECT for MVP

simulationWinrateDelta:
  -3% to +3% APPROVE
  +3% to +7% REVIEW
  +7% to +12% HIGH_RISK_REVIEW
  >+12% REJECT / NERF
```

BalanceValidator не обязан идеально определять “интересность” карты. Он обязан отсекать:

```text
- явно сломанные карты;
- бесконечные циклы;
- слишком сильный mana-cheat;
- слишком сильный draw;
- слишком сильную генерацию ресурсов;
- неконтролируемые random swings;
- OTK/high-roll сценарии;
- карты, ломающие симуляцию.
```

---

# 48. Scoped Repair

Repair должен быть точечным.

Нельзя перегенерировать всю карту при ошибке одного эффекта.

Правильный repair request:

```text
Исправь только effects[1].actions[0].
Не меняй:
- name
- manaCost
- stats
- rarity
- cardType
- assetMode

Ошибки:
- poolQuery resolves to empty pool
- sourceZone incompatible with trigger
- modifier duration missing
```

Типы repair:

```text
SkeletonRepair
EffectRepair
BalanceRepair
SchemaRepair
TextRepair
AssetPromptRepair
```

После repair карта снова проходит полный validation pipeline.

---

# 49. Catalog Publication Lifecycle

Публикация карты должна быть транзакционной и версионированной.

## 49.1 CardVersion

Каждая опубликованная карта имеет:

```text
cardId
cardVersion
schemaVersion
engineVersion
catalogSnapshotId
generationPipelineVersion
generatorPromptVersion
balanceModelVersion
assetManifestVersion
publishedAt
publicationStatus
```

## 49.2 CatalogSnapshot

`CatalogSnapshot` immutable.

Replay всегда ссылается на конкретный `catalogSnapshotId`.

Нельзя воспроизводить старую партию против изменившегося каталога.

## 49.3 Publication transaction

`PublishCandidate` должен быть atomic:

```text
1. Проверить validation report.
2. Проверить simulation report.
3. Проверить replay report.
4. Проверить balance report.
5. Проверить approval decision.
6. Проверить target RulesetProfile.
7. Создать CardVersion.
8. Создать новый CatalogSnapshot.
9. Записать audit log.
```

## 49.4 Deprecation policy

Карта не изменяется задним числом.

Если карта изменена, создается новая `CardVersion`.

Старые replay используют старую версию карты.

## 49.5 Rollback policy

Rollback не удаляет `CardVersion`.

Rollback только меняет active format snapshot.

## 49.6 Format legality

CardVersion может быть:

```text
legal
banned
rotated
experimental
deprecated
```

Format legality не должна менять саму CardVersion.

---

# 50. Asset Generation

Ассеты не являются частью gameplay engine.

Режимы:

```text
NONE
STATIC_IMAGE
IDLE_GIF
```

## 50.1 NONE

```text
placeholder
массовая генерация
balance simulation
без затрат на изображения
```

## 50.2 STATIC_IMAGE

```text
image prompt
статичная картинка
assetManifest
review
```

## 50.3 IDLE_GIF

```text
только после approve карты
короткая idle-анимация
отдельный review
fallback на STATIC_IMAGE или NONE
```

Game Engine видит только:

```text
assetManifestId
assetMode
assetState
```

---

# 51. Logging, Audit и Replay

Генерация должна быть полностью логируемой.

## 51.1 Generation Log

Нужно хранить:

```text
generationId
intent
contextVersion
schemaVersion
model
promptVersion
pipelineVersion
rawStructuredOutput
skeleton
effectSlots
expandedEffects
validationResults
repairAttempts
balanceReport
simulationReport
finalDecision
```

## 51.2 Replay

Каждая опубликованная карта должна иметь:

```text
cardId
cardVersion
schemaVersion
engineVersion
catalogSnapshotId
generationPipelineVersion
generatorPromptVersion
balanceModelVersion
assetManifestVersion
rulesetProfileId
```

Источник истины:

```text
seed + command log + catalog snapshot = deterministic replay
```

Любой random должен логироваться:

```text
rngRollId
poolId
candidateCount
selectedIndex
selectedCardOrEntity
preStateHash
postStateHash
```

---

# 52. Coverage-аудит и эталонные карты

Должен существовать файл:

```text
card_coverage_examples.md
```

Он должен содержать 20+ эталонных карт/механик.

## 52.1 Обязательные stress-test категории

```text
Ragnaros-like triggered board effect
Ice Block-like prevention secret
Brawl-like batch destroy
Loatheb-like temporary cost modifier
Caverns-like rest-of-game stat override
Kazakus-like basic card crafting
Zephrys-like smart offer
Shudderwock-like battlecry replay
Rexxar-like hero card + crafting hero power
Yogg-like random spell repeat
Reno-like highlander condition
Open the Waygate-like extra turn quest
Gul’dan-like hero card resurrect
Tess-like card play replay
Demon Seed-like questline replacement rule
Baku-like start-of-game deck validator
Patches-like deck trigger
Zilliax-like magnetic play mode
Kingsbane-like enchantment retention
Denathrius-like infuse + damage distribution
```

## 52.2 Двойная coverage matrix

Coverage matrix должна иметь две оси:

```text
Architecture Coverage
Production Readiness
```

Пример:

| #  | Паттерн          | Architecture Coverage | Production Readiness | Tier | Комментарий                       |
| -- | ---------------- | --------------------: | -------------------: | ---: | --------------------------------- |
| 1  | Ragnaros-like    |                     ✅ |                    ✅ |    0 | Простая triggered board карта     |
| 2  | Ice Block-like   |                     ✅ |                   🟨 |    1 | Нужна prevention/replacement spec |
| 3  | Brawl-like       |                     ✅ |                    ✅ |    0 | Batch destroy нужен в MVP         |
| 4  | Loatheb-like     |                     ✅ |                    ✅ |  0/1 | Cost modifier нужен рано          |
| 5  | Caverns-like     |                     ✅ |                   🟨 |    1 | Нужен stat override spec          |
| 6  | Kazakus-like     |                     ✅ |                   🟨 |    1 | Basic crafting spec               |
| 7  | Zephrys-like     |                     ✅ |                    ❌ |    2 | AdvisorEngine post-MVP            |
| 8  | Shudderwock-like |                     ✅ |                   🟨 |  1/2 | ReExecutable history              |
| 9  | Rexxar-like      |                     ✅ |                   🟨 |  1/2 | Hero + crafting                   |
| 10 | Yogg-like        |                     ✅ |                   🟨 |  1/2 | Random repeat risk                |
| 11 | Reno-like        |                     ✅ |                    ✅ |    0 | Simple highlander condition       |
| 12 | Extra turn quest |                     ✅ |                   🟨 |    1 | TurnScheduler spec                |
| 13 | Gul’dan-like     |                     ✅ |                   🟨 |    1 | Hero + history resurrect          |
| 14 | Tess-like        |                     ✅ |                   🟨 |  1/2 | ReExecutableCardPlayHistory       |
| 15 | Demon Seed-like  |                     ✅ |                   🟨 |  1/2 | Questline + replacement           |
| 16 | Baku-like        |                     ✅ |                   🟨 |    1 | Start-of-game ordering            |
| 17 | Patches-like     |                     ✅ |                   🟨 |    1 | Deck trigger                      |
| 18 | Zilliax-like     |                     ✅ |                   🟨 |    1 | Magnetic spec                     |
| 19 | Kingsbane-like   |                     ✅ |                   🟨 |    1 | Enchantment retention             |
| 20 | Denathrius-like  |                     ✅ |                   🟨 |    1 | Infuse + damage distribution      |

Legend:

```text
✅ supported / ready
🟨 supported, but needs detailed spec/tests
❌ not supported / not ready
```

## 52.3 Current intended coverage

```text
Architecture Coverage:
  20 / 20 stress patterns

Production-ready for MVP:
  Tier 0 subset only

Production DSL-ready without detailed specs:
  примерно 10–13 / 20

Requires separate production specs:
  примерно 7–10 / 20
```

---

# 53. Механики, требующие отдельных production specs

До production implementation нужны отдельные specs:

```text
1. AdvisorEngine / SmartOfferSpec
2. TurnSchedulerSpec
3. QuestlineSpec
4. MagneticSpec
5. PersistentEnchantmentSpec
6. DamageDistributionSpec
7. ReExecutableCardPlayHistorySpec
8. StartOfGameOrderingSpec
9. ReplacementLoopSpec
10. BalanceModelSpec
11. CatalogPublicationSpec
12. BasicCardCraftingSpec
13. BasicHeroCardSpec
14. APIContractsSpec
15. RulesetProfileSpec
16. MechanicStatusSpec
```

---

# 54. Влияние на игровой движок

Если генератор умеет создавать:

```text
SECRET_EFFECT
QUEST_EFFECT
QUESTLINE_EFFECT
AURA_EFFECT
HISTORY_BASED_EFFECT
REEXECUTABLE_HISTORY_EFFECT
START_OF_GAME_EFFECT
CARD_CRAFTING_EFFECT
SMART_OFFER_EFFECT
REPLACEMENT_EFFECT
DAMAGE_DISTRIBUTION_ACTION
MAGNETIC_PLAY_MODE
```

то движок обязан иметь:

```text
PhaseManager
StartOfGameResolver
SecretResolver
QuestResolver
QuestlineResolver
AuraSystem
ModifierSystem
ReplacementResolver
HistoryStore
ReExecutableCardPlayHistoryStore
PoolResolver
BatchResolver
ChoiceTreeResolver
AdvisorEngine
DamageDistributionResolver
TurnScheduler
ReplayRunner
CanonicalKeywordRegistry
RulesetProfileResolver
MechanicStatusRegistry
```

Если этих компонентов нет, генератор начнет производить карты, которые невозможно корректно исполнить.

---

# 55. Milestone boundaries

Scope большой. Разработка идет по milestones.

## Milestone 0 — Foundation

```text
[ ] Architecture Contract утвержден.
[ ] Repository structure создана.
[ ] TDD setup готов.
[ ] CI готов.
[ ] Docs skeleton готов.
[ ] card_coverage_examples.md создан.
[ ] product_ruleset_philosophy.md создан.
[ ] extension_rfc_process.md создан.
[ ] ruleset_profile_spec.md создан.
[ ] mechanic_status_spec.md создан.
```

## Milestone 1 — Primitive & DSL Foundation

```text
[ ] Primitive Registry v1.
[ ] Action Registry v1.
[ ] EffectKind Registry v1.
[ ] Phase Registry v1.
[ ] SourceZone Registry v1.
[ ] Keyword Canonical Registry v1.
[ ] JSON Schema v1.
[ ] Schema tests.
```

## Milestone 2 — Basic Generator MVP

```text
[ ] GenerationContext Builder.
[ ] RulesetProfile-aware GenerationContext.
[ ] Skeleton Generation.
[ ] Basic Effect Expansion.
[ ] Structured Outputs integration.
[ ] Scoped Repair v1.
[ ] Basic generation logs.
```

## Milestone 3 — Basic Engine Compatibility

```text
[ ] Simple play effects.
[ ] Battlecry-like on-play effect.
[ ] Deathrattle-like death effect.
[ ] Basic summon/damage/draw.
[ ] Basic target resolver.
[ ] Basic event queue.
[ ] Deterministic RNG.
```

## Milestone 4 — Validation & Simulation Gate

```text
[ ] Schema validation.
[ ] Semantic validation.
[ ] Rule validation.
[ ] Static analysis v1.
[ ] Balance report v1.
[ ] Simulation harness.
[ ] Replay validation.
```

## Milestone 5 — Tier 0 Mechanics

```text
[ ] Basic secrets.
[ ] Basic quests.
[ ] Basic aura/modifier.
[ ] Basic discover.
[ ] Batch destroy.
[ ] History-based resurrect.
[ ] Generated deck full match.
[ ] MVP card set generation.
[ ] MVP RulesetProfile locked.
```

## Milestone 6 — Controlled Tier 1 Compatibility

```text
[ ] Start-of-game effects.
[ ] Questline.
[ ] Take extra turn.
[ ] Basic hero cards.
[ ] Basic card crafting.
[ ] ReExecutableCardPlayHistory.
[ ] Magnetic.
[ ] Infuse.
[ ] Persistent enchantment carry.
[ ] Damage distribution.
[ ] Playerbound replacement.
```

## Milestone 7 — Advanced Systems

```text
[ ] Full card crafting.
[ ] Advanced hero powers.
[ ] AdvisorEngine prototype.
[ ] Advanced replacement loops.
[ ] Advanced balance simulation.
[ ] Broader re-executable card play semantics.
[ ] Meta-analysis prototype.
```

---

# 56. MVP Scope

MVP генератора должен поддерживать:

```text
- vanilla minion;
- simple spell;
- battlecry-like on-play effect;
- deathrattle-like death effect;
- triggered board effect;
- basic secret;
- basic quest;
- basic aura;
- basic modifier;
- basic discover;
- summon from fixed pool;
- resurrect from history;
- batch destroy;
- generated token;
- generated deck simulation;
- deterministic replay.
```

MVP не должен доказывать полную Hearthstone-совместимость.

MVP должен доказать:

```text
controlled DSL pipeline работает end-to-end
+
RulesetProfile реально ограничивает генерацию
+
generated cards могут играть полный матч
+
replay совпадает
```

---

# 57. TDD и тестирование

Разработка ведется через TDD.

Для каждого блока нужны:

```text
- valid example;
- invalid example;
- schema test;
- semantic validator test;
- static analysis test;
- simulation smoke test;
- replay test.
```

Для каждой механики нужны:

```text
- isolated unit tests;
- integration tests with engine;
- generated card tests;
- edge case tests;
- regression tests against card_coverage_examples.md.
```

Обязательные edge cases:

```text
- full board;
- full hand;
- empty deck;
- no valid targets;
- dead target before resolution;
- empty pool;
- duplicated aura source;
- simultaneous deaths;
- modifier expiration;
- cross-zone movement;
- replay mismatch;
- random retargeting;
- repair after invalid generation;
- disabled mechanic in RulesetProfile;
- deprecated mechanic in old replay;
- catalog version mismatch.
```

---

# 58. Definition of Done для MVP

Генератор считается готовым для production-ready MVP, если:

```text
[ ] Генерирует CardSkeleton через Structured Outputs.
[ ] Раскрывает effects через EffectKind-aware expansion.
[ ] Использует специализированные action-модели.
[ ] Не передает модели весь CardCatalog.
[ ] Использует whitelist/pool manifests.
[ ] Учитывает RulesetProfile.
[ ] Блокирует disabled mechanics.
[ ] Поддерживает sourceZones.
[ ] Поддерживает timingPolicy.
[ ] Поддерживает resolutionPolicy.
[ ] Поддерживает fizzlePolicy.
[ ] Поддерживает basic secrets.
[ ] Поддерживает basic quests.
[ ] Поддерживает basic aura/modifier.
[ ] Поддерживает history-based effects.
[ ] Поддерживает batch effects.
[ ] Поддерживает deterministic RNG через engine.
[ ] Выполняет schema validation.
[ ] Выполняет semantic validation.
[ ] Выполняет rule validation.
[ ] Выполняет static analysis.
[ ] Выполняет balance validation.
[ ] Выполняет simulation validation.
[ ] Выполняет replay validation.
[ ] Делает scoped repair.
[ ] Сохраняет generation logs.
[ ] Публикует только immutable CardVersion.
[ ] Проверяется через card_coverage_examples.md.
[ ] Полный match replay проходит на колодах со сгенерированными картами.
[ ] MVP использует только approved Tier 0 primitives.
[ ] Catalog publication создает immutable CatalogSnapshot.
```

---

# 59. Финальная архитектурная формула

Правильная система генерации карт:

```text
GenerationIntent
↓
RulesetProfile
↓
GenerationContext
↓
CardSkeleton
↓
EffectSlotIntent[]
↓
EffectKind-aware Expansion
↓
Specialized EffectDefinition[]
↓
GeneratedCardCandidate
↓
Schema Validation
↓
Semantic Validation
↓
Rule Validation
↓
Static Analysis
↓
Balance Validation
↓
Simulation Validation
↓
Replay Validation
↓
Scoped Repair if needed
↓
Catalog Candidate
↓
Published CardVersion
↓
CatalogSnapshot
```

Главная цель — не повторить Hearthstone.

Главная цель — построить controlled CCG platform, которая:

```text
- использует собственный approved Primitive Registry;
- умеет архитектурно выражать Hearthstone-class mechanics;
- не тащит historical baggage;
- валидирует каждую карту;
- симулирует каждую карту;
- воспроизводит каждую карту через deterministic replay;
- добавляет новые mechanics только через RFC/spec/test process;
- сохраняет стабильное ядро движка;
- разрешает mechanics через RulesetProfile;
- отличает architecture-supported от production-enabled.
```

Финальный принцип:

```text
Генератор не должен угадывать правила игры.
Он должен собирать карту из разрешенных строительных блоков.

Движок не должен доверять генератору.
Он должен валидировать, симулировать и воспроизводить.

Hearthstone-like карты — это stress-test, а не product target.

Production ruleset должен быть уже, строже и чище,
чем полный Hearthstone-like набор механик.
```
