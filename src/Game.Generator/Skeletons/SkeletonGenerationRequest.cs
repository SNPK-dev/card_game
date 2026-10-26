using Game.Generator.Generation;
using Game.Shared.Primitives;

namespace Game.Generator.Skeletons;

public sealed record SkeletonGenerationRequest(
  string SkeletonId,
  string SchemaVersion,
  string EngineVersion,
  string PrimitiveRegistryVersion,
  string Name,
  CardType CardType,
  IReadOnlyList<string> ClassTags,
  Rarity Rarity,
  int ManaCost,
  IReadOnlyList<string> Tribes,
  IReadOnlyList<string> Keywords,
  bool Collectible,
  IReadOnlyList<string> FormatTags,
  int? Attack,
  int? Health,
  bool CanAttack,
  bool RequiresTarget,
  string CreatedBy,
  DateTimeOffset CreatedAt,
  GenerationContext Context,
  int HeroArmorGain = 0,
  string? HeroPortraitAssetRef = null,
  int? HeroPowerCost = null,
  string HeroPowerActivationTiming = "ACTIVE_PLAYER_MAIN");
