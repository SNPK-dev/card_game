using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Primitives;

namespace Game.Application.Players;

public static class StarterHeroPowerCatalog
{
  private const string TargetRefId = "targetRequirement";

  public static string CardId(StarterHeroProfile profile) =>
    $"starter_hero_power_{profile.HeroPowerId.Replace('-', '_')}";

  public static string CardVersionId(StarterHeroProfile profile) =>
    $"{CardId(profile)}_v1";

  public static string SourceCardInstanceId(string playerId, StarterHeroProfile profile) =>
    $"{playerId}:{CardId(profile)}:power";

  public static IReadOnlyList<CardVersion> CreateAll(CatalogSnapshot catalog) =>
    StarterHeroProfiles.All.Select(profile => Create(profile, catalog)).ToArray();

  public static CardVersion Create(StarterHeroProfile profile, CatalogSnapshot catalog)
  {
    ArgumentNullException.ThrowIfNull(profile);
    ArgumentNullException.ThrowIfNull(catalog);

    var template = catalog.Cards.FirstOrDefault();
    var cardId = CardId(profile);
    var versionId = CardVersionId(profile);
    var target = new TargetSpec(
      Required: true,
      TargetSelectionMode.PlayerChosen,
      new EntityQuery(
        StringComparer.Ordinal.Equals(profile.HeroId, StarterHeroProfiles.CinderVanguardId) ? "OPPONENT" : "SELF",
        [SourceZone.Board, SourceZone.Global],
        [EntityType.Minion, EntityType.Hero],
        RequiredTags: [],
        ExcludedTags: [],
        AliveOnly: true),
      new CountSpec(1, 1),
      "FIZZLE");
    var action = StringComparer.Ordinal.Equals(profile.HeroId, StarterHeroProfiles.CinderVanguardId)
      ? (ActionDefinition)new DealDamageActionDefinition(
        "action_cinder_spark_damage",
        new ValueExpression("FIXED", 1, Ref: null),
        new TargetRef(TargetRefId),
        DamageSchool: "FIRE",
        Lifesteal: false,
        Preventable: true)
      : new HealActionDefinition(
        "action_astral_restoration_heal",
        new ValueExpression("FIXED", 2, Ref: null),
        new TargetRef(TargetRefId));
    var effect = new EffectDefinition(
      $"effect_{profile.HeroPowerId.Replace('-', '_')}",
      EffectKind.SimplePlayEffect,
      [SourceZone.SetAside],
      Trigger: null,
      new TimingPolicy(Phase.OnPlay, Window: null, Priority: 0, OrderingPolicy: "DECLARED_ORDER"),
      Conditions: [],
      target,
      [action],
      new ResolutionPolicy("SEQUENTIAL"),
      new FizzlePolicy("FIZZLE"),
      new ReplayPolicy(LogActionResults: true, RequiresRngLog: false),
      Tags: ["HERO_POWER", profile.HeroPowerId.ToUpperInvariant().Replace('-', '_')]);
    var schemaVersion = template?.SchemaVersion ?? "card-dsl-0.2.0";
    var engineVersion = template?.EngineVersion ?? "engine-bootstrap-0.3.0";
    var pipelineVersion = template?.GenerationPipelineVersion ?? "starter-hero-power-v1";
    var definition = new CardDefinition(
      cardId,
      versionId,
      schemaVersion,
      engineVersion,
      "primitive-registry-bootstrap-0.2.0",
      catalog.RulesetProfileId,
      catalog.SnapshotId,
      new StaticCardData(
        profile.HeroPowerDisplayName,
        CardType.HeroPowerCard,
        [profile.HeroId],
        Rarity.Common,
        ManaCost: 0,
        Tribes: [],
        Keywords: ["HERO_POWER"],
        Collectible: false,
        FormatTags: ["STARTER_HERO_POWER"]),
      new EntityDefinition(
        Minion: null,
        Spell: null,
        Weapon: null,
        Location: null,
        HeroCard: null,
        HeroPower: new HeroPowerDefinition(profile.HeroPowerCost, "MAIN_PHASE", target, [effect])),
      [effect],
      new CardTextGeneration(profile.HeroPowerRulesText, FlavorText: null),
      new BalanceMetadata(1, 0, []),
      new AssetMetadata(null, $"hero-power://{profile.HeroPowerId}", null),
      new AuthoringMetadata("starter-hero-profile", catalog.CreatedAt, "starter-hero-power-v1", [profile.HeroId]));

    return new CardVersion(
      cardId,
      versionId,
      schemaVersion,
      engineVersion,
      catalog.SnapshotId,
      pipelineVersion,
      catalog.RulesetProfileId,
      catalog.RulesetProfileHash,
      catalog.CreatedAt,
      definition,
      Published: true);
  }
}
