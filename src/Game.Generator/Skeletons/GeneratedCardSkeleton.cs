using Game.Shared.Primitives;

namespace Game.Generator.Skeletons;

public sealed record GeneratedCardSkeleton(
  string SkeletonId,
  string SchemaVersion,
  string EngineVersion,
  string PrimitiveRegistryVersion,
  string RulesetProfileId,
  string? CatalogSnapshotId,
  string Name,
  CardType CardType,
  IReadOnlyList<string> ClassTags,
  Rarity Rarity,
  int ManaCost,
  IReadOnlyList<string> Tribes,
  IReadOnlyList<string> Keywords,
  bool Collectible,
  IReadOnlyList<string> FormatTags,
  MinionSkeleton? Minion,
  SpellSkeleton? Spell,
  string CreatedBy,
  DateTimeOffset CreatedAt,
  HeroCardSkeleton? HeroCard = null);

public sealed record MinionSkeleton(
  int Attack,
  int Health,
  bool CanAttack);

public sealed record SpellSkeleton(bool RequiresTarget);

public sealed record HeroCardSkeleton(
  int ArmorGain,
  string? PortraitAssetRef,
  HeroPowerSkeleton? HeroPowerReplacement);

public sealed record HeroPowerSkeleton(
  int Cost,
  string ActivationTiming);
