using Game.Generator.Providers;
using Game.Shared.Dsl;
using Game.Shared.Primitives;

namespace Game.UnityCardPackExporter;

public static class UnityCardPackScenarioCatalog
{
    public static IReadOnlyList<UnityCardPackScenario> CreateDefault()
    {
        return
        [
            Scenario(
                "plain-guardian",
                ["vanilla-minion", "statline-display", "board-minion"],
                "Simple creature without rules text for baseline card, hand, board, and stats rendering.",
                new StructuredCardOutput(
                    "Plain Guardian",
                    CardType.MinionCard,
                    ["mage"],
                    Rarity.Common,
                    2,
                    [],
                    [],
                    true,
                    ["standard"],
                    2,
                    3,
                    true,
                    false,
                    "A reliable 2/3 minion with no effect.",
                    [],
                    1,
                    0,
                    [],
                    Prompt("clear fantasy trading-card art of a plain armored guardian with blue cloth, centered full body, readable silhouette"),
                    Prompt("idle breathing pose for an armored guardian, subtle cape movement"),
                    Prompt("short shield-ready chime"),
                    ["ON_PLAY"],
                    "SFX_ONLY")),

            Scenario(
                "spark-herald",
                ["battlecry", "on-play-effect", "targeted-minion-effect"],
                "Battlecry/on-play minion for Unity command preview, target highlighting, and effect resolution playback.",
                new StructuredCardOutput(
                    "Spark Herald",
                    CardType.MinionCard,
                    ["mage"],
                    Rarity.Common,
                    3,
                    [],
                    ["BATTLECRY"],
                    true,
                    ["standard"],
                    2,
                    4,
                    true,
                    true,
                    "Battlecry: Deal 1 damage to a minion.",
                    [
                        Effect(EffectKind.SimplePlayEffect, Phase.OnPlay, SourceZone.Hand, ["BATTLECRY"],
                            [new DealDamageActionDefinition("battlecry_damage", Fixed(1), Target("chosenMinion"), DamageSchool: null, Lifesteal: false, Preventable: true)],
                            TargetSpec("chosenMinion", EntityType.Minion, "ANY"))
                    ],
                    2,
                    0,
                    [],
                    Prompt("young herald carrying crackling blue sparks, fantasy card art, clean silhouette, no text"),
                    Prompt("idle herald raises a sparking hand, looped subtle energy"),
                    Prompt("bright spark impact"),
                    ["ON_PLAY", "IMPACT"],
                    "SFX_ONLY")),

            Scenario(
                "last-light-acolyte",
                ["deathrattle", "draw-card", "death-playback"],
                "Deathrattle minion for death event sequencing, graveyard transition, and draw animation hooks.",
                new StructuredCardOutput(
                    "Last-Light Acolyte",
                    CardType.MinionCard,
                    ["mage"],
                    Rarity.Rare,
                    2,
                    [],
                    ["DEATHRATTLE"],
                    true,
                    ["standard"],
                    2,
                    2,
                    true,
                    false,
                    "Deathrattle: Draw a card.",
                    [
                        Effect(EffectKind.DeathrattleEffect, Phase.DeathrattlePhase, SourceZone.Board, ["DEATHRATTLE"],
                            [new DrawCardActionDefinition("deathrattle_draw", PlayerSelector.Self, Fixed(1), "FATIGUE", "drawn_card")])
                    ],
                    2,
                    0,
                    [],
                    Prompt("solemn acolyte holding a fading lantern, fantasy card art, centered readable character"),
                    Prompt("lantern light slowly fades then returns, seamless idle loop"),
                    Prompt("soft fading lantern tone"),
                    ["DEATH", "DRAW"],
                    "SFX_ONLY")),

            Scenario(
                "piercing-bolt",
                ["targeted-spell", "spell-targeting", "damage"],
                "Targeted spell for legal action UI, command submission with target ids, and impact event playback.",
                new StructuredCardOutput(
                    "Piercing Bolt",
                    CardType.SpellCard,
                    ["mage"],
                    Rarity.Common,
                    2,
                    [],
                    [],
                    true,
                    ["standard"],
                    null,
                    null,
                    false,
                    true,
                    "Deal 3 damage to a minion.",
                    [
                        Effect(EffectKind.SimplePlayEffect, Phase.OnPlay, SourceZone.Hand, [],
                            [new DealDamageActionDefinition("bolt_damage", Fixed(3), Target("chosenMinion"), DamageSchool: null, Lifesteal: false, Preventable: true)],
                            TargetSpec("chosenMinion", EntityType.Minion, "ANY"))
                    ],
                    2,
                    0,
                    [],
                    Prompt("single blue lightning bolt striking a training dummy, fantasy spell card art, high contrast"),
                    Prompt("electric bolt crackles forward in a short loop"),
                    Prompt("sharp lightning strike"),
                    ["CAST", "IMPACT"],
                    "SFX_ONLY")),

            Scenario(
                "chaos-spark",
                ["random-target-spell", "random-resolution", "multi-hit-playback"],
                "Random-target spell for client-side event stream playback without predicting RNG locally.",
                new StructuredCardOutput(
                    "Chaos Spark",
                    CardType.SpellCard,
                    ["mage"],
                    Rarity.Rare,
                    3,
                    [],
                    ["RANDOM_TARGET"],
                    true,
                    ["standard"],
                    null,
                    null,
                    false,
                    false,
                    "Deal 3 damage randomly split among enemy minions.",
                    [
                        Effect(EffectKind.SimplePlayEffect, Phase.OnPlay, SourceZone.Hand, ["RANDOM_TARGET_RESOLUTION"],
                            [new DamageDistributionActionDefinition("chaos_split", Fixed(3), Query("OPPONENT", [SourceZone.Board], [EntityType.Minion]), "RANDOM_SPLIT", "SEPARATE_DAMAGE_INSTANCES", "NO_LIFESTEAL", "ALLOW_OVERKILL", true, "RANDOM_TARGET")],
                            TargetSpec("enemyMinions", EntityType.Minion, "OPPONENT"))
                    ],
                    3,
                    2,
                    [],
                    Prompt("erratic arcane sparks bouncing between shadowy minions, fantasy spell art, no text"),
                    Prompt("three small sparks jump between targets in a loop"),
                    Prompt("three quick arcane pops"),
                    ["CAST", "IMPACT"],
                    "SFX_ONLY")),

            Scenario(
                "archive-choice",
                ["discover-choice", "choice-ui", "generated-options"],
                "Discover/choice card for modal option rendering and selected-result command handling.",
                new StructuredCardOutput(
                    "Archive Choice",
                    CardType.SpellCard,
                    ["mage"],
                    Rarity.Rare,
                    2,
                    [],
                    ["DISCOVER"],
                    true,
                    ["standard"],
                    null,
                    null,
                    false,
                    false,
                    "Discover a card from the Unity test pool.",
                    [
                        Effect(EffectKind.SimplePlayEffect, Phase.OnPlay, SourceZone.Hand, ["DISCOVER_FIXED_POOL_CHOICE"],
                            [new DiscoverActionDefinition("discover_choice", PlayerSelector.Self, new PoolRef("UNITY_FIXED_POOL"), 3, "PLAYER_CHOICE", SourceZone.Hand, "FIZZLE", "discover_result")])
                    ],
                    3,
                    0,
                    [],
                    Prompt("open magical archive book showing three glowing card choices, fantasy card art"),
                    Prompt("three cards rise from an open book in a subtle idle loop"),
                    Prompt("soft page turn with magic shimmer"),
                    ["CAST", "CHOICE"],
                    "SFX_ONLY")),

            Scenario(
                "warding-rune",
                ["damage-prevention", "replacement-effect", "shield-vfx"],
                "Prevention/replacement card for shield indicators and future damage replacement event playback.",
                new StructuredCardOutput(
                    "Warding Rune",
                    CardType.SpellCard,
                    ["mage"],
                    Rarity.Rare,
                    1,
                    [],
                    ["DAMAGE_PREVENTION"],
                    true,
                    ["standard"],
                    null,
                    null,
                    false,
                    true,
                    "Prevent the next 2 damage that would be dealt to a friendly character.",
                    [
                        Effect(EffectKind.SimplePlayEffect, Phase.OnPlay, SourceZone.Hand, ["DAMAGE_PREVENTION"],
                            [new PreventDamageActionDefinition("ward_prevent", Target("friendlyCharacter"), Fixed(2), "NEXT_DAMAGE", "PREVENT_UP_TO_AMOUNT", "ward_prevent_result")],
                            TargetSpec("friendlyCharacter", EntityType.Minion, "SELF"))
                    ],
                    3,
                    0,
                    [],
                    Prompt("glowing blue protective rune forming a shield around an ally, fantasy spell card art"),
                    Prompt("protective rune pulses around a silhouette, seamless loop"),
                    Prompt("gentle shield pulse"),
                    ["CAST", "SHIELD"],
                    "SFX_ONLY")),

            Scenario(
                "copper-vanguard-blade",
                ["weapon-card", "weapon-equip", "durability-display"],
                "Weapon card for client equip zone, attack/durability presentation, and weapon asset refs.",
                new StructuredCardOutput(
                    "Copper Vanguard Blade",
                    CardType.WeaponCard,
                    ["mage"],
                    Rarity.Common,
                    3,
                    [],
                    ["WEAPON"],
                    true,
                    ["standard"],
                    2,
                    3,
                    true,
                    false,
                    "A 2/3 weapon.",
                    [],
                    1,
                    0,
                    [],
                    Prompt("polished copper sword on a blue cloth background, fantasy weapon card art, centered object"),
                    Prompt("copper sword glints with a subtle idle shine"),
                    Prompt("short metallic equip sound"),
                    ["EQUIP"],
                    "SFX_ONLY")),

            Scenario(
                "emberwell-outpost",
                ["location-card", "location-ability", "durability-display"],
                "Location card for board slot rendering, durability UI, and activation playback contract checks.",
                new StructuredCardOutput(
                    "Emberwell Outpost",
                    CardType.LocationCard,
                    ["mage"],
                    Rarity.Epic,
                    2,
                    [],
                    ["LOCATION"],
                    true,
                    ["standard"],
                    null,
                    2,
                    false,
                    false,
                    "Location ability: Draw a card.",
                    [
                        Effect(EffectKind.LocationAbilityEffect, Phase.OnPlay, SourceZone.Board, ["LOCATION_PLAY"],
                            [new DrawCardActionDefinition("location_draw", PlayerSelector.Self, Fixed(1), "FATIGUE", "drawn_card")])
                    ],
                    3,
                    0,
                    [],
                    Prompt("small fortified magical outpost with ember-lit windows, fantasy location card art, clear landmark"),
                    Prompt("embers glow in the outpost windows, subtle looping idle"),
                    Prompt("low ember flare"),
                    ["ACTIVATE"],
                    "SFX_ONLY")),

            Scenario(
                "banner-of-the-line",
                ["aura-modifier", "continuous-effect", "board-buff-display"],
                "Aura/modifier card for continuous board buff display and aura refresh event handling.",
                new StructuredCardOutput(
                    "Banner of the Line",
                    CardType.MinionCard,
                    ["mage"],
                    Rarity.Epic,
                    3,
                    [],
                    ["AURA"],
                    true,
                    ["standard"],
                    1,
                    4,
                    true,
                    false,
                    "Your other minions have +1 Attack while this is in play.",
                    [
                        Effect(EffectKind.AuraEffect, Phase.AuraRefresh, SourceZone.Board, ["AURA_REFRESH", "BOARD_ATTACK_MODIFIER"],
                            [new ApplyAuraActionDefinition("banner_aura", PlayerSelector.Self, Aura("friendly_board_attack", "WHILE_SOURCE_ACTIVE", "BOARD_ATTACK_MODIFIER", 1))])
                    ],
                    4,
                    0,
                    [],
                    Prompt("battle standard planted behind allied soldiers, fantasy card art, blue and silver banner"),
                    Prompt("banner waves gently with a magical glow"),
                    Prompt("soft rally horn"),
                    ["AURA"],
                    "SFX_ONLY")),

            Scenario(
                "renewing-scroll",
                ["heal", "draw-card", "multi-action-spell"],
                "Heal/draw card for multi-action event ordering and friendly target presentation.",
                new StructuredCardOutput(
                    "Renewing Scroll",
                    CardType.SpellCard,
                    ["mage"],
                    Rarity.Common,
                    2,
                    [],
                    ["HEAL"],
                    true,
                    ["standard"],
                    null,
                    null,
                    false,
                    true,
                    "Restore 2 Health to a friendly minion. Draw a card.",
                    [
                        Effect(EffectKind.SimplePlayEffect, Phase.OnPlay, SourceZone.Hand, [],
                            [
                                new HealActionDefinition("restore_health", Fixed(2), Target("friendlyMinion")),
                                new DrawCardActionDefinition("scroll_draw", PlayerSelector.Self, Fixed(1), "FATIGUE", "drawn_card")
                            ],
                            TargetSpec("friendlyMinion", EntityType.Minion, "SELF"))
                    ],
                    2,
                    0,
                    [],
                    Prompt("ancient blue scroll with healing runes and soft light, fantasy spell card art"),
                    Prompt("healing runes float upward from an opened scroll"),
                    Prompt("warm healing shimmer"),
                    ["CAST", "HEAL", "DRAW"],
                    "SFX_ONLY")),

            Scenario(
                "grave-summons",
                ["resurrect", "graveyard-value", "history-query"],
                "Resurrect/value card for graveyard-derived events and replay/state reconstruction checks.",
                new StructuredCardOutput(
                    "Grave Summons",
                    CardType.SpellCard,
                    ["mage"],
                    Rarity.Epic,
                    4,
                    [],
                    ["RESURRECT"],
                    true,
                    ["standard"],
                    null,
                    null,
                    false,
                    false,
                    "Resurrect a friendly minion that died this game.",
                    [
                        Effect(EffectKind.SimplePlayEffect, Phase.OnPlay, SourceZone.Hand, ["GRAVEYARD_RESURRECT"],
                            [new ResurrectActionDefinition("grave_resurrect", Target("ownGraveyardMinion"), PlayerSelector.Self, Fixed(2), Fixed(2), "RIGHTMOST", "SKIP_ACTION", "resurrected_minion", History("OWN_GRAVEYARD_MINION", PlayerSelector.Self))])
                    ],
                    4,
                    0,
                    [],
                    Prompt("friendly spirit rising from a blue-lit grave marker, fantasy spell card art, hopeful tone"),
                    Prompt("blue spirit rises then fades into a loop"),
                    Prompt("ethereal resurrection swell"),
                    ["CAST", "SUMMON"],
                    "SFX_ONLY"))
        ];
    }

