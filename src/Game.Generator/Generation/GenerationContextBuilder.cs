using Game.Shared.Mechanics;
using Game.Shared.Ruleset;

namespace Game.Generator.Generation;

public sealed class GenerationContextBuilder
{
  public GenerationContext Build(
    ResolvedRulesetProfile profile,
    IEnumerable<MechanicStatusRecord> mechanicStatuses) =>
    Build(profile, mechanicStatuses, intent: null);

  public GenerationContext Build(
    ResolvedRulesetProfile profile,
    IEnumerable<MechanicStatusRecord> mechanicStatuses,
    GenerationIntent? intent)
  {
    ArgumentNullException.ThrowIfNull(profile);
    ArgumentNullException.ThrowIfNull(mechanicStatuses);

    var statusByMechanic = mechanicStatuses
      .GroupBy(record => record.MechanicId, StringComparer.Ordinal)
      .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    var disabledByFormat = profile.FormatMechanicStates
      .Where(state => state.State is FormatMechanicStateKind.Disabled or FormatMechanicStateKind.LegacyOnly)
      .Select(state => state.MechanicId)
      .ToHashSet(StringComparer.Ordinal);

    var enabledMechanics = profile.EnabledMechanics
      .Where(mechanic => !profile.DisabledMechanics.Contains(mechanic, StringComparer.Ordinal))
      .Where(mechanic => !disabledByFormat.Contains(mechanic))
      .Where(mechanic =>
        statusByMechanic.TryGetValue(mechanic, out var status)
        && status.CurrentStatus is MechanicStatus.ApprovedForRuleset)
      .ToHashSet(StringComparer.Ordinal);

    enabledMechanics = IntersectIfRequested(enabledMechanics, intent?.RequestedMechanics);

    var allowedActions = IntersectIfRequested(profile.AllowedActions, intent?.RequestedActions);
    var allowedEffectKinds = IntersectIfRequested(profile.AllowedEffectKinds, intent?.RequestedEffectKinds);
    var allowedSourceZones = IntersectIfRequested(profile.AllowedSourceZones, intent?.RequestedSourceZones);
    var allowedPhases = IntersectIfRequested(profile.AllowedPhases, intent?.RequestedPhases);
    var allowedPools = IntersectIfRequested(profile.AllowedPools, intent?.RequestedPools);
    var allowedCardTypes = IntersectIfRequested(profile.AllowedCardTypes, intent?.RequestedCardTypes);
    var allowedRarities = IntersectIfRequested(profile.AllowedRarities, intent?.RequestedRarities);
    var allowedClasses = IntersectIfRequested(profile.AllowedClasses, intent?.RequestedClasses);
    var allowedTribes = IntersectIfRequested(profile.AllowedTribes, intent?.RequestedTribes);
    var allowedKeywords = IntersectIfRequested(profile.AllowedKeywords, intent?.RequestedKeywords);

    return new GenerationContext(
      profile.Id,
      profile.ResolvedHash,
      intent?.IntentId,
      intent?.CatalogSnapshotId,
      enabledMechanics,
      allowedActions,
      allowedEffectKinds,
      allowedSourceZones,
      allowedPhases,
      allowedPools,
      allowedCardTypes,
      allowedRarities,
      allowedClasses,
      allowedTribes,
      allowedKeywords,
      BuildManifests(
        enabledMechanics,
        allowedActions,
        allowedEffectKinds,
        allowedSourceZones,
        allowedPhases,
        allowedPools,
        allowedCardTypes,
        allowedRarities,
        allowedClasses,
        allowedTribes,
        allowedKeywords),
      new GenerationLimits(
        profile.Limits.MaxComplexityScore,
        profile.Limits.MaxRandomnessScore,
        profile.Limits.MaxActionsPerEffect,
        profile.Limits.MaxEffectsPerCard),
      profile.BalancePolicy,
      profile.SimulationPolicy,
      profile.PublicationPolicy);
  }

  private static HashSet<string> IntersectIfRequested(
    IEnumerable<string> allowed,
    IReadOnlyList<string>? requested)
  {
    var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
    if (requested is null || requested.Count == 0)
    {
      return allowedSet;
    }

    allowedSet.IntersectWith(requested);
    return allowedSet;
  }

  private static IReadOnlyList<GenerationWhitelistManifest> BuildManifests(
    IReadOnlySet<string> mechanics,
    IReadOnlySet<string> actions,
    IReadOnlySet<string> effectKinds,
    IReadOnlySet<string> sourceZones,
    IReadOnlySet<string> phases,
    IReadOnlySet<string> pools,
    IReadOnlySet<string> cardTypes,
    IReadOnlySet<string> rarities,
    IReadOnlySet<string> classes,
    IReadOnlySet<string> tribes,
    IReadOnlySet<string> keywords) =>
    [
      CreateManifest("mechanics", mechanics),
      CreateManifest("actions", actions),
      CreateManifest("effect-kinds", effectKinds),
      CreateManifest("source-zones", sourceZones),
      CreateManifest("phases", phases),
      CreateManifest("pools", pools),
      CreateManifest("card-types", cardTypes),
      CreateManifest("rarities", rarities),
      CreateManifest("classes", classes),
      CreateManifest("tribes", tribes),
      CreateManifest("keywords", keywords)
    ];

  private static GenerationWhitelistManifest CreateManifest(
    string kind,
    IEnumerable<string> values) =>
    new(
      $"generation-context:{kind}",
      kind,
      values.Order(StringComparer.Ordinal).ToArray());
}
