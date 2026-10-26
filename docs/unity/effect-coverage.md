# Unity Effect Coverage

Status values: `unity-ready`, `engine-only`, `generator-blocked`, `presentation-blocked`, `future/deferred`.

| Effect family | Engine | Public state | Public event | Legal/command | Replay | Presentation | Status | Example/test |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| vanilla/stat-only | yes | yes | yes | yes | yes | baseline | unity-ready | public API smoke minion |
| play card | yes | yes | `CARD_PLAYED`, movement | `PLAY_CARD` | yes | baseline cue | unity-ready | legal action examples |
| attack/combat | yes | yes | damage/death | `ATTACK` | yes | baseline cue | unity-ready | combat tests |
| end turn | yes | yes | turn events | `END_TURN` | yes | silence/turn cue | unity-ready | public smoke |
| draw | yes | safe counts/cards | `CARD_DRAWN` | automatic | yes | baseline cue | unity-ready | replay/effect tests |
| fatigue | yes | fatigue counter | modifier/damage | automatic | yes | baseline | unity-ready | replay tests |
| targeted damage | yes | yes | `ENTITY_DAMAGED` | action dependent | yes | baseline | unity-ready | effect tests |
| healing | yes | yes | `ENTITY_HEALED` | action dependent | yes | baseline | unity-ready | effect tests |
| damage distribution | yes | yes | damage events | action dependent | yes | baseline | unity-ready | effect tests |
| batch destroy / partial invalid targets | yes | yes | destroyed/died | action dependent | yes | baseline | unity-ready | effect tests |
| death processing | yes | yes | `ENTITY_DIED` | automatic | yes | baseline | unity-ready | effect tests |
| deathrattle | yes | yes | `DEATHRATTLE_TRIGGERED` | automatic | yes | baseline | unity-ready | effect tests |
| discover/choice | yes | safe pending choices | `CHOICE_CREATED`, `CHOICE_RESOLVED` | `RESOLVE_CHOICE` | yes | baseline | unity-ready | choice tests |
| multi-target choice | yes | safe choices | choice/damage events | `RESOLVE_CHOICE` | yes | baseline | unity-ready | choice tests |
| prevention | yes | modifier state | `DAMAGE_WOULD_BE_DEALT`, `REPLACEMENT_RESOLVED` | automatic | yes | baseline | unity-ready | replacement tests |
| replacement | yes | modifier state | `REPLACEMENT_RESOLVED` | automatic | yes | baseline | unity-ready | replacement tests |
| divine shield consumption | yes | keyword/modifier | modifier expired/damage prevention | automatic | yes | baseline | unity-ready | replacement tests |
| aura refresh | yes | aura/modifier state | `AURA_REFRESHED` | automatic | yes | baseline | unity-ready | aura tests |
| board stat modifiers | yes | modifiers/entities | `MODIFIER_APPLIED` | action dependent | yes | baseline | unity-ready | modifier tests |
| board keyword modifiers | yes | keywords/modifiers | modifier events | action dependent | yes | baseline | unity-ready | modifier tests |
| hand zone modifiers | yes | self hand/modifiers | owner-filtered modifier events | action dependent | yes | baseline | unity-ready | modifier tests |
| cost modifiers | yes | legal action payload | modifier events | automatic | yes | baseline | unity-ready | cost tests |
| resurrect/history/graveyard | yes | graveyard public | move/summon events | action dependent | yes | baseline | unity-ready | history tests |
| quest progress | yes | quest state | `QUEST_PROGRESS_CHANGED` | automatic | yes | baseline | unity-ready | quest tests |
| quest reward | yes | quest state/events | choice/damage/draw events | automatic | yes | baseline | unity-ready | quest tests |
| secret reveal | yes | redacted until reveal | `SECRET_REVEALED` | automatic | yes | baseline | unity-ready | secret tests |
| weapon equip | yes | weapon slot | `WEAPON_EQUIPPED` | play card | yes | baseline | unity-ready | weapon tests |
| weapon attack | yes | weapon slot/entity damage | damage/durability via entity events | `ATTACK` | yes | baseline | unity-ready | weapon tests |
| weapon durability/destruction | yes | weapon slot | `ENTITY_DESTROYED` | automatic | yes | baseline | unity-ready | weapon tests |
| location activation | yes | location slot | `LOCATION_ACTIVATED` | `ACTIVATE_LOCATION` | yes | baseline | unity-ready | location tests |
| location durability | yes | location slot | entity damage/destroyed | automatic | yes | baseline | unity-ready | location tests |
| hero card / hero power replacement | yes | hero/heroPower | `HERO_CARD_PLAYED`, `HERO_POWER_REPLACED` | play card | yes | baseline | unity-ready | hero tests |
| hero power use | yes | heroPower | `HERO_POWER_USED` | `USE_HERO_POWER` | yes | baseline | unity-ready | hero power tests |
| extra turn | yes | turn state | `EXTRA_TURN_SCHEDULED` | automatic | yes | baseline | unity-ready | extra turn tests |
| engine fault terminal handling | yes | terminal state | `ENGINE_FAULTED` | automatic | yes | no animation guarantee | unity-ready | fault tests |
| replay RNG trace | yes | not live state | replay/debug only | automatic | yes | none | unity-ready | replay tests |

Generated media polish is now available through in-repo theme, animation binding, and audio cue routing. Final commissioned art/audio quality remains deferred; contract availability remains ready.