    private static UnityCardPackScenario Scenario(string slug, IReadOnlyList<string> coverage, string unityComment, StructuredCardOutput output)
        => new(slug, coverage, unityComment, output);

    private static AssetGenerationPrompt Prompt(string text)
        => new(text, "docs/generator/asset-pipeline.md", "local-provider-safety-v1");

    private static EffectDefinition Effect(
        EffectKind kind,
        Phase phase,
        SourceZone sourceZone,
        IReadOnlyList<string> tags,
        IReadOnlyList<ActionDefinition> actions,
        TargetSpec? targetSpec = null)
    {
        return new EffectDefinition(
            Guid.NewGuid().ToString("N"),
            kind,
            [sourceZone],
            new TriggerDefinition(EventType.CardPlayed, EventFilter: null, ControllerScope: "SOURCE_CONTROLLER", OncePerEvent: false),
            new TimingPolicy(phase, Window: null, Priority: 0, "CARD_TEXT_ORDER"),
            [],
            targetSpec,
            actions,
            new ResolutionPolicy("SOURCE_ORDER"),
            new FizzlePolicy("FIZZLE_WHOLE_EFFECT"),
            new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
            tags);
    }

    private static TargetRef Target(string id) => new(id);

    private static TargetSpec TargetSpec(string requirementId, EntityType entityType, string controller)
    {
        return new TargetSpec(
            Required: true,
            TargetSelectionMode.PlayerChosen,
            Query(controller, [SourceZone.Board, SourceZone.Hand, SourceZone.Graveyard], [entityType]),
            new CountSpec(1, 1),
            "FIZZLE");
    }

    private static AuraDefinition Aura(string id, string duration, string modifierType, int value)
    {
        return new AuraDefinition(
            id,
            "CONTINUOUS_MODIFIER",
            Query("SELF", [SourceZone.Board], [EntityType.Minion]),
            new ModifierDefinition($"{id}_modifier", modifierType, Fixed(value), duration, 0),
            duration,
            0);
    }

    private static EntityQuery Query(string controller, IReadOnlyList<SourceZone> zones, IReadOnlyList<EntityType> entityTypes)
        => new(controller, zones, entityTypes, RequiredTags: [], ExcludedTags: [], AliveOnly: true);

    private static ValueExpression Fixed(int value) => new("FIXED", value, Ref: null);

    private static HistoryQuerySpec History(string kind, PlayerSelector player)
        => new(kind, player, "LAST", 1);
}

public sealed record UnityCardPackScenario(
    string Slug,
    IReadOnlyList<string> Coverage,
    string UnityComment,
    StructuredCardOutput Output);
