using Game.Domain.Conditions;
using Game.Domain.History;
using Game.Domain.Mutation;
using Game.Domain.Pooling;
using Game.Domain.Randomness;
using Game.Domain.State;
using Game.Domain.Targeting;
using Game.Shared.Dsl;
using Game.Shared.Primitives;

namespace Game.Domain.Effects;

public sealed class EffectActionCompiler(
  IConditionEvaluator? conditionEvaluator = null) : IEffectActionCompiler
{
  private readonly IConditionEvaluator _conditionEvaluator = conditionEvaluator ?? new ConditionEvaluator();

  public EffectActionCompilationResult Compile(EffectActionCompilationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.State);
    ArgumentNullException.ThrowIfNull(request.Effect);

    if (request.CommandSeq <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(request.CommandSeq), "Command sequence must be positive.");
    }

    if (string.IsNullOrWhiteSpace(request.PreStateHash))
    {
      throw new ArgumentException("Pre-state hash is required.", nameof(request));
    }

    var conditionResult = _conditionEvaluator.Evaluate(
      request.State,
      new ConditionEvaluationRequest(
        request.Effect.Conditions,
        request.ActingPlayerId,
        request.Effect.TargetRequirement,
        request.SourceControllerPlayerId,
        request.Source,
        request.ChosenTargets,
        request.Pools));

    if (!conditionResult.IsAccepted)
    {
      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.ConditionEvaluationRejected,
        "Effect conditions were rejected by the condition evaluator.",
        request.Effect.EffectId));
    }

    var phase = ToCanonicalPhase(request.Effect.TimingPolicy.Phase);
    var sourceRef = $"effect:{request.Effect.EffectId}:{request.SourceRef}";
    var mutations = new List<StateMutation>();
    var actionResults = new Dictionary<string, IReadOnlyList<ResolvedTargetRef>>(StringComparer.Ordinal);
    var rngRolls = new List<EffectActionRngRoll>();

    if (!conditionResult.AreAllSatisfied)
    {
      return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, mutations));
    }

    var actionIndex = 0;
    foreach (var action in request.Effect.Actions)
    {
      actionIndex++;

      var compiled = CompileAction(request, action, actionIndex, sourceRef, phase, actionResults);
      if (!compiled.IsAccepted)
      {
        return compiled;
      }

      mutations.AddRange(compiled.Batch!.Mutations);
      rngRolls.AddRange(compiled.RngRolls);
    }

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, mutations), rngRolls);
  }

  private EffectActionCompilationResult CompileAction(
    EffectActionCompilationRequest request,
    ActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase,
    IDictionary<string, IReadOnlyList<ResolvedTargetRef>> actionResults)
  {
    return action switch
    {
      DealDamageActionDefinition dealDamage => CompileDealDamage(
        request,
        dealDamage,
        actionIndex,
        sourceRef,
        phase),
      DamageDistributionActionDefinition damageDistribution => CompileDamageDistribution(
        request,
        damageDistribution,
        actionIndex,
        sourceRef,
        phase),
      HealActionDefinition heal => CompileHeal(
        request,
        heal,
        actionIndex,
        sourceRef,
        phase),
      FullyHealActionDefinition fullyHeal => CompileFullyHeal(
        request,
        fullyHeal,
        actionIndex,
        sourceRef,
        phase),
      DrawCardActionDefinition drawCard => CompileDrawCard(
        request,
        drawCard,
        actionIndex,
        sourceRef,
        phase),
      DiscardCardActionDefinition discardCard => CompileDiscardCard(
        request,
        discardCard,
        actionIndex,
        sourceRef,
        phase),
      DestroyActionDefinition destroy => CompileDestroy(
        request,
        destroy,
        actionIndex,
        sourceRef,
        phase),
      BatchDestroyActionDefinition batchDestroy => CompileBatchDestroy(
        request,
        batchDestroy,
        actionIndex,
        sourceRef,
        phase,
        actionResults),
      RandomSelectActionDefinition randomSelect => CompileRandomSelect(
        request,
        randomSelect,
        sourceRef,
        phase,
        actionResults),
      MoveToZoneActionDefinition moveToZone => CompileMoveToZone(
        request,
        moveToZone,
        actionIndex,
        sourceRef,
        phase),
      ReturnToHandActionDefinition returnToHand => CompileReturnToHand(
        request,
        returnToHand,
        actionIndex,
        sourceRef,
        phase),
      ApplyModifierActionDefinition applyModifier => CompileApplyModifier(
        request,
        applyModifier,
        actionIndex,
        sourceRef,
        phase),
      ApplyAuraActionDefinition applyAura => CompileApplyAura(
        request,
        applyAura,
        actionIndex,
        sourceRef,
        phase),
      PreventDamageActionDefinition preventDamage => CompilePreventDamage(
        request,
        preventDamage,
        actionIndex,
        sourceRef,
        phase),
      ReplaceEventActionDefinition replaceEvent => CompileReplaceEvent(
        request,
        replaceEvent,
        actionIndex,
        sourceRef,
        phase),
      ShuffleIntoDeckActionDefinition shuffleIntoDeck => CompileShuffleIntoDeck(
        request,
        shuffleIntoDeck,
        actionIndex,
        sourceRef,
        phase),
      DiscoverActionDefinition discover => CompileDiscover(
        request,
        discover,
        actionIndex,
        sourceRef,
        phase),
      ResurrectActionDefinition resurrect => CompileResurrect(
        request,
        resurrect,
        actionIndex,
        sourceRef,
        phase),
      TakeExtraTurnActionDefinition takeExtraTurn => CompileTakeExtraTurn(
        request,
        takeExtraTurn,
        actionIndex,
        sourceRef,
        phase),
      _ => EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.UnsupportedAction,
        $"Action '{action.GetType().Name}' is not supported by bootstrap effect action compilation.",
        request.Effect.EffectId,
        action.ActionId))
    };
  }

  private EffectActionCompilationResult CompileDealDamage(
    EffectActionCompilationRequest request,
    DealDamageActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (!TryReadPositiveFixedValue(action.Amount, out var amount))
    {
      return UnsupportedValue(request, action.ActionId, "DEAL_DAMAGE amount must be a positive fixed value.");
    }

    var targets = request.ChosenTargets ?? [];
    if (targets.Count == 0)
    {
      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.MissingTarget,
        "DEAL_DAMAGE requires at least one resolved target.",
        request.Effect.EffectId,
        action.ActionId));
    }

    var mutations = new List<StateMutation>();
    var targetIndex = 0;
    foreach (var target in targets)
    {
      targetIndex++;

      if (!string.Equals(target.TargetKind, ResolvedTargetRef.EntityKind, StringComparison.Ordinal)
        && !string.Equals(target.TargetKind, ResolvedTargetRef.HeroKind, StringComparison.Ordinal))
      {
        return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
          EffectActionCompilationDiagnosticCode.UnsupportedTarget,
          "DEAL_DAMAGE currently supports entity and hero targets only.",
          request.Effect.EffectId,
          action.ActionId));
      }

      if (!request.State.Players.TryGetValue(target.PlayerId, out var player)
        || !TargetEntityExists(player, target))
      {
        return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
          EffectActionCompilationDiagnosticCode.MissingTarget,
          $"Target entity '{target.TargetId}' is not available for player '{target.PlayerId}'.",
          request.Effect.EffectId,
          action.ActionId));
      }

      mutations.Add(new StateMutation(
        $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}:{targetIndex:0000}:deal-damage",
        ApplyDamageMutationHandler.SupportedMutationType,
        sourceRef,
        target.TargetId,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["amount"] = amount
        },
        phase,
        request.CommandSeq,
        null,
        OrderingKey(request.Effect.EffectId, actionIndex, targetIndex),
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["actionId"] = action.ActionId,
          ["entityInstanceId"] = target.TargetId,
          ["preventable"] = action.Preventable.ToString(System.Globalization.CultureInfo.InvariantCulture),
          ["playerId"] = target.PlayerId
        }));
    }

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, mutations));
  }

  private EffectActionCompilationResult CompileDamageDistribution(
    EffectActionCompilationRequest request,
    DamageDistributionActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (!TryReadPositiveFixedValue(action.TotalAmount, out var totalAmount))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "DAMAGE_DISTRIBUTION totalAmount must be a positive fixed value.");
    }

    if (!string.Equals(action.PerTargetInstancePolicy, "SEPARATE_DAMAGE_INSTANCES", StringComparison.Ordinal))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "DAMAGE_DISTRIBUTION currently supports SEPARATE_DAMAGE_INSTANCES only.");
    }

    if (!IsSupportedDamageDistributionLifestealPolicy(action.LifestealAggregationPolicy))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "DAMAGE_DISTRIBUTION currently supports NO_LIFESTEAL and AGGREGATE_LIFESTEAL only.");
    }

    if (!IsSupportedDamageDistributionOverkillPolicy(action.OverkillPolicy))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "DAMAGE_DISTRIBUTION currently supports ALLOW_OVERKILL, CAP_AT_REMAINING_HEALTH, and SKIP_DEAD_TARGETS only.");
    }

    return action.DistributionMode switch
    {
      "SOURCE_ORDER" => CompileSourceOrderDamageDistribution(
        request,
        action,
        actionIndex,
        sourceRef,
        phase,
        totalAmount),
      "RANDOM_SPLIT" => CompileRandomSplitDamageDistribution(
        request,
        action,
        actionIndex,
        sourceRef,
        phase,
        totalAmount),
      _ => UnsupportedValue(
        request,
        action.ActionId,
        "DAMAGE_DISTRIBUTION currently supports SOURCE_ORDER and RANDOM_SPLIT distribution modes.")
    };
  }

  private EffectActionCompilationResult CompileSourceOrderDamageDistribution(
    EffectActionCompilationRequest request,
    DamageDistributionActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase,
    int totalAmount)
  {
    var resolution = new TargetResolver().Resolve(request.State, new TargetResolutionRequest(
      new TargetSpec(
        Required: !action.FizzleIfNoTargets,
        TargetSelectionMode.AllMatching,
        action.CandidateTargets,
        new CountSpec(action.FizzleIfNoTargets ? 0 : 1, int.MaxValue),
        "FIZZLE"),
      request.ActingPlayerId,
      request.SourceControllerPlayerId,
      EventSource: request.Source));

    if (!resolution.IsAccepted || resolution.Targets.Count == 0)
    {
      return action.FizzleIfNoTargets
        ? EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, []))
        : MissingDamageDistributionTarget(request, action.ActionId, resolution);
    }

    var plan = CreateDamageDistributionMutations(
        request,
        action.ActionId,
        actionIndex,
        sourceRef,
        phase,
        action,
        Enumerable.Range(0, totalAmount)
          .Select(index => resolution.Targets[index % resolution.Targets.Count])
          .ToArray());

    if (!plan.IsAccepted)
    {
      return plan.Result!;
    }

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, plan.Mutations));
  }

  private EffectActionCompilationResult CompileRandomSplitDamageDistribution(
    EffectActionCompilationRequest request,
    DamageDistributionActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase,
    int totalAmount)
  {
    if (!string.Equals(action.RngPolicy, "RANDOM_TARGET", StringComparison.Ordinal))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "DAMAGE_DISTRIBUTION RANDOM_SPLIT currently supports RANDOM_TARGET rngPolicy only.");
    }

    if (request.RngRoller is null)
    {
      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.UnsupportedAction,
        "DAMAGE_DISTRIBUTION RANDOM_SPLIT requires deterministic RNG roll logging context.",
        request.Effect.EffectId,
        action.ActionId));
    }

    var selectedTargets = new List<ResolvedTargetRef>(totalAmount);
    var rngRolls = new List<EffectActionRngRoll>(totalAmount);
    for (var instanceIndex = 0; instanceIndex < totalAmount; instanceIndex++)
    {
      var resolution = new TargetResolver().Resolve(request.State, new TargetResolutionRequest(
        new TargetSpec(
          Required: true,
          TargetSelectionMode.Random,
          action.CandidateTargets,
          new CountSpec(1, 1),
          "FIZZLE"),
        request.ActingPlayerId,
        request.SourceControllerPlayerId,
        EventSource: request.Source,
        RngRoller: request.RngRoller,
        CommandSeq: request.CommandSeq,
        RngRollScope: $"effect:{request.Effect.EffectId}:action:{action.ActionId}:damage:{instanceIndex + 1:0000}"));

      if (!resolution.IsAccepted || resolution.Targets.Count == 0)
      {
        return action.FizzleIfNoTargets
          ? EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, []), rngRolls)
          : MissingDamageDistributionTarget(request, action.ActionId, resolution);
      }

      selectedTargets.Add(resolution.Targets[0]);
      rngRolls.AddRange(resolution.RngRolls.Select(roll => new EffectActionRngRoll(roll, resolution.Targets)));
    }

    var plan = CreateDamageDistributionMutations(
        request,
        action.ActionId,
        actionIndex,
        sourceRef,
        phase,
        action,
        selectedTargets);

    if (!plan.IsAccepted)
    {
      return plan.Result!;
    }

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, plan.Mutations), rngRolls);
  }

  private static DamageDistributionPlan CreateDamageDistributionMutations(
    EffectActionCompilationRequest request,
    string actionId,
    int actionIndex,
    string sourceRef,
    string phase,
    DamageDistributionActionDefinition action,
    IReadOnlyList<ResolvedTargetRef> targets)
  {
    var mutations = new List<StateMutation>(targets.Count);
    var remainingHealthByTarget = targets
      .DistinctBy(target => target.TargetId)
      .ToDictionary(
        target => target.TargetId,
        target => TryGetRemainingHealth(request.State, target, out var remaining) ? remaining : 0,
        StringComparer.Ordinal);
    var totalEffectiveDamage = 0;
    var damageMutationIndex = 0;
    for (var index = 0; index < targets.Count; index++)
    {
      var target = targets[index];
      if (!TryGetRemainingHealth(request.State, target, out var currentRemainingHealth))
      {
        return DamageDistributionPlan.Rejected(EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
          EffectActionCompilationDiagnosticCode.MissingTarget,
          $"DAMAGE_DISTRIBUTION target entity '{target.TargetId}' is no longer available.",
          request.Effect.EffectId,
          actionId)));
      }

      var remainingHealth = remainingHealthByTarget[target.TargetId];
      if (string.Equals(action.OverkillPolicy, "SKIP_DEAD_TARGETS", StringComparison.Ordinal)
        && remainingHealth <= 0)
      {
        continue;
      }

      var damageAmount = string.Equals(action.OverkillPolicy, "CAP_AT_REMAINING_HEALTH", StringComparison.Ordinal)
        ? Math.Min(1, Math.Max(0, remainingHealth))
        : 1;
      if (damageAmount <= 0)
      {
        continue;
      }

      remainingHealthByTarget[target.TargetId] = remainingHealth - damageAmount;
      totalEffectiveDamage += damageAmount;
      damageMutationIndex++;
      mutations.Add(new StateMutation(
        $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{actionId}:{damageMutationIndex:0000}:damage-distribution",
        ApplyDamageMutationHandler.SupportedMutationType,
        sourceRef,
        target.TargetId,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["amount"] = damageAmount
        },
        phase,
        request.CommandSeq,
        null,
        OrderingKey(request.Effect.EffectId, actionIndex, damageMutationIndex),
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["actionId"] = actionId,
          ["damageInstance"] = damageMutationIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
          ["entityInstanceId"] = target.TargetId,
          ["overkillPolicy"] = action.OverkillPolicy,
          ["playerId"] = target.PlayerId,
          ["preventable"] = bool.TrueString,
          ["targetDisappearancePolicy"] = "SKIP_MISSING_TARGET"
        }));
    }

    if (string.Equals(action.LifestealAggregationPolicy, "AGGREGATE_LIFESTEAL", StringComparison.Ordinal)
      && totalEffectiveDamage > 0)
    {
      if (!request.State.Players.TryGetValue(request.SourceControllerPlayerId, out var sourceController)
        || sourceController.Hero is null)
      {
        return DamageDistributionPlan.Rejected(EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
          EffectActionCompilationDiagnosticCode.MissingTarget,
          $"DAMAGE_DISTRIBUTION lifesteal source controller '{request.SourceControllerPlayerId}' has no hero.",
          request.Effect.EffectId,
          actionId)));
      }

      mutations.Add(new StateMutation(
        $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{actionId}:{damageMutationIndex + 1:0000}:lifesteal",
        HealEntityMutationHandler.SupportedMutationType,
        sourceRef,
        sourceController.Hero.EntityInstanceId,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["amount"] = totalEffectiveDamage
        },
        phase,
        request.CommandSeq,
        null,
        OrderingKey(request.Effect.EffectId, actionIndex, damageMutationIndex + 1),
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["actionId"] = actionId,
          ["entityInstanceId"] = sourceController.Hero.EntityInstanceId,
          ["lifestealAggregationPolicy"] = action.LifestealAggregationPolicy,
          ["playerId"] = request.SourceControllerPlayerId
        }));
    }

    return DamageDistributionPlan.Accepted(mutations);
  }

  private static EffectActionCompilationResult MissingDamageDistributionTarget(
    EffectActionCompilationRequest request,
    string actionId,
    TargetResolutionResult resolution)
  {
    var error = resolution.Errors.FirstOrDefault();
    return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
      EffectActionCompilationDiagnosticCode.MissingTarget,
      error is null
        ? "DAMAGE_DISTRIBUTION found no legal target."
        : $"DAMAGE_DISTRIBUTION target resolution failed: {error.Code}.",
      request.Effect.EffectId,
      actionId));
  }

  private EffectActionCompilationResult CompileHeal(
    EffectActionCompilationRequest request,
    HealActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (!TryReadPositiveFixedValue(action.Amount, out var amount))
    {
      return UnsupportedValue(request, action.ActionId, "HEAL amount must be a positive fixed value.");
    }

    if (!TryGetResolvedTargets(request, action.ActionId, out var targets, out var missingTarget))
    {
      return missingTarget!;
    }

    var mutations = new List<StateMutation>();
    var targetIndex = 0;
    foreach (var target in targets)
    {
      targetIndex++;
      if (!TryGetEntityOrHero(request, action.ActionId, target, out var entity, out var unsupportedTarget))
      {
        return unsupportedTarget!;
      }

      mutations.Add(CreateEntityMutation(
        request,
        action.ActionId,
        actionIndex,
        targetIndex,
        "heal",
        HealEntityMutationHandler.SupportedMutationType,
        sourceRef,
        target.PlayerId,
        entity.EntityInstanceId,
        phase,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["amount"] = amount
        }));
    }

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, mutations));
  }

  private static EffectActionCompilationResult CompileTakeExtraTurn(
    EffectActionCompilationRequest request,
    TakeExtraTurnActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (!action.CurrentTurnEndsNormally)
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "TAKE_EXTRA_TURN currently requires currentTurnEndsNormally=true.");
    }

    if (!string.Equals(action.ExtraTurnStartPolicy, "AFTER_CURRENT_TURN", StringComparison.Ordinal))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "TAKE_EXTRA_TURN currently supports AFTER_CURRENT_TURN start policy only.");
    }

    if (!string.Equals(action.TurnSchedulerPolicy, "QUEUE_AFTER_CURRENT_TURN", StringComparison.Ordinal))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "TAKE_EXTRA_TURN currently supports QUEUE_AFTER_CURRENT_TURN scheduler policy only.");
    }

    if (!string.IsNullOrWhiteSpace(action.CommandButtonReplacement))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "TAKE_EXTRA_TURN commandButtonReplacement is not supported by the current runtime slice.");
    }

    if (!string.Equals(action.ModifierDurationInteractionPolicy, "EXPIRE_CURRENT_TURN_ONLY", StringComparison.Ordinal))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "TAKE_EXTRA_TURN currently supports EXPIRE_CURRENT_TURN_ONLY duration interaction only.");
    }

    var playerId = ResolvePlayerId(request, action.Player);
    if (!request.State.Players.ContainsKey(playerId))
    {
      return MissingPlayer(request, action.ActionId, playerId);
    }

    if (action.OncePerGame && ExtraTurnSchedule.HasOncePerGameUse(request.State, playerId))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        $"TAKE_EXTRA_TURN once-per-game extra turn was already scheduled for player '{playerId}'.");
    }

    var queueKey = ExtraTurnSchedule.QueueKey(
      request.CommandSeq,
      request.Effect.EffectId,
      action.ActionId,
      playerId);
    var mutation = new StateMutation(
      $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}:schedule-extra-turn",
      ScheduleExtraTurnMutationHandler.SupportedMutationType,
      sourceRef,
      playerId,
      new SortedDictionary<string, int>(StringComparer.Ordinal),
      phase,
      request.CommandSeq,
      null,
      OrderingKey(request.Effect.EffectId, actionIndex, 1),
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["actionId"] = action.ActionId,
        ["currentTurnEndsNormally"] = action.CurrentTurnEndsNormally.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["extraTurnStartPolicy"] = action.ExtraTurnStartPolicy,
        ["modifierDurationInteractionPolicy"] = action.ModifierDurationInteractionPolicy,
        ["oncePerGame"] = action.OncePerGame.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["playerId"] = playerId,
        ["queueKey"] = queueKey,
        ["turnSchedulerPolicy"] = action.TurnSchedulerPolicy
      });

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, [mutation]));
  }

  private EffectActionCompilationResult CompileFullyHeal(
    EffectActionCompilationRequest request,
    FullyHealActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (!TryGetResolvedTargets(request, action.ActionId, out var targets, out var missingTarget))
    {
      return missingTarget!;
    }

    var mutations = new List<StateMutation>();
    var targetIndex = 0;
    foreach (var target in targets)
    {
      targetIndex++;
      if (!TryGetEntityOrHero(request, action.ActionId, target, out var entity, out var unsupportedTarget))
      {
        return unsupportedTarget!;
      }

      if (entity.Damage <= 0)
      {
        continue;
      }

      mutations.Add(CreateEntityMutation(
        request,
        action.ActionId,
        actionIndex,
        targetIndex,
        "fully-heal",
        HealEntityMutationHandler.SupportedMutationType,
        sourceRef,
        target.PlayerId,
        entity.EntityInstanceId,
        phase,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["amount"] = entity.Damage
        }));
    }

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, mutations));
  }

  private EffectActionCompilationResult CompileDrawCard(
    EffectActionCompilationRequest request,
    DrawCardActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (!TryReadPositiveFixedValue(action.Count, out var count) || count != 1)
    {
      return UnsupportedValue(request, action.ActionId, "DRAW_CARD count must be fixed to exactly one.");
    }

    var playerId = ResolvePlayerId(request, action.Player);
    if (!request.State.Players.TryGetValue(playerId, out var player))
    {
      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.MissingPlayer,
        $"Player '{playerId}' does not exist.",
        request.Effect.EffectId,
        action.ActionId));
    }

    var card = player.Zones.Deck.FirstOrDefault();
    if (card is null)
    {
      if (IsSkipDeckEmptyPolicy(action.DeckEmptyPolicy))
      {
        return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, []));
      }

      if (string.Equals(action.DeckEmptyPolicy, "FATIGUE", StringComparison.OrdinalIgnoreCase))
      {
        var fatigueDamage = player.FatigueCounter + 1;
        var mutations = new List<StateMutation>
        {
          new(
            $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}:fatigue",
            ModifyFatigueMutationHandler.SupportedMutationType,
            sourceRef,
            playerId,
            new SortedDictionary<string, int>(StringComparer.Ordinal)
            {
              ["amount"] = 1
            },
            phase,
            request.CommandSeq,
            null,
            OrderingKey(request.Effect.EffectId, actionIndex, 1),
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
              ["actionId"] = action.ActionId,
              ["playerId"] = playerId
            })
        };

        if (player.Hero is not null)
        {
          mutations.Add(new StateMutation(
            $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}:fatigue-damage",
            ApplyDamageMutationHandler.SupportedMutationType,
            sourceRef,
            player.Hero.EntityInstanceId,
            new SortedDictionary<string, int>(StringComparer.Ordinal)
            {
              ["amount"] = fatigueDamage
            },
            phase,
            request.CommandSeq,
            null,
            OrderingKey(request.Effect.EffectId, actionIndex, 2),
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
              ["actionId"] = action.ActionId,
              ["entityInstanceId"] = player.Hero.EntityInstanceId,
              ["preventable"] = bool.FalseString,
              ["playerId"] = playerId
            }));
        }

        return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, mutations));
      }

      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.MissingDeckCard,
        $"Player '{playerId}' has no deck card to draw.",
        request.Effect.EffectId,
        action.ActionId));
    }

    var mutation = new StateMutation(
      $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}:draw-card",
      DrawCardMutationHandler.SupportedMutationType,
      sourceRef,
      card.CardInstanceId,
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["count"] = 1
      },
      phase,
      request.CommandSeq,
      null,
      OrderingKey(request.Effect.EffectId, actionIndex, 1),
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["actionId"] = action.ActionId,
        ["cardInstanceId"] = card.CardInstanceId,
        ["playerId"] = playerId
      });

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, [mutation]));
  }

  private EffectActionCompilationResult CompileDiscardCard(
    EffectActionCompilationRequest request,
    DiscardCardActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (!TryGetResolvedTargets(request, action.ActionId, out var targets, out var missingTarget))
    {
      return missingTarget!;
    }

    var mutations = new List<StateMutation>();
    var targetIndex = 0;
    foreach (var target in targets)
    {
      targetIndex++;
      if (!TryGetCardInZone(request, action.ActionId, target, action.FromZone, out _, out var invalidTarget))
      {
        return invalidTarget!;
      }

      mutations.Add(CreateCardZoneMutation(
        request,
        action.ActionId,
        actionIndex,
        targetIndex,
        "discard-card",
        sourceRef,
        target.PlayerId,
        target.TargetId,
        action.FromZone,
        SourceZone.Graveyard,
        phase));
    }

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, mutations));
  }

  private EffectActionCompilationResult CompileDestroy(
    EffectActionCompilationRequest request,
    DestroyActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (!TryGetResolvedTargets(request, action.ActionId, out var targets, out var missingTarget))
    {
      return missingTarget!;
    }

    if (targets.Count != 1)
    {
      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.UnsupportedTarget,
        "DESTROY requires exactly one resolved entity target.",
        request.Effect.EffectId,
        action.ActionId));
    }

    if (!TryGetBoardEntity(request, action.ActionId, targets[0], out var entity, out var unsupportedTarget))
    {
      return unsupportedTarget!;
    }

    var mutation = CreateEntityMutation(
      request,
      action.ActionId,
      actionIndex,
      1,
      "destroy",
      DestroyEntityMutationHandler.SupportedMutationType,
      sourceRef,
      targets[0].PlayerId,
      entity.EntityInstanceId,
      phase,
      new SortedDictionary<string, int>(StringComparer.Ordinal));

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, [mutation]));
  }

  private EffectActionCompilationResult CompileBatchDestroy(
    EffectActionCompilationRequest request,
    BatchDestroyActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase,
    IDictionary<string, IReadOnlyList<ResolvedTargetRef>> actionResults)
  {
    if (!string.Equals(action.DeathProcessingPolicy, "SIMULTANEOUS", StringComparison.OrdinalIgnoreCase))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "BATCH_DESTROY supports only SIMULTANEOUS death processing.");
    }

    if (!TryResolveBatchDestroyTargets(request, action.ActionId, action.Targets, actionResults, out var targets, out var missingTarget))
    {
      return missingTarget!;
    }

    var mutations = new List<StateMutation>();
    var targetIndex = 0;
    foreach (var target in targets)
    {
      if (!TryGetBoardEntity(request, action.ActionId, target, out var entity, out var unsupportedTarget))
      {
        if (IsSkipInvalidTargetsPolicy(action.InvalidTargetPolicy))
        {
          continue;
        }

        return unsupportedTarget!;
      }

      if (entity.Damage >= entity.Health)
      {
        if (IsSkipInvalidTargetsPolicy(action.InvalidTargetPolicy))
        {
          continue;
        }

        return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
          EffectActionCompilationDiagnosticCode.MissingTarget,
          $"Target entity '{entity.EntityInstanceId}' is not alive for player '{target.PlayerId}'.",
          request.Effect.EffectId,
          action.ActionId));
      }

      targetIndex++;
      mutations.Add(CreateEntityMutation(
        request,
        action.ActionId,
        actionIndex,
        targetIndex,
        "batch-destroy",
        DestroyEntityMutationHandler.SupportedMutationType,
        sourceRef,
        target.PlayerId,
        entity.EntityInstanceId,
        phase,
        new SortedDictionary<string, int>(StringComparer.Ordinal)));
    }

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, mutations));
  }

  private EffectActionCompilationResult CompileRandomSelect(
    EffectActionCompilationRequest request,
    RandomSelectActionDefinition action,
    string sourceRef,
    string phase,
    IDictionary<string, IReadOnlyList<ResolvedTargetRef>> actionResults)
  {
    if (action.Count.Min != 1 || action.Count.Max != 1)
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "RANDOM_SELECT currently supports exactly one selected entity.");
    }

    if (string.IsNullOrWhiteSpace(action.ResultRef))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "RANDOM_SELECT requires a non-empty resultRef.");
    }

    if (request.RngRoller is null)
    {
      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.UnsupportedAction,
        "RANDOM_SELECT requires deterministic RNG roll logging context.",
        request.Effect.EffectId,
        action.ActionId));
    }

    var targetSpec = new TargetSpec(
      Required: true,
      TargetSelectionMode.Random,
      action.From,
      action.Count,
      "FIZZLE");
    var resolution = new TargetResolver().Resolve(request.State, new TargetResolutionRequest(
      targetSpec,
      request.ActingPlayerId,
      request.SourceControllerPlayerId,
      EventSource: request.Source,
      RngRoller: request.RngRoller,
      CommandSeq: request.CommandSeq,
      RngRollScope: $"effect:{request.Effect.EffectId}:action:{action.ActionId}"));

    if (!resolution.IsAccepted)
    {
      if (IsSkipEmptyPolicy(action.EmptyPolicy))
      {
        actionResults[action.ActionId] = [];
        actionResults[action.ResultRef] = [];
        return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, []));
      }

      var error = resolution.Errors.FirstOrDefault();
      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.MissingTarget,
        error is null
          ? "RANDOM_SELECT found no legal target."
          : $"RANDOM_SELECT target resolution failed: {error.Code}.",
        request.Effect.EffectId,
        action.ActionId));
    }

    actionResults[action.ActionId] = resolution.Targets;
    actionResults[action.ResultRef] = resolution.Targets;
    var rngRolls = resolution.RngRolls
      .Select(roll => new EffectActionRngRoll(roll, resolution.Targets))
      .ToArray();

    return EffectActionCompilationResult.Accepted(
      CreateBatch(request, sourceRef, phase, []),
      rngRolls);
  }

  private EffectActionCompilationResult CompileMoveToZone(
    EffectActionCompilationRequest request,
    MoveToZoneActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (!TryGetResolvedTargets(request, action.ActionId, out var targets, out var missingTarget))
    {
      return missingTarget!;
    }

    var mutations = new List<StateMutation>();
    var targetIndex = 0;
    foreach (var target in targets)
    {
      targetIndex++;
      if (!TryFindCardZone(request, action.ActionId, target, out var fromZone, out var invalidTarget))
      {
        return invalidTarget!;
      }

      mutations.Add(CreateCardZoneMutation(
        request,
        action.ActionId,
        actionIndex,
        targetIndex,
        "move-to-zone",
        sourceRef,
        target.PlayerId,
        target.TargetId,
        fromZone,
        action.DestinationZone,
        phase));
    }

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, mutations));
  }

  private EffectActionCompilationResult CompileShuffleIntoDeck(
    EffectActionCompilationRequest request,
    ShuffleIntoDeckActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (!TryGetResolvedTargets(request, action.ActionId, out var targets, out var missingTarget))
    {
      return missingTarget!;
    }

    var mutations = new List<StateMutation>();
    var targetIndex = 0;
    foreach (var target in targets)
    {
      targetIndex++;
      if (!TryFindCardZone(request, action.ActionId, target, out var fromZone, out var invalidTarget))
      {
        return invalidTarget!;
      }

      mutations.Add(CreateCardZoneMutation(
        request,
        action.ActionId,
        actionIndex,
        targetIndex,
        "shuffle-into-deck",
        sourceRef,
        target.PlayerId,
        target.TargetId,
        fromZone,
        SourceZone.Deck,
        phase));
    }

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, mutations));
  }

  private EffectActionCompilationResult CompileReturnToHand(
    EffectActionCompilationRequest request,
    ReturnToHandActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (!string.Equals(action.Owner, "ORIGINAL_OWNER", StringComparison.OrdinalIgnoreCase)
      && !string.Equals(action.Owner, "CONTROLLER", StringComparison.OrdinalIgnoreCase))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "RETURN_TO_HAND supports ORIGINAL_OWNER or CONTROLLER owner policies.");
    }

    if (!TryGetResolvedTargets(request, action.ActionId, out var targets, out var missingTarget))
    {
      return missingTarget!;
    }

    if (targets.Count != 1)
    {
      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.UnsupportedTarget,
        "RETURN_TO_HAND requires exactly one resolved board entity target.",
        request.Effect.EffectId,
        action.ActionId));
    }

    if (!TryGetBoardEntity(request, action.ActionId, targets[0], out var entity, out var unsupportedTarget))
    {
      return unsupportedTarget!;
    }

    if (!request.State.Players[targets[0].PlayerId].Zones.ContainsCardInZone(entity.SourceCardInstanceId, SourceZone.SetAside))
    {
      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.MissingTarget,
        $"Source card '{entity.SourceCardInstanceId}' for entity '{entity.EntityInstanceId}' is not set aside.",
        request.Effect.EffectId,
        action.ActionId));
    }

    var mutation = new StateMutation(
      $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}:0001:return-to-hand",
      ReturnEntityToHandMutationHandler.SupportedMutationType,
      sourceRef,
      entity.EntityInstanceId,
      new SortedDictionary<string, int>(StringComparer.Ordinal),
      phase,
      request.CommandSeq,
      null,
      OrderingKey(request.Effect.EffectId, actionIndex, 1),
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["actionId"] = action.ActionId,
        ["cardInstanceId"] = entity.SourceCardInstanceId,
        ["entityInstanceId"] = entity.EntityInstanceId,
        ["fromZone"] = "SET_ASIDE",
        ["playerId"] = targets[0].PlayerId,
        ["toZone"] = "HAND"
      });

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, [mutation]));
  }

  private EffectActionCompilationResult CompileApplyModifier(
    EffectActionCompilationRequest request,
    ApplyModifierActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (IsSupportedBoardStatModifier(action.Modifier.ModifierType, action.Modifier.Duration))
    {
      return CompileBoardStatModifier(request, action, actionIndex, sourceRef, phase);
    }

    if (IsSupportedBoardKeywordModifier(action.Modifier.ModifierType, action.Modifier.Duration))
    {
      return CompileBoardKeywordModifier(request, action, actionIndex, sourceRef, phase);
    }

    if (IsSupportedHandZoneModifier(action.Modifier.ModifierType, action.Modifier.Duration))
    {
      return CompileHandZoneModifier(request, action, actionIndex, sourceRef, phase);
    }

    if (!IsSupportedModifier(action.Modifier.ModifierType, action.Modifier.Duration))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "APPLY_MODIFIER currently supports COST_MODIFIER/NEXT_PLAY, COST_MODIFIER/REST_OF_TURN, DAMAGE_PREVENTION/NEXT_DAMAGE, BOARD_ATTACK_MODIFIER/PERMANENT, BOARD_HEALTH_MODIFIER/PERMANENT, BOARD_KEYWORD_MODIFIER/PERMANENT, and HAND_ZONE_MODIFIER/PERMANENT only.");
    }

    if (!TryReadPositiveFixedValue(action.Modifier.Value, out var amount))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "APPLY_MODIFIER modifier value must be a positive fixed value.");
    }

    var playerId = ResolvePlayerId(request, action.Player);
    if (!request.State.Players.TryGetValue(playerId, out var player))
    {
      return MissingPlayer(request, action.ActionId, playerId);
    }

    var modifierId = string.IsNullOrWhiteSpace(action.Modifier.ModifierId)
      ? $"modifier:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}"
      : action.Modifier.ModifierId;
    if (player.Modifiers.Any(modifier => StringComparer.Ordinal.Equals(modifier.ModifierId, modifierId)))
    {
      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.UnsupportedValueExpression,
        $"Modifier '{modifierId}' already exists for player '{playerId}'.",
        request.Effect.EffectId,
        action.ActionId));
    }

    var mutation = new StateMutation(
      $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}:0001:apply-modifier",
      ApplyModifierMutationHandler.SupportedMutationType,
      sourceRef,
      playerId,
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["amount"] = amount
      },
      phase,
      request.CommandSeq,
      null,
      OrderingKey(request.Effect.EffectId, actionIndex, 1),
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["actionId"] = action.ActionId,
        ["duration"] = action.Modifier.Duration,
        ["modifierId"] = modifierId,
        ["modifierType"] = action.Modifier.ModifierType,
        ["playerId"] = playerId,
        ["priority"] = action.Modifier.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["sourceRef"] = sourceRef
      });

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, [mutation]));
  }

  private EffectActionCompilationResult CompileApplyAura(
    EffectActionCompilationRequest request,
    ApplyAuraActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (!IsSupportedAuraDuration(action.Aura.Duration))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "APPLY_AURA currently supports PERMANENT and WHILE_SOURCE_ACTIVE board-source aura lifecycles only.");
    }

    if (!IsSupportedAuraModifier(action.Aura.Modifier.ModifierType, action.Aura.Modifier.Duration))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "APPLY_AURA currently supports BOARD_ATTACK_MODIFIER, BOARD_HEALTH_MODIFIER, and BOARD_KEYWORD_MODIFIER aura modifiers with PERMANENT or WHILE_SOURCE_ACTIVE durations only.");
    }

    if (!TryResolveAuraSource(request, action.ActionId, out var sourceEntity, out var sourceCardVersion, out var sourceFailure))
    {
      return sourceFailure!;
    }

    var playerId = ResolvePlayerId(request, action.Controller);
    if (!request.State.Players.ContainsKey(playerId))
    {
      return MissingPlayer(request, action.ActionId, playerId);
    }

    var existingAuras = request.State.Players[playerId].Auras ?? [];
    var auraId = string.IsNullOrWhiteSpace(action.Aura.AuraId)
      ? $"aura:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}"
      : action.Aura.AuraId;
    var nextAura = new PlayerAura(
      auraId,
      sourceEntity.EntityInstanceId,
      sourceEntity.SourceCardInstanceId,
      sourceCardVersion,
      playerId,
      request.Effect.EffectId,
      action.Aura.Priority);
    var nextAuras = existingAuras
      .Where(aura => !StringComparer.Ordinal.Equals(aura.AuraId, auraId))
      .Append(nextAura)
      .OrderBy(aura => aura.SourceEntityInstanceId, StringComparer.Ordinal)
      .ThenBy(aura => aura.Priority)
      .ThenBy(aura => aura.AuraId, StringComparer.Ordinal)
      .ToArray();

    var metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
      ["actionId"] = action.ActionId,
      ["auraId"] = auraId,
      ["auraType"] = action.Aura.AuraType,
      ["controllerPlayerId"] = playerId,
      ["duration"] = action.Aura.Duration,
      ["modifierDuration"] = action.Aura.Modifier.Duration,
      ["modifierType"] = action.Aura.Modifier.ModifierType,
      ["playerId"] = playerId,
      ["priority"] = action.Aura.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture),
      ["sourceRef"] = sourceRef
    };
    WriteAuraMetadata(metadata, nextAuras);

    var mutation = new StateMutation(
      $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}:0001:apply-aura",
      RefreshAurasMutationHandler.SupportedMutationType,
      sourceRef,
      playerId,
      new SortedDictionary<string, int>(StringComparer.Ordinal),
      phase,
      request.CommandSeq,
      null,
      OrderingKey(request.Effect.EffectId, actionIndex, 1),
      metadata);

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, [mutation]));
  }

  private EffectActionCompilationResult CompilePreventDamage(
    EffectActionCompilationRequest request,
    PreventDamageActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (!string.Equals(action.Duration, PlayerModifierDurations.NextDamage, StringComparison.Ordinal))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "PREVENT_DAMAGE currently supports NEXT_DAMAGE duration only.");
    }

    if (!string.Equals(action.PreventionPolicy, "PREVENT_UP_TO_AMOUNT", StringComparison.Ordinal))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "PREVENT_DAMAGE currently supports PREVENT_UP_TO_AMOUNT policy only.");
    }

    if (!TryReadPositiveFixedValue(action.Amount, out var amount))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "PREVENT_DAMAGE amount must be a positive fixed value.");
    }

    if (!TryGetResolvedTargets(request, action.ActionId, out var targets, out var missingTarget))
    {
      return missingTarget!;
    }

    if (targets.Count != 1)
    {
      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.UnsupportedTarget,
        "PREVENT_DAMAGE requires exactly one resolved target.",
        request.Effect.EffectId,
        action.ActionId));
    }

    var playerId = targets[0].PlayerId;
    if (!request.State.Players.TryGetValue(playerId, out var player))
    {
      return MissingPlayer(request, action.ActionId, playerId);
    }

    var modifierSuffix = string.IsNullOrWhiteSpace(action.ResultRef) ? action.ActionId : action.ResultRef;
    var modifierId = $"modifier:{request.CommandSeq}:effect:{request.Effect.EffectId}:{modifierSuffix}";
    if (player.Modifiers.Any(modifier => StringComparer.Ordinal.Equals(modifier.ModifierId, modifierId)))
    {
      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.UnsupportedValueExpression,
        $"Modifier '{modifierId}' already exists for player '{playerId}'.",
        request.Effect.EffectId,
        action.ActionId));
    }

    var mutation = new StateMutation(
      $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}:0001:prevent-damage",
      ApplyModifierMutationHandler.SupportedMutationType,
      sourceRef,
      playerId,
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["amount"] = amount
      },
      phase,
      request.CommandSeq,
      null,
      OrderingKey(request.Effect.EffectId, actionIndex, 1),
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["actionId"] = action.ActionId,
        ["duration"] = PlayerModifierDurations.NextDamage,
        ["modifierId"] = modifierId,
        ["modifierType"] = PlayerModifierTypes.DamagePrevention,
        ["preventionPolicy"] = action.PreventionPolicy,
        ["priority"] = "0",
        ["sourceRef"] = sourceRef,
        ["targetId"] = targets[0].TargetId,
        ["targetKind"] = targets[0].TargetKind,
        ["playerId"] = playerId
      });

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, [mutation]));
  }

  private EffectActionCompilationResult CompileReplaceEvent(
    EffectActionCompilationRequest request,
    ReplaceEventActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (!string.Equals(action.EventType, "DAMAGE_WOULD_BE_DEALT", StringComparison.Ordinal))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "REPLACE_EVENT currently supports DAMAGE_WOULD_BE_DEALT only.");
    }

    if (!string.Equals(action.ReplacementPolicy, "PREVENT_DAMAGE", StringComparison.Ordinal))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "REPLACE_EVENT currently supports PREVENT_DAMAGE replacement policy only.");
    }

    if (!string.Equals(action.Duration, PlayerModifierDurations.NextDamage, StringComparison.Ordinal))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "REPLACE_EVENT currently supports NEXT_DAMAGE duration only.");
    }

    if (!TryGetResolvedTargets(request, action.ActionId, out var targets, out var missingTarget))
    {
      return missingTarget!;
    }

    if (targets.Count != 1)
    {
      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.UnsupportedTarget,
        "REPLACE_EVENT requires exactly one resolved target.",
        request.Effect.EffectId,
        action.ActionId));
    }

    var target = targets[0];
    if (!request.State.Players.TryGetValue(target.PlayerId, out var player))
    {
      return MissingPlayer(request, action.ActionId, target.PlayerId);
    }

    var modifierId = string.IsNullOrWhiteSpace(action.ResultRef)
      ? $"replacement:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}"
      : action.ResultRef;
    if (player.Modifiers.Any(modifier => StringComparer.Ordinal.Equals(modifier.ModifierId, modifierId)))
    {
      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.UnsupportedValueExpression,
        $"Replacement modifier '{modifierId}' already exists for player '{target.PlayerId}'.",
        request.Effect.EffectId,
        action.ActionId));
    }

    var mutation = new StateMutation(
      $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}:0001:replace-event",
      ApplyModifierMutationHandler.SupportedMutationType,
      sourceRef,
      target.PlayerId,
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["amount"] = int.MaxValue
      },
      phase,
      request.CommandSeq,
      null,
      OrderingKey(request.Effect.EffectId, actionIndex, 1),
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["actionId"] = action.ActionId,
        ["duration"] = PlayerModifierDurations.NextDamage,
        ["eventType"] = action.EventType,
        ["modifierId"] = modifierId,
        ["modifierType"] = PlayerModifierTypes.DamagePrevention,
        ["playerId"] = target.PlayerId,
        ["priority"] = "0",
        ["replacementPolicy"] = action.ReplacementPolicy,
        ["sourceRef"] = sourceRef,
        ["targetId"] = target.TargetId,
        ["targetKind"] = target.TargetKind
      });

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, [mutation]));
  }

  private EffectActionCompilationResult CompileBoardStatModifier(
    EffectActionCompilationRequest request,
    ApplyModifierActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (!TryReadPositiveFixedValue(action.Modifier.Value, out var amount))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "APPLY_MODIFIER board stat modifier value must be a positive fixed value.");
    }

    var playerId = ResolvePlayerId(request, action.Player);
    if (!request.State.Players.TryGetValue(playerId, out var player))
    {
      return MissingPlayer(request, action.ActionId, playerId);
    }

    var mutationType = string.Equals(action.Modifier.ModifierType, PlayerModifierTypes.BoardAttackModifier, StringComparison.Ordinal)
      ? ModifyAttackMutationHandler.SupportedMutationType
      : ModifyHealthMutationHandler.SupportedMutationType;
    var modifierId = string.IsNullOrWhiteSpace(action.Modifier.ModifierId)
      ? $"modifier:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}"
      : action.Modifier.ModifierId;

    var mutations = player.Zones.Board
      .OrderBy(entity => entity.EntityInstanceId, StringComparer.Ordinal)
      .Select((entity, index) => new StateMutation(
        $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}:{index + 1:0000}:modify-stat",
        mutationType,
        sourceRef,
        entity.EntityInstanceId,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["amount"] = amount
        },
        phase,
        request.CommandSeq,
        null,
        OrderingKey(request.Effect.EffectId, actionIndex, index + 1),
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["actionId"] = action.ActionId,
          ["duration"] = action.Modifier.Duration,
          ["entityInstanceId"] = entity.EntityInstanceId,
          ["modifierId"] = modifierId,
          ["modifierType"] = action.Modifier.ModifierType,
          ["playerId"] = playerId,
          ["priority"] = action.Modifier.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture),
          ["sourceRef"] = sourceRef
        }))
      .ToArray();

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, mutations));
  }

  private EffectActionCompilationResult CompileHandZoneModifier(
    EffectActionCompilationRequest request,
    ApplyModifierActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (!TryReadZoneValue(action.Modifier.Value, out var destinationZone)
      || destinationZone is not (SourceZone.Deck or SourceZone.Graveyard))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "APPLY_MODIFIER hand zone modifier value must use kind ZONE with DECK or GRAVEYARD ref.");
    }

    var playerId = ResolvePlayerId(request, action.Player);
    if (!request.State.Players.TryGetValue(playerId, out var player))
    {
      return MissingPlayer(request, action.ActionId, playerId);
    }

    var modifierId = string.IsNullOrWhiteSpace(action.Modifier.ModifierId)
      ? $"modifier:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}"
      : action.Modifier.ModifierId;

    var mutations = player.Zones.Hand
      .OrderBy(card => card.CardInstanceId, StringComparer.Ordinal)
      .Select((card, index) => new StateMutation(
        $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}:{index + 1:0000}:hand-zone-modifier",
        MoveCardZoneMutationHandler.SupportedMutationType,
        sourceRef,
        card.CardInstanceId,
        new SortedDictionary<string, int>(StringComparer.Ordinal),
        phase,
        request.CommandSeq,
        null,
        OrderingKey(request.Effect.EffectId, actionIndex, index + 1),
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["actionId"] = action.ActionId,
          ["cardInstanceId"] = card.CardInstanceId,
          ["duration"] = action.Modifier.Duration,
          ["fromZone"] = ZoneId(SourceZone.Hand),
          ["modifierId"] = modifierId,
          ["modifierType"] = action.Modifier.ModifierType,
          ["playerId"] = playerId,
          ["priority"] = action.Modifier.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture),
          ["sourceRef"] = sourceRef,
          ["toZone"] = ZoneId(destinationZone)
        }))
      .ToArray();

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, mutations));
  }

  private EffectActionCompilationResult CompileBoardKeywordModifier(
    EffectActionCompilationRequest request,
    ApplyModifierActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (!TryReadKeywordValue(action.Modifier.Value, out var keyword))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "APPLY_MODIFIER board keyword modifier value must use kind KEYWORD with a keyword ref.");
    }

    var playerId = ResolvePlayerId(request, action.Player);
    if (!request.State.Players.TryGetValue(playerId, out var player))
    {
      return MissingPlayer(request, action.ActionId, playerId);
    }

    var modifierId = string.IsNullOrWhiteSpace(action.Modifier.ModifierId)
      ? $"modifier:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}"
      : action.Modifier.ModifierId;
    var mutationIndex = 0;
    var mutations = player.Zones.Board
      .OrderBy(entity => entity.EntityInstanceId, StringComparer.Ordinal)
      .Where(entity => !(entity.Keywords ?? []).Contains(keyword, StringComparer.Ordinal))
      .Select(entity =>
      {
        mutationIndex++;
        return new StateMutation(
          $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}:{mutationIndex:0000}:add-keyword",
          AddKeywordMutationHandler.SupportedMutationType,
          sourceRef,
          entity.EntityInstanceId,
          new SortedDictionary<string, int>(StringComparer.Ordinal),
          phase,
          request.CommandSeq,
          null,
          OrderingKey(request.Effect.EffectId, actionIndex, mutationIndex),
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["actionId"] = action.ActionId,
            ["duration"] = action.Modifier.Duration,
            ["entityInstanceId"] = entity.EntityInstanceId,
            ["keyword"] = keyword,
            ["modifierId"] = modifierId,
            ["modifierType"] = action.Modifier.ModifierType,
            ["playerId"] = playerId,
            ["priority"] = action.Modifier.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["sourceRef"] = sourceRef
          });
      })
      .ToArray();

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, mutations));
  }

  private EffectActionCompilationResult CompileResurrect(
    EffectActionCompilationRequest request,
    ResurrectActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (!TryReadNonNegativeFixedValue(action.Attack, out var attack))
    {
      return UnsupportedValue(request, action.ActionId, "RESURRECT attack must be a non-negative fixed value.");
    }

    if (!TryReadPositiveFixedValue(action.Health, out var health))
    {
      return UnsupportedValue(request, action.ActionId, "RESURRECT health must be a positive fixed value.");
    }

    if (!string.Equals(action.PlacementPolicy, "APPEND", StringComparison.OrdinalIgnoreCase)
      && !string.Equals(action.PlacementPolicy, "RIGHTMOST", StringComparison.OrdinalIgnoreCase))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "RESURRECT currently supports APPEND or RIGHTMOST placement policies.");
    }

    if (!IsSkipEmptyPolicy(action.BoardFullPolicy)
      && !string.Equals(action.BoardFullPolicy, "FIZZLE_WHOLE_EFFECT", StringComparison.OrdinalIgnoreCase))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "RESURRECT currently supports SKIP_ACTION, SKIP, or FIZZLE_WHOLE_EFFECT board-full policies.");
    }

    if (!TryResolveResurrectTargets(request, action, out var targets, out var missingTarget, out var historyRngRolls))
    {
      return missingTarget!;
    }

    if (targets.Count != 1)
    {
      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.UnsupportedTarget,
        "RESURRECT requires exactly one resolved graveyard card target.",
        request.Effect.EffectId,
        action.ActionId));
    }

    var target = targets[0];
    if (!TryGetCardInZone(request, action.ActionId, target, SourceZone.Graveyard, out var card, out var invalidTarget))
    {
      return invalidTarget!;
    }

    if (card.CardType is not CardType.MinionCard and not CardType.TokenCard)
    {
      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.UnsupportedTarget,
        $"RESURRECT target card '{card.CardInstanceId}' must be a minion or token card.",
        request.Effect.EffectId,
        action.ActionId));
    }

    var controllerId = ResolvePlayerId(request, action.Controller);
    if (!request.State.Players.ContainsKey(controllerId))
    {
      return MissingPlayer(request, action.ActionId, controllerId);
    }

    if (request.MaxBoardSize.HasValue
      && request.State.Players[controllerId].Zones.Board.Count >= request.MaxBoardSize.Value)
    {
      if (IsSkipEmptyPolicy(action.BoardFullPolicy))
      {
        return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, []));
      }

      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.UnsupportedTarget,
        $"RESURRECT cannot summon because player '{controllerId}' board is full.",
        request.Effect.EffectId,
        action.ActionId));
    }

    var entityInstanceId = $"entity:{card.CardInstanceId}:resurrect:{request.CommandSeq:000000}:{action.ActionId}";
    var move = CreateCardZoneMutation(
      request,
      action.ActionId,
      actionIndex,
      1,
      "resurrect-move",
      sourceRef,
      target.PlayerId,
      card.CardInstanceId,
      SourceZone.Graveyard,
      SourceZone.SetAside,
      phase);

    var summonMetadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
      ["actionId"] = action.ActionId,
      ["cardInstanceId"] = card.CardInstanceId,
      ["entityInstanceId"] = entityInstanceId,
      ["entityType"] = "MINION",
      ["fromZone"] = "GRAVEYARD",
      ["playerId"] = controllerId,
      ["sourceCardInstanceId"] = card.CardInstanceId,
      ["toZone"] = "BOARD"
    };
    if (request.MaxBoardSize.HasValue)
    {
      summonMetadata["maxBoardSize"] = request.MaxBoardSize.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    var summon = new StateMutation(
      $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}:0002:resurrect-summon",
      SummonEntityMutationHandler.SupportedMutationType,
      sourceRef,
      entityInstanceId,
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["attack"] = attack,
        ["health"] = health
      },
      phase,
      request.CommandSeq,
      null,
      OrderingKey(request.Effect.EffectId, actionIndex, 2),
      summonMetadata);

    return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, [move, summon]), historyRngRolls);
  }

  private EffectActionCompilationResult CompileDiscover(
    EffectActionCompilationRequest request,
    DiscoverActionDefinition action,
    int actionIndex,
    string sourceRef,
    string phase)
  {
    if (action.OfferCount <= 0)
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "DISCOVER offerCount must be greater than zero.");
    }

    if (!string.Equals(action.ChoicePolicy, "PLAYER_CHOICE", StringComparison.Ordinal))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "DISCOVER currently supports PLAYER_CHOICE only.");
    }

    if (action.AddToZone is not SourceZone.Hand)
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        "DISCOVER currently supports addToZone HAND only.");
    }

    var playerId = ResolvePlayerId(request, action.Player);
    if (!request.State.Players.ContainsKey(playerId))
    {
      return MissingPlayer(request, action.ActionId, playerId);
    }

    if (request.Pools is null
      || !request.Pools.TryGetValue(action.PoolRef.Ref, out var pool))
    {
      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.MissingTarget,
        $"DISCOVER pool '{action.PoolRef.Ref}' is not available.",
        request.Effect.EffectId,
        action.ActionId));
    }

    var poolResolution = new PoolResolver().Resolve(new PoolResolutionRequest(
      pool.Ordering == FixedPoolOrdering.Random
        ? pool with { Ordering = FixedPoolOrdering.Canonical }
        : pool));
    if (!poolResolution.IsAccepted)
    {
      if (poolResolution.Errors.Any(error => error.Code is PoolResolutionErrorCode.EmptyPool)
        && IsSkipEmptyPolicy(action.PoolEmptyPolicy))
      {
        return EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, []));
      }

      var error = poolResolution.Errors.FirstOrDefault();
      return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.MissingTarget,
        error is null
          ? $"DISCOVER pool '{action.PoolRef.Ref}' could not be resolved."
          : $"DISCOVER pool '{action.PoolRef.Ref}' resolution failed: {error.Code}.",
        request.Effect.EffectId,
        action.ActionId));
    }

    var offerSelection = SelectDiscoverOffers(request, action, pool, poolResolution.Candidates, playerId);
    if (!offerSelection.IsAccepted)
    {
      return offerSelection.Result!;
    }

    var offers = offerSelection.Offers;
    if (offers.Count == 0)
    {
      return IsSkipEmptyPolicy(action.PoolEmptyPolicy)
        ? EffectActionCompilationResult.Accepted(CreateBatch(request, sourceRef, phase, []))
        : EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
          EffectActionCompilationDiagnosticCode.MissingTarget,
          $"DISCOVER pool '{action.PoolRef.Ref}' produced no offers.",
          request.Effect.EffectId,
          action.ActionId));
    }

    var choiceId = string.IsNullOrWhiteSpace(action.ResultRef)
      ? $"choice:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}"
      : action.ResultRef;
    if (request.State.PendingChoices.ContainsKey(choiceId))
    {
      return UnsupportedValue(
        request,
        action.ActionId,
        $"Choice '{choiceId}' already exists.");
    }

    var metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
      ["actionId"] = action.ActionId,
      ["choiceId"] = choiceId,
      ["choicePolicy"] = action.ChoicePolicy,
      ["choiceType"] = "DISCOVER",
      ["playerId"] = playerId,
      ["poolId"] = pool.PoolId,
      ["poolRef"] = action.PoolRef.Ref,
      ["resultZone"] = ZoneId(action.AddToZone),
      ["visibilityPolicy"] = "PUBLIC"
    };

    for (var index = 0; index < offers.Count; index++)
    {
      var optionNumber = index + 1;
      var prefix = $"option:{optionNumber:0000}:";
      metadata[prefix + "id"] = $"{choiceId}:option:{optionNumber:0000}";
      metadata[prefix + "itemKind"] = offers[index].ItemKind;
      metadata[prefix + "itemId"] = offers[index].ItemId;
    }

    var mutation = new StateMutation(
      $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{action.ActionId}:0001:create-choice",
      CreateChoiceMutationHandler.SupportedMutationType,
      sourceRef,
      playerId,
      new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["optionCount"] = offers.Count
      },
      phase,
      request.CommandSeq,
      null,
      OrderingKey(request.Effect.EffectId, actionIndex, 1),
      metadata);

    return EffectActionCompilationResult.Accepted(
      CreateBatch(request, sourceRef, phase, [mutation]),
      offerSelection.RngRolls);
  }

  private DiscoverOfferSelection SelectDiscoverOffers(
    EffectActionCompilationRequest request,
    DiscoverActionDefinition action,
    FixedPoolDefinition pool,
    IReadOnlyList<PoolItemRef> candidates,
    string playerId)
  {
    if (!string.Equals(pool.Ordering, FixedPoolOrdering.Random, StringComparison.Ordinal))
    {
      return DiscoverOfferSelection.Accepted(candidates.Take(action.OfferCount).ToArray(), []);
    }

    if (request.RngRoller is null)
    {
      return DiscoverOfferSelection.Rejected(EffectActionCompilationResult.Rejected(
        new EffectActionCompilationDiagnostic(
          EffectActionCompilationDiagnosticCode.UnsupportedAction,
          "DISCOVER random pool ordering requires deterministic RNG roll logging context.",
          request.Effect.EffectId,
          action.ActionId)));
    }

    var remaining = candidates.ToList();
    var selected = new List<PoolItemRef>();
    var rngRolls = new List<EffectActionRngRoll>();
    var offerCount = Math.Min(action.OfferCount, remaining.Count);
    for (var index = 0; index < offerCount; index++)
    {
      var roll = request.RngRoller.SelectOne(
        remaining,
        RngStreamKind.DiscoverOffer,
        $"DISCOVER:{action.PoolRef.Ref}:offer:{index + 1:0000}",
        request.CommandSeq,
        $"effect:{request.Effect.EffectId}:action:{action.ActionId}:offer:{index + 1:0000}");
      selected.Add(roll.Selected);
      remaining.RemoveAt(roll.Roll.SelectedIndex);
      rngRolls.Add(new EffectActionRngRoll(
        roll.Roll,
        [new ResolvedTargetRef(ResolvedTargetRef.CardKind, playerId, roll.Selected.ItemId)]));
    }

    return DiscoverOfferSelection.Accepted(selected, rngRolls);
  }

  private static bool IsSkipDeckEmptyPolicy(string policy) =>
    string.Equals(policy, "SKIP_ACTION", StringComparison.OrdinalIgnoreCase)
    || string.Equals(policy, "FIZZLE", StringComparison.OrdinalIgnoreCase)
    || string.Equals(policy, "FIZZLE_WHOLE_EFFECT", StringComparison.OrdinalIgnoreCase);

  private static bool IsSkipEmptyPolicy(string policy) =>
    string.Equals(policy, "SKIP_ACTION", StringComparison.OrdinalIgnoreCase)
    || string.Equals(policy, "SKIP", StringComparison.OrdinalIgnoreCase);

  private static bool IsSkipInvalidTargetsPolicy(string policy) =>
    string.Equals(policy, "SKIP_INVALID_TARGETS", StringComparison.OrdinalIgnoreCase)
    || string.Equals(policy, "RESOLVE_PARTIAL", StringComparison.OrdinalIgnoreCase);

  private sealed record DiscoverOfferSelection(
    bool IsAccepted,
    IReadOnlyList<PoolItemRef> Offers,
    IReadOnlyList<EffectActionRngRoll> RngRolls,
    EffectActionCompilationResult? Result)
  {
    public static DiscoverOfferSelection Accepted(
      IReadOnlyList<PoolItemRef> offers,
      IReadOnlyList<EffectActionRngRoll> rngRolls) =>
      new(true, offers, rngRolls, null);

    public static DiscoverOfferSelection Rejected(EffectActionCompilationResult? result) =>
      new(false, [], [], result);
  }

  private static EffectActionCompilationResult UnsupportedValue(
    EffectActionCompilationRequest request,
    string actionId,
    string message)
  {
    return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
      EffectActionCompilationDiagnosticCode.UnsupportedValueExpression,
      message,
      request.Effect.EffectId,
      actionId));
  }

  private static bool IsSupportedModifier(string modifierType, string duration) =>
    string.Equals(modifierType, PlayerModifierTypes.CostModifier, StringComparison.Ordinal)
      && (string.Equals(duration, PlayerModifierDurations.NextPlay, StringComparison.Ordinal)
        || string.Equals(duration, PlayerModifierDurations.RestOfTurn, StringComparison.Ordinal))
    || string.Equals(modifierType, PlayerModifierTypes.DamagePrevention, StringComparison.Ordinal)
      && string.Equals(duration, PlayerModifierDurations.NextDamage, StringComparison.Ordinal);

  private static bool IsSupportedBoardStatModifier(string modifierType, string duration) =>
    string.Equals(duration, PlayerModifierDurations.Permanent, StringComparison.Ordinal)
    && (string.Equals(modifierType, PlayerModifierTypes.BoardAttackModifier, StringComparison.Ordinal)
      || string.Equals(modifierType, PlayerModifierTypes.BoardHealthModifier, StringComparison.Ordinal));

  private static bool IsSupportedBoardKeywordModifier(string modifierType, string duration) =>
    string.Equals(modifierType, PlayerModifierTypes.BoardKeywordModifier, StringComparison.Ordinal)
    && string.Equals(duration, PlayerModifierDurations.Permanent, StringComparison.Ordinal);

  private static bool IsSupportedHandZoneModifier(string modifierType, string duration) =>
    string.Equals(modifierType, PlayerModifierTypes.HandZoneModifier, StringComparison.Ordinal)
    && string.Equals(duration, PlayerModifierDurations.Permanent, StringComparison.Ordinal);

  private static bool IsSupportedAuraModifier(string modifierType, string duration) =>
    IsSupportedAuraDuration(duration)
    && (string.Equals(modifierType, PlayerModifierTypes.BoardAttackModifier, StringComparison.Ordinal)
      || string.Equals(modifierType, PlayerModifierTypes.BoardHealthModifier, StringComparison.Ordinal)
      || string.Equals(modifierType, PlayerModifierTypes.BoardKeywordModifier, StringComparison.Ordinal));

  private static bool IsSupportedAuraDuration(string duration) =>
    string.Equals(duration, PlayerModifierDurations.Permanent, StringComparison.Ordinal)
    || string.Equals(duration, PlayerModifierDurations.WhileSourceActive, StringComparison.Ordinal);

  private static bool IsSupportedDamageDistributionLifestealPolicy(string policy) =>
    string.Equals(policy, "NO_LIFESTEAL", StringComparison.Ordinal)
    || string.Equals(policy, "AGGREGATE_LIFESTEAL", StringComparison.Ordinal);

  private static bool IsSupportedDamageDistributionOverkillPolicy(string policy) =>
    string.Equals(policy, "ALLOW_OVERKILL", StringComparison.Ordinal)
    || string.Equals(policy, "CAP_AT_REMAINING_HEALTH", StringComparison.Ordinal)
    || string.Equals(policy, "SKIP_DEAD_TARGETS", StringComparison.Ordinal);

  private static bool TryReadKeywordValue(ValueExpression expression, out string keyword)
  {
    keyword = expression.Ref ?? string.Empty;
    return string.Equals(expression.Kind, "KEYWORD", StringComparison.OrdinalIgnoreCase)
      && !string.IsNullOrWhiteSpace(expression.Ref);
  }

  private static bool TryReadZoneValue(ValueExpression expression, out SourceZone zone)
  {
    zone = expression.Ref switch
    {
      "DECK" => SourceZone.Deck,
      "GRAVEYARD" => SourceZone.Graveyard,
      _ => default
    };

    return string.Equals(expression.Kind, "ZONE", StringComparison.OrdinalIgnoreCase)
      && expression.Ref is "DECK" or "GRAVEYARD";
  }

  private static bool TryReadPositiveFixedValue(ValueExpression expression, out int value)
  {
    value = expression.FixedValue ?? 0;
    return string.Equals(expression.Kind, "FIXED", StringComparison.OrdinalIgnoreCase)
      && expression.FixedValue is > 0;
  }

  private static bool TryReadNonNegativeFixedValue(ValueExpression expression, out int value)
  {
    value = expression.FixedValue ?? -1;
    return string.Equals(expression.Kind, "FIXED", StringComparison.OrdinalIgnoreCase)
      && expression.FixedValue is >= 0;
  }

  private static bool TryGetRemainingHealth(
    IGameState state,
    ResolvedTargetRef target,
    out int remainingHealth)
  {
    remainingHealth = 0;
    if (!state.Players.TryGetValue(target.PlayerId, out var player))
    {
      return false;
    }

    var entity = player.Zones.Board.SingleOrDefault(candidate =>
      StringComparer.Ordinal.Equals(candidate.EntityInstanceId, target.TargetId));
    if (entity is null
      && player.Hero is not null
      && StringComparer.Ordinal.Equals(player.Hero.EntityInstanceId, target.TargetId))
    {
      entity = player.Hero;
    }

    if (entity is null)
    {
      return false;
    }

    remainingHealth = entity.Health - entity.Damage;
    return true;
  }

  private static bool TryResolveAuraSource(
    EffectActionCompilationRequest request,
    string actionId,
    out EntityInstance sourceEntity,
    out string sourceCardVersion,
    out EffectActionCompilationResult? result)
  {
    sourceEntity = null!;
    sourceCardVersion = string.Empty;
    result = null;

    if (request.Source is null)
    {
      result = EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.MissingTarget,
        "APPLY_AURA requires a board entity or source-card ref.",
        request.Effect.EffectId,
        actionId));
      return false;
    }

    if (!request.State.Players.TryGetValue(request.Source.PlayerId, out var sourcePlayer))
    {
      result = MissingPlayer(request, actionId, request.Source.PlayerId);
      return false;
    }

    var entity = string.Equals(request.Source.TargetKind, ResolvedTargetRef.EntityKind, StringComparison.Ordinal)
      ? sourcePlayer.Zones.Board.SingleOrDefault(candidate =>
        StringComparer.Ordinal.Equals(candidate.EntityInstanceId, request.Source.TargetId))
      : string.Equals(request.Source.TargetKind, ResolvedTargetRef.CardKind, StringComparison.Ordinal)
        ? sourcePlayer.Zones.Board.SingleOrDefault(candidate =>
          StringComparer.Ordinal.Equals(candidate.SourceCardInstanceId, request.Source.TargetId))
        : null;
    if (entity is null)
    {
      result = EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.MissingTarget,
        $"APPLY_AURA source '{request.Source.TargetId}' is not represented by a board entity.",
        request.Effect.EffectId,
        actionId));
      return false;
    }

    var sourceCard = sourcePlayer.Zones.SetAside
      .Concat(sourcePlayer.Zones.Hand)
      .Concat(sourcePlayer.Zones.Deck)
      .Concat(sourcePlayer.Zones.Graveyard)
      .Concat(sourcePlayer.Zones.SecretZone)
      .SingleOrDefault(card => StringComparer.Ordinal.Equals(card.CardInstanceId, entity.SourceCardInstanceId));
    sourceEntity = entity;
    sourceCardVersion = sourceCard?.CardVersion ?? "unknown";
    return true;
  }

  private static void WriteAuraMetadata(
    SortedDictionary<string, string> metadata,
    IReadOnlyList<PlayerAura> auras)
  {
    for (var index = 0; index < auras.Count; index++)
    {
      var aura = auras[index];
      var prefix = $"aura:{index + 1:0000}:";
      metadata[prefix + "auraId"] = aura.AuraId;
      metadata[prefix + "controllerPlayerId"] = aura.ControllerPlayerId;
      metadata[prefix + "effectId"] = aura.EffectId;
      metadata[prefix + "priority"] = aura.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture);
      metadata[prefix + "sourceCardInstanceId"] = aura.SourceCardInstanceId;
      metadata[prefix + "sourceCardVersion"] = aura.SourceCardVersion;
      metadata[prefix + "sourceEntityInstanceId"] = aura.SourceEntityInstanceId;
    }
  }

  private static bool TryGetResolvedTargets(
    EffectActionCompilationRequest request,
    string actionId,
    out IReadOnlyList<ResolvedTargetRef> targets,
    out EffectActionCompilationResult? result)
  {
    targets = request.ChosenTargets ?? [];
    result = null;
    if (targets.Count > 0)
    {
      return true;
    }

    result = EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
      EffectActionCompilationDiagnosticCode.MissingTarget,
      "Action requires at least one resolved target.",
      request.Effect.EffectId,
      actionId));
    return false;
  }

  private static bool TryResolveResurrectTargets(
    EffectActionCompilationRequest request,
    ResurrectActionDefinition action,
    out IReadOnlyList<ResolvedTargetRef> targets,
    out EffectActionCompilationResult? result,
    out IReadOnlyList<EffectActionRngRoll> rngRolls)
  {
    targets = request.ChosenTargets ?? [];
    result = null;
    rngRolls = [];
    if (targets.Count > 0)
    {
      return true;
    }

    if (action.HistoryQuery is null)
    {
      result = EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.MissingTarget,
        "RESURRECT requires a resolved target or supported history query.",
        request.Effect.EffectId,
        action.ActionId));
      return false;
    }

    if (action.HistoryQuery.Kind is not ("OWN_GRAVEYARD_MINION" or "OPPONENT_GRAVEYARD_MINION" or "ANY_GRAVEYARD_MINION" or "ANY_DEAD_MINION"))
    {
      result = UnsupportedValue(
        request,
        action.ActionId,
        "RESURRECT history query currently supports OWN_GRAVEYARD_MINION, OPPONENT_GRAVEYARD_MINION, ANY_GRAVEYARD_MINION, and ANY_DEAD_MINION only.");
      return false;
    }

    if (action.HistoryQuery.MaxCandidates is <= 0 or > 30)
    {
      result = UnsupportedValue(
        request,
        action.ActionId,
        "RESURRECT history query maxCandidates must be between 1 and 30.");
      return false;
    }

    if (!TryGetHistoryQueryCandidates(request, action, out var candidates, out var missingPlayerResult))
    {
      result = missingPlayerResult;
      return false;
    }

    if (candidates.Count == 0)
    {
      if (IsSkipEmptyPolicy(action.BoardFullPolicy))
      {
        targets = [];
        result = EffectActionCompilationResult.Accepted(CreateBatch(
          request,
          $"effect:{request.Effect.EffectId}:{request.SourceRef}",
          ToCanonicalPhase(request.Effect.TimingPolicy.Phase),
          []));
        return false;
      }

      result = EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.MissingTarget,
        $"RESURRECT history query '{action.HistoryQuery.Kind}' found no eligible graveyard minion.",
        request.Effect.EffectId,
        action.ActionId));
      return false;
    }

    var selected = SelectHistoryQueryCard(request, action, candidates, out var rngRoll);
    if (selected is null)
    {
      result = UnsupportedValue(
        request,
        action.ActionId,
        "RESURRECT history query ordering must be FIRST, LAST, LOWEST_CARD_INSTANCE_ID, or RANDOM.");
      return false;
    }

    targets = [new ResolvedTargetRef(ResolvedTargetRef.CardKind, selected.OwnerPlayerId, selected.Card.CardInstanceId)];
    if (rngRoll is not null)
    {
      rngRolls = [rngRoll];
    }
    return true;
  }

  private static bool TryGetHistoryQueryCandidates(
    EffectActionCompilationRequest request,
    ResurrectActionDefinition action,
    out IReadOnlyList<HistoryQueryCandidate> candidates,
    out EffectActionCompilationResult? result)
  {
    candidates = [];
    result = null;
    if (string.Equals(action.HistoryQuery?.Kind, "ANY_DEAD_MINION", StringComparison.Ordinal))
    {
      if (request.HistoryStore is null)
      {
        result = UnsupportedValue(
          request,
          action.ActionId,
          "RESURRECT ANY_DEAD_MINION history query requires replay-regenerated history context.");
        return false;
      }

      candidates = request.HistoryStore
        .FindDeadMinions(maxCandidates: action.HistoryQuery!.MaxCandidates)
        .Select(candidate => TryFindCurrentGraveyardCard(request.State, candidate.OwnerPlayerId, candidate.SourceCardInstanceId, out var card)
          ? new HistoryQueryCandidate(candidate.OwnerPlayerId, card, candidate.EventSeq)
          : null)
        .Where(candidate => candidate is not null)
        .Select(candidate => candidate!)
        .ToArray();
      return true;
    }

    if (string.Equals(action.HistoryQuery?.Kind, "ANY_GRAVEYARD_MINION", StringComparison.Ordinal))
    {
      candidates = request.State.Players
        .OrderBy(player => player.Key, StringComparer.Ordinal)
        .SelectMany(player => player.Value.Zones.Graveyard
          .Where(card => card.CardType is CardType.MinionCard or CardType.TokenCard)
          .Select(card => new HistoryQueryCandidate(player.Key, card, null)))
        .Take(action.HistoryQuery!.MaxCandidates)
        .ToArray();
      return true;
    }

    var playerId = ResolvePlayerId(request, action.HistoryQuery!.Player);
    if (!request.State.Players.TryGetValue(playerId, out var player))
    {
      result = MissingPlayer(request, action.ActionId, playerId);
      return false;
    }

    candidates = player.Zones.Graveyard
      .Where(card => card.CardType is CardType.MinionCard or CardType.TokenCard)
      .Select(card => new HistoryQueryCandidate(playerId, card, null))
      .Take(action.HistoryQuery.MaxCandidates)
      .ToArray();
    return true;
  }

  private static bool TryFindCurrentGraveyardCard(
    IGameState state,
    string playerId,
    string cardInstanceId,
    out CardInstance card)
  {
    card = null!;
    if (!state.Players.TryGetValue(playerId, out var player))
    {
      return false;
    }

    var candidate = player.Zones.Graveyard.SingleOrDefault(cardInZone =>
      StringComparer.Ordinal.Equals(cardInZone.CardInstanceId, cardInstanceId)
      && cardInZone.CardType is CardType.MinionCard or CardType.TokenCard);
    if (candidate is null)
    {
      return false;
    }

    card = candidate;
    return true;
  }

  private static HistoryQueryCandidate? SelectHistoryQueryCard(
    EffectActionCompilationRequest request,
    ResurrectActionDefinition action,
    IReadOnlyList<HistoryQueryCandidate> candidates,
    out EffectActionRngRoll? rngRoll)
  {
    rngRoll = null;
    if (string.Equals(action.HistoryQuery?.Ordering, "RANDOM", StringComparison.Ordinal))
    {
      if (request.RngRoller is null)
      {
        return null;
      }

      var selection = request.RngRoller.SelectOne(
        candidates,
        RngStreamKind.RandomTarget,
        "HISTORY_QUERY",
        request.CommandSeq,
        $"history-query:{request.Effect.EffectId}:{action.ActionId}");
      var selectedTarget = new ResolvedTargetRef(
        ResolvedTargetRef.CardKind,
        selection.Selected.OwnerPlayerId,
        selection.Selected.Card.CardInstanceId);
      rngRoll = new EffectActionRngRoll(selection.Roll, [selectedTarget]);
      return selection.Selected;
    }

    return action.HistoryQuery?.Ordering switch
    {
      "FIRST" => candidates.FirstOrDefault(),
      "LAST" => candidates.LastOrDefault(),
      "LOWEST_CARD_INSTANCE_ID" => candidates.OrderBy(candidate => candidate.Card.CardInstanceId, StringComparer.Ordinal).FirstOrDefault(),
      _ => null
    };
  }

  private sealed record HistoryQueryCandidate(string OwnerPlayerId, CardInstance Card, long? HistoryEventSeq);

  private static bool TryResolveBatchDestroyTargets(
    EffectActionCompilationRequest request,
    string actionId,
    TargetRef targetRef,
    IDictionary<string, IReadOnlyList<ResolvedTargetRef>> actionResults,
    out IReadOnlyList<ResolvedTargetRef> targets,
    out EffectActionCompilationResult? result)
  {
    targets = [];
    result = null;

    if (TryParseAllMinionsExceptActionResult(targetRef.Ref, out var resultKey))
    {
      if (!actionResults.TryGetValue(resultKey, out var excludedTargets))
      {
        result = EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
          EffectActionCompilationDiagnosticCode.MissingTarget,
          $"Action result '{resultKey}' is not available.",
          request.Effect.EffectId,
          actionId));
        return false;
      }

      var excluded = excludedTargets.ToHashSet();
      targets = AllBoardMinionTargets(request.State)
        .Where(target => !excluded.Contains(target))
        .ToArray();
      return true;
    }

    if (TryParseActionResult(targetRef.Ref, out resultKey))
    {
      if (!actionResults.TryGetValue(resultKey, out var actionResultTargets))
      {
        result = EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
          EffectActionCompilationDiagnosticCode.MissingTarget,
          $"Action result '{resultKey}' is not available.",
          request.Effect.EffectId,
          actionId));
        return false;
      }

      targets = actionResultTargets;
      return true;
    }

    return TryGetResolvedTargets(request, actionId, out targets, out result);
  }

  private static IReadOnlyList<ResolvedTargetRef> AllBoardMinionTargets(IGameState state)
  {
    return state.Players.Values
      .OrderBy(player => player.PlayerId, StringComparer.Ordinal)
      .SelectMany(player => player.Zones.Board
        .Where(entity => entity.EntityType is EntityType.Minion && entity.Damage < entity.Health)
        .OrderBy(entity => entity.EntityInstanceId, StringComparer.Ordinal)
        .Select(entity => new ResolvedTargetRef(
          ResolvedTargetRef.EntityKind,
          player.PlayerId,
          entity.EntityInstanceId)))
      .ToArray();
  }

  private static bool TryParseAllMinionsExceptActionResult(string value, out string resultKey)
  {
    resultKey = string.Empty;
    const string prefix = "ALL_MINIONS_EXCEPT(";
    const string suffix = ")";
    if (!value.StartsWith(prefix, StringComparison.Ordinal)
      || !value.EndsWith(suffix, StringComparison.Ordinal))
    {
      return false;
    }

    var inner = value[prefix.Length..^suffix.Length];
    if (TryParseActionResult(inner, out resultKey))
    {
      return true;
    }

    resultKey = inner;
    return !string.IsNullOrWhiteSpace(resultKey);
  }

  private static bool TryParseActionResult(string value, out string resultKey)
  {
    resultKey = string.Empty;
    const string prefix = "ACTION_RESULT:";
    if (!value.StartsWith(prefix, StringComparison.Ordinal))
    {
      return false;
    }

    resultKey = value[prefix.Length..];
    return !string.IsNullOrWhiteSpace(resultKey);
  }

  private static bool TryGetEntityOrHero(
    EffectActionCompilationRequest request,
    string actionId,
    ResolvedTargetRef target,
    out EntityInstance entity,
    out EffectActionCompilationResult? result)
  {
    entity = null!;
    result = null;
    if (!request.State.Players.TryGetValue(target.PlayerId, out var player))
    {
      result = MissingPlayer(request, actionId, target.PlayerId);
      return false;
    }

    if (string.Equals(target.TargetKind, ResolvedTargetRef.EntityKind, StringComparison.Ordinal))
    {
      var boardEntity = player.Zones.Board.SingleOrDefault(candidate =>
        StringComparer.Ordinal.Equals(candidate.EntityInstanceId, target.TargetId));
      if (boardEntity is not null)
      {
        entity = boardEntity;
        return true;
      }
    }

    if (string.Equals(target.TargetKind, ResolvedTargetRef.HeroKind, StringComparison.Ordinal)
      && player.Hero is not null
      && StringComparer.Ordinal.Equals(player.Hero.EntityInstanceId, target.TargetId))
    {
      entity = player.Hero;
      return true;
    }

    result = EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
      EffectActionCompilationDiagnosticCode.UnsupportedTarget,
      $"Target '{target.TargetId}' is not a live entity or hero for player '{target.PlayerId}'.",
      request.Effect.EffectId,
      actionId));
    return false;
  }

  private static bool TargetEntityExists(PlayerState player, ResolvedTargetRef target)
  {
    if (string.Equals(target.TargetKind, ResolvedTargetRef.EntityKind, StringComparison.Ordinal))
    {
      return player.Zones.ContainsEntityOnBoard(target.TargetId);
    }

    return string.Equals(target.TargetKind, ResolvedTargetRef.HeroKind, StringComparison.Ordinal)
      && player.Hero is not null
      && StringComparer.Ordinal.Equals(player.Hero.EntityInstanceId, target.TargetId);
  }

  private sealed record DamageDistributionPlan(
    bool IsAccepted,
    IReadOnlyList<StateMutation> Mutations,
    EffectActionCompilationResult? Result)
  {
    public static DamageDistributionPlan Accepted(IReadOnlyList<StateMutation> mutations) =>
      new(true, mutations, null);

    public static DamageDistributionPlan Rejected(EffectActionCompilationResult result) =>
      new(false, [], result);
  }

  private static bool TryGetBoardEntity(
    EffectActionCompilationRequest request,
    string actionId,
    ResolvedTargetRef target,
    out EntityInstance entity,
    out EffectActionCompilationResult? result)
  {
    entity = null!;
    result = null;
    if (!string.Equals(target.TargetKind, ResolvedTargetRef.EntityKind, StringComparison.Ordinal))
    {
      result = EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.UnsupportedTarget,
        "Destroy actions support board entity targets only.",
        request.Effect.EffectId,
        actionId));
      return false;
    }

    if (!request.State.Players.TryGetValue(target.PlayerId, out var player))
    {
      result = MissingPlayer(request, actionId, target.PlayerId);
      return false;
    }

    var boardEntity = player.Zones.Board.SingleOrDefault(candidate =>
      StringComparer.Ordinal.Equals(candidate.EntityInstanceId, target.TargetId));
    if (boardEntity is null)
    {
      result = EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.MissingTarget,
        $"Target entity '{target.TargetId}' is not on the board for player '{target.PlayerId}'.",
        request.Effect.EffectId,
        actionId));
      return false;
    }

    entity = boardEntity;
    return true;
  }

  private static bool TryFindCardZone(
    EffectActionCompilationRequest request,
    string actionId,
    ResolvedTargetRef target,
    out SourceZone zone,
    out EffectActionCompilationResult? result)
  {
    zone = default;
    result = null;
    if (!string.Equals(target.TargetKind, ResolvedTargetRef.CardKind, StringComparison.Ordinal))
    {
      result = EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.UnsupportedTarget,
        "Card zone actions support card targets only.",
        request.Effect.EffectId,
        actionId));
      return false;
    }

    if (!request.State.Players.TryGetValue(target.PlayerId, out var player))
    {
      result = MissingPlayer(request, actionId, target.PlayerId);
      return false;
    }

    foreach (var candidateZone in CardInstanceZones())
    {
      if (player.Zones.ContainsCardInZone(target.TargetId, candidateZone))
      {
        zone = candidateZone;
        return true;
      }
    }

    result = EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
      EffectActionCompilationDiagnosticCode.MissingTarget,
      $"Target card '{target.TargetId}' does not exist in a card zone for player '{target.PlayerId}'.",
      request.Effect.EffectId,
      actionId));
    return false;
  }

  private static bool TryGetCardInZone(
    EffectActionCompilationRequest request,
    string actionId,
    ResolvedTargetRef target,
    SourceZone zone,
    out CardInstance card,
    out EffectActionCompilationResult? result)
  {
    card = null!;
    result = null;
    if (!string.Equals(target.TargetKind, ResolvedTargetRef.CardKind, StringComparison.Ordinal))
    {
      result = EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
        EffectActionCompilationDiagnosticCode.UnsupportedTarget,
        "Card zone actions support card targets only.",
        request.Effect.EffectId,
        actionId));
      return false;
    }

    if (!request.State.Players.TryGetValue(target.PlayerId, out var player))
    {
      result = MissingPlayer(request, actionId, target.PlayerId);
      return false;
    }

    var candidateCard = zone switch
    {
      SourceZone.Deck => player.Zones.Deck.SingleOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.CardInstanceId, target.TargetId)),
      SourceZone.Hand => player.Zones.Hand.SingleOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.CardInstanceId, target.TargetId)),
      SourceZone.Graveyard => player.Zones.Graveyard.SingleOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.CardInstanceId, target.TargetId)),
      SourceZone.SetAside => player.Zones.SetAside.SingleOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.CardInstanceId, target.TargetId)),
      _ => null
    };

    if (candidateCard is not null)
    {
      card = candidateCard;
      return true;
    }

    result = EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
      EffectActionCompilationDiagnosticCode.MissingTarget,
      $"Target card '{target.TargetId}' is not in zone '{ZoneId(zone)}' for player '{target.PlayerId}'.",
      request.Effect.EffectId,
      actionId));
    return false;
  }

  private static StateMutation CreateEntityMutation(
    EffectActionCompilationRequest request,
    string actionId,
    int actionIndex,
    int targetIndex,
    string suffix,
    string mutationType,
    string sourceRef,
    string playerId,
    string entityInstanceId,
    string phase,
    SortedDictionary<string, int> payload)
  {
    return new StateMutation(
      $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{actionId}:{targetIndex:0000}:{suffix}",
      mutationType,
      sourceRef,
      entityInstanceId,
      payload,
      phase,
      request.CommandSeq,
      null,
      OrderingKey(request.Effect.EffectId, actionIndex, targetIndex),
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["actionId"] = actionId,
        ["entityInstanceId"] = entityInstanceId,
        ["playerId"] = playerId
      });
  }

  private static StateMutation CreateCardZoneMutation(
    EffectActionCompilationRequest request,
    string actionId,
    int actionIndex,
    int targetIndex,
    string suffix,
    string sourceRef,
    string playerId,
    string cardInstanceId,
    SourceZone fromZone,
    SourceZone toZone,
    string phase)
  {
    return new StateMutation(
      $"mutation:{request.CommandSeq}:effect:{request.Effect.EffectId}:{actionId}:{targetIndex:0000}:{suffix}",
      MoveCardZoneMutationHandler.SupportedMutationType,
      sourceRef,
      cardInstanceId,
      new SortedDictionary<string, int>(StringComparer.Ordinal),
      phase,
      request.CommandSeq,
      null,
      OrderingKey(request.Effect.EffectId, actionIndex, targetIndex),
      new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["actionId"] = actionId,
        ["cardInstanceId"] = cardInstanceId,
        ["fromZone"] = ZoneId(fromZone),
        ["playerId"] = playerId,
        ["toZone"] = ZoneId(toZone)
      });
  }

  private static EffectActionCompilationResult MissingPlayer(
    EffectActionCompilationRequest request,
    string actionId,
    string playerId)
  {
    return EffectActionCompilationResult.Rejected(new EffectActionCompilationDiagnostic(
      EffectActionCompilationDiagnosticCode.MissingPlayer,
      $"Player '{playerId}' does not exist.",
      request.Effect.EffectId,
      actionId));
  }

  private static IReadOnlyList<SourceZone> CardInstanceZones() =>
  [
    SourceZone.Deck,
    SourceZone.Hand,
    SourceZone.Graveyard,
    SourceZone.SetAside
  ];

  private static string ZoneId(SourceZone zone)
  {
    return zone switch
    {
      SourceZone.Deck => "DECK",
      SourceZone.Hand => "HAND",
      SourceZone.Graveyard => "GRAVEYARD",
      SourceZone.SetAside => "SET_ASIDE",
      _ => zone.ToString().ToUpperInvariant()
    };
  }

  private static string ResolvePlayerId(EffectActionCompilationRequest request, PlayerSelector selector)
  {
    return selector switch
    {
      PlayerSelector.Self => request.ActingPlayerId,
      PlayerSelector.SourceController => request.SourceControllerPlayerId,
      PlayerSelector.Opponent => request.State.Players.Keys
        .Where(playerId => !string.Equals(playerId, request.ActingPlayerId, StringComparison.Ordinal))
        .Order(StringComparer.Ordinal)
        .FirstOrDefault() ?? string.Empty,
      _ => string.Empty
    };
  }

  private static ProposedMutationBatch CreateBatch(
    EffectActionCompilationRequest request,
    string sourceRef,
    string phase,
    IReadOnlyList<StateMutation> mutations)
  {
    return new ProposedMutationBatch(
      $"batch:{request.CommandSeq}:effect:{request.Effect.EffectId}",
      request.CommandSeq,
      null,
      phase,
      sourceRef,
      "EFFECT_ACTION_COMPILER",
      mutations,
      request.PreStateHash,
      null,
      request.Effect.FizzlePolicy.Kind);
  }

  private static string OrderingKey(string effectId, int actionIndex, int mutationIndex)
  {
    return $"effect:{effectId}:{actionIndex:0000}:{mutationIndex:0000}";
  }

  private static string ToCanonicalPhase(Phase phase)
  {
    return phase switch
    {
      Phase.OnPlay => "ON_PLAY",
      Phase.BattlecryPhase => "BATTLECRY_PHASE",
      Phase.OnSummon => "ON_SUMMON",
      Phase.AfterPlay => "AFTER_PLAY",
      Phase.SecretWindow => "SECRET_WINDOW",
      Phase.DeathrattlePhase => "DEATHRATTLE_PHASE",
      Phase.TurnStart => "TURN_START",
      Phase.TurnEnd => "TURN_END",
      Phase.AuraRefresh => "AURA_REFRESH",
      _ => phase.ToString().ToUpperInvariant()
    };
  }
}
