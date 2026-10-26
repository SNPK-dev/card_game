namespace Game.Domain.Targeting;

public sealed record ResolvedTargetRef(
  string TargetKind,
  string PlayerId,
  string TargetId)
{
  public const string EntityKind = "ENTITY";
  public const string HeroKind = "HERO";
  public const string CardKind = "CARD";
}
