using Game.Application.Execution;
using Game.Domain.Commands;
using Game.Domain.Events;
using Game.Domain.Hashing;
using Game.Domain.Mutation;
using Game.Domain.Replay;
using Game.Domain.State;
using Game.Shared.Catalog;
using Game.Shared.Events;

namespace Game.Application.Replay;

public sealed class CommandLogReplayer
{
  private readonly IStateMutator _mutator;
  private readonly IDerivedEventLog _eventLog;
  private readonly IStateHasher _stateHasher;
  private readonly IReadOnlyList<CardVersion>? _catalogCards;
  private readonly ICommandValidator _validator;
  private readonly CommandValidationOptions _validationOptions;

  public CommandLogReplayer(
    IStateMutator mutator,
    IDerivedEventLog eventLog,
    IStateHasher stateHasher,
    IReadOnlyList<CardVersion>? catalogCards = null,
    ICommandValidator? validator = null,
    CommandValidationOptions? validationOptions = null)
  {
    ArgumentNullException.ThrowIfNull(stateHasher);

    _mutator = mutator;
    _eventLog = eventLog;
    _stateHasher = stateHasher;
    _catalogCards = catalogCards;
    _validator = validator ?? new DeterministicCommandValidator();
    _validationOptions = validationOptions ?? CommandValidationOptions.DebugReplay;
  }

  public CommandLogReplayResult Replay(IGameState initialState, CanonicalCommandLog commandLog)
  {
    ArgumentNullException.ThrowIfNull(initialState);
    ArgumentNullException.ThrowIfNull(commandLog);

    var derivedEvents = new List<CommittedEventRecord>();
    var state = initialState;
    long nextEventSeq = 1;
    var executor = new DeterministicCommandExecutor(
      _mutator,
      _eventLog,
      _catalogCards,
      stateHasher: _stateHasher);
    foreach (var payload in commandLog.Payloads)
    {
      var validation = _validator.Validate(payload, _validationOptions);
      if (!validation.IsAccepted)
      {
        throw new InvalidOperationException(
          $"Command validation failed at commandSeq {payload.CommandSeq}: {validation.Errors[0].Code}.");
      }

      if (payload.CommandCategory == CommandCategory.Debug && !payload.DebugReplayFlag)
      {
        throw new InvalidOperationException(
          $"Debug command replay requires debugReplayFlag at commandSeq {payload.CommandSeq}.");
      }

      if (payload.ExpectedPreStateHash is not null)
      {
        var actualPreStateHash = _stateHasher.HashGameplayState(state);
        if (!StringComparer.Ordinal.Equals(payload.ExpectedPreStateHash, actualPreStateHash))
        {
          throw new InvalidOperationException(
            $"Replay pre-state hash mismatch at commandSeq {payload.CommandSeq}: expected {payload.ExpectedPreStateHash}, actual {actualPreStateHash}.");
        }
      }

      var result = executor.Execute(state, payload.ToCommand(), nextEventSeq);
      state = result.State;
      derivedEvents.AddRange(result.Events);
      nextEventSeq += result.Events.Count;
    }

    return new CommandLogReplayResult(state, derivedEvents);
  }
}
