using Game.Domain.Actions;
using Game.Domain.Commands;
using Game.Domain.Effects;
using Game.Domain.Events;
using Game.Domain.History;
using Game.Domain.Hashing;
using Game.Domain.Mutation;
using Game.Domain.Randomness;
using Game.Domain.Serialization;
using Game.Domain.State;
using Game.Domain.Targeting;
using Game.Domain.Triggers;
using Game.Shared.Catalog;
using Game.Shared.Dsl;
using Game.Shared.Events;
using Game.Shared.Primitives;
using System.Globalization;
using System.Net;

namespace Game.Application.Execution;

public sealed class DeterministicCommandExecutor
{
  private readonly IStateMutator _mutator;
  private readonly ICommittedEventFactory _eventFactory;
  private readonly ActionExecutorRegistry _actionExecutors;
  private readonly ICommandActionResolver _actionResolver;
  private readonly IStateHasher _stateHasher;
  private readonly IReadOnlyDictionary<string, CardVersion> _cardVersionsByVersionId;
  private readonly IEffectActionCompiler _effectActionCompiler;
  private readonly ITargetResolver _targetResolver;
  private readonly ITriggerResolver _triggerResolver;
  private IRngRoller _rngRoller;
  private readonly bool _usesCommandScopedRng;
  private HistoryStore _historyStore = new();
  private const int MaxDeathProcessingPasses = 8;

  public DeterministicCommandExecutor(
    IStateMutator mutator,
    IDerivedEventLog eventLog,
    ActionExecutorRegistry? actionExecutors,
    ICommandActionResolver? actionResolver,
    IStateHasher? stateHasher)
    : this(
      mutator,
      eventLog,
      catalogCards: null,
      actionExecutors,
      actionResolver,
      stateHasher)
  {
  }

  public DeterministicCommandExecutor(
    IStateMutator mutator,
    IDerivedEventLog eventLog,
    IReadOnlyList<CardVersion>? catalogCards = null,
    ActionExecutorRegistry? actionExecutors = null,
    ICommandActionResolver? actionResolver = null,
    IStateHasher? stateHasher = null,
    IEffectActionCompiler? effectActionCompiler = null,
    ITargetResolver? targetResolver = null,
    ITriggerResolver? triggerResolver = null,
    IRngRoller? rngRoller = null)
  {
    _mutator = mutator ?? throw new ArgumentNullException(nameof(mutator));
    ArgumentNullException.ThrowIfNull(eventLog);
    _eventFactory = new CommittedEventFactory();
    _actionExecutors = actionExecutors
      ?? new ActionExecutorRegistry(
      [
        new ActivateLocationActionExecutor(),
        new CommandCounterActionExecutor(),
        new PlayCardActionExecutor(),
        new AttackActionExecutor(),
        new ConcedeActionExecutor(),
        new EndTurnActionExecutor(),
        new UseHeroPowerActionExecutor(),
        new ResolveChoiceActionExecutor()
      ]);
    _actionResolver = actionResolver ?? new GameplayCommandActionResolver();
    _stateHasher = stateHasher ?? new StateHasher(new CanonicalJsonSerializer());
    _cardVersionsByVersionId = (catalogCards ?? [])
      .ToDictionary(card => card.CardVersionId, StringComparer.Ordinal);
    _effectActionCompiler = effectActionCompiler ?? new EffectActionCompiler();
    _targetResolver = targetResolver ?? new TargetResolver();
    _triggerResolver = triggerResolver ?? new TriggerResolver();
    _usesCommandScopedRng = rngRoller is null;
    _rngRoller = rngRoller ?? CreateCommandScopedRngRoller();
  }

