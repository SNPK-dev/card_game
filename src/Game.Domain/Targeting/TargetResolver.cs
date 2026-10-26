using Game.Domain.State;
using Game.Domain.Randomness;
using Game.Shared.Dsl;
using Game.Shared.Primitives;

namespace Game.Domain.Targeting;

public sealed class TargetResolver : ITargetResolver
{
  public TargetResolutionResult Resolve(IGameState state, TargetResolutionRequest request)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.TargetSpec);

    var targetSpec = request.TargetSpec;
    var requiredMinimum = RequiredTargetCount(targetSpec);
    if (targetSpec.Count.Min < 0 || targetSpec.Count.Max < requiredMinimum)
    {
      return TargetResolutionResult.Rejected(
        [],
        new TargetResolutionError(
          TargetResolutionErrorCode.InvalidTargetSpec,
          "Target count must have a non-negative minimum and maximum greater than or equal to minimum."));
    }

    var candidateResult = BuildCandidates(state, request);
    if (!candidateResult.IsAccepted)
    {
      return candidateResult;
    }

    var candidates = candidateResult.Candidates;
    return targetSpec.SelectionMode switch
    {
      TargetSelectionMode.None => ResolveNone(targetSpec, candidates),
      TargetSelectionMode.PlayerChosen => ResolveExplicitTargets(
        targetSpec,
        candidates,
        request.ChosenTargets ?? []),
      TargetSelectionMode.AutoValid => ResolveAutoValid(targetSpec, candidates),
      TargetSelectionMode.AllMatching => ResolveAllMatching(targetSpec, candidates),
      TargetSelectionMode.EventSource => ResolveExplicitTargets(
        targetSpec,
        candidates,
        request.EventSource is null ? [] : [request.EventSource]),
      TargetSelectionMode.EventTarget => ResolveExplicitTargets(
        targetSpec,
        candidates,
        request.EventTarget is null ? [] : [request.EventTarget]),
      TargetSelectionMode.PreviousActionResult => ResolveExplicitTargets(
        targetSpec,
        candidates,
        request.PreviousActionResults ?? []),
      TargetSelectionMode.Random => ResolveRandom(targetSpec, candidates, request),
      _ => TargetResolutionResult.Rejected(
        candidates,
        new TargetResolutionError(
          TargetResolutionErrorCode.UnsupportedSelectionMode,
          $"Selection mode '{targetSpec.SelectionMode}' is not supported."))
    };
  }

  private static TargetResolutionResult ResolveRandom(
    TargetSpec targetSpec,
    IReadOnlyList<ResolvedTargetRef> candidates,
    TargetResolutionRequest request)
  {
    var requiredMinimum = RequiredTargetCount(targetSpec);
    if (requiredMinimum != 1 || targetSpec.Count.Max != 1)
    {
      return TargetResolutionResult.Rejected(
        candidates,
        new TargetResolutionError(
          TargetResolutionErrorCode.UnsupportedSelectionMode,
          "Random target selection currently supports exactly one required target."));
    }

    if (candidates.Count < requiredMinimum)
    {
      return TargetResolutionResult.Rejected(
        candidates,
        new TargetResolutionError(
          TargetResolutionErrorCode.TooFewTargets,
          "Not enough matching candidates for random target resolution."));
    }

    if (request.RngRoller is null || request.CommandSeq is null || string.IsNullOrWhiteSpace(request.RngRollScope))
    {
      return TargetResolutionResult.Rejected(
        candidates,
        new TargetResolutionError(
          TargetResolutionErrorCode.UnsupportedSelectionMode,
          "Random target selection requires deterministic RNG roll logging context."));
    }

    var selection = request.RngRoller.SelectOne(
      candidates,
      RngStreamKind.RandomTarget,
      "TARGET_SELECTION",
      request.CommandSeq.Value,
      request.RngRollScope);

    return TargetResolutionResult.Accepted([selection.Selected], candidates, [selection.Roll]);
  }

  private static TargetResolutionResult BuildCandidates(
    IGameState state,
    TargetResolutionRequest request)
  {
    var query = request.TargetSpec.Candidates;
    if (query.RequiredTags.Count > 0 || query.ExcludedTags.Count > 0)
    {
      return TargetResolutionResult.Rejected(
        [],
        new TargetResolutionError(
          TargetResolutionErrorCode.UnsupportedTagFilter,
          "Runtime entity tags are not available in the bootstrap state model."));
    }

    var unsupportedZone = query.Zones.FirstOrDefault(zone =>
      zone is not SourceZone.Board and not SourceZone.Global);
    if (unsupportedZone is not 0)
    {
      return TargetResolutionResult.Rejected(
        [],
        new TargetResolutionError(
          TargetResolutionErrorCode.UnsupportedZone,
          $"Source zone '{unsupportedZone}' is not supported by bootstrap target resolution."));
    }

    var controllerPlayerIds = ResolveControllerPlayerIds(
      state,
      request.ActingPlayerId,
      request.SourceControllerPlayerId,
      query.Controller);
    if (controllerPlayerIds is null)
    {
      return TargetResolutionResult.Rejected(
        [],
        new TargetResolutionError(
          TargetResolutionErrorCode.UnsupportedControllerScope,
          $"Controller scope '{query.Controller}' is not supported."));
    }

    var allowedTypes = query.EntityTypes.Count == 0
      ? null
      : query.EntityTypes.ToHashSet();
    var candidates = new List<ResolvedTargetRef>();

    foreach (var playerId in controllerPlayerIds)
    {
      if (!state.Players.TryGetValue(playerId, out var player))
      {
        continue;
      }

      if (query.Zones.Contains(SourceZone.Board))
      {
        candidates.AddRange(player.Zones.Board
          .Where(entity => MatchesEntity(entity, allowedTypes, query.AliveOnly))
          .Select(entity => new ResolvedTargetRef(
            ResolvedTargetRef.EntityKind,
            player.PlayerId,
            entity.EntityInstanceId)));
      }

      if (query.Zones.Contains(SourceZone.Global)
        && player.Hero is not null
        && MatchesEntity(player.Hero, allowedTypes, query.AliveOnly))
      {
        candidates.Add(new ResolvedTargetRef(
          ResolvedTargetRef.HeroKind,
          player.PlayerId,
          player.Hero.EntityInstanceId));
      }
    }

    var ordered = candidates
      .OrderBy(target => target.PlayerId, StringComparer.Ordinal)
      .ThenBy(target => target.TargetKind, StringComparer.Ordinal)
      .ThenBy(target => target.TargetId, StringComparer.Ordinal)
      .ToArray();

    return TargetResolutionResult.Accepted([], ordered);
  }

  private static IReadOnlyList<string>? ResolveControllerPlayerIds(
    IGameState state,
    string actingPlayerId,
    string? sourceControllerPlayerId,
    string controller)
  {
    return Normalize(controller) switch
    {
      "SELF" => [actingPlayerId],
      "SOURCE_CONTROLLER" => [sourceControllerPlayerId ?? actingPlayerId],
      "OPPONENT" => state.Players.Keys
        .Where(playerId => !StringComparer.Ordinal.Equals(playerId, actingPlayerId))
        .Order(StringComparer.Ordinal)
        .ToArray(),
      "ANY" or "ALL" => state.Players.Keys.Order(StringComparer.Ordinal).ToArray(),
      _ => null
    };
  }

  private static TargetResolutionResult ResolveNone(
    TargetSpec targetSpec,
    IReadOnlyList<ResolvedTargetRef> candidates)
  {
    if (RequiredTargetCount(targetSpec) > 0)
    {
      return TargetResolutionResult.Rejected(
        candidates,
        new TargetResolutionError(
          TargetResolutionErrorCode.MissingRequiredTarget,
          "Required target spec cannot use NONE selection mode."));
    }

    return TargetResolutionResult.Accepted([], candidates);
  }

  private static TargetResolutionResult ResolveAutoValid(
    TargetSpec targetSpec,
    IReadOnlyList<ResolvedTargetRef> candidates)
  {
    var requiredMinimum = RequiredTargetCount(targetSpec);
    if (candidates.Count < requiredMinimum)
    {
      return TargetResolutionResult.Rejected(
        candidates,
        new TargetResolutionError(
          TargetResolutionErrorCode.TooFewTargets,
          "Not enough matching candidates for automatic target resolution."));
    }

    var targetCount = Math.Min(targetSpec.Count.Max, candidates.Count);
    var targets = candidates.Take(targetCount).ToArray();
    return TargetResolutionResult.Accepted(targets, candidates);
  }

  private static TargetResolutionResult ResolveAllMatching(
    TargetSpec targetSpec,
    IReadOnlyList<ResolvedTargetRef> candidates)
  {
    var requiredMinimum = RequiredTargetCount(targetSpec);
    if (candidates.Count < requiredMinimum)
    {
      return TargetResolutionResult.Rejected(
        candidates,
        new TargetResolutionError(
          TargetResolutionErrorCode.TooFewTargets,
          "Not enough matching candidates for all-matching target resolution."));
    }

    if (candidates.Count > targetSpec.Count.Max)
    {
      return TargetResolutionResult.Rejected(
        candidates,
        new TargetResolutionError(
          TargetResolutionErrorCode.TooManyTargets,
          "All-matching target resolution found more candidates than allowed."));
    }

    return TargetResolutionResult.Accepted(candidates, candidates);
  }

  private static TargetResolutionResult ResolveExplicitTargets(
    TargetSpec targetSpec,
    IReadOnlyList<ResolvedTargetRef> candidates,
    IReadOnlyList<ResolvedTargetRef> selectedTargets)
  {
    var requiredMinimum = RequiredTargetCount(targetSpec);
    if (selectedTargets.Count == 0 && requiredMinimum == 0)
    {
      return TargetResolutionResult.Accepted([], candidates);
    }

    if (selectedTargets.Count < requiredMinimum)
    {
      return TargetResolutionResult.Rejected(
        candidates,
        new TargetResolutionError(
          TargetResolutionErrorCode.TooFewTargets,
          "Fewer chosen targets were supplied than required."));
    }

    if (selectedTargets.Count > targetSpec.Count.Max)
    {
      return TargetResolutionResult.Rejected(
        candidates,
        new TargetResolutionError(
          TargetResolutionErrorCode.TooManyTargets,
          "More chosen targets were supplied than allowed."));
    }

    foreach (var target in selectedTargets)
    {
      if (!candidates.Contains(target))
      {
        return TargetResolutionResult.Rejected(
          candidates,
          new TargetResolutionError(
            TargetResolutionErrorCode.InvalidChosenTarget,
            "Chosen target is not a legal candidate.",
            target.TargetId));
      }
    }

    return TargetResolutionResult.Accepted(selectedTargets.ToArray(), candidates);
  }

  private static bool MatchesEntity(
    EntityInstance entity,
    IReadOnlySet<EntityType>? allowedTypes,
    bool aliveOnly)
  {
    if (allowedTypes is not null && !allowedTypes.Contains(entity.EntityType))
    {
      return false;
    }

    return !aliveOnly || entity.Damage < entity.Health;
  }

  private static string Normalize(string value)
  {
    return value.Trim().ToUpperInvariant();
  }

  private static int RequiredTargetCount(TargetSpec targetSpec)
  {
    return targetSpec.Required ? Math.Max(1, targetSpec.Count.Min) : targetSpec.Count.Min;
  }
}
