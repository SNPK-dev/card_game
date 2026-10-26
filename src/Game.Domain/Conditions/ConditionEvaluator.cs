using System.Text.Json;
using Game.Domain.Pooling;
using Game.Domain.State;
using Game.Domain.Targeting;
using Game.Shared.Dsl;
using Game.Shared.Primitives;

namespace Game.Domain.Conditions;

public sealed class ConditionEvaluator(
  ITargetResolver? targetResolver = null,
  IPoolResolver? poolResolver = null) : IConditionEvaluator
{
  private const string HasValidTarget = "HAS_VALID_TARGET";
  private const string PlayerHealthLessOrEqual = "PLAYER_HEALTH_LESS_OR_EQUAL";
  private const string BoardHasSpace = "BOARD_HAS_SPACE";
  private const string HandHasSpace = "HAND_HAS_SPACE";
  private const string SourceIsInZone = "SOURCE_IS_IN_ZONE";
  private const string PoolNotEmpty = "POOL_NOT_EMPTY";

  private readonly ITargetResolver _targetResolver = targetResolver ?? new TargetResolver();
  private readonly IPoolResolver _poolResolver = poolResolver ?? new PoolResolver();

  public ConditionEvaluationResult Evaluate(
    IGameState state,
    ConditionEvaluationRequest request)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(request);

    var evaluations = new List<ConditionEvaluation>();

    foreach (var condition in request.Conditions)
    {
      var kind = Normalize(condition.Kind);
      var result = kind switch
      {
        HasValidTarget => EvaluateHasValidTarget(state, request, condition),
        PlayerHealthLessOrEqual => EvaluatePlayerHealthLessOrEqual(state, request, condition),
        BoardHasSpace => EvaluateBoardHasSpace(state, request, condition),
        HandHasSpace => EvaluateHandHasSpace(state, request, condition),
        SourceIsInZone => EvaluateSourceIsInZone(state, request, condition),
        PoolNotEmpty => EvaluatePoolNotEmpty(request, condition),
        _ => SingleError(
          evaluations,
          ConditionEvaluationErrorCode.UnsupportedConditionKind,
          $"Condition kind '{condition.Kind}' is not supported by bootstrap condition evaluation.",
          condition.Kind)
      };

      if (!result.IsAccepted)
      {
        return result;
      }

      evaluations.Add(result.Evaluations.Single());
    }

    return ConditionEvaluationResult.Accepted(evaluations);
  }

  private ConditionEvaluationResult EvaluateHasValidTarget(
    IGameState state,
    ConditionEvaluationRequest request,
    Condition condition)
  {
    if (request.TargetSpec is null)
    {
      return SingleError(
        [],
        ConditionEvaluationErrorCode.MissingTargetSpec,
        "HAS_VALID_TARGET requires a target spec.",
        condition.Kind);
    }

    var targetResult = _targetResolver.Resolve(
      state,
      new TargetResolutionRequest(
        request.TargetSpec,
        request.ActingPlayerId,
        request.SourceControllerPlayerId,
        request.ChosenTargets));

    return ConditionEvaluationResult.Accepted(
      [new ConditionEvaluation(condition.Kind, targetResult.IsAccepted)]);
  }

  private static ConditionEvaluationResult EvaluatePlayerHealthLessOrEqual(
    IGameState state,
    ConditionEvaluationRequest request,
    Condition condition)
  {
    if (!TryReadString(condition.Parameters, "controller", out var controller, out var error))
    {
      return ParameterError(condition.Kind, error);
    }

    if (!TryReadInt(condition.Parameters, "threshold", out var threshold, out error))
    {
      return ParameterError(condition.Kind, error);
    }

    var playerIds = ResolvePlayerIds(
      state,
      request.ActingPlayerId,
      request.SourceControllerPlayerId,
      controller);
    if (playerIds is null)
    {
      return SingleError(
        [],
        ConditionEvaluationErrorCode.InvalidParameter,
        $"Controller scope '{controller}' is not supported.",
        condition.Kind);
    }

    var isSatisfied = playerIds.Any(playerId =>
      state.Players.TryGetValue(playerId, out var player)
      && player.Hero is not null
      && player.Hero.Health - player.Hero.Damage <= threshold);

    return ConditionEvaluationResult.Accepted(
      [new ConditionEvaluation(condition.Kind, isSatisfied)]);
  }

  private static ConditionEvaluationResult EvaluateBoardHasSpace(
    IGameState state,
    ConditionEvaluationRequest request,
    Condition condition)
  {
    if (!TryReadString(condition.Parameters, "controller", out var controller, out var error))
    {
      return ParameterError(condition.Kind, error);
    }

    if (!TryReadInt(condition.Parameters, "maxBoardSize", out var maxBoardSize, out error))
    {
      return ParameterError(condition.Kind, error);
    }

    return EvaluateZoneCapacity(
      state,
      request,
      condition,
      controller,
      maxBoardSize,
      player => player.Zones.Board.Count);
  }

  private static ConditionEvaluationResult EvaluateHandHasSpace(
    IGameState state,
    ConditionEvaluationRequest request,
    Condition condition)
  {
    if (!TryReadString(condition.Parameters, "controller", out var controller, out var error))
    {
      return ParameterError(condition.Kind, error);
    }

    if (!TryReadInt(condition.Parameters, "maxHandSize", out var maxHandSize, out error))
    {
      return ParameterError(condition.Kind, error);
    }

    return EvaluateZoneCapacity(
      state,
      request,
      condition,
      controller,
      maxHandSize,
      player => player.Zones.Hand.Count);
  }

  private static ConditionEvaluationResult EvaluateZoneCapacity(
    IGameState state,
    ConditionEvaluationRequest request,
    Condition condition,
    string controller,
    int maxSize,
    Func<PlayerState, int> currentSize)
  {
    if (maxSize < 0)
    {
      return SingleError(
        [],
        ConditionEvaluationErrorCode.InvalidParameter,
        "Capacity must be non-negative.",
        condition.Kind);
    }

    var playerIds = ResolvePlayerIds(
      state,
      request.ActingPlayerId,
      request.SourceControllerPlayerId,
      controller);
    if (playerIds is null)
    {
      return SingleError(
        [],
        ConditionEvaluationErrorCode.InvalidParameter,
        $"Controller scope '{controller}' is not supported.",
        condition.Kind);
    }

    var isSatisfied = playerIds.Any(playerId =>
      state.Players.TryGetValue(playerId, out var player)
      && currentSize(player) < maxSize);

    return ConditionEvaluationResult.Accepted(
      [new ConditionEvaluation(condition.Kind, isSatisfied)]);
  }

  private static ConditionEvaluationResult EvaluateSourceIsInZone(
    IGameState state,
    ConditionEvaluationRequest request,
    Condition condition)
  {
    if (request.Source is null)
    {
      return SingleError(
        [],
        ConditionEvaluationErrorCode.MissingSource,
        "SOURCE_IS_IN_ZONE requires a source target ref.",
        condition.Kind);
    }

    if (!TryReadStringArray(condition.Parameters, "zones", out var zones, out var error))
    {
      return ParameterError(condition.Kind, error);
    }

    var allowedZones = zones.Select(Normalize).ToHashSet(StringComparer.Ordinal);
    var isSatisfied = request.Source.TargetKind switch
    {
      ResolvedTargetRef.EntityKind => allowedZones.Contains("BOARD")
        && state.Players.TryGetValue(request.Source.PlayerId, out var player)
        && player.Zones.ContainsEntityOnBoard(request.Source.TargetId),
      ResolvedTargetRef.HeroKind => allowedZones.Contains("GLOBAL")
        && state.Players.TryGetValue(request.Source.PlayerId, out var player)
        && player.Hero is not null
        && StringComparer.Ordinal.Equals(player.Hero.EntityInstanceId, request.Source.TargetId),
      _ => false
    };

    return request.Source.TargetKind is ResolvedTargetRef.EntityKind or ResolvedTargetRef.HeroKind
      ? ConditionEvaluationResult.Accepted([new ConditionEvaluation(condition.Kind, isSatisfied)])
      : SingleError(
        [],
        ConditionEvaluationErrorCode.UnsupportedSourceKind,
        $"Source kind '{request.Source.TargetKind}' is not supported.",
        condition.Kind);
  }

  private ConditionEvaluationResult EvaluatePoolNotEmpty(
    ConditionEvaluationRequest request,
    Condition condition)
  {
    if (!TryReadString(condition.Parameters, "poolId", out var poolId, out var error))
    {
      return ParameterError(condition.Kind, error);
    }

    if (request.Pools is null || !request.Pools.TryGetValue(poolId, out var pool))
    {
      return SingleError(
        [],
        ConditionEvaluationErrorCode.MissingPool,
        $"Pool '{poolId}' was not supplied for POOL_NOT_EMPTY evaluation.",
        condition.Kind);
    }

    var result = _poolResolver.Resolve(new PoolResolutionRequest(pool));
    return result.IsAccepted
      ? ConditionEvaluationResult.Accepted([new ConditionEvaluation(condition.Kind, result.Candidates.Count > 0)])
      : SingleError(
        [],
        ConditionEvaluationErrorCode.PoolResolutionFailed,
        $"Pool '{poolId}' could not be resolved: {result.Errors[0].Code}.",
        condition.Kind);
  }

  private static IReadOnlyList<string>? ResolvePlayerIds(
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

  private static bool TryReadString(
    JsonElement parameters,
    string propertyName,
    out string value,
    out ConditionEvaluationError error)
  {
    value = string.Empty;
    if (!parameters.TryGetProperty(propertyName, out var property))
    {
      error = MissingProperty(propertyName);
      return false;
    }

    if (property.ValueKind != JsonValueKind.String)
    {
      error = InvalidProperty(propertyName, "string");
      return false;
    }

    value = property.GetString() ?? string.Empty;
    error = default!;
    return !string.IsNullOrWhiteSpace(value);
  }

  private static bool TryReadInt(
    JsonElement parameters,
    string propertyName,
    out int value,
    out ConditionEvaluationError error)
  {
    value = 0;
    if (!parameters.TryGetProperty(propertyName, out var property))
    {
      error = MissingProperty(propertyName);
      return false;
    }

    if (!property.TryGetInt32(out value))
    {
      error = InvalidProperty(propertyName, "integer");
      return false;
    }

    error = default!;
    return true;
  }

  private static bool TryReadStringArray(
    JsonElement parameters,
    string propertyName,
    out IReadOnlyList<string> values,
    out ConditionEvaluationError error)
  {
    values = [];
    if (!parameters.TryGetProperty(propertyName, out var property))
    {
      error = MissingProperty(propertyName);
      return false;
    }

    if (property.ValueKind != JsonValueKind.Array)
    {
      error = InvalidProperty(propertyName, "string array");
      return false;
    }

    var strings = new List<string>();
    foreach (var item in property.EnumerateArray())
    {
      if (item.ValueKind != JsonValueKind.String)
      {
        error = InvalidProperty(propertyName, "string array");
        return false;
      }

      var value = item.GetString();
      if (!string.IsNullOrWhiteSpace(value))
      {
        strings.Add(value);
      }
    }

    values = strings;
    error = default!;
    return true;
  }

  private static ConditionEvaluationError MissingProperty(string propertyName)
  {
    return new ConditionEvaluationError(
      ConditionEvaluationErrorCode.MissingParameter,
      $"Required condition parameter '{propertyName}' is missing.");
  }

  private static ConditionEvaluationError InvalidProperty(
    string propertyName,
    string expectedType)
  {
    return new ConditionEvaluationError(
      ConditionEvaluationErrorCode.InvalidParameter,
      $"Condition parameter '{propertyName}' must be a {expectedType}.");
  }

  private static ConditionEvaluationResult ParameterError(
    string conditionKind,
    ConditionEvaluationError error)
  {
    return SingleError([], error.Code, error.Message, conditionKind);
  }

  private static ConditionEvaluationResult SingleError(
    IReadOnlyList<ConditionEvaluation> evaluations,
    ConditionEvaluationErrorCode code,
    string message,
    string? conditionKind)
  {
    return ConditionEvaluationResult.Rejected(
      evaluations,
      new ConditionEvaluationError(code, message, conditionKind));
  }

  private static string Normalize(string value)
  {
    return value.Trim().ToUpperInvariant();
  }
}
