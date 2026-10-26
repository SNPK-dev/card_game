using Game.Application.Commands;
using Game.Application.Execution;
using Game.Application.Legality;
using Game.Application.Replay;
using Game.Domain.Commands;
using Game.Domain.Events;
using Game.Domain.Hashing;
using Game.Domain.Mutation;
using Game.Domain.Randomness;
using Game.Domain.Replay;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Shared.Events;

namespace Game.Application.Simulation;

public sealed class BotSimulationRunner
{
  private readonly IStateMutator _mutator;
  private readonly IDerivedEventLog _eventLog;
  private readonly IStateHasher _stateHasher;

  public BotSimulationRunner(
    IStateMutator? mutator = null,
    IDerivedEventLog? eventLog = null,
    IStateHasher? stateHasher = null)
  {
    _mutator = mutator ?? new StateMutator();
    _eventLog = eventLog ?? new DerivedEventLog();
    _stateHasher = stateHasher ?? new StateHasher(new CanonicalJsonSerializer());
  }

  public BotSimulationResult Run(BotSimulationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.InitialState);
    ArgumentNullException.ThrowIfNull(request.CatalogCards);
    ArgumentNullException.ThrowIfNull(request.BotsByPlayerId);

    var options = request.Options ?? new BotSimulationOptions();
    if (options.OpeningHandSize < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(request), "Opening hand size must be non-negative.");
    }

    if (options.MaxCommands <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(request), "Maximum command count must be positive.");
    }

    var initialHash = _stateHasher.HashGameplayState(request.InitialState);
    var setupState = DrawOpeningHands(request.InitialState, request.SimulationId, options.OpeningHandSize);
    var setupHash = _stateHasher.HashGameplayState(setupState);
    var cardVersionsByVersionId = request.CatalogCards.ToDictionary(
      card => card.CardVersionId,
      StringComparer.Ordinal);
    var executor = new DeterministicCommandExecutor(
      _mutator,
      _eventLog,
      request.CatalogCards,
      stateHasher: _stateHasher);
    var state = setupState;
    var payloads = new List<DeterministicCommandPayload>();
    var derivedEvents = new List<CommittedEventRecord>();
    var botDecisionRolls = new List<RngRollRecord>();
    long nextEventSeq = 1;
    var botRngRoller = new DeterministicRngRoller(new BotRandomStream(options.BotSeed));
    var admission = new CommandAdmissionService();

    for (var commandSeq = 1; commandSeq <= options.MaxCommands; commandSeq++)
    {
      var playerId = state.ActivePlayerId;
      if (string.IsNullOrWhiteSpace(playerId))
      {
        break;
      }

      if (!request.BotsByPlayerId.TryGetValue(playerId, out var bot))
      {
        throw new InvalidOperationException($"Bot for active player '{playerId}' was not supplied.");
      }

      var decision = bot.Decide(new BotDecisionContext(
        state,
        playerId,
        cardVersionsByVersionId,
        commandSeq,
        botRngRoller,
        request.RulesetProfile,
        request.EngineCapabilities));
      if (decision.RngRoll is not null)
      {
        botDecisionRolls.Add(decision.RngRoll);
      }

      var preStateHash = _stateHasher.HashGameplayState(state);
      var envelope = ToEnvelope(request.SimulationId, commandSeq, decision.Command, preStateHash);
      var admitted = admission.Admit(
        envelope,
        new LegalActionQuery(
          state,
          playerId,
          cardVersionsByVersionId,
          request.RulesetProfile,
          request.EngineCapabilities));
      if (!admitted.IsAccepted || admitted.Payload is null)
      {
        throw new InvalidOperationException(
          $"Bot command admission rejected at command {commandSeq}: {string.Join("; ", admitted.Errors.Select(error => $"{error.Code}:{error.Message}"))}");
      }

      payloads.Add(admitted.Payload);

      var execution = executor.Execute(state, admitted.Payload.ToCommand(), nextEventSeq);
      state = execution.State;
      derivedEvents.AddRange(execution.Events);
      nextEventSeq += execution.Events.Count;

      if (state.TerminalState.IsTerminal)
      {
        break;
      }
    }

    var commandLog = new CanonicalCommandLog(payloads);
    var finalHash = _stateHasher.HashGameplayState(state);
    var replayVerificationService = new ReplayVerificationService(
      new CommandLogReplayer(_mutator, _eventLog, _stateHasher, request.CatalogCards),
      _stateHasher);
    var replayVerifications = Enumerable.Range(1, 2)
      .Select(_ => replayVerificationService.Verify(new ReplayVerificationRequest(
        setupState,
        commandLog,
        finalHash,
        setupHash)))
      .ToArray();
    var replayVerification = CombineReplayVerifications(replayVerifications, finalHash);

    return new BotSimulationResult(
      request.SimulationId,
      request.InitialState,
      setupState,
      state,
      commandLog,
      initialHash,
      setupHash,
      finalHash,
      state.TerminalState.IsTerminal
        ? state.TerminalState
        : new GameTerminalState(
          IsTerminal: false,
          "COMMAND_LIMIT_REACHED",
          null,
          null,
          $"Simulation stopped after {payloads.Count} commands without terminal game state."),
      derivedEvents,
      replayVerification,
      replayVerifications.Length,
      botDecisionRolls);
  }

  private static ReplayVerificationResult CombineReplayVerifications(
    IReadOnlyList<ReplayVerificationResult> verifications,
    string expectedFinalStateHash)
  {
    if (verifications.Count == 0)
    {
      return ReplayVerificationResult.Rejected(
        null,
        0,
        [
          new ReplayVerificationDiagnostic(
            "REPLAY_NOT_RUN",
            "simulation.replayVerification",
            string.Empty,
            "Simulation replay verification did not run.")
        ]);
    }

    var diagnostics = verifications
      .SelectMany(verification => verification.Diagnostics)
      .ToList();
    var allVerified = verifications.All(verification =>
      verification.IsVerified
      && string.Equals(verification.FinalStateHash, expectedFinalStateHash, StringComparison.Ordinal));
    var maxEventCount = verifications.Max(verification => verification.DerivedEventCount);
    var expectedRngRollRefs = verifications[0].RngRollRefs;
    if (verifications.Any(verification =>
      !verification.RngRollRefs.SequenceEqual(expectedRngRollRefs, StringComparer.Ordinal)))
    {
      diagnostics.Add(new ReplayVerificationDiagnostic(
        "RNG_ROLL_REFS_MISMATCH",
        "simulation.replayVerification.rngRollRefs",
        string.Join(",", verifications.Last().RngRollRefs),
        "Repeated simulation replay verification produced different RNG roll refs."));
      allVerified = false;
    }

    return allVerified
      ? ReplayVerificationResult.Verified(expectedFinalStateHash, maxEventCount, expectedRngRollRefs)
      : ReplayVerificationResult.Rejected(
        verifications.LastOrDefault(verification => !verification.IsVerified)?.FinalStateHash,
        maxEventCount,
        diagnostics,
        expectedRngRollRefs);
  }

  private IGameState DrawOpeningHands(
    IGameState initialState,
    string simulationId,
    int openingHandSize)
  {
    if (openingHandSize == 0)
    {
      return initialState;
    }

    var state = initialState;
    foreach (var player in state.Players.Values.OrderBy(player => player.PlayerId, StringComparer.Ordinal))
    {
      var cardsToDraw = player.Zones.Deck
        .OrderBy(card => card.CardInstanceId, StringComparer.Ordinal)
        .Take(openingHandSize)
        .ToArray();
      if (cardsToDraw.Length == 0)
      {
        continue;
      }

      var commandSeq = state.Revision + 1;
      var mutations = cardsToDraw
        .Select((card, index) => new StateMutation(
          $"mutation:{simulationId}:opening-draw:{player.PlayerId}:{card.CardInstanceId}",
          DrawCardMutationHandler.SupportedMutationType,
          $"simulation:{simulationId}:opening-hand",
          card.CardInstanceId,
          new SortedDictionary<string, int>(StringComparer.Ordinal),
          "COMMAND",
          commandSeq,
          null,
          $"opening-draw:{player.PlayerId}:{index + 1:0000}",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["cardInstanceId"] = card.CardInstanceId,
            ["playerId"] = player.PlayerId
          }))
        .ToArray();

      var batch = new ProposedMutationBatch(
        $"batch:{simulationId}:opening-draw:{player.PlayerId}",
        commandSeq,
        null,
        "COMMAND",
        $"simulation:{simulationId}:opening-hand",
        "BOT_SIMULATION_OPENING_HAND",
        mutations,
        _stateHasher.HashGameplayState(state),
        null,
        null);

      state = _mutator.Apply(state, batch).State;
    }

    return state;
  }

  private static GameCommandEnvelope ToEnvelope(
    string simulationId,
    int commandSeq,
    IGameCommand command,
    string preStateHash) =>
    command switch
    {
      ActivateLocationCommand activateLocation => new GameCommandEnvelope(
        $"command:{simulationId}:{commandSeq:0000}",
        DateTimeOffset.UnixEpoch,
        null,
        simulationId,
        "bot-simulation",
        null,
        $"bot-command:{simulationId}:{commandSeq:0000}",
        commandSeq,
        activateLocation.PlayerId,
        ActivateLocationCommand.CommandKind,
        refs: ActivateLocationRefs(activateLocation),
        expectedPreStateHash: preStateHash),
      PlayCardCommand playCard => new GameCommandEnvelope(
        $"command:{simulationId}:{commandSeq:0000}",
        DateTimeOffset.UnixEpoch,
        null,
        simulationId,
        "bot-simulation",
        null,
        $"bot-command:{simulationId}:{commandSeq:0000}",
        commandSeq,
        playCard.PlayerId,
        PlayCardCommand.CommandKind,
        PlayCardPayload(playCard),
        PlayCardRefs(playCard),
        preStateHash),
      AttackCommand attack => new GameCommandEnvelope(
        $"command:{simulationId}:{commandSeq:0000}",
        DateTimeOffset.UnixEpoch,
        null,
        simulationId,
        "bot-simulation",
        null,
        $"bot-command:{simulationId}:{commandSeq:0000}",
        commandSeq,
        attack.PlayerId,
        AttackCommand.CommandKind,
        refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["attackerEntityInstanceId"] = attack.AttackerEntityInstanceId,
          ["targetEntityInstanceId"] = attack.TargetEntityInstanceId,
          ["targetPlayerId"] = attack.TargetPlayerId
        },
        expectedPreStateHash: preStateHash),
      UseHeroPowerCommand useHeroPower => new GameCommandEnvelope(
        $"command:{simulationId}:{commandSeq:0000}",
        DateTimeOffset.UnixEpoch,
        null,
        simulationId,
        "bot-simulation",
        null,
        $"bot-command:{simulationId}:{commandSeq:0000}",
        commandSeq,
        useHeroPower.PlayerId,
        UseHeroPowerCommand.CommandKind,
        new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
          ["manaCost"] = useHeroPower.ManaCost
        },
        UseHeroPowerRefs(useHeroPower),
        preStateHash),
      ConcedeCommand concede => new GameCommandEnvelope(
        $"command:{simulationId}:{commandSeq:0000}",
        DateTimeOffset.UnixEpoch,
        null,
        simulationId,
        "bot-simulation",
        null,
        $"bot-command:{simulationId}:{commandSeq:0000}",
        commandSeq,
        concede.PlayerId,
        ConcedeCommand.CommandKind,
        expectedPreStateHash: preStateHash),
      EndTurnCommand endTurn => new GameCommandEnvelope(
        $"command:{simulationId}:{commandSeq:0000}",
        DateTimeOffset.UnixEpoch,
        null,
        simulationId,
        "bot-simulation",
        null,
        $"bot-command:{simulationId}:{commandSeq:0000}",
        commandSeq,
        endTurn.PlayerId,
        EndTurnCommand.CommandKind,
        endTurn.MaxHandSize.HasValue
          ? new SortedDictionary<string, int>(StringComparer.Ordinal)
          {
            ["maxHandSize"] = endTurn.MaxHandSize.Value
          }
          : null,
        refs: null,
        expectedPreStateHash: preStateHash),
      _ => new GameCommandEnvelope(
        $"command:{simulationId}:{commandSeq:0000}",
        DateTimeOffset.UnixEpoch,
        null,
        simulationId,
        "bot-simulation",
        null,
        $"bot-command:{simulationId}:{commandSeq:0000}",
        commandSeq,
        null,
        command.Kind,
        command.Parameters,
        expectedPreStateHash: preStateHash)
    };

  private static IReadOnlyDictionary<string, string> PlayCardRefs(PlayCardCommand playCard)
  {
    var refs = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
      ["cardInstanceId"] = playCard.CardInstanceId
    };
    if (!string.IsNullOrWhiteSpace(playCard.EntityInstanceId))
    {
      refs["entityInstanceId"] = playCard.EntityInstanceId;
    }

    if (!string.IsNullOrWhiteSpace(playCard.HeroPowerActivationTiming))
    {
      refs["heroPowerActivationTiming"] = playCard.HeroPowerActivationTiming;
    }
    if(!string.IsNullOrWhiteSpace(playCard.TargetPlayerId))refs["targetPlayerId"]=playCard.TargetPlayerId;
    if(!string.IsNullOrWhiteSpace(playCard.TargetEntityInstanceId))refs["targetEntityInstanceId"]=playCard.TargetEntityInstanceId;

    foreach (var keyword in (playCard.Keywords ?? []).Order(StringComparer.Ordinal).Select((value, index) => new { value, index }))
    {
      refs[$"entityKeyword:{keyword.index + 1:0000}"] = keyword.value;
    }

    return refs;
  }

  private static IReadOnlyDictionary<string, string> ActivateLocationRefs(ActivateLocationCommand activateLocation)
  {
    var refs = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
      ["locationEntityInstanceId"] = activateLocation.LocationEntityInstanceId,
      ["sourceCardInstanceId"] = activateLocation.SourceCardInstanceId
    };
    if (!string.IsNullOrWhiteSpace(activateLocation.TargetPlayerId))
    {
      refs["targetPlayerId"] = activateLocation.TargetPlayerId;
    }

    if (!string.IsNullOrWhiteSpace(activateLocation.TargetEntityInstanceId))
    {
      refs["targetEntityInstanceId"] = activateLocation.TargetEntityInstanceId;
    }

    return refs;
  }

  private static IReadOnlyDictionary<string, int> PlayCardPayload(PlayCardCommand playCard)
  {
    var payload = new SortedDictionary<string, int>(StringComparer.Ordinal)
    {
      ["attack"] = playCard.Attack,
      ["health"] = playCard.Health,
      ["manaCost"] = playCard.ManaCost
    };
    if (playCard.MaxBoardSize.HasValue)
    {
      payload["maxBoardSize"] = playCard.MaxBoardSize.Value;
    }

    if (playCard.CostReduction > 0)
    {
      payload["costReduction"] = playCard.CostReduction;
    }

    if (playCard.Durability > 0)
    {
      payload["durability"] = playCard.Durability;
    }

    if (playCard.ArmorGain > 0)
    {
      payload["armorGain"] = playCard.ArmorGain;
    }

    if (playCard.HeroPowerCost.HasValue)
    {
      payload["heroPowerCost"] = playCard.HeroPowerCost.Value;
    }

    return payload;
  }

  private static IReadOnlyDictionary<string, string> UseHeroPowerRefs(UseHeroPowerCommand command)
  {
    var refs = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
      ["sourceCardInstanceId"] = command.SourceCardInstanceId
    };
    if (!string.IsNullOrWhiteSpace(command.TargetPlayerId))
    {
      refs["targetPlayerId"] = command.TargetPlayerId;
    }

    if (!string.IsNullOrWhiteSpace(command.TargetEntityInstanceId))
    {
      refs["targetEntityInstanceId"] = command.TargetEntityInstanceId;
    }

    return refs;
  }

}
