using Game.Domain.Commands;
using Game.Application.Legality;
using Game.Domain.State;

namespace Game.Application.Commands;

public sealed class CommandAdmissionService(
  ICommandValidator? validator = null,
  CommandValidationOptions? options = null,
  ILegalActionOracle? legalActionOracle = null)
{
  private readonly ICommandValidator _validator = validator ?? new DeterministicCommandValidator();
  private readonly CommandValidationOptions _options = options ?? CommandValidationOptions.Production;
  private readonly ILegalActionOracle _legalActionOracle = legalActionOracle ?? new LegalActionOracle();

  public CommandAdmissionResult Admit(GameCommandEnvelope envelope)
  {
    ArgumentNullException.ThrowIfNull(envelope);

    var payload = envelope.ToDeterministicPayload();
    var validation = _validator.Validate(payload, _options);

    return validation.IsAccepted
      ? CommandAdmissionResult.Accepted(payload)
      : CommandAdmissionResult.Rejected(validation.Errors);
  }

  public CommandAdmissionResult Admit(
    GameCommandEnvelope envelope,
    LegalActionQuery legalActionQuery)
  {
    ArgumentNullException.ThrowIfNull(legalActionQuery);

    var basicAdmission = Admit(envelope);
    if (!basicAdmission.IsAccepted || basicAdmission.Payload is null)
    {
      return basicAdmission;
    }

    var payload = basicAdmission.Payload;
    if (payload.CommandType is not (
      ActivateLocationCommand.CommandKind
      or PlayCardCommand.CommandKind
      or AttackCommand.CommandKind
      or UseHeroPowerCommand.CommandKind
      or ConcedeCommand.CommandKind
      or EndTurnCommand.CommandKind
      or ResolveChoiceCommand.CommandKind))
    {
      return basicAdmission;
    }

    IGameCommand requestedCommand;
    try
    {
      requestedCommand = payload.ToCommand();
    }
    catch (InvalidOperationException ex)
    {
      return CommandAdmissionResult.Rejected(
      [
        new CommandValidationError(
          CommandValidationCode.IllegalCommand,
          payload.CommandSeq,
          ex.Message)
      ]);
    }

    if (requestedCommand is ResolveChoiceCommand resolveChoice)
    {
      return ValidateResolveChoice(payload.CommandSeq, resolveChoice, legalActionQuery)
        ?? basicAdmission;
    }

    var legalActions = _legalActionOracle.GetLegalActions(legalActionQuery);
    var matchingCommand = legalActions.Commands.Any(command => CommandsMatch(command, requestedCommand));
    if (matchingCommand)
    {
      return basicAdmission;
    }

    var diagnostics = legalActions.Diagnostics
      .Where(diagnostic => IsRelevantDiagnostic(requestedCommand, diagnostic))
      .ToArray();
    var errors = diagnostics.Length == 0
      ? CreateNoMatchingCommandErrors(payload.CommandSeq, requestedCommand, legalActions.Commands)
      : diagnostics
        .Select(diagnostic => new CommandValidationError(
          CommandValidationCode.IllegalCommand,
          payload.CommandSeq,
          FormatLegalDiagnostic(diagnostic)))
        .ToArray();

    return CommandAdmissionResult.Rejected(errors);
  }

  private static string FormatLegalDiagnostic(LegalActionDiagnostic diagnostic) =>
    $"{diagnostic.Code} path={diagnostic.Path} identifier={diagnostic.Identifier}: {diagnostic.Message}";

  private static bool IsRelevantDiagnostic(IGameCommand requestedCommand, LegalActionDiagnostic diagnostic) =>
    requestedCommand switch
    {
      PlayCardCommand playCard => IsPlayCardDiagnostic(playCard, diagnostic),
      ActivateLocationCommand activateLocation => IsActivateLocationDiagnostic(activateLocation, diagnostic),
      AttackCommand attack => IsAttackDiagnostic(attack, diagnostic),
      UseHeroPowerCommand useHeroPower => IsUseHeroPowerDiagnostic(useHeroPower, diagnostic),
      ConcedeCommand => IsCommandActionDiagnostic(ConcedeCommand.CommandKind, diagnostic)
        || IsPlayerContextDiagnostic(diagnostic),
      EndTurnCommand => IsCommandActionDiagnostic(EndTurnCommand.CommandKind, diagnostic)
        || IsPlayerContextDiagnostic(diagnostic),
      _ => false
    };

  private static bool IsPlayCardDiagnostic(PlayCardCommand command, LegalActionDiagnostic diagnostic) =>
    diagnostic.Path.StartsWith($"hand[{command.CardInstanceId}]", StringComparison.Ordinal)
    || IsCommandActionDiagnostic(PlayCardCommand.CommandKind, diagnostic)
    || StringComparer.Ordinal.Equals(diagnostic.Path, "rulesetProfile.limits.maxBoardSize")
    || StringComparer.Ordinal.Equals(diagnostic.Path, "rulesetProfile.limits.maxBoardSummonsPerAction")
    || IsPlayerContextDiagnostic(diagnostic);

  private static bool IsActivateLocationDiagnostic(ActivateLocationCommand command, LegalActionDiagnostic diagnostic) =>
    StringComparer.Ordinal.Equals(diagnostic.Path, "location")
    || diagnostic.Path.StartsWith("location.", StringComparison.Ordinal)
    || IsCommandActionDiagnostic(ActivateLocationCommand.CommandKind, diagnostic)
    || IsPlayerContextDiagnostic(diagnostic);

  private static bool IsAttackDiagnostic(AttackCommand command, LegalActionDiagnostic diagnostic) =>
    diagnostic.Path.StartsWith($"board[{command.AttackerEntityInstanceId}]", StringComparison.Ordinal)
    || IsCommandActionDiagnostic(AttackCommand.CommandKind, diagnostic)
    || IsPlayerContextDiagnostic(diagnostic);

  private static bool IsUseHeroPowerDiagnostic(UseHeroPowerCommand command, LegalActionDiagnostic diagnostic) =>
    StringComparer.Ordinal.Equals(diagnostic.Path, "heroPower")
    || diagnostic.Path.StartsWith("heroPower.", StringComparison.Ordinal)
    || IsCommandActionDiagnostic(UseHeroPowerCommand.CommandKind, diagnostic)
    || IsPlayerContextDiagnostic(diagnostic);

  private static bool IsCommandActionDiagnostic(string commandKind, LegalActionDiagnostic diagnostic) =>
    StringComparer.Ordinal.Equals(diagnostic.Identifier, commandKind);

  private static bool IsPlayerContextDiagnostic(LegalActionDiagnostic diagnostic) =>
    diagnostic.Code is "PLAYER_NOT_FOUND" or "NOT_ACTIVE_PLAYER" or "INVALID_PLAY_PHASE";

  private static IReadOnlyList<CommandValidationError> CreateNoMatchingCommandErrors(
    int commandSeq,
    IGameCommand requestedCommand,
    IReadOnlyList<IGameCommand> legalCommands)
  {
    var diagnostic = requestedCommand switch
    {
      PlayCardCommand playCard => DiagnosePlayCardMismatch(playCard, legalCommands),
      ActivateLocationCommand activateLocation => DiagnoseActivateLocationMismatch(activateLocation, legalCommands),
      AttackCommand attack => DiagnoseAttackMismatch(attack, legalCommands),
      UseHeroPowerCommand useHeroPower => DiagnoseUseHeroPowerMismatch(useHeroPower, legalCommands),
      ConcedeCommand concede => new LegalActionDiagnostic(
        "CONCEDE_NOT_LEGAL",
        "playerId",
        concede.PlayerId,
        "Requested CONCEDE command is not legal for the current state."),
      EndTurnCommand endTurn => new LegalActionDiagnostic(
        "END_TURN_NOT_LEGAL",
        "playerId",
        endTurn.PlayerId,
        "Requested END_TURN command is not legal for the current state."),
      ResolveChoiceCommand resolveChoice => new LegalActionDiagnostic(
        "RESOLVE_CHOICE_NOT_LEGAL",
        "refs.choiceId",
        resolveChoice.ChoiceId,
        "Requested RESOLVE_CHOICE command is not legal for the current state."),
      _ => new LegalActionDiagnostic(
        "COMMAND_NOT_LEGAL",
        "commandType",
        requestedCommand.Kind,
        "Requested command is not legal for the current state, catalog, profile, or capability context.")
    };

    return
    [
      new CommandValidationError(
        CommandValidationCode.IllegalCommand,
        commandSeq,
        FormatLegalDiagnostic(diagnostic))
    ];
  }

  private static LegalActionDiagnostic DiagnosePlayCardMismatch(
    PlayCardCommand requested,
    IReadOnlyList<IGameCommand> legalCommands)
  {
    var sameCard = legalCommands
      .OfType<PlayCardCommand>()
      .FirstOrDefault(command =>
        StringComparer.Ordinal.Equals(command.PlayerId, requested.PlayerId)
        && StringComparer.Ordinal.Equals(command.CardInstanceId, requested.CardInstanceId));
    if (sameCard is null)
    {
      return new LegalActionDiagnostic(
        "PLAY_CARD_NOT_LEGAL",
        "refs.cardInstanceId",
        requested.CardInstanceId,
        "Requested card is not legal to play for the current state, catalog, profile, or capability context.");
    }

    return new LegalActionDiagnostic(
      "PLAY_CARD_PAYLOAD_MISMATCH",
      "payload",
      requested.CardInstanceId,
      "Requested PLAY_CARD payload does not match the oracle-derived command for this card.");
  }

  private static LegalActionDiagnostic DiagnoseAttackMismatch(
    AttackCommand requested,
    IReadOnlyList<IGameCommand> legalCommands)
  {
    var sameAttacker = legalCommands
      .OfType<AttackCommand>()
      .Any(command =>
        StringComparer.Ordinal.Equals(command.PlayerId, requested.PlayerId)
        && StringComparer.Ordinal.Equals(command.AttackerEntityInstanceId, requested.AttackerEntityInstanceId));
    if (!sameAttacker)
    {
      return new LegalActionDiagnostic(
        "ATTACKER_NOT_LEGAL",
        "refs.attackerEntityInstanceId",
        requested.AttackerEntityInstanceId,
        "Requested attacker has no legal attack commands for the current state.");
    }

    return new LegalActionDiagnostic(
      "ATTACK_TARGET_NOT_LEGAL",
      "refs.targetEntityInstanceId",
      requested.TargetEntityInstanceId,
      "Requested attack target is not legal for the selected attacker.");
  }

  private static LegalActionDiagnostic DiagnoseActivateLocationMismatch(
    ActivateLocationCommand requested,
    IReadOnlyList<IGameCommand> legalCommands)
  {
    var sameLocation = legalCommands
      .OfType<ActivateLocationCommand>()
      .Any(command =>
        StringComparer.Ordinal.Equals(command.PlayerId, requested.PlayerId)
        && StringComparer.Ordinal.Equals(command.LocationEntityInstanceId, requested.LocationEntityInstanceId)
        && StringComparer.Ordinal.Equals(command.SourceCardInstanceId, requested.SourceCardInstanceId));
    if (!sameLocation)
    {
      return new LegalActionDiagnostic(
        "ACTIVATE_LOCATION_NOT_LEGAL",
        "refs.locationEntityInstanceId",
        requested.LocationEntityInstanceId,
        "Requested location is not legal for the current state, catalog, profile, or capability context.");
    }

    return new LegalActionDiagnostic(
      "ACTIVATE_LOCATION_PAYLOAD_MISMATCH",
      "refs.targetEntityInstanceId",
      requested.TargetEntityInstanceId,
      "Requested ACTIVATE_LOCATION target or payload does not match an oracle-derived command.");
  }

  private static LegalActionDiagnostic DiagnoseUseHeroPowerMismatch(
    UseHeroPowerCommand requested,
    IReadOnlyList<IGameCommand> legalCommands)
  {
    var samePower = legalCommands
      .OfType<UseHeroPowerCommand>()
      .Any(command =>
        StringComparer.Ordinal.Equals(command.PlayerId, requested.PlayerId)
        && StringComparer.Ordinal.Equals(command.SourceCardInstanceId, requested.SourceCardInstanceId));
    if (!samePower)
    {
      return new LegalActionDiagnostic(
        "USE_HERO_POWER_NOT_LEGAL",
        "refs.sourceCardInstanceId",
        requested.SourceCardInstanceId,
        "Requested hero power is not legal for the current state, catalog, profile, or capability context.");
    }

    return new LegalActionDiagnostic(
      "USE_HERO_POWER_PAYLOAD_MISMATCH",
      "refs.targetEntityInstanceId",
      requested.TargetEntityInstanceId,
      "Requested USE_HERO_POWER target or payload does not match an oracle-derived command.");
  }

  private static bool CommandsMatch(IGameCommand legal, IGameCommand requested) =>
    (legal, requested) switch
    {
      (PlayCardCommand left, PlayCardCommand right) =>
        StringComparer.Ordinal.Equals(left.PlayerId, right.PlayerId)
        && StringComparer.Ordinal.Equals(left.CardInstanceId, right.CardInstanceId)
        && StringComparer.Ordinal.Equals(left.EntityInstanceId, right.EntityInstanceId)
        && left.Attack == right.Attack
        && left.Health == right.Health
        && left.ManaCost == right.ManaCost
        && left.CostReduction == right.CostReduction
        && left.Durability == right.Durability
        && left.ArmorGain == right.ArmorGain
        && left.HeroPowerCost == right.HeroPowerCost
        && StringComparer.Ordinal.Equals(left.HeroPowerActivationTiming, right.HeroPowerActivationTiming)
        && StringComparer.Ordinal.Equals(left.TargetPlayerId,right.TargetPlayerId)
        && StringComparer.Ordinal.Equals(left.TargetEntityInstanceId,right.TargetEntityInstanceId)
        && left.MaxBoardSize == right.MaxBoardSize
        && (left.Keywords ?? []).SequenceEqual(right.Keywords ?? [], StringComparer.Ordinal),
      (ActivateLocationCommand left, ActivateLocationCommand right) =>
        StringComparer.Ordinal.Equals(left.PlayerId, right.PlayerId)
        && StringComparer.Ordinal.Equals(left.LocationEntityInstanceId, right.LocationEntityInstanceId)
        && StringComparer.Ordinal.Equals(left.SourceCardInstanceId, right.SourceCardInstanceId)
        && StringComparer.Ordinal.Equals(left.TargetPlayerId, right.TargetPlayerId)
        && StringComparer.Ordinal.Equals(left.TargetEntityInstanceId, right.TargetEntityInstanceId),
      (AttackCommand left, AttackCommand right) =>
        StringComparer.Ordinal.Equals(left.PlayerId, right.PlayerId)
        && StringComparer.Ordinal.Equals(left.AttackerEntityInstanceId, right.AttackerEntityInstanceId)
        && StringComparer.Ordinal.Equals(left.TargetPlayerId, right.TargetPlayerId)
        && StringComparer.Ordinal.Equals(left.TargetEntityInstanceId, right.TargetEntityInstanceId),
      (UseHeroPowerCommand left, UseHeroPowerCommand right) =>
        StringComparer.Ordinal.Equals(left.PlayerId, right.PlayerId)
        && StringComparer.Ordinal.Equals(left.SourceCardInstanceId, right.SourceCardInstanceId)
        && left.ManaCost == right.ManaCost
        && StringComparer.Ordinal.Equals(left.TargetPlayerId, right.TargetPlayerId)
        && StringComparer.Ordinal.Equals(left.TargetEntityInstanceId, right.TargetEntityInstanceId),
      (ConcedeCommand left, ConcedeCommand right) =>
        StringComparer.Ordinal.Equals(left.PlayerId, right.PlayerId),
      (EndTurnCommand left, EndTurnCommand right) =>
        StringComparer.Ordinal.Equals(left.PlayerId, right.PlayerId)
        && left.MaxHandSize == right.MaxHandSize,
      (ResolveChoiceCommand left, ResolveChoiceCommand right) =>
        StringComparer.Ordinal.Equals(left.PlayerId, right.PlayerId)
        && StringComparer.Ordinal.Equals(left.ChoiceId, right.ChoiceId)
        && StringComparer.Ordinal.Equals(left.SelectedOptionId, right.SelectedOptionId)
        && left.SelectedOptionIds.SequenceEqual(right.SelectedOptionIds, StringComparer.Ordinal)
        && StringComparer.Ordinal.Equals(left.CardInstanceId, right.CardInstanceId)
        && StringComparer.Ordinal.Equals(left.CardVersion, right.CardVersion)
        && left.CardType == right.CardType,
      _ => false
    };

  private static CommandAdmissionResult? ValidateResolveChoice(
    int commandSeq,
    ResolveChoiceCommand command,
    LegalActionQuery legalActionQuery)
  {
    if (!legalActionQuery.State.PendingChoices.TryGetValue(command.ChoiceId, out var choice))
    {
      return RejectChoice(commandSeq, "CHOICE_NOT_FOUND", "refs.choiceId", command.ChoiceId,
        "Requested choice is not pending in the current state.");
    }

    if (!StringComparer.Ordinal.Equals(choice.PlayerId, command.PlayerId))
    {
      return RejectChoice(commandSeq, "CHOICE_PLAYER_MISMATCH", "playerId", command.PlayerId,
        "Requested choice belongs to a different player.");
    }

    if (StringComparer.Ordinal.Equals(choice.ChoiceType, "EFFECT_TARGET"))
    {
      if (command.SelectedOptionIds.Count == 0)
      {
        return RejectChoice(commandSeq, "CHOICE_OPTION_NOT_FOUND", "refs.selectedOptionId", command.SelectedOptionId,
          "Requested choice option is not available for the pending choice.");
      }

      foreach (var optionId in command.SelectedOptionIds)
      {
        var targetOption = choice.Options.SingleOrDefault(option =>
          StringComparer.Ordinal.Equals(option.OptionId, optionId));
        if (targetOption is null)
        {
          return RejectChoice(commandSeq, "CHOICE_OPTION_NOT_FOUND", "refs.selectedOptionIds", optionId,
            "Requested choice option is not available for the pending choice.");
        }

        if (!StringComparer.Ordinal.Equals(targetOption.ItemKind, "TARGET"))
        {
          return RejectChoice(commandSeq, "CHOICE_OPTION_KIND_NOT_SUPPORTED", "refs.selectedOptionIds", targetOption.ItemKind,
            "Only target choice options can resolve an effect target choice.");
        }
      }

      return null;
    }

    if (!StringComparer.Ordinal.Equals(choice.ChoiceType, "DISCOVER"))
    {
      return RejectChoice(commandSeq, "CHOICE_TYPE_NOT_SUPPORTED", "refs.choiceId", choice.ChoiceType,
        "Only DISCOVER and EFFECT_TARGET choices can be resolved by the current runtime.");
    }

    var selected = choice.Options.SingleOrDefault(option =>
      StringComparer.Ordinal.Equals(option.OptionId, command.SelectedOptionId));
    if (selected is null)
    {
      return RejectChoice(commandSeq, "CHOICE_OPTION_NOT_FOUND", "refs.selectedOptionId", command.SelectedOptionId,
        "Requested choice option is not available for the pending choice.");
    }

    if (!StringComparer.Ordinal.Equals(selected.ItemKind, "CARD"))
    {
      return RejectChoice(commandSeq, "CHOICE_OPTION_KIND_NOT_SUPPORTED", "refs.selectedOptionId", selected.ItemKind,
        "Only card choice options can be materialized by the current runtime.");
    }

    if (choice.ResultZone is not ("HAND" or "DECK" or "SET_ASIDE"))
    {
      return RejectChoice(commandSeq, "CHOICE_RESULT_ZONE_NOT_SUPPORTED", "refs.choiceId", choice.ResultZone,
        "Requested choice result zone is not supported by the current runtime.");
    }

    if (CardInstanceExists(legalActionQuery.State, command.CardInstanceId))
    {
      return RejectChoice(commandSeq, "CARD_INSTANCE_ALREADY_EXISTS", "refs.cardInstanceId", command.CardInstanceId,
        "Requested card instance id already exists in the current state.");
    }

    return null;
  }

  private static CommandAdmissionResult RejectChoice(
    int commandSeq,
    string code,
    string path,
    string identifier,
    string message)
  {
    var diagnostic = new LegalActionDiagnostic(code, path, identifier, message);
    return CommandAdmissionResult.Rejected(
    [
      new CommandValidationError(
        CommandValidationCode.IllegalCommand,
        commandSeq,
        FormatLegalDiagnostic(diagnostic))
    ]);
  }

  private static bool CardInstanceExists(IGameState state, string cardInstanceId) =>
    state.Players.Values.Any(player => player.Zones.Deck
      .Concat(player.Zones.Hand)
      .Concat(player.Zones.Graveyard)
      .Concat(player.Zones.SetAside)
      .Any(card => StringComparer.Ordinal.Equals(card.CardInstanceId, cardInstanceId)));
}
