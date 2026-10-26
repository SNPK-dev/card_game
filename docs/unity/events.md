# Unity Events

Unity consumes `PublicMatchEventRecord` from command responses, event polling, and reconnect. Events are ordered by `eventSeq`; use `afterEventSeq` as an exclusive cursor. Store the highest processed `eventSeq` and ignore duplicates.

Unknown future event types must not crash the client. Log, refresh state, and continue unless the event is `ENGINE_FAULTED`. Animation handling details are defined in `event-animation-map.md`.

## Event Matrix

| Event | Visibility | Unity implication | Refresh required |
| --- | --- | --- | --- |
| `GAME_STARTED` | Public | Initialize board presentation. | Yes |
| `TURN_STARTED` | Public | Update turn banner/resources. | Yes |
| `EXTRA_TURN_SCHEDULED` | Public | Queue extra-turn indicator. | Yes |
| `TURN_ENDED` | Public | End active-player affordances. | Yes |
| `CARD_PLAYED` | Public/owner filtered | Animate card leaving hand. | Yes |
| `CARD_MOVED_ZONE` | Public/owner filtered | Move card between visible zones or update counts. | Yes |
| `CARD_DRAWN` | Public/owner filtered | Draw animation for owner; count update for others. | Yes |
| `CARD_DISCARDED` | Public | Discard animation. | Yes |
| `CARD_RETURNED_TO_HAND` | Public/owner filtered | Return entity/card to hand. | Yes |
| `MINION_SUMMONED` | Public | Spawn board entity. | Yes |
| `ENTITY_DAMAGED` | Public | Damage number and health update. | Yes |
| `DAMAGE_WOULD_BE_DEALT` | Public-safe/replay | Pre-damage prevention cue. | Usually |
| `REPLACEMENT_RESOLVED` | Public-safe/replay | Replacement/prevention cue. | Usually |
| `ENTITY_HEALED` | Public | Healing number and health update. | Yes |
| `MANA_CHANGED` | Public | Resource UI update. | Yes |
| `ENTITY_DESTROYED` | Public | Destroy animation. | Yes |
| `ENTITY_DIED` | Public | Death animation and graveyard update. | Yes |
| `DEATHRATTLE_TRIGGERED` | Public | Trigger animation. | Yes |
| `SECRET_REVEALED` | Public after reveal | Secret reveal animation. | Yes |
| `QUEST_PROGRESS_CHANGED` | Public/owner filtered | Quest progress UI. | Yes |
| `MODIFIER_APPLIED` | Public/owner filtered | Buff/debuff/keyword/cost UI update. | Yes |
| `MODIFIER_EXPIRED` | Public/owner filtered | Remove modifier UI. | Yes |
| `AURA_REFRESHED` | Public | Recompute aura indicators. | Yes |
| `WEAPON_EQUIPPED` | Public | Weapon slot animation. | Yes |
| `LOCATION_PLAYED` | Public | Location slot animation. | Yes |
| `LOCATION_ACTIVATED` | Public | Location activation animation. | Yes |
| `HERO_CARD_PLAYED` | Public | Hero replacement animation. | Yes |
| `HERO_POWER_USED` | Public | Hero power animation. | Yes |
| `HERO_POWER_REPLACED` | Public | Hero power slot replacement. | Yes |
| `CHOICE_CREATED` | Owner/private or public | Show choice UI only when visible. | Yes |
| `CHOICE_RESOLVED` | Public/owner filtered | Close choice UI and animate result. | Yes |
| `RNG_ROLLED` | Replay/debug only when surfaced | Do not animate live RNG internals. | No |
| `GAME_ENDED` | Public | End-of-match UI. | Yes |
| `ENGINE_FAULTED` | Public terminal fault | Disable input and show fault state. | Yes |

Realtime push is not required for v1; see `realtime.md`.
