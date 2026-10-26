# Unity Event Animation Map

Use this as the first animation system map. State remains authoritative; animations are presentation hints over public events.

| Event type | Animation category | Required visual target | Optional visual target | Refresh | Timing | Blocking | Fallback | Presentation mapping | Ignore safely | Reconnect trigger |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `GAME_STARTED` | setup | board root | players | yes | immediate | blocking | render latest state | timeline silence | no | yes if missing state |
| `TURN_STARTED` | turn | active player banner | mana tray | yes | 250 ms | non-blocking | update UI instantly | silence/cue | no | no |
| `EXTRA_TURN_SCHEDULED` | turn | active player banner | turn queue marker | yes | 250 ms | non-blocking | show text marker | silence | yes | no |
| `TURN_ENDED` | turn | ending player banner | action bar | yes | 250 ms | non-blocking | update UI instantly | silence/cue | no | no |
| `CARD_PLAYED` | card | source card or hand slot | board/graveyard target | yes | 400 ms | blocking for command chain | snap to refreshed state | `OnPlay` cue | no | no |
| `CARD_MOVED_ZONE` | zone | source/destination zone | card instance | yes | 300 ms | non-blocking | update counts/state | cue when available | no | no |
| `CARD_DRAWN` | zone | owner hand/deck count | card back | yes | 300 ms | non-blocking | update hand/count | cue when available | no | no |
| `CARD_DISCARDED` | zone | hand/graveyard | card instance | yes | 300 ms | non-blocking | update state | cue when available | yes | no |
| `CARD_RETURNED_TO_HAND` | zone | board entity | hand slot | yes | 300 ms | non-blocking | update state | cue when available | yes | no |
| `MINION_SUMMONED` | board | board slot | source card | yes | 350 ms | blocking for chain clarity | spawn from state | `OnSummon` cue | no | no |
| `ENTITY_DAMAGED` | combat | damaged entity/hero | source entity/card | yes | 300 ms | blocking for death chains | show state damage | `OnAttack` or `OnTrigger` cue | no | no |
| `DAMAGE_WOULD_BE_DEALT` | prevention | proposed target | source | usually | 150 ms | non-blocking | skip and wait for resolved event | silence | yes | no |
| `REPLACEMENT_RESOLVED` | prevention | replaced target/effect | source modifier | yes | 250 ms | non-blocking | show generic shield/prevent | `OnTrigger` cue | yes | no |
| `ENTITY_HEALED` | combat | healed entity/hero | source | yes | 300 ms | non-blocking | update health | cue when available | yes | no |
| `MANA_CHANGED` | resources | mana tray | player portrait | yes | 150 ms | non-blocking | update instantly | silence | yes | no |
| `ENTITY_DESTROYED` | board | entity/weapon/location slot | graveyard | yes | 350 ms | blocking for death chains | remove from refreshed state | `OnDeath` cue | no | no |
| `ENTITY_DIED` | board | dead entity | graveyard | yes | 350 ms | blocking for deathrattle | remove from refreshed state | `OnDeath` cue | no | no |
| `DEATHRATTLE_TRIGGERED` | trigger | triggering entity/card | affected targets | yes | 350 ms | blocking | generic trigger pulse | `OnTrigger` cue | no | no |
| `SECRET_REVEALED` | secret | secret slot | triggering target | yes | 450 ms | blocking | reveal panel from state | `OnSecretReveal` cue | no | no |
| `QUEST_PROGRESS_CHANGED` | quest | quest tracker | source event target | yes | 250 ms | non-blocking | update tracker | `OnQuestComplete` when completed | yes | no |
| `MODIFIER_APPLIED` | modifier | affected entity/player/card | source | yes | 250 ms | non-blocking | update badges/stats | `OnTrigger` cue | yes | no |
| `MODIFIER_EXPIRED` | modifier | affected entity/player/card | source | yes | 200 ms | non-blocking | remove badge | silence | yes | no |
| `AURA_REFRESHED` | modifier | board modifier badges | aura source | yes | 200 ms | non-blocking | refresh all badges | silence | yes | no |
| `WEAPON_EQUIPPED` | equipment | weapon slot | hero | yes | 350 ms | non-blocking | update slot | cue when available | no | no |
| `LOCATION_PLAYED` | location | location slot | source card | yes | 350 ms | non-blocking | update slot | cue when available | no | no |
| `LOCATION_ACTIVATED` | location | location slot | target | yes | 350 ms | blocking for effect chain | pulse location | `OnTrigger` cue | no | no |
| `HERO_CARD_PLAYED` | hero | hero portrait | armor/hero power | yes | 450 ms | blocking | refresh hero UI | `OnPlay` cue | no | no |
| `HERO_POWER_USED` | hero power | hero power button | target | yes | 350 ms | blocking for effect chain | pulse button | `OnTrigger` cue | no | no |
| `HERO_POWER_REPLACED` | hero power | hero power slot | hero card | yes | 300 ms | non-blocking | replace from state | cue when available | no | no |
| `CHOICE_CREATED` | choice | choice panel | source card | yes | immediate | blocks input for owner | show generic choice | silence | no for owner | yes if owner cannot render |
| `CHOICE_RESOLVED` | choice | choice panel | selected target | yes | 250 ms | blocking | close panel and refresh | cue when available | no | no |
| `RNG_ROLLED` | debug/replay | replay panel | none | no live | none | non-blocking | ignore live | none | yes live | no |
| `GAME_ENDED` | terminal | match result panel | final board | yes | 500 ms | blocking | show terminal state | silence/cue | no | yes if final state missing |
| `ENGINE_FAULTED` | terminal fault | fault panel | logs | yes | immediate | blocking | disable input | silence | no | yes |

Runtime stage note: every declared public event has a named presentation binding and an audio cue. `PresentationEffectsHost` performs presentation-only flash, pulse, banner, and unscaled-time tween playback over the gameplay Canvas. It plays the registered asset cue when available and uses a short procedural tone as the safe local fallback. Reduced-motion mode shortens the visual transition without suppressing state refresh. Unknown future events use the generic visible/audio fallback and never affect gameplay state, RNG, command logs, or replay hashes. Bespoke authored VFX/audio assets can replace these bindings without changing event routing.
