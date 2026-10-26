using Game.Shared.Dsl;
using Game.Shared.Primitives;

namespace Game.Generator.Skeletons;

public sealed class GeneratedCardSkeletonCompiler
{
  public CardDefinition Compile(GeneratedCardSkeleton skeleton)
  {
    ArgumentNullException.ThrowIfNull(skeleton);

    return new CardDefinition(
      CardId: null,
      CardVersion: null,
      skeleton.SchemaVersion,
      skeleton.EngineVersion,
      skeleton.PrimitiveRegistryVersion,
      skeleton.RulesetProfileId,
      skeleton.CatalogSnapshotId,
      new StaticCardData(
        skeleton.Name,
        skeleton.CardType,
        skeleton.ClassTags,
        skeleton.Rarity,
        skeleton.ManaCost,
        skeleton.Tribes,
        skeleton.Keywords,
        skeleton.Collectible,
        skeleton.FormatTags),
      new EntityDefinition(
        skeleton.Minion is null ? null : new MinionDefinition(
          skeleton.Minion.Attack,
          skeleton.Minion.Health,
          skeleton.Tribes,
          skeleton.Keywords,
          skeleton.Minion.CanAttack),
        skeleton.Spell is null ? null : new SpellDefinition(
          SpellSchool: null,
          skeleton.Spell.RequiresTarget),
        Weapon: null,
        Location: null,
        HeroCard: skeleton.HeroCard is null ? null : new HeroCardDefinition(
          skeleton.HeroCard.ArmorGain,
          skeleton.HeroCard.HeroPowerReplacement is null ? null : new HeroPowerDefinition(
            skeleton.HeroCard.HeroPowerReplacement.Cost,
            skeleton.HeroCard.HeroPowerReplacement.ActivationTiming,
            TargetRequirement: null,
            Effects: []),
          skeleton.HeroCard.PortraitAssetRef),
        HeroPower: null),
      Effects: [],
      new CardTextGeneration(
        RulesText: string.Empty,
        FlavorText: null),
      new BalanceMetadata(
        ComplexityScore: 0,
        RandomnessScore: 0,
        RiskFlags: []),
      new AssetMetadata(
        AssetManifestId: null,
        VisualAssetRef: null,
        AudioManifestId: null),
      new AuthoringMetadata(
        skeleton.CreatedBy,
        skeleton.CreatedAt,
        GenerationPipelineVersion: null,
        SourceRefs: ["generator:skeleton-bootstrap"]));
  }
}
