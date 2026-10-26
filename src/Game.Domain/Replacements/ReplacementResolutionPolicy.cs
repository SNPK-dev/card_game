namespace Game.Domain.Replacements;

public sealed record ReplacementResolutionPolicy(int MaxProposedEvents)
{
  public static ReplacementResolutionPolicy MvpDefaults { get; } = new(512);
}