  public CommandExecutionResult Execute(IGameState state, IGameCommand command, long firstEventSeq = 1)
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(command);
    if (firstEventSeq <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(firstEventSeq), "First event sequence must be positive.");
    }

    if (state.TerminalState.IsTerminal)
    {
      throw new InvalidOperationException("Cannot execute gameplay commands after the game is terminal.");
    }

    // MatchRuntimeService constructs an executor per accepted command. Replayers may
    // retain one executor so history queries span commands, therefore the default
    // gameplay stream must still restart at the same command boundary as live play.
    if (_usesCommandScopedRng)
    {
      _rngRoller = CreateCommandScopedRngRoller();
    }

    var commandSeq = state.Revision + 1;
    var context = new ActionExecutionContext(
      state,
      command,
      commandSeq,
      _stateHasher.HashGameplayState(state));
    var actionType = _actionResolver.ResolveActionType(command);
    var actionExecutor = _actionExecutors.GetRequiredExecutor(actionType);
    if (!actionExecutor.CanExecute(context))
    {
      throw new InvalidOperationException(
        $"Action executor '{actionType}' cannot execute command '{command.Kind}'.");
    }

    var events = new List<CommittedEventRecord>();
    var faultState = state;
    try
    {
      var batch = actionExecutor.Execute(context);
      var batchExecutor = new DeterministicMutationBatchExecutor(_mutator, _eventFactory);
      var application = batchExecutor.Execute(state, batch, firstEventSeq);
      faultState = application.State;
      events.AddRange(application.CommittedEvents);

      var resolvedChoiceEffectApplication = ApplyResolvedEffectTargetChoice(
        state,
        application.State,
        command,
        commandSeq,
        firstEventSeq + events.Count,
        events.LastOrDefault()?.EventSeq);
      faultState = resolvedChoiceEffectApplication.State;
      events.AddRange(resolvedChoiceEffectApplication.Events);

      var effectApplication = ApplyOnPlayEffects(
        state,
        resolvedChoiceEffectApplication.State,
        command,
        commandSeq,
        firstEventSeq + events.Count);
      faultState = effectApplication.State;
      events.AddRange(effectApplication.Events);

      var secretApplication = ApplySecretRevealEffects(
        effectApplication.State,
        events,
        commandSeq,
        firstEventSeq + events.Count);
      faultState = secretApplication.State;
      events.AddRange(secretApplication.Events);

      var heroPowerApplication = ApplyHeroPowerEffects(
        secretApplication.State,
        command,
        commandSeq,
        firstEventSeq + events.Count);
      faultState = heroPowerApplication.State;
      events.AddRange(heroPowerApplication.Events);

      var locationApplication = ApplyLocationActivationEffects(
        heroPowerApplication.State,
        command,
        commandSeq,
        firstEventSeq + events.Count);
      faultState = locationApplication.State;
      events.AddRange(locationApplication.Events);

      var cleanupApplication = ApplyDeathProcessingWindows(
        locationApplication.State,
        command,
        commandSeq,
        firstEventSeq + events.Count);
      faultState = cleanupApplication.State;
      events.AddRange(cleanupApplication.Events);

      var auraApplication = ApplyAuraRefresh(
        cleanupApplication.State,
        commandSeq,
        firstEventSeq + events.Count);
      faultState = auraApplication.State;
      events.AddRange(auraApplication.Events);

      var questApplication = ApplyQuestProgressAndRewards(
        auraApplication.State,
        events,
        commandSeq,
        firstEventSeq + events.Count);
      faultState = questApplication.State;
      events.AddRange(questApplication.Events);

      var terminalApplication = ApplyTerminalStateIfNeeded(
        questApplication.State,
        command,
        commandSeq,
        firstEventSeq + events.Count);
      faultState = terminalApplication.State;
      events.AddRange(terminalApplication.Events);

      _historyStore = _historyStore.AppendCommittedEvents(events);
      return new CommandExecutionResult(terminalApplication.State, events);
    }
    catch (InvalidOperationException ex)
    {
      return ApplyEngineFault(
        faultState,
        command,
        commandSeq,
        firstEventSeq + events.Count,
        events,
        ex);
    }
  }

  private static IRngRoller CreateCommandScopedRngRoller() =>
    new DeterministicRngRoller(new GameplayRandomStream(0x43434747504C4159UL));

  private CommandExecutionResult ApplyEngineFault(
    IGameState state,
    IGameCommand command,
    int commandSeq,
    long firstEventSeq,
    List<CommittedEventRecord> priorEvents,
    InvalidOperationException exception)
  {
    if (state.TerminalState.IsTerminal)
    {
      return new CommandExecutionResult(state, priorEvents);
    }

    var application = ApplyEngineFaultTerminalState(
      state,
      command,
      commandSeq,
      firstEventSeq,
      exception.Message);
    priorEvents.AddRange(application.Events);
    _historyStore = _historyStore.AppendCommittedEvents(priorEvents);
    return new CommandExecutionResult(application.State, priorEvents);
  }

  private EffectApplicationResult ApplyEngineFaultTerminalState(
    IGameState state,
    IGameCommand command,
    int commandSeq,
    long firstEventSeq,
    string message)
  {
    if (state.TerminalState.IsTerminal)
    {
      return new EffectApplicationResult(state, []);
    }

    var batch = new ProposedMutationBatch(
      $"batch:{commandSeq}:engine-fault",
      commandSeq,
      null,
      "COMMAND",
      $"command:{command.Kind}",
      "ENGINE_FAULT",
      [
        new StateMutation(
          $"mutation:{commandSeq}:engine-fault",
          SetTerminalStateMutationHandler.SupportedMutationType,
          $"command:{command.Kind}",
          "game",
          new SortedDictionary<string, int>(StringComparer.Ordinal),
          "COMMAND",
          commandSeq,
          null,
          "engine-fault:0001",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["failedCommandKind"] = command.Kind,
            ["faultCode"] = "ENGINE_FAULT",
            ["losingPlayerId"] = string.Empty,
            ["message"] = message,
            ["reason"] = "ENGINE_FAULT",
            ["winningPlayerId"] = string.Empty
          })
      ],
      _stateHasher.HashGameplayState(state),
      null,
      null);
    var application = new DeterministicMutationBatchExecutor(_mutator, _eventFactory)
      .Execute(state, batch, firstEventSeq);

    return new EffectApplicationResult(application.State, application.CommittedEvents);
  }

  private EffectApplicationResult ApplyTerminalStateIfNeeded(
    IGameState state,
    IGameCommand command,
    int commandSeq,
    long firstEventSeq)
  {
    if (state.TerminalState.IsTerminal)
    {
      return new EffectApplicationResult(state, []);
    }

    var terminalState = EvaluateTerminalState(state, command);
    if (!terminalState.IsTerminal)
    {
      return new EffectApplicationResult(state, []);
    }

    var batch = new ProposedMutationBatch(
      $"batch:{commandSeq}:set-terminal-state",
      commandSeq,
      null,
      "COMMAND",
      $"command:{command.Kind}",
      "SET_TERMINAL_STATE",
      [
        new StateMutation(
          $"mutation:{commandSeq}:set-terminal-state",
          SetTerminalStateMutationHandler.SupportedMutationType,
          $"command:{command.Kind}",
          "game",
          new SortedDictionary<string, int>(StringComparer.Ordinal),
          "COMMAND",
          commandSeq,
          null,
          "terminal-state:0001",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["reason"] = terminalState.Reason,
            ["winningPlayerId"] = terminalState.WinningPlayerId ?? string.Empty,
            ["losingPlayerId"] = terminalState.LosingPlayerId ?? string.Empty,
            ["message"] = terminalState.Message
          })
      ],
      _stateHasher.HashGameplayState(state),
      null,
      null);
    var application = new DeterministicMutationBatchExecutor(_mutator, _eventFactory)
      .Execute(state, batch, firstEventSeq);

    return new EffectApplicationResult(application.State, application.CommittedEvents);
  }

  private EffectApplicationResult ApplyDeathProcessingWindows(
    IGameState state,
    IGameCommand command,
    int commandSeq,
    long firstEventSeq)
  {
    var currentState = state;
    var events = new List<CommittedEventRecord>();
    var nextEventSeq = firstEventSeq;

    for (var pass = 1; pass <= MaxDeathProcessingPasses; pass++)
    {
      var deadEntities = CollectDeadEntities(currentState);
      if (deadEntities.Count == 0)
      {
        return new EffectApplicationResult(currentState, events);
      }

      var cleanupApplication = ApplyDeathCleanup(
        currentState,
        command,
        commandSeq,
        nextEventSeq,
        deadEntities);
      currentState = cleanupApplication.State;
      events.AddRange(cleanupApplication.Events);
      nextEventSeq += cleanupApplication.Events.Count;

      var diedEvents = CreateEntityDiedEvents(
        cleanupApplication.Events,
        deadEntities,
        currentState,
        nextEventSeq);
      events.AddRange(diedEvents);
      nextEventSeq += diedEvents.Count;

      var deathrattleApplication = ApplyDeathrattleEffects(
        currentState,
        deadEntities,
        diedEvents,
        commandSeq,
        nextEventSeq);
      currentState = deathrattleApplication.State;
      events.AddRange(deathrattleApplication.Events);
      nextEventSeq += deathrattleApplication.Events.Count;
    }

    var faultApplication = ApplyEngineFaultTerminalState(
      currentState,
      command,
      commandSeq,
      nextEventSeq,
      $"Death processing exceeded the maximum deterministic pass count of {MaxDeathProcessingPasses}.");
    events.AddRange(faultApplication.Events);
    return new EffectApplicationResult(faultApplication.State, events);
  }

  private EffectApplicationResult ApplyDeathCleanup(
    IGameState state,
    IGameCommand command,
    int commandSeq,
    long firstEventSeq,
    IReadOnlyList<DeadEntitySnapshot> deadEntities)
  {
    if (deadEntities.Count == 0)
    {
      return new EffectApplicationResult(state, []);
    }

    var mutations = deadEntities
      .Select((entity, index) => new StateMutation(
        $"mutation:{commandSeq}:death-cleanup:{entity.PlayerId}:{entity.EntityInstanceId}",
        DestroyEntityMutationHandler.SupportedMutationType,
        $"command:{command.Kind}",
        entity.EntityInstanceId,
        new SortedDictionary<string, int>(StringComparer.Ordinal),
        "DEATHRATTLE_PHASE",
        commandSeq,
        null,
        $"death-cleanup:{index + 1:D4}",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
          ["entityInstanceId"] = entity.EntityInstanceId,
          ["playerId"] = entity.PlayerId
        }))
      .ToArray();

    var batch = new ProposedMutationBatch(
      $"batch:{commandSeq}:death-cleanup",
      commandSeq,
      null,
      "DEATHRATTLE_PHASE",
      $"command:{command.Kind}",
      "DEATH_CLEANUP",
      mutations,
      _stateHasher.HashGameplayState(state),
      null,
      null);

    var application = new DeterministicMutationBatchExecutor(_mutator, _eventFactory)
      .Execute(state, batch, firstEventSeq);

    return new EffectApplicationResult(application.State, application.CommittedEvents);
  }

  private IReadOnlyList<DeadEntitySnapshot> CollectDeadEntities(IGameState state) =>
    state.Players.Values
      .SelectMany(player => player.Zones.Board
        .Where(entity => entity.Health - entity.Damage <= 0)
        .Select(entity =>
        {
          var sourceCard = player.Zones.SetAside.SingleOrDefault(card =>
            StringComparer.Ordinal.Equals(card.CardInstanceId, entity.SourceCardInstanceId));
          return new DeadEntitySnapshot(
            player.PlayerId,
            entity.EntityInstanceId,
            entity.SourceCardInstanceId,
            sourceCard?.CardVersion,
            entity.ControllerPlayerId);
        }))
      .OrderBy(item => item.PlayerId, StringComparer.Ordinal)
      .ThenBy(item => item.EntityInstanceId, StringComparer.Ordinal)
      .ToArray();

  private IReadOnlyList<CommittedEventRecord> CreateEntityDiedEvents(
    IReadOnlyList<CommittedEventRecord> destroyedEvents,
    IReadOnlyList<DeadEntitySnapshot> deadEntities,
    IGameState state,
    long firstEventSeq)
  {
    var stateHash = _stateHasher.HashGameplayState(state);
    var destroyedByEntity = destroyedEvents
      .Where(@event => @event.EventType is CommittedEventType.EntityDestroyed)
      .ToDictionary(@event => PayloadString(@event, "entityInstanceId") ?? string.Empty, StringComparer.Ordinal);
    var events = new List<CommittedEventRecord>();
    var nextEventSeq = firstEventSeq;

    foreach (var dead in deadEntities)
    {
      destroyedByEntity.TryGetValue(dead.EntityInstanceId, out var destroyedEvent);
      events.Add(new CommittedEventRecord(
        nextEventSeq++,
        destroyedEvent?.EventSeq,
        destroyedEvent?.CommandSeq,
        CommittedEventType.EntityDied,
        EventCategory.Committed,
        Phase.DeathrattlePhase,
        destroyedEvent?.SourceRefs ?? [new EventRef("SOURCE", $"entity:{dead.EntityInstanceId}", null, dead.PlayerId)],
        [new EventRef("ENTITY_INSTANCE", dead.EntityInstanceId, null, dead.PlayerId)],
        [
          StringPayload("entityInstanceId", dead.EntityInstanceId),
          StringPayload("playerId", dead.PlayerId),
          StringPayload("sourceCardInstanceId", dead.SourceCardInstanceId),
          StringPayload("cardVersion", dead.CardVersion ?? string.Empty),
          StringPayload("reason", "DEATH_CLEANUP")
        ],
        destroyedEvent?.MutationBatchRef,
        destroyedEvent?.MutationRefs ?? [],
        [],
        destroyedEvent?.PreStateHash ?? stateHash,
        destroyedEvent?.PostStateHash ?? stateHash,
        EventVisibility.Public));
    }

    return events;
  }

  private EffectApplicationResult ApplyDeathrattleEffects(
    IGameState state,
    IReadOnlyList<DeadEntitySnapshot> deadEntities,
    IReadOnlyList<CommittedEventRecord> diedEvents,
    int commandSeq,
    long firstEventSeq)
  {
    if (!_cardVersionsByVersionId.Any())
    {
      return new EffectApplicationResult(state, []);
    }

    var matches = new List<DeathrattleMatch>();
    foreach (var dead in deadEntities)
    {
      if (string.IsNullOrWhiteSpace(dead.CardVersion)
        || !_cardVersionsByVersionId.TryGetValue(dead.CardVersion, out var cardVersion))
      {
        continue;
      }

      var diedEvent = diedEvents.Single(@event =>
        StringComparer.Ordinal.Equals(PayloadString(@event, "entityInstanceId"), dead.EntityInstanceId));
      var registrations = cardVersion.Definition.Effects
        .Where(IsDeathrattleEffect)
        .Select(effect => new TriggerRegistration(
          $"deathrattle:{dead.EntityInstanceId}:{effect.EffectId}",
          effect.EffectId,
          effect.Trigger ?? new TriggerDefinition(EventType.EntityDied, EventFilter: null, "SOURCE_CONTROLLER", OncePerEvent: true),
          dead.ControllerPlayerId,
          dead.EntityInstanceId,
          [dead.PlayerId, dead.EntityInstanceId, effect.TimingPolicy.Priority.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), effect.EffectId]))
        .ToArray();
      if (registrations.Length == 0)
      {
        continue;
      }

      var resolution = _triggerResolver.Resolve(new TriggerResolutionRequest([diedEvent], registrations));
      if (!resolution.IsAccepted)
      {
        var diagnostic = resolution.Diagnostics.FirstOrDefault();
        throw new InvalidOperationException(
          diagnostic is null
            ? "Deathrattle trigger resolution failed."
            : $"Deathrattle trigger resolution failed: {diagnostic.Code}.");
      }

      foreach (var match in resolution.Matches)
      {
        var effect = cardVersion.Definition.Effects.Single(candidate =>
          StringComparer.Ordinal.Equals(candidate.EffectId, match.EffectId));
        matches.Add(new DeathrattleMatch(dead, diedEvent, match, effect));
      }
    }

    if (matches.Count == 0)
    {
      return new EffectApplicationResult(state, []);
    }

    var currentState = state;
    var events = new List<CommittedEventRecord>();
    var nextEventSeq = firstEventSeq;
    var batchExecutor = new DeterministicMutationBatchExecutor(_mutator, _eventFactory);

    foreach (var item in matches
      .OrderBy(match => match.TriggerMatch.SourceEventSeq)
      .ThenBy(match => match.TriggerMatch.CanonicalOrderingKeys, CanonicalOrderingKeyComparer.Instance)
      .ThenBy(match => match.TriggerMatch.TriggerId, StringComparer.Ordinal))
    {
      var triggerEvent = CreateDeathrattleTriggeredEvent(item, currentState, nextEventSeq++);
      events.Add(triggerEvent);

      var targetResolution = ResolveDeathrattleTargetsOrFizzle(
        currentState,
        item.Effect,
        item.DeadEntity,
        commandSeq);
      if (targetResolution is null)
      {
        continue;
      }

      if (targetResolution.PendingChoice is not null)
      {
        var choiceBatch = CreateEffectTargetChoiceBatch(
          currentState,
          targetResolution.PendingChoice,
          commandSeq,
          triggerEvent.EventSeq,
          $"deathrattle-choice:{item.DeadEntity.EntityInstanceId}:{item.Effect.EffectId}",
          "DEATHRATTLE_PHASE");
        var choiceApplication = batchExecutor.Execute(currentState, choiceBatch, nextEventSeq);
        currentState = choiceApplication.State;
        events.AddRange(choiceApplication.CommittedEvents);
        nextEventSeq += choiceApplication.CommittedEvents.Count;
        continue;
      }

      foreach (var rngRoll in targetResolution.RngRolls)
      {
        events.Add(CreateRngRolledEvent(
          rngRoll,
          currentState,
          nextEventSeq++,
          targetResolution.Targets,
          Phase.DeathrattlePhase));
      }

      var compilation = _effectActionCompiler.Compile(new EffectActionCompilationRequest(
        currentState,
        item.Effect,
        item.DeadEntity.ControllerPlayerId,
        item.DeadEntity.ControllerPlayerId,
        item.DeadEntity.SourceCardInstanceId,
        commandSeq,
        _stateHasher.HashGameplayState(currentState),
        targetResolution.Targets,
        new ResolvedTargetRef(
          ResolvedTargetRef.EntityKind,
          item.DeadEntity.PlayerId,
          item.DeadEntity.EntityInstanceId),
        RngRoller: _rngRoller,
        HistoryStore: _historyStore));
      if (!compilation.IsAccepted)
      {
        if (ShouldFizzleUnsupportedTarget(item.Effect, compilation))
        {
          continue;
        }
        var diagnostic = compilation.Diagnostics.FirstOrDefault();
        throw new InvalidOperationException(
          diagnostic is null
            ? $"Deathrattle effect '{item.Effect.EffectId}' was rejected."
            : $"Deathrattle effect '{item.Effect.EffectId}' was rejected: {diagnostic.Code}.");
      }

      foreach (var rngRoll in compilation.RngRolls)
      {
        events.Add(CreateRngRolledEvent(
          rngRoll.Roll,
          currentState,
          nextEventSeq++,
          rngRoll.SelectedTargets,
          Phase.DeathrattlePhase));
      }

      var batch = compilation.Batch! with { ParentEventSeq = (int)triggerEvent.EventSeq };
      var application = batchExecutor.Execute(currentState, batch, nextEventSeq);
      currentState = application.State;
      events.AddRange(application.CommittedEvents);
      nextEventSeq += application.CommittedEvents.Count;
    }

    return new EffectApplicationResult(currentState, events);
  }

  private EffectApplicationResult ApplyQuestProgressAndRewards(
    IGameState state,
    IReadOnlyList<CommittedEventRecord> observedEvents,
    int commandSeq,
    long firstEventSeq)
  {
    if (observedEvents.Count == 0)
    {
      return new EffectApplicationResult(state, []);
    }

    var matches = CollectQuestProgressMatches(state, observedEvents, commandSeq);
    if (matches.Count == 0)
    {
      return new EffectApplicationResult(state, []);
    }

    var batch = new ProposedMutationBatch(
      $"batch:{commandSeq}:quest-progress",
      commandSeq,
      null,
      "QUEST_PROGRESS",
      "quest-system",
      "QUEST_PROGRESS",
      matches.Select(match => match.Mutation).ToArray(),
      _stateHasher.HashGameplayState(state),
      null,
      null);
    var batchExecutor = new DeterministicMutationBatchExecutor(_mutator, _eventFactory);
    var application = batchExecutor.Execute(state, batch, firstEventSeq);
    var currentState = application.State;
    var events = new List<CommittedEventRecord>(application.CommittedEvents);
    var nextEventSeq = firstEventSeq + application.CommittedEvents.Count;

    foreach (var match in matches
      .Where(match => match.IsCompleted)
      .OrderBy(match => match.SourceEvent.EventSeq)
      .ThenBy(match => match.PlayerId, StringComparer.Ordinal)
      .ThenBy(match => match.Quest.QuestId, StringComparer.Ordinal))
    {
      var progressEvent = events.SingleOrDefault(@event =>
        @event.EventType == CommittedEventType.QuestProgressChanged
        && StringComparer.Ordinal.Equals(PayloadString(@event, "questId"), match.Quest.QuestId)
        && StringComparer.Ordinal.Equals(PayloadString(@event, "playerId"), match.PlayerId)
        && StringComparer.Ordinal.Equals(PayloadString(@event, "sourceEventSeq"), match.SourceEvent.EventSeq.ToString(CultureInfo.InvariantCulture)));
      if (progressEvent is null)
      {
        continue;
      }

      var rewardApplication = ApplyQuestRewardEffect(
        currentState,
        match,
        progressEvent,
        commandSeq,
        nextEventSeq);
      currentState = rewardApplication.State;
      events.AddRange(rewardApplication.Events);
      nextEventSeq += rewardApplication.Events.Count;
    }

    return new EffectApplicationResult(currentState, events);
  }

  private IReadOnlyList<QuestProgressMatch> CollectQuestProgressMatches(
    IGameState state,
    IReadOnlyList<CommittedEventRecord> observedEvents,
    int commandSeq)
  {
    var quests = state.Players.Values
      .OrderBy(player => player.PlayerId, StringComparer.Ordinal)
      .SelectMany(player => (player.Quests ?? [])
        .Where(quest => !quest.IsCompleted && quest.RequiredProgress > 0)
        .OrderBy(quest => quest.QuestId, StringComparer.Ordinal)
        .Select(quest => (PlayerId: player.PlayerId, Quest: quest)))
      .ToArray();
    if (quests.Length == 0)
    {
      return [];
    }

    var progressByQuest = quests.ToDictionary(
      item => QuestKey(item.PlayerId, item.Quest.QuestId),
      item => item.Quest.Progress,
      StringComparer.Ordinal);
    var matches = new List<QuestProgressMatch>();

    foreach (var sourceEvent in observedEvents
      .Where(@event => @event.EventType != CommittedEventType.QuestProgressChanged)
      .OrderBy(@event => @event.EventSeq))
    {
      var eventType = ToEventTypeId(sourceEvent.EventType);
      var eventPlayerId = PayloadString(sourceEvent, "playerId");
      foreach (var item in quests)
      {
        if (!StringComparer.Ordinal.Equals(item.Quest.ProgressEventType, eventType)
          || !StringComparer.Ordinal.Equals(item.PlayerId, eventPlayerId))
        {
          continue;
        }

        var key = QuestKey(item.PlayerId, item.Quest.QuestId);
        var currentProgress = progressByQuest[key];
        if (currentProgress >= item.Quest.RequiredProgress)
        {
          continue;
        }

        var nextProgress = Math.Min(item.Quest.RequiredProgress, currentProgress + 1);
        progressByQuest[key] = nextProgress;
        var isCompleted = nextProgress >= item.Quest.RequiredProgress;
        matches.Add(new QuestProgressMatch(
          item.PlayerId,
          item.Quest,
          sourceEvent,
          isCompleted,
          new StateMutation(
            $"mutation:{commandSeq}:quest-progress:{item.PlayerId}:{item.Quest.QuestId}:{sourceEvent.EventSeq}",
            UpdateQuestProgressMutationHandler.SupportedMutationType,
            $"event:{sourceEvent.EventSeq}",
            item.PlayerId,
            new SortedDictionary<string, int>(StringComparer.Ordinal)
            {
              ["amount"] = 1
            },
            "QUEST_PROGRESS",
            commandSeq,
            (int)sourceEvent.EventSeq,
            $"quest-progress:{sourceEvent.EventSeq:D10}:{item.PlayerId}:{item.Quest.QuestId}",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
              ["isCompleted"] = isCompleted.ToString(CultureInfo.InvariantCulture),
              ["nextProgress"] = nextProgress.ToString(CultureInfo.InvariantCulture),
              ["playerId"] = item.PlayerId,
              ["previousProgress"] = currentProgress.ToString(CultureInfo.InvariantCulture),
              ["questId"] = item.Quest.QuestId,
              ["requiredProgress"] = item.Quest.RequiredProgress.ToString(CultureInfo.InvariantCulture),
              ["sourceCardInstanceId"] = item.Quest.SourceCardInstanceId,
              ["sourceEventSeq"] = sourceEvent.EventSeq.ToString(CultureInfo.InvariantCulture),
              ["sourceEventType"] = eventType
            })));
      }
    }

    return matches;
  }

  private EffectApplicationResult ApplyQuestRewardEffect(
    IGameState state,
    QuestProgressMatch match,
    CommittedEventRecord progressEvent,
    int commandSeq,
    long firstEventSeq)
  {
    if (string.IsNullOrWhiteSpace(match.Quest.RewardEffectId)
      || !_cardVersionsByVersionId.Any())
    {
      return new EffectApplicationResult(state, []);
    }

    var cardVersionId = ResolveQuestSourceCardVersion(state, match.PlayerId, match.Quest);
    if (cardVersionId is null
      || !_cardVersionsByVersionId.TryGetValue(cardVersionId, out var cardVersion))
    {
      return new EffectApplicationResult(state, []);
    }

    var effect = cardVersion.Definition.Effects.SingleOrDefault(candidate =>
      StringComparer.Ordinal.Equals(candidate.EffectId, match.Quest.RewardEffectId));
    if (effect is null)
    {
      throw new InvalidOperationException(
        $"Quest reward effect '{match.Quest.RewardEffectId}' is missing from card version '{cardVersionId}'.");
    }

    var targetResolution = ResolveQuestTargetsOrFizzle(
      state,
      effect,
      match,
      commandSeq);
    if (targetResolution is null)
    {
      return new EffectApplicationResult(state, []);
    }

    var currentState = state;
    var events = new List<CommittedEventRecord>();
    var nextEventSeq = firstEventSeq;
    if (targetResolution.PendingChoice is not null)
    {
      var choiceBatch = CreateEffectTargetChoiceBatch(
        currentState,
        targetResolution.PendingChoice,
        commandSeq,
        progressEvent.EventSeq,
        $"quest-choice:{match.Quest.QuestId}:{effect.EffectId}",
        "AFTER_PLAY");
      var choiceApplication = new DeterministicMutationBatchExecutor(_mutator, _eventFactory)
        .Execute(currentState, choiceBatch, nextEventSeq);
      currentState = choiceApplication.State;
      events.AddRange(choiceApplication.CommittedEvents);
      return new EffectApplicationResult(currentState, events);
    }

    foreach (var rngRoll in targetResolution.RngRolls)
    {
      events.Add(CreateRngRolledEvent(
        rngRoll,
        currentState,
        nextEventSeq++,
        targetResolution.Targets,
        Phase.AfterPlay));
    }

    var compilation = _effectActionCompiler.Compile(new EffectActionCompilationRequest(
      currentState,
      effect,
      match.PlayerId,
      match.PlayerId,
      match.Quest.SourceCardInstanceId,
      commandSeq,
      _stateHasher.HashGameplayState(currentState),
      targetResolution.Targets,
      new ResolvedTargetRef(
        ResolvedTargetRef.CardKind,
        match.PlayerId,
        match.Quest.SourceCardInstanceId),
      RngRoller: _rngRoller,
      HistoryStore: _historyStore));
    if (!compilation.IsAccepted)
    {
      var diagnostic = compilation.Diagnostics.FirstOrDefault();
      throw new InvalidOperationException(
        diagnostic is null
          ? $"Quest reward effect '{effect.EffectId}' was rejected."
          : $"Quest reward effect '{effect.EffectId}' was rejected: {diagnostic.Code}.");
    }

    foreach (var rngRoll in compilation.RngRolls)
    {
      events.Add(CreateRngRolledEvent(
        rngRoll.Roll,
        currentState,
        nextEventSeq++,
        rngRoll.SelectedTargets,
        Phase.AfterPlay));
    }

    var batch = compilation.Batch! with { ParentEventSeq = (int)progressEvent.EventSeq };
    var application = new DeterministicMutationBatchExecutor(_mutator, _eventFactory)
      .Execute(currentState, batch, nextEventSeq);
    currentState = application.State;
    events.AddRange(application.CommittedEvents);

    return new EffectApplicationResult(currentState, events);
  }

  private static GameTerminalState EvaluateTerminalState(
    IGameState state,
    IGameCommand command)
  {
    var defeatedPlayers = state.Players.Values
      .Where(player => player.Hero is not null
        && player.Hero.Health - player.Hero.Damage <= 0)
      .Select(player => player.PlayerId)
      .Order(StringComparer.Ordinal)
      .ToArray();
    if (defeatedPlayers.Length > 1)
    {
      return new GameTerminalState(
        true,
        "DRAW",
        null,
        null,
        "Multiple heroes were defeated simultaneously.");
    }

    if (defeatedPlayers.Length == 1)
    {
      var losingPlayerId = defeatedPlayers[0];
      var winningPlayerId = state.Players.Keys
        .Where(playerId => !StringComparer.Ordinal.Equals(playerId, losingPlayerId))
        .Order(StringComparer.Ordinal)
        .FirstOrDefault();

      return new GameTerminalState(
        true,
        "HERO_DEFEATED",
        winningPlayerId,
        losingPlayerId,
        $"Hero for player '{losingPlayerId}' was defeated.");
    }

    if (command is EndTurnCommand && AllPlayersOutOfResourcesAndBoard(state))
    {
      return new GameTerminalState(
        true,
        "EMPTY_RESOURCES_DRAW",
        null,
        null,
        "All players have empty decks and hands after turn end.");
    }

    return GameTerminalState.None;
  }

  private EffectApplicationResult ApplyOnPlayEffects(
    IGameState preCommandState,
    IGameState postPlayState,
    IGameCommand command,
    int commandSeq,
    long firstEventSeq)
  {
    if (command is not PlayCardCommand playCard
      || !_cardVersionsByVersionId.Any()
      || !preCommandState.Players.TryGetValue(playCard.PlayerId, out var player))
    {
      return new EffectApplicationResult(postPlayState, []);
    }

    var playedCard = player.Zones.Hand.SingleOrDefault(card =>
      StringComparer.Ordinal.Equals(card.CardInstanceId, playCard.CardInstanceId));
    if (playedCard is null
      || !_cardVersionsByVersionId.TryGetValue(playedCard.CardVersion, out var cardVersion))
    {
      return new EffectApplicationResult(postPlayState, []);
    }

    var state = postPlayState;
    var events = new List<CommittedEventRecord>();
    var nextEventSeq = firstEventSeq;
    var batchExecutor = new DeterministicMutationBatchExecutor(_mutator, _eventFactory);
    foreach (var effect in cardVersion.Definition.Effects
      .Where(effect =>
        effect.SourceZones.Contains(SourceZone.Hand)
        && (effect.TimingPolicy.Phase is Phase.OnPlay || IsSecretEffect(effect))))
    {
      if (IsSecretEffect(effect))
      {
        var setSecretApplication = ApplySetSecretEffect(
          state,
          effect,
          playCard,
          playedCard.CardVersion,
          nextEventSeq);
        state = setSecretApplication.State;
        events.AddRange(setSecretApplication.Events);
        nextEventSeq += setSecretApplication.Events.Count;
        continue;
      }

      var targetResolution = ResolveTargetsOrFizzle(state, effect, playCard, commandSeq);
      if (targetResolution is null)
      {
        continue;
      }

      foreach (var rngRoll in targetResolution.RngRolls)
      {
        events.Add(CreateRngRolledEvent(
          rngRoll,
          state,
          nextEventSeq++,
          targetResolution.Targets));
      }

      var compilation = _effectActionCompiler.Compile(new EffectActionCompilationRequest(
        state,
        effect,
        playCard.PlayerId,
        playCard.PlayerId,
        playCard.CardInstanceId,
        commandSeq,
        _stateHasher.HashGameplayState(state),
        targetResolution.Targets,
        PlayCardSource(playCard),
        RngRoller: _rngRoller,
        MaxBoardSize: playCard.MaxBoardSize,
        HistoryStore: _historyStore));

      if (!compilation.IsAccepted)
      {
        if (ShouldFizzleUnsupportedTarget(effect, compilation))
        {
          continue;
        }
        var diagnostic = compilation.Diagnostics.FirstOrDefault();
        throw new InvalidOperationException(
          diagnostic is null
            ? $"Generated effect '{effect.EffectId}' was rejected."
            : $"Generated effect '{effect.EffectId}' was rejected: {diagnostic.Code}.");
      }

      foreach (var rngRoll in compilation.RngRolls)
      {
        events.Add(CreateRngRolledEvent(
          rngRoll.Roll,
          state,
          nextEventSeq++,
          rngRoll.SelectedTargets));
      }

      var effectApplication = batchExecutor.Execute(state, compilation.Batch!, nextEventSeq);
      state = effectApplication.State;
      events.AddRange(effectApplication.CommittedEvents);
      nextEventSeq += effectApplication.CommittedEvents.Count;
    }

    return new EffectApplicationResult(state, events);
  }

  private static bool ShouldFizzleUnsupportedTarget(
    EffectDefinition effect,
    EffectActionCompilationResult compilation) =>
    (string.Equals(effect.FizzlePolicy.Kind, "FIZZLE", StringComparison.OrdinalIgnoreCase)
      || string.Equals(effect.FizzlePolicy.Kind, "FIZZLE_WHOLE_EFFECT", StringComparison.OrdinalIgnoreCase))
    && compilation.Diagnostics.Count > 0
    && compilation.Diagnostics.All(item => item.Code == EffectActionCompilationDiagnosticCode.UnsupportedTarget);

  private EffectApplicationResult ApplySetSecretEffect(
    IGameState state,
    Game.Shared.Dsl.EffectDefinition effect,
    PlayCardCommand playCard,
    string sourceCardVersion,
    long firstEventSeq)
  {
    var revealEventType = effect.Trigger is null
      ? "MINION_SUMMONED"
      : ToEventTypeId(ToCommittedEventType(effect.Trigger.EventType));
    var secretId = $"secret:{playCard.PlayerId}:{playCard.CardInstanceId}:{effect.EffectId}";
    var batch = new ProposedMutationBatch(
      $"batch:{playCard.Kind}:{playCard.CardInstanceId}:set-secret:{effect.EffectId}",
      state.Revision + 1,
      null,
      "SECRET_WINDOW",
      $"effect:{effect.EffectId}:card:{playCard.CardInstanceId}",
      "SECRET_SET",
      [
        new StateMutation(
          $"mutation:{state.Revision + 1}:secret:{playCard.CardInstanceId}:{effect.EffectId}:set",
          SetSecretMutationHandler.SupportedMutationType,
          $"effect:{effect.EffectId}:card:{playCard.CardInstanceId}",
          playCard.CardInstanceId,
          new SortedDictionary<string, int>(StringComparer.Ordinal),
          "SECRET_WINDOW",
          state.Revision + 1,
          null,
          $"secret:{playCard.CardInstanceId}:{effect.EffectId}:0001",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["effectId"] = effect.EffectId,
            ["playerId"] = playCard.PlayerId,
            ["revealEventType"] = revealEventType,
            ["secretId"] = secretId,
            ["sourceCardInstanceId"] = playCard.CardInstanceId,
            ["sourceCardVersion"] = sourceCardVersion
          })
      ],
      _stateHasher.HashGameplayState(state),
      null,
      null);

    var application = new DeterministicMutationBatchExecutor(_mutator, _eventFactory)
      .Execute(state, batch, firstEventSeq);
    return new EffectApplicationResult(application.State, application.CommittedEvents);
  }

  private EffectApplicationResult ApplyResolvedEffectTargetChoice(
    IGameState preCommandState,
    IGameState postResolveState,
    IGameCommand command,
    int commandSeq,
    long firstEventSeq,
    long? parentEventSeq)
  {
    if (command is not ResolveChoiceCommand resolveChoice
      || !_cardVersionsByVersionId.Any()
      || !preCommandState.PendingChoices.TryGetValue(resolveChoice.ChoiceId, out var choice)
      || !StringComparer.Ordinal.Equals(choice.ChoiceType, "EFFECT_TARGET"))
    {
      return new EffectApplicationResult(postResolveState, []);
    }

    var selectedOptions = resolveChoice.SelectedOptionIds
      .Select(optionId => choice.Options.SingleOrDefault(option =>
        StringComparer.Ordinal.Equals(option.OptionId, optionId)))
      .ToArray();
    if (selectedOptions.Length == 0
      || selectedOptions.Any(option => option is null)
      || !TryDecodeEffectTargetChoiceSource(choice.SourceRef, out var source)
      || !_cardVersionsByVersionId.TryGetValue(source.SourceCardVersion, out var cardVersion))
    {
      throw new InvalidOperationException(
        $"Effect target choice '{choice.ChoiceId}' cannot be resolved.");
    }

    var selectedTargets = new List<ResolvedTargetRef>();
    foreach (var selectedOption in selectedOptions)
    {
      if (!TryDecodeTargetOption(selectedOption!.ItemId, out var selectedTarget))
      {
        throw new InvalidOperationException(
          $"Effect target choice '{choice.ChoiceId}' cannot decode selected target '{selectedOption.OptionId}'.");
      }

      selectedTargets.Add(selectedTarget);
    }

    var effect = cardVersion.Definition.Effects.SingleOrDefault(candidate =>
      StringComparer.Ordinal.Equals(candidate.EffectId, source.EffectId));
    if (effect is null)
    {
      throw new InvalidOperationException(
        $"Effect target choice '{choice.ChoiceId}' references missing effect '{source.EffectId}'.");
    }

    if (effect.TargetRequirement is not null)
    {
      var validation = _targetResolver.Resolve(postResolveState, new TargetResolutionRequest(
        effect.TargetRequirement,
        source.ControllerPlayerId,
        source.ControllerPlayerId,
        ChosenTargets: selectedTargets,
        EventSource: new ResolvedTargetRef(
          source.SourceTargetKind,
          source.SourceTargetPlayerId,
          source.SourceTargetId),
        CommandSeq: commandSeq,
        RngRollScope: $"effect-choice:{choice.ChoiceId}:target"));
      if (!validation.IsAccepted)
      {
        var error = validation.Errors.FirstOrDefault();
        throw new InvalidOperationException(
          error is null
            ? $"Effect target choice '{choice.ChoiceId}' target validation failed."
            : $"Effect target choice '{choice.ChoiceId}' target validation failed: {error.Code}.");
      }
    }

    var compilation = _effectActionCompiler.Compile(new EffectActionCompilationRequest(
      postResolveState,
      effect,
      source.ControllerPlayerId,
      source.ControllerPlayerId,
      source.SourceCardInstanceId,
      commandSeq,
      _stateHasher.HashGameplayState(postResolveState),
      selectedTargets,
      new ResolvedTargetRef(
        source.SourceTargetKind,
        source.SourceTargetPlayerId,
        source.SourceTargetId),
      RngRoller: _rngRoller,
      HistoryStore: _historyStore));
    if (!compilation.IsAccepted)
    {
      var diagnostic = compilation.Diagnostics.FirstOrDefault();
      throw new InvalidOperationException(
        diagnostic is null
          ? $"Effect target choice '{choice.ChoiceId}' effect '{effect.EffectId}' was rejected."
          : $"Effect target choice '{choice.ChoiceId}' effect '{effect.EffectId}' was rejected: {diagnostic.Code}.");
    }

    var events = new List<CommittedEventRecord>();
    var nextEventSeq = firstEventSeq;
    var effectPhase = ToEffectTargetPhase(source.SourceKind);
    foreach (var rngRoll in compilation.RngRolls)
    {
      events.Add(CreateRngRolledEvent(
        rngRoll.Roll,
        postResolveState,
        nextEventSeq++,
        rngRoll.SelectedTargets,
        effectPhase));
    }

    var batch = compilation.Batch! with { ParentEventSeq = parentEventSeq is null ? null : (int)parentEventSeq.Value };
    var application = new DeterministicMutationBatchExecutor(_mutator, _eventFactory)
      .Execute(postResolveState, batch, nextEventSeq);
    events.AddRange(application.CommittedEvents);
    return new EffectApplicationResult(application.State, events);
  }

  private ProposedMutationBatch CreateEffectTargetChoiceBatch(
    IGameState state,
    PendingChoice choice,
    int commandSeq,
    long parentEventSeq,
    string orderingPrefix,
    string phase)
  {
    var metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
      ["choiceId"] = choice.ChoiceId,
      ["choiceType"] = choice.ChoiceType,
      ["playerId"] = choice.PlayerId,
      ["resultZone"] = choice.ResultZone,
      ["visibilityPolicy"] = choice.VisibilityPolicy
    };
    foreach (var option in choice.Options.Select((value, index) => new { value, index }))
    {
      var prefix = $"option:{option.index + 1:0000}:";
      metadata[prefix + "id"] = option.value.OptionId;
      metadata[prefix + "itemId"] = option.value.ItemId;
      metadata[prefix + "itemKind"] = option.value.ItemKind;
    }

    return new ProposedMutationBatch(
      $"batch:{commandSeq}:effect-target-choice:{choice.ChoiceId}",
      commandSeq,
      (int)parentEventSeq,
      phase,
      choice.SourceRef,
      "EFFECT_TARGET_CHOICE",
      [
        new StateMutation(
          $"mutation:{commandSeq}:effect-target-choice:{choice.ChoiceId}",
          CreateChoiceMutationHandler.SupportedMutationType,
          choice.SourceRef,
          choice.PlayerId,
          new SortedDictionary<string, int>(StringComparer.Ordinal)
          {
            ["optionCount"] = choice.Options.Count
          },
          phase,
          commandSeq,
          (int)parentEventSeq,
          $"{orderingPrefix}:0001",
          metadata)
      ],
      _stateHasher.HashGameplayState(state),
      null,
      null);
  }

  private EffectApplicationResult ApplySecretRevealEffects(
    IGameState state,
    IReadOnlyList<CommittedEventRecord> observedEvents,
    int commandSeq,
    long firstEventSeq)
  {
    if (observedEvents.Count == 0 || !_cardVersionsByVersionId.Any())
    {
      return new EffectApplicationResult(state, []);
    }

    var activeSecrets = state.Players.Values
      .OrderBy(player => player.PlayerId, StringComparer.Ordinal)
      .SelectMany(player => (player.Secrets ?? [])
        .Where(secret => !secret.IsRevealed)
        .OrderBy(secret => secret.SecretId, StringComparer.Ordinal)
        .Select(secret => (PlayerId: player.PlayerId, Secret: secret)))
      .ToArray();
    if (activeSecrets.Length == 0)
    {
      return new EffectApplicationResult(state, []);
    }

    var events = new List<CommittedEventRecord>();
    var currentState = state;
    var nextEventSeq = firstEventSeq;
    var batchExecutor = new DeterministicMutationBatchExecutor(_mutator, _eventFactory);

    foreach (var item in activeSecrets)
    {
      if (!_cardVersionsByVersionId.TryGetValue(item.Secret.SourceCardVersion, out var cardVersion))
      {
        continue;
      }

      var effect = cardVersion.Definition.Effects.SingleOrDefault(candidate =>
        StringComparer.Ordinal.Equals(candidate.EffectId, item.Secret.EffectId));
      if (effect is null)
      {
        continue;
      }

      var registrations = new[]
      {
        new TriggerRegistration(
          $"secret:{item.Secret.SecretId}:reveal",
          effect.EffectId,
          effect.Trigger ?? new TriggerDefinition(EventType.CardSummoned, EventFilter: null, "OPPONENT", OncePerEvent: true),
          item.PlayerId,
          item.Secret.SourceCardInstanceId,
          [item.PlayerId, item.Secret.SecretId, effect.TimingPolicy.Priority.ToString("D4", CultureInfo.InvariantCulture), effect.EffectId])
      };
      var resolution = _triggerResolver.Resolve(new TriggerResolutionRequest(observedEvents, registrations));
      if (!resolution.IsAccepted || resolution.Matches.Count == 0)
      {
        continue;
      }

      var match = resolution.Matches[0];
      var revealBatch = new ProposedMutationBatch(
        $"batch:{commandSeq}:secret:{item.Secret.SecretId}:reveal",
        commandSeq,
        (int)match.SourceEventSeq,
        "SECRET_WINDOW",
        $"secret:{item.Secret.SecretId}",
        "SECRET_REVEAL",
        [
          new StateMutation(
            $"mutation:{commandSeq}:secret:{item.Secret.SecretId}:reveal",
            RevealSecretMutationHandler.SupportedMutationType,
            $"secret:{item.Secret.SecretId}",
            item.Secret.SourceCardInstanceId,
            new SortedDictionary<string, int>(StringComparer.Ordinal),
            "SECRET_WINDOW",
            commandSeq,
            (int)match.SourceEventSeq,
            $"secret:{match.SourceEventSeq:D10}:{item.Secret.SecretId}:0001",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
              ["effectId"] = item.Secret.EffectId,
              ["playerId"] = item.PlayerId,
              ["secretId"] = item.Secret.SecretId,
              ["sourceCardInstanceId"] = item.Secret.SourceCardInstanceId,
              ["sourceEventSeq"] = match.SourceEventSeq.ToString(CultureInfo.InvariantCulture)
            })
        ],
        _stateHasher.HashGameplayState(currentState),
        null,
        null);
      var revealApplication = batchExecutor.Execute(currentState, revealBatch, nextEventSeq);
      currentState = revealApplication.State;
      events.AddRange(revealApplication.CommittedEvents);
      nextEventSeq += revealApplication.CommittedEvents.Count;

      var targetResolution = ResolveSecretTargetsOrFizzle(currentState, effect, item.Secret, commandSeq);
      if (targetResolution is null)
      {
        continue;
      }

      var compilation = _effectActionCompiler.Compile(new EffectActionCompilationRequest(
        currentState,
        effect,
        item.PlayerId,
        item.PlayerId,
        item.Secret.SourceCardInstanceId,
        commandSeq,
        _stateHasher.HashGameplayState(currentState),
        targetResolution.Targets,
        new ResolvedTargetRef(ResolvedTargetRef.CardKind, item.PlayerId, item.Secret.SourceCardInstanceId),
        RngRoller: _rngRoller,
        HistoryStore: _historyStore));
      if (!compilation.IsAccepted)
      {
        var diagnostic = compilation.Diagnostics.FirstOrDefault();
        throw new InvalidOperationException(
          diagnostic is null
            ? $"Secret effect '{effect.EffectId}' was rejected."
            : $"Secret effect '{effect.EffectId}' was rejected: {diagnostic.Code}.");
      }

      var batch = compilation.Batch! with { ParentEventSeq = (int)events[^1].EventSeq };
      var effectApplication = batchExecutor.Execute(currentState, batch, nextEventSeq);
      currentState = effectApplication.State;
      events.AddRange(effectApplication.CommittedEvents);
      nextEventSeq += effectApplication.CommittedEvents.Count;
    }

    return new EffectApplicationResult(currentState, events);
  }

  private EffectApplicationResult ApplyAuraRefresh(
    IGameState state,
    int commandSeq,
    long firstEventSeq)
  {
    if (!_cardVersionsByVersionId.Any())
    {
      return new EffectApplicationResult(state, []);
    }

    var events = new List<CommittedEventRecord>();
    var currentState = state;
    var nextEventSeq = firstEventSeq;
    var batchExecutor = new DeterministicMutationBatchExecutor(_mutator, _eventFactory);

    foreach (var player in state.Players.Values.OrderBy(player => player.PlayerId, StringComparer.Ordinal))
    {
      var activeAuras = state.Players.Values
        .SelectMany(sourcePlayer => sourcePlayer.Zones.Board
          .OrderBy(entity => entity.EntityInstanceId, StringComparer.Ordinal)
          .SelectMany(entity => ResolveAuraEffects(state, sourcePlayer.PlayerId, entity)))
        .Where(aura => StringComparer.Ordinal.Equals(aura.ControllerPlayerId, player.PlayerId))
        .OrderByDescending(aura => aura.Priority)
        .ThenBy(aura => aura.AuraId, StringComparer.Ordinal)
        .ToArray();

      if (AreAurasEqual(player.Auras ?? [], activeAuras))
      {
        continue;
      }

      var metadata = new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["playerId"] = player.PlayerId
      };
      for (var index = 0; index < activeAuras.Length; index++)
      {
        var aura = activeAuras[index];
        var prefix = $"aura:{index + 1:0000}:";
        metadata[prefix + "auraId"] = aura.AuraId;
        metadata[prefix + "controllerPlayerId"] = aura.ControllerPlayerId;
        metadata[prefix + "effectId"] = aura.EffectId;
        metadata[prefix + "priority"] = aura.Priority.ToString(CultureInfo.InvariantCulture);
        metadata[prefix + "sourceCardInstanceId"] = aura.SourceCardInstanceId;
        metadata[prefix + "sourceCardVersion"] = aura.SourceCardVersion;
        metadata[prefix + "sourceEntityInstanceId"] = aura.SourceEntityInstanceId;
      }

      var batch = new ProposedMutationBatch(
        $"batch:{commandSeq}:aura-refresh:{player.PlayerId}",
        commandSeq,
        null,
        "AURA_REFRESH",
        "aura-system",
        "AURA_REFRESH",
        [
          new StateMutation(
            $"mutation:{commandSeq}:aura-refresh:{player.PlayerId}",
            RefreshAurasMutationHandler.SupportedMutationType,
            "aura-system",
            player.PlayerId,
            new SortedDictionary<string, int>(StringComparer.Ordinal)
            {
              ["auraCount"] = activeAuras.Length
            },
            "AURA_REFRESH",
            commandSeq,
            null,
            $"aura-refresh:{player.PlayerId}:0001",
            metadata)
        ],
        _stateHasher.HashGameplayState(currentState),
        null,
        null);
      var application = batchExecutor.Execute(currentState, batch, nextEventSeq);
      currentState = application.State;
      events.AddRange(application.CommittedEvents);
      nextEventSeq += application.CommittedEvents.Count;
    }

    return new EffectApplicationResult(currentState, events);
  }

  private EffectApplicationResult ApplyHeroPowerEffects(
    IGameState state,
    IGameCommand command,
    int commandSeq,
    long firstEventSeq)
  {
    if (command is not UseHeroPowerCommand useHeroPower
      || !_cardVersionsByVersionId.Any()
      || !state.Players.TryGetValue(useHeroPower.PlayerId, out var player)
      || player.HeroPower is null)
    {
      return new EffectApplicationResult(state, []);
    }

    var sourceCard = player.Zones.SetAside
      .Concat(player.Zones.Hand)
      .Concat(player.Zones.Deck)
      .Concat(player.Zones.Graveyard)
      .SingleOrDefault(card =>
        StringComparer.Ordinal.Equals(card.CardInstanceId, useHeroPower.SourceCardInstanceId));
    if (sourceCard is null
      || !_cardVersionsByVersionId.TryGetValue(sourceCard.CardVersion, out var cardVersion))
    {
      return new EffectApplicationResult(state, []);
    }

    var heroPower = ResolveHeroPowerDefinition(cardVersion);
    if (heroPower is null || heroPower.Effects.Count == 0)
    {
      return new EffectApplicationResult(state, []);
    }

    var currentState = state;
    var events = new List<CommittedEventRecord>();
    var nextEventSeq = firstEventSeq;
    var batchExecutor = new DeterministicMutationBatchExecutor(_mutator, _eventFactory);
    foreach (var effect in heroPower.Effects
      .Where(effect =>
        effect.TimingPolicy.Phase is Phase.OnPlay))
    {
      var targetResolution = ResolveHeroPowerTargetsOrFizzle(currentState, effect, useHeroPower, commandSeq);
      if (targetResolution is null)
      {
        continue;
      }

      foreach (var rngRoll in targetResolution.RngRolls)
      {
        events.Add(CreateRngRolledEvent(
          rngRoll,
          currentState,
          nextEventSeq++,
          targetResolution.Targets));
      }

      var compilation = _effectActionCompiler.Compile(new EffectActionCompilationRequest(
        currentState,
        effect,
        useHeroPower.PlayerId,
        useHeroPower.PlayerId,
        useHeroPower.SourceCardInstanceId,
        commandSeq,
        _stateHasher.HashGameplayState(currentState),
        targetResolution.Targets,
        HeroPowerSource(useHeroPower),
        RngRoller: _rngRoller,
        HistoryStore: _historyStore));

      if (!compilation.IsAccepted)
      {
        var diagnostic = compilation.Diagnostics.FirstOrDefault();
        throw new InvalidOperationException(
          diagnostic is null
            ? $"Hero power effect '{effect.EffectId}' was rejected."
            : $"Hero power effect '{effect.EffectId}' was rejected: {diagnostic.Code}.");
      }

      foreach (var rngRoll in compilation.RngRolls)
      {
        events.Add(CreateRngRolledEvent(
          rngRoll.Roll,
          currentState,
          nextEventSeq++,
          rngRoll.SelectedTargets));
      }

      var effectApplication = batchExecutor.Execute(currentState, compilation.Batch!, nextEventSeq);
      currentState = effectApplication.State;
      events.AddRange(effectApplication.CommittedEvents);
      nextEventSeq += effectApplication.CommittedEvents.Count;
    }

    return new EffectApplicationResult(currentState, events);
  }

  private static Game.Shared.Dsl.HeroPowerDefinition? ResolveHeroPowerDefinition(CardVersion cardVersion) =>
    cardVersion.Definition.Entity.HeroCard?.HeroPowerReplacement
    ?? cardVersion.Definition.Entity.HeroPower;

  private EffectApplicationResult ApplyLocationActivationEffects(
    IGameState state,
    IGameCommand command,
    int commandSeq,
    long firstEventSeq)
  {
    if (command is not ActivateLocationCommand activateLocation
      || !_cardVersionsByVersionId.Any()
      || !state.Players.TryGetValue(activateLocation.PlayerId, out var player))
    {
      return new EffectApplicationResult(state, []);
    }

    var sourceCard = player.Zones.SetAside
      .Concat(player.Zones.Graveyard)
      .Concat(player.Zones.Hand)
      .Concat(player.Zones.Deck)
      .SingleOrDefault(card =>
        StringComparer.Ordinal.Equals(card.CardInstanceId, activateLocation.SourceCardInstanceId));
    if (sourceCard is null
      || !_cardVersionsByVersionId.TryGetValue(sourceCard.CardVersion, out var cardVersion))
    {
      return new EffectApplicationResult(state, []);
    }

    var locationEffects = cardVersion.Definition.Effects
      .Where(IsLocationAbilityEffect)
      .OrderBy(effect => effect.TimingPolicy.Priority)
      .ThenBy(effect => effect.EffectId, StringComparer.Ordinal)
      .ToArray();
    if (locationEffects.Length == 0)
    {
      return new EffectApplicationResult(state, []);
    }

    var currentState = state;
    var events = new List<CommittedEventRecord>();
    var nextEventSeq = firstEventSeq;
    var batchExecutor = new DeterministicMutationBatchExecutor(_mutator, _eventFactory);
    foreach (var effect in locationEffects)
    {
      var targetResolution = ResolveLocationTargetsOrFizzle(currentState, effect, activateLocation, commandSeq);
      if (targetResolution is null)
      {
        continue;
      }

      foreach (var rngRoll in targetResolution.RngRolls)
      {
        events.Add(CreateRngRolledEvent(
          rngRoll,
          currentState,
          nextEventSeq++,
          targetResolution.Targets));
      }

      var compilation = _effectActionCompiler.Compile(new EffectActionCompilationRequest(
        currentState,
        effect,
        activateLocation.PlayerId,
        activateLocation.PlayerId,
        activateLocation.SourceCardInstanceId,
        commandSeq,
        _stateHasher.HashGameplayState(currentState),
        targetResolution.Targets,
        LocationSource(activateLocation),
        RngRoller: _rngRoller,
        HistoryStore: _historyStore));

      if (!compilation.IsAccepted)
      {
        var diagnostic = compilation.Diagnostics.FirstOrDefault();
        throw new InvalidOperationException(
          diagnostic is null
            ? $"Location ability effect '{effect.EffectId}' was rejected."
            : $"Location ability effect '{effect.EffectId}' was rejected: {diagnostic.Code}.");
      }

      foreach (var rngRoll in compilation.RngRolls)
      {
        events.Add(CreateRngRolledEvent(
          rngRoll.Roll,
          currentState,
          nextEventSeq++,
          rngRoll.SelectedTargets));
      }

      var effectApplication = batchExecutor.Execute(currentState, compilation.Batch!, nextEventSeq);
      currentState = effectApplication.State;
      events.AddRange(effectApplication.CommittedEvents);
      nextEventSeq += effectApplication.CommittedEvents.Count;
    }

    return new EffectApplicationResult(currentState, events);
  }

  private TargetResolutionOutcome? ResolveHeroPowerTargetsOrFizzle(
    IGameState state,
    Game.Shared.Dsl.EffectDefinition effect,
    UseHeroPowerCommand command,
    int commandSeq)
  {
    if (effect.TargetRequirement is null)
    {
      return new TargetResolutionOutcome([], []);
    }

    var chosenTargets = CreateHeroPowerChosenTargets(state, command);
    var resolution = _targetResolver.Resolve(state, new TargetResolutionRequest(
      effect.TargetRequirement,
      command.PlayerId,
      command.PlayerId,
      ChosenTargets: chosenTargets,
      EventSource: HeroPowerSource(command),
      RngRoller: _rngRoller,
      CommandSeq: commandSeq,
      RngRollScope: $"hero-power:{command.SourceCardInstanceId}:effect:{effect.EffectId}:target"));

    if (resolution.IsAccepted)
    {
      return new TargetResolutionOutcome(resolution.Targets, resolution.RngRolls);
    }

    if (string.Equals(effect.FizzlePolicy.Kind, "FIZZLE", StringComparison.OrdinalIgnoreCase)
      || string.Equals(effect.FizzlePolicy.Kind, "FIZZLE_WHOLE_EFFECT", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    var error = resolution.Errors.FirstOrDefault();
    throw new InvalidOperationException(
      error is null
        ? $"Hero power effect '{effect.EffectId}' target resolution failed."
        : $"Hero power effect '{effect.EffectId}' target resolution failed: {error.Code}.");
  }

  private TargetResolutionOutcome? ResolveLocationTargetsOrFizzle(
    IGameState state,
    Game.Shared.Dsl.EffectDefinition effect,
    ActivateLocationCommand command,
    int commandSeq)
  {
    if (effect.TargetRequirement is null)
    {
      return new TargetResolutionOutcome([], []);
    }

    var chosenTargets = CreateLocationChosenTargets(state, command);
    var resolution = _targetResolver.Resolve(state, new TargetResolutionRequest(
      effect.TargetRequirement,
      command.PlayerId,
      command.PlayerId,
      ChosenTargets: chosenTargets,
      EventSource: LocationSource(command),
      RngRoller: _rngRoller,
      CommandSeq: commandSeq,
      RngRollScope: $"location:{command.LocationEntityInstanceId}:effect:{effect.EffectId}:target"));

    if (resolution.IsAccepted)
    {
      return new TargetResolutionOutcome(resolution.Targets, resolution.RngRolls);
    }

    if (string.Equals(effect.FizzlePolicy.Kind, "FIZZLE", StringComparison.OrdinalIgnoreCase)
      || string.Equals(effect.FizzlePolicy.Kind, "FIZZLE_WHOLE_EFFECT", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    var error = resolution.Errors.FirstOrDefault();
    throw new InvalidOperationException(
      error is null
        ? $"Location ability effect '{effect.EffectId}' target resolution failed."
        : $"Location ability effect '{effect.EffectId}' target resolution failed: {error.Code}.");
  }

  private TargetResolutionOutcome? ResolveDeathrattleTargetsOrFizzle(
    IGameState state,
    Game.Shared.Dsl.EffectDefinition effect,
    DeadEntitySnapshot deadEntity,
    int commandSeq)
  {
    if (effect.TargetRequirement is null)
    {
      return new TargetResolutionOutcome([], []);
    }

    if (effect.TargetRequirement.SelectionMode is TargetSelectionMode.PlayerChosen)
    {
      var candidateResolution = _targetResolver.Resolve(state, new TargetResolutionRequest(
        effect.TargetRequirement,
        deadEntity.ControllerPlayerId,
        deadEntity.ControllerPlayerId,
        ChosenTargets: [],
        EventSource: new ResolvedTargetRef(
          ResolvedTargetRef.EntityKind,
          deadEntity.PlayerId,
          deadEntity.EntityInstanceId),
        CommandSeq: commandSeq,
        RngRollScope: $"deathrattle:{deadEntity.EntityInstanceId}:effect:{effect.EffectId}:target"));

      if (candidateResolution.Candidates.Count > 0)
      {
        return new TargetResolutionOutcome(
          [],
          [],
          CreateEffectTargetPendingChoice(
            effect,
            deadEntity,
            candidateResolution.Candidates));
      }

      if (string.Equals(effect.FizzlePolicy.Kind, "FIZZLE", StringComparison.OrdinalIgnoreCase)
        || string.Equals(effect.FizzlePolicy.Kind, "FIZZLE_WHOLE_EFFECT", StringComparison.OrdinalIgnoreCase))
      {
        return null;
      }

      var candidateError = candidateResolution.Errors.FirstOrDefault();
      throw new InvalidOperationException(
        candidateError is null
          ? $"Deathrattle effect '{effect.EffectId}' target choice has no legal candidates."
          : $"Deathrattle effect '{effect.EffectId}' target choice failed: {candidateError.Code}.");
    }

    var resolution = _targetResolver.Resolve(state, new TargetResolutionRequest(
      effect.TargetRequirement,
      deadEntity.ControllerPlayerId,
      deadEntity.ControllerPlayerId,
      ChosenTargets: [],
      EventSource: new ResolvedTargetRef(
        ResolvedTargetRef.EntityKind,
        deadEntity.PlayerId,
        deadEntity.EntityInstanceId),
      RngRoller: _rngRoller,
      CommandSeq: commandSeq,
      RngRollScope: $"deathrattle:{deadEntity.EntityInstanceId}:effect:{effect.EffectId}:target"));

    if (resolution.IsAccepted)
    {
      return new TargetResolutionOutcome(resolution.Targets, resolution.RngRolls);
    }

    if (string.Equals(effect.FizzlePolicy.Kind, "FIZZLE", StringComparison.OrdinalIgnoreCase)
      || string.Equals(effect.FizzlePolicy.Kind, "FIZZLE_WHOLE_EFFECT", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    var error = resolution.Errors.FirstOrDefault();
    throw new InvalidOperationException(
      error is null
        ? $"Deathrattle effect '{effect.EffectId}' target resolution failed."
        : $"Deathrattle effect '{effect.EffectId}' target resolution failed: {error.Code}.");
  }

  private TargetResolutionOutcome? ResolveQuestTargetsOrFizzle(
    IGameState state,
    Game.Shared.Dsl.EffectDefinition effect,
    QuestProgressMatch match,
    int commandSeq)
  {
    if (effect.TargetRequirement is null)
    {
      return new TargetResolutionOutcome([], []);
    }

    if (effect.TargetRequirement.SelectionMode is TargetSelectionMode.PlayerChosen)
    {
      var candidateResolution = _targetResolver.Resolve(state, new TargetResolutionRequest(
        effect.TargetRequirement,
        match.PlayerId,
        match.PlayerId,
        ChosenTargets: [],
        EventSource: new ResolvedTargetRef(
          ResolvedTargetRef.CardKind,
          match.PlayerId,
          match.Quest.SourceCardInstanceId),
        CommandSeq: commandSeq,
        RngRollScope: $"quest:{match.Quest.QuestId}:effect:{effect.EffectId}:target"));

      if (candidateResolution.Candidates.Count > 0)
      {
        return new TargetResolutionOutcome(
          [],
          [],
          CreateEffectTargetPendingChoice(
            effect,
            match,
            candidateResolution.Candidates));
      }

      if (string.Equals(effect.FizzlePolicy.Kind, "FIZZLE", StringComparison.OrdinalIgnoreCase)
        || string.Equals(effect.FizzlePolicy.Kind, "FIZZLE_WHOLE_EFFECT", StringComparison.OrdinalIgnoreCase))
      {
        return null;
      }

      var candidateError = candidateResolution.Errors.FirstOrDefault();
      throw new InvalidOperationException(
        candidateError is null
          ? $"Quest reward effect '{effect.EffectId}' target choice has no legal candidates."
          : $"Quest reward effect '{effect.EffectId}' target choice failed: {candidateError.Code}.");
    }

    var resolution = _targetResolver.Resolve(state, new TargetResolutionRequest(
      effect.TargetRequirement,
      match.PlayerId,
      match.PlayerId,
      ChosenTargets: [],
      EventSource: new ResolvedTargetRef(
        ResolvedTargetRef.CardKind,
        match.PlayerId,
        match.Quest.SourceCardInstanceId),
      RngRoller: _rngRoller,
      CommandSeq: commandSeq,
      RngRollScope: $"quest:{match.Quest.QuestId}:effect:{effect.EffectId}:target"));

    if (resolution.IsAccepted)
    {
      return new TargetResolutionOutcome(resolution.Targets, resolution.RngRolls);
    }

    if (string.Equals(effect.FizzlePolicy.Kind, "FIZZLE", StringComparison.OrdinalIgnoreCase)
      || string.Equals(effect.FizzlePolicy.Kind, "FIZZLE_WHOLE_EFFECT", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    var error = resolution.Errors.FirstOrDefault();
    throw new InvalidOperationException(
      error is null
        ? $"Quest reward effect '{effect.EffectId}' target resolution failed."
        : $"Quest reward effect '{effect.EffectId}' target resolution failed: {error.Code}.");
  }

  private TargetResolutionOutcome? ResolveSecretTargetsOrFizzle(
    IGameState state,
    Game.Shared.Dsl.EffectDefinition effect,
    PlayerSecret secret,
    int commandSeq)
  {
    if (effect.TargetRequirement is null)
    {
      return new TargetResolutionOutcome([], []);
    }

    var resolution = _targetResolver.Resolve(state, new TargetResolutionRequest(
      effect.TargetRequirement,
      secret.ControllerPlayerId,
      secret.ControllerPlayerId,
      ChosenTargets: [],
      EventSource: new ResolvedTargetRef(
        ResolvedTargetRef.CardKind,
        secret.ControllerPlayerId,
        secret.SourceCardInstanceId),
      RngRoller: _rngRoller,
      CommandSeq: commandSeq,
      RngRollScope: $"secret:{secret.SecretId}:effect:{effect.EffectId}:target"));

    if (resolution.IsAccepted)
    {
      return new TargetResolutionOutcome(resolution.Targets, resolution.RngRolls);
    }

    if (string.Equals(effect.FizzlePolicy.Kind, "FIZZLE", StringComparison.OrdinalIgnoreCase)
      || string.Equals(effect.FizzlePolicy.Kind, "FIZZLE_WHOLE_EFFECT", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    var error = resolution.Errors.FirstOrDefault();
    throw new InvalidOperationException(
      error is null
        ? $"Secret effect '{effect.EffectId}' target resolution failed."
        : $"Secret effect '{effect.EffectId}' target resolution failed: {error.Code}.");
  }

  private IReadOnlyList<PlayerAura> ResolveAuraEffects(IGameState state, string playerId, EntityInstance entity)
  {
    if (!state.Players.TryGetValue(playerId, out var player))
    {
      return [];
    }

    var sourceCard = player.Zones.SetAside
      .Concat(player.Zones.SecretZone)
      .Concat(player.Zones.Graveyard)
      .Concat(player.Zones.Hand)
      .Concat(player.Zones.Deck)
      .SingleOrDefault(card =>
        StringComparer.Ordinal.Equals(card.CardInstanceId, entity.SourceCardInstanceId));
    if (sourceCard is null
      || !_cardVersionsByVersionId.TryGetValue(sourceCard.CardVersion, out var cardVersion))
    {
      return [];
    }

    return cardVersion.Definition.Effects
      .Where(IsAuraEffect)
      .OrderBy(effect => effect.TimingPolicy.Priority)
      .ThenBy(effect => effect.EffectId, StringComparer.Ordinal)
      .Select(effect => new PlayerAura(
        $"aura:{entity.EntityInstanceId}:{effect.EffectId}",
        entity.EntityInstanceId,
        entity.SourceCardInstanceId,
        cardVersion.CardVersionId,
        playerId,
        effect.EffectId,
        effect.TimingPolicy.Priority))
      .ToArray();
  }

  private static bool AreAurasEqual(IReadOnlyList<PlayerAura> left, IReadOnlyList<PlayerAura> right)
  {
    if (left.Count != right.Count)
    {
      return false;
    }

    for (var index = 0; index < left.Count; index++)
    {
      if (left[index] != right[index])
      {
        return false;
      }
    }

    return true;
  }

  private TargetResolutionOutcome? ResolveTargetsOrFizzle(
    IGameState state,
    Game.Shared.Dsl.EffectDefinition effect,
    PlayCardCommand playCard,
    int commandSeq)
  {
    if (effect.TargetRequirement is null)
    {
      return new TargetResolutionOutcome([], []);
    }

    var resolution = _targetResolver.Resolve(state, new TargetResolutionRequest(
      effect.TargetRequirement,
      playCard.PlayerId,
      playCard.PlayerId,
      ChosenTargets:CreatePlayCardChosenTargets(state,playCard),
      EventSource: PlayCardSource(playCard),
      RngRoller: _rngRoller,
      CommandSeq: commandSeq,
      RngRollScope: $"effect:{effect.EffectId}:target"));

    if (resolution.IsAccepted)
    {
      return new TargetResolutionOutcome(resolution.Targets, resolution.RngRolls);
    }

    if (string.Equals(effect.FizzlePolicy.Kind, "FIZZLE", StringComparison.OrdinalIgnoreCase)
      || string.Equals(effect.FizzlePolicy.Kind, "FIZZLE_WHOLE_EFFECT", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    var error = resolution.Errors.FirstOrDefault();
    throw new InvalidOperationException(
      error is null
        ? $"Generated effect '{effect.EffectId}' target resolution failed."
        : $"Generated effect '{effect.EffectId}' target resolution failed: {error.Code}.");
  }

  private static IReadOnlyList<ResolvedTargetRef> CreatePlayCardChosenTargets(IGameState state,PlayCardCommand command)
  {
    if(string.IsNullOrWhiteSpace(command.TargetEntityInstanceId))return [];
    var playerId=string.IsNullOrWhiteSpace(command.TargetPlayerId)?state.Players.FirstOrDefault(pair=>pair.Value.Zones.Board.Any(entity=>StringComparer.Ordinal.Equals(entity.EntityInstanceId,command.TargetEntityInstanceId))).Key:command.TargetPlayerId;
    if(string.IsNullOrWhiteSpace(playerId))return [];
    return [new ResolvedTargetRef(ResolvedTargetRef.EntityKind,playerId,command.TargetEntityInstanceId)];
  }

  private sealed record EffectApplicationResult(
    IGameState State,
    IReadOnlyList<CommittedEventRecord> Events);

  private sealed record DeadEntitySnapshot(
    string PlayerId,
    string EntityInstanceId,
    string SourceCardInstanceId,
    string? CardVersion,
    string ControllerPlayerId);

  private sealed record DeathrattleMatch(
    DeadEntitySnapshot DeadEntity,
    CommittedEventRecord DiedEvent,
    TriggerMatch TriggerMatch,
    Game.Shared.Dsl.EffectDefinition Effect);

  private sealed record QuestProgressMatch(
    string PlayerId,
    PlayerQuest Quest,
    CommittedEventRecord SourceEvent,
    bool IsCompleted,
    StateMutation Mutation);

  private static ResolvedTargetRef PlayCardSource(PlayCardCommand playCard) =>
    string.IsNullOrWhiteSpace(playCard.EntityInstanceId)
      ? new ResolvedTargetRef(
        ResolvedTargetRef.CardKind,
        playCard.PlayerId,
        playCard.CardInstanceId)
      : new ResolvedTargetRef(
        ResolvedTargetRef.EntityKind,
        playCard.PlayerId,
        playCard.EntityInstanceId);

  private static ResolvedTargetRef HeroPowerSource(UseHeroPowerCommand command) =>
    new(
      ResolvedTargetRef.CardKind,
      command.PlayerId,
      command.SourceCardInstanceId);

  private static ResolvedTargetRef LocationSource(ActivateLocationCommand command) =>
    new(
      ResolvedTargetRef.EntityKind,
      command.PlayerId,
      command.LocationEntityInstanceId);

  private static IReadOnlyList<ResolvedTargetRef> CreateHeroPowerChosenTargets(
    IGameState state,
    UseHeroPowerCommand command)
  {
    if (string.IsNullOrWhiteSpace(command.TargetPlayerId)
      || string.IsNullOrWhiteSpace(command.TargetEntityInstanceId))
    {
      return [];
    }

    var targetKind = ResolvedTargetRef.EntityKind;
    if (state.Players.TryGetValue(command.TargetPlayerId, out var targetPlayer)
      && targetPlayer.Hero is not null
      && StringComparer.Ordinal.Equals(targetPlayer.Hero.EntityInstanceId, command.TargetEntityInstanceId))
    {
      targetKind = ResolvedTargetRef.HeroKind;
    }

    return
    [
      new ResolvedTargetRef(
        targetKind,
        command.TargetPlayerId,
        command.TargetEntityInstanceId)
    ];
  }

  private static IReadOnlyList<ResolvedTargetRef> CreateLocationChosenTargets(
    IGameState state,
    ActivateLocationCommand command)
  {
    if (string.IsNullOrWhiteSpace(command.TargetPlayerId)
      || string.IsNullOrWhiteSpace(command.TargetEntityInstanceId))
    {
      return [];
    }

    var targetKind = ResolvedTargetRef.EntityKind;
    if (state.Players.TryGetValue(command.TargetPlayerId, out var targetPlayer)
      && targetPlayer.Hero is not null
      && StringComparer.Ordinal.Equals(targetPlayer.Hero.EntityInstanceId, command.TargetEntityInstanceId))
    {
      targetKind = ResolvedTargetRef.HeroKind;
    }

    return
    [
      new ResolvedTargetRef(
        targetKind,
        command.TargetPlayerId,
        command.TargetEntityInstanceId)
    ];
  }

  private sealed record TargetResolutionOutcome(
    IReadOnlyList<ResolvedTargetRef> Targets,
    IReadOnlyList<RngRollRecord> RngRolls,
    PendingChoice? PendingChoice = null);

  private static PendingChoice CreateEffectTargetPendingChoice(
    Game.Shared.Dsl.EffectDefinition effect,
    DeadEntitySnapshot deadEntity,
    IReadOnlyList<ResolvedTargetRef> candidates)
  {
    var choiceId = $"choice:deathrattle:{deadEntity.EntityInstanceId}:{effect.EffectId}";
    var options = candidates
      .Select((candidate, index) => new ChoiceOption(
        $"{choiceId}:option:{index + 1:0000}",
        "TARGET",
        EncodeTargetOption(candidate)))
      .ToArray();

    return new PendingChoice(
      choiceId,
      deadEntity.ControllerPlayerId,
      "EFFECT_TARGET",
      "PUBLIC",
      "TARGET",
      EncodeEffectTargetChoiceSource(new EffectTargetChoiceSource(
        "DEATHRATTLE",
        deadEntity.ControllerPlayerId,
        deadEntity.SourceCardInstanceId,
        deadEntity.CardVersion ?? string.Empty,
        effect.EffectId,
        ResolvedTargetRef.EntityKind,
        deadEntity.PlayerId,
        deadEntity.EntityInstanceId)),
      options);
  }

  private static PendingChoice CreateEffectTargetPendingChoice(
    Game.Shared.Dsl.EffectDefinition effect,
    QuestProgressMatch match,
    IReadOnlyList<ResolvedTargetRef> candidates)
  {
    var choiceId = $"choice:quest:{match.Quest.QuestId}:{effect.EffectId}";
    var options = candidates
      .Select((candidate, index) => new ChoiceOption(
        $"{choiceId}:option:{index + 1:0000}",
        "TARGET",
        EncodeTargetOption(candidate)))
      .ToArray();

    return new PendingChoice(
      choiceId,
      match.PlayerId,
      "EFFECT_TARGET",
      "PUBLIC",
      "TARGET",
      EncodeEffectTargetChoiceSource(new EffectTargetChoiceSource(
        "QUEST",
        match.PlayerId,
        match.Quest.SourceCardInstanceId,
        match.Quest.SourceCardVersion ?? string.Empty,
        effect.EffectId,
        ResolvedTargetRef.CardKind,
        match.PlayerId,
        match.Quest.SourceCardInstanceId)),
      options);
  }

  private static Phase ToEffectTargetPhase(string sourceKind) =>
    sourceKind switch
    {
      "DEATHRATTLE" => Phase.DeathrattlePhase,
      "QUEST" => Phase.AfterPlay,
      _ => Phase.OnPlay
    };

  private static string EncodeTargetOption(ResolvedTargetRef target) =>
    string.Join("|",
    [
      "target-v1",
      Escape(target.TargetKind),
      Escape(target.PlayerId),
      Escape(target.TargetId)
    ]);

  private static bool TryDecodeTargetOption(string value, out ResolvedTargetRef target)
  {
    target = default!;
    var parts = value.Split('|');
    if (parts.Length != 4 || !StringComparer.Ordinal.Equals(parts[0], "target-v1"))
    {
      return false;
    }

    target = new ResolvedTargetRef(Unescape(parts[1]), Unescape(parts[2]), Unescape(parts[3]));
    return true;
  }

  private static string EncodeEffectTargetChoiceSource(EffectTargetChoiceSource source) =>
    string.Join("|",
    [
      "effect-choice-v1",
      Escape(source.SourceKind),
      Escape(source.ControllerPlayerId),
      Escape(source.SourceCardInstanceId),
      Escape(source.SourceCardVersion),
      Escape(source.EffectId),
      Escape(source.SourceTargetKind),
      Escape(source.SourceTargetPlayerId),
      Escape(source.SourceTargetId)
    ]);

  private static bool TryDecodeEffectTargetChoiceSource(
    string value,
    out EffectTargetChoiceSource source)
  {
    source = default!;
    var parts = value.Split('|');
    if (parts.Length != 9 || !StringComparer.Ordinal.Equals(parts[0], "effect-choice-v1"))
    {
      return false;
    }

    source = new EffectTargetChoiceSource(
      Unescape(parts[1]),
      Unescape(parts[2]),
      Unescape(parts[3]),
      Unescape(parts[4]),
      Unescape(parts[5]),
      Unescape(parts[6]),
      Unescape(parts[7]),
      Unescape(parts[8]));
    return true;
  }

  private static string Escape(string value) => WebUtility.UrlEncode(value);

  private static string Unescape(string value) => WebUtility.UrlDecode(value);

  private sealed record EffectTargetChoiceSource(
    string SourceKind,
    string ControllerPlayerId,
    string SourceCardInstanceId,
    string SourceCardVersion,
    string EffectId,
    string SourceTargetKind,
    string SourceTargetPlayerId,
    string SourceTargetId);

  private CommittedEventRecord CreateRngRolledEvent(
    RngRollRecord roll,
    IGameState state,
    long eventSeq,
    IReadOnlyList<ResolvedTargetRef> selectedTargets,
    Phase phase = Phase.OnPlay)
  {
    var stateHash = _stateHasher.HashGameplayState(state);
    var eventRoll = roll with { EventSeq = eventSeq };
    return new CommittedEventRecord(
      eventSeq,
      null,
      eventRoll.CommandSeq,
      CommittedEventType.RngRolled,
      EventCategory.Resolver,
      phase,
      [new EventRef("RNG_STREAM", ToStreamId(eventRoll.Stream), null, null)],
      selectedTargets
        .Select(target => new EventRef(
          target.TargetKind,
          target.TargetId,
          null,
          target.PlayerId))
        .ToArray(),
      [
        StringPayload("rngRollId", eventRoll.RngRollId),
        StringPayload("stream", ToStreamId(eventRoll.Stream)),
        StringPayload("preRngState", eventRoll.PreRngState.ToString("X16", System.Globalization.CultureInfo.InvariantCulture)),
        StringPayload("postRngState", eventRoll.PostRngState.ToString("X16", System.Globalization.CultureInfo.InvariantCulture)),
        IntPayload("candidateCount", eventRoll.CandidateCount),
        IntPayload("selectedIndex", eventRoll.SelectedIndex),
        StringPayload("reason", eventRoll.Reason)
      ],
      null,
      [],
      [eventRoll.RngRollId],
      stateHash,
      stateHash,
      EventVisibility.DebugOnly);
  }

  private static EventPayloadEntry StringPayload(string key, string value) =>
    new(key, new EventPayloadValue("STRING", value, null, null, []));

  private static EventPayloadEntry IntPayload(string key, int value) =>
    new(key, new EventPayloadValue("INT", null, value, null, []));

  private CommittedEventRecord CreateDeathrattleTriggeredEvent(
    DeathrattleMatch match,
    IGameState state,
    long eventSeq)
  {
    var stateHash = _stateHasher.HashGameplayState(state);
    return new CommittedEventRecord(
      eventSeq,
      match.DiedEvent.EventSeq,
      match.DiedEvent.CommandSeq,
      CommittedEventType.DeathrattleTriggered,
      EventCategory.Committed,
      Phase.DeathrattlePhase,
      [new EventRef("ENTITY_INSTANCE", match.DeadEntity.EntityInstanceId, null, match.DeadEntity.PlayerId)],
      [new EventRef("EFFECT", match.Effect.EffectId, null, match.DeadEntity.ControllerPlayerId)],
      [
        StringPayload("entityInstanceId", match.DeadEntity.EntityInstanceId),
        StringPayload("playerId", match.DeadEntity.PlayerId),
        StringPayload("sourceCardInstanceId", match.DeadEntity.SourceCardInstanceId),
        StringPayload("effectId", match.Effect.EffectId),
        StringPayload("triggerId", match.TriggerMatch.TriggerId)
      ],
      null,
      [],
      [],
      stateHash,
      stateHash,
      EventVisibility.Public);
  }

  private static bool IsDeathrattleEffect(Game.Shared.Dsl.EffectDefinition effect) =>
    effect.EffectKind is EffectKind.DeathrattleEffect
    || effect.TimingPolicy.Phase is Phase.DeathrattlePhase
    || effect.Trigger?.EventType is EventType.EntityDied;

  private static bool IsSecretEffect(Game.Shared.Dsl.EffectDefinition effect) =>
    effect.EffectKind is EffectKind.SecretEffect
    || effect.TimingPolicy.Phase is Phase.SecretWindow
    || effect.SourceZones.Contains(SourceZone.SecretZone);

  private static bool IsAuraEffect(Game.Shared.Dsl.EffectDefinition effect) =>
    effect.EffectKind is EffectKind.AuraEffect
    || effect.TimingPolicy.Phase is Phase.AuraRefresh;

  private static bool IsLocationAbilityEffect(Game.Shared.Dsl.EffectDefinition effect) =>
    effect.EffectKind is EffectKind.LocationAbilityEffect;

  private static CommittedEventType ToCommittedEventType(EventType eventType)
  {
    return eventType switch
    {
      EventType.CardPlayed => CommittedEventType.CardPlayed,
      EventType.CardSummoned => CommittedEventType.MinionSummoned,
      EventType.TurnStarted => CommittedEventType.TurnStarted,
      EventType.TurnEnded => CommittedEventType.TurnEnded,
      EventType.DamageDealt => CommittedEventType.EntityDamaged,
      EventType.EntityDied => CommittedEventType.EntityDied,
      EventType.CardDrawn => CommittedEventType.CardDrawn,
      _ => CommittedEventType.EngineFaulted
    };
  }

  private static string? PayloadString(CommittedEventRecord committedEvent, string key) =>
    committedEvent.Payload
      .Where(entry => StringComparer.Ordinal.Equals(entry.Key, key))
      .Select(entry => entry.Value.StringValue)
      .FirstOrDefault(value => value is not null);

  private static string QuestKey(string playerId, string questId) => $"{playerId}:{questId}";

  private static string ToEventTypeId(CommittedEventType eventType)
  {
    return eventType switch
    {
      CommittedEventType.GameStarted => "GAME_STARTED",
      CommittedEventType.TurnStarted => "TURN_STARTED",
      CommittedEventType.TurnEnded => "TURN_ENDED",
      CommittedEventType.CardPlayed => "CARD_PLAYED",
      CommittedEventType.CardMovedZone => "CARD_MOVED_ZONE",
      CommittedEventType.CardDrawn => "CARD_DRAWN",
      CommittedEventType.CardDiscarded => "CARD_DISCARDED",
      CommittedEventType.CardReturnedToHand => "CARD_RETURNED_TO_HAND",
      CommittedEventType.MinionSummoned => "MINION_SUMMONED",
      CommittedEventType.EntityDamaged => "ENTITY_DAMAGED",
      CommittedEventType.EntityHealed => "ENTITY_HEALED",
      CommittedEventType.ManaChanged => "MANA_CHANGED",
      CommittedEventType.EntityDestroyed => "ENTITY_DESTROYED",
      CommittedEventType.EntityDied => "ENTITY_DIED",
      CommittedEventType.DeathrattleTriggered => "DEATHRATTLE_TRIGGERED",
      CommittedEventType.SecretRevealed => "SECRET_REVEALED",
      CommittedEventType.QuestProgressChanged => "QUEST_PROGRESS_CHANGED",
      CommittedEventType.ModifierApplied => "MODIFIER_APPLIED",
      CommittedEventType.ModifierExpired => "MODIFIER_EXPIRED",
      CommittedEventType.AuraRefreshed => "AURA_REFRESHED",
      CommittedEventType.WeaponEquipped => "WEAPON_EQUIPPED",
      CommittedEventType.LocationPlayed => "LOCATION_PLAYED",
      CommittedEventType.LocationActivated => "LOCATION_ACTIVATED",
      CommittedEventType.HeroCardPlayed => "HERO_CARD_PLAYED",
      CommittedEventType.HeroPowerUsed => "HERO_POWER_USED",
      CommittedEventType.ChoiceCreated => "CHOICE_CREATED",
      CommittedEventType.ChoiceResolved => "CHOICE_RESOLVED",
      CommittedEventType.RngRolled => "RNG_ROLLED",
      CommittedEventType.GameEnded => "GAME_ENDED",
      CommittedEventType.EngineFaulted => "ENGINE_FAULTED",
      _ => eventType.ToString().ToUpperInvariant()
    };
  }

  private static string? ResolveQuestSourceCardVersion(
    IGameState state,
    string playerId,
    PlayerQuest quest)
  {
    if (!string.IsNullOrWhiteSpace(quest.SourceCardVersion))
    {
      return quest.SourceCardVersion;
    }

    if (!state.Players.TryGetValue(playerId, out var player))
    {
      return null;
    }

    return player.Zones.Deck
      .Concat(player.Zones.Hand)
      .Concat(player.Zones.Graveyard)
      .Concat(player.Zones.SetAside)
      .SingleOrDefault(card =>
        StringComparer.Ordinal.Equals(card.CardInstanceId, quest.SourceCardInstanceId))
      ?.CardVersion;
  }

  private static string ToStreamId(RngStreamKind stream) =>
    stream switch
    {
      RngStreamKind.GameSetup => "GAME_SETUP",
      RngStreamKind.Shuffle => "SHUFFLE",
      RngStreamKind.CardDrawRandom => "CARD_DRAW_RANDOM",
      RngStreamKind.CardEffect => "CARD_EFFECT",
      RngStreamKind.RandomTarget => "RANDOM_TARGET",
      RngStreamKind.DiscoverOffer => "DISCOVER_OFFER",
      RngStreamKind.BotDecision => "BOT_DECISION",
      RngStreamKind.SimulationOnly => "SIMULATION_ONLY",
      RngStreamKind.Presentation => "PRESENTATION",
      _ => stream.ToString().ToUpperInvariant()
    };

  private static bool AllPlayersOutOfResourcesAndBoard(IGameState state) =>
    state.Players.Values.All(player =>
      player.Zones.Deck.Count == 0
      && player.Zones.Hand.Count == 0
      && player.Zones.Board.Count == 0
      && player.Hero is null);

  private sealed class CanonicalOrderingKeyComparer : IComparer<IReadOnlyList<string>>
  {
    public static CanonicalOrderingKeyComparer Instance { get; } = new();

    public int Compare(IReadOnlyList<string>? x, IReadOnlyList<string>? y)
    {
      x ??= [];
      y ??= [];

      var sharedLength = Math.Min(x.Count, y.Count);
      for (var i = 0; i < sharedLength; i++)
      {
        var comparison = StringComparer.Ordinal.Compare(x[i], y[i]);
        if (comparison != 0)
        {
          return comparison;
        }
      }

      return x.Count.CompareTo(y.Count);
    }
  }
}
