using Game.Shared.Primitives;

namespace Game.Shared.Dsl;

public sealed record EntityDefinition(
  MinionDefinition? Minion,
  SpellDefinition? Spell,
  WeaponDefinition? Weapon,
  LocationDefinition? Location,
  HeroCardDefinition? HeroCard,
  HeroPowerDefinition? HeroPower);

public sealed record MinionDefinition(
  int Attack,
  int Health,
  IReadOnlyList<string> Tribes,
  IReadOnlyList<string> BaseKeywords,
  bool CanAttack);

public sealed record SpellDefinition(
  string? SpellSchool,
  bool RequiresTarget);

public sealed record WeaponDefinition(
  int Attack,
  int Durability,
  IReadOnlyList<string> BaseKeywords);

public sealed record LocationDefinition(
  int Durability,
  string CooldownPolicy,
  string ActivationLimit);

public sealed record HeroCardDefinition(
  int ArmorGain,
  HeroPowerDefinition? HeroPowerReplacement,
  string? PortraitAssetRef);

public sealed record HeroPowerDefinition(
  int Cost,
  string ActivationTiming,
  TargetSpec? TargetRequirement,
  IReadOnlyList<EffectDefinition> Effects);
