using Game.Domain.Commands;
using Game.Domain.Mutation;
using Game.Domain.State;
using Game.Shared.Primitives;

namespace Game.Domain.Actions;

public sealed class ResolveChoiceActionExecutor : IActionExecutor
{
  public const string SupportedActionType = "RESOLVE_CHOICE";

  public string ActionType => SupportedActionType;

  public bool CanExecute(ActionExecutionContext context)
  {
    ArgumentNullException.ThrowIfNull(context);

    if (context.Command is not ResolveChoiceCommand command
      || context.CommandSeq <= 0
      || string.IsNullOrWhiteSpace(context.PreStateHash))
    {
      return false;
    }

    if (TryGetSelectedEffectTargetOptions(context.State, command, out _))
    {
      return true;
    }

    return TryGetSelectedCardOption(context.State, command, out _)
      && !context.State.Players.Values.Any(player => PlayerHasCard(player, command.CardInstanceId))
      && !string.IsNullOrWhiteSpace(command.CardVersion);
  }

  public ProposedMutationBatch Execute(ActionExecutionContext context)
  {
    if (context.Command is not ResolveChoiceCommand command)
    {
      throw new InvalidOperationException(
        $"Action '{ActionType}' requires a {nameof(ResolveChoiceCommand)}.");
    }

    if (!CanExecute(context))
    {
      throw new InvalidOperationException(
        $"Cannot execute action '{ActionType}' for command '{context.Command.Kind}'.");
    }

    if (TryGetSelectedEffectTargetOptions(context.State, command, out _))
    {
      var phase = ResolveEffectTargetPhase(context.State, command);
      return new ProposedMutationBatch(
        $"batch:{context.CommandSeq}:resolve-choice:{command.ChoiceId}",
        context.CommandSeq,
        null,
        phase,
        $"command:{context.Command.Kind}",
        "RESOLVE_CHOICE_ACTION_EXECUTOR",
        [
          new StateMutation(
            $"mutation:{context.CommandSeq}:resolve-choice:{command.ChoiceId}",
            ResolveChoiceMutationHandler.SupportedMutationType,
            $"command:{context.Command.Kind}",
            command.PlayerId,
            new SortedDictionary<string, int>(StringComparer.Ordinal),
            phase,
            context.CommandSeq,
            null,
            $"resolve-choice:{command.ChoiceId}:0001",
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
              ["choiceId"] = command.ChoiceId,
              ["playerId"] = command.PlayerId,
              ["selectedOptionId"] = command.SelectedOptionId,
              ["selectedOptionIds"] = string.Join(",", command.SelectedOptionIds)
            })
        ],
        context.PreStateHash,
        null,
        null);
    }

    if (!TryGetSelectedCardOption(context.State, command, out var selectedOption))
    {
      throw new InvalidOperationException(
        $"Cannot execute action '{ActionType}' for command '{context.Command.Kind}'.");
    }

    var resultZone = context.State.PendingChoices[command.ChoiceId].ResultZone;
    return new ProposedMutationBatch(
      $"batch:{context.CommandSeq}:resolve-choice:{command.ChoiceId}",
      context.CommandSeq,
      null,
      "ON_PLAY",
      $"command:{context.Command.Kind}",
      "RESOLVE_CHOICE_ACTION_EXECUTOR",
      [
        new StateMutation(
          $"mutation:{context.CommandSeq}:resolve-choice:{command.ChoiceId}",
          ResolveChoiceMutationHandler.SupportedMutationType,
          $"command:{context.Command.Kind}",
          command.PlayerId,
          new SortedDictionary<string, int>(StringComparer.Ordinal),
          "ON_PLAY",
          context.CommandSeq,
          null,
          $"resolve-choice:{command.ChoiceId}:0001",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["choiceId"] = command.ChoiceId,
            ["playerId"] = command.PlayerId,
            ["selectedOptionId"] = command.SelectedOptionId
          }),
        new StateMutation(
          $"mutation:{context.CommandSeq}:resolve-choice:{command.ChoiceId}:create-card",
          CreateCardInstanceMutationHandler.SupportedMutationType,
          $"command:{context.Command.Kind}",
          command.CardInstanceId,
          new SortedDictionary<string, int>(StringComparer.Ordinal),
          "ON_PLAY",
          context.CommandSeq,
          null,
          $"resolve-choice:{command.ChoiceId}:0002",
          new SortedDictionary<string, string>(StringComparer.Ordinal)
          {
            ["cardId"] = selectedOption.ItemId,
            ["cardInstanceId"] = command.CardInstanceId,
            ["cardType"] = ToCardTypeId(command.CardType),
            ["cardVersion"] = command.CardVersion,
            ["fromZone"] = "GENERATED",
            ["playerId"] = command.PlayerId,
            ["selectedOptionId"] = command.SelectedOptionId,
            ["toZone"] = resultZone
          })
      ],
      context.PreStateHash,
      null,
      null);
  }

  private static bool TryGetSelectedCardOption(
    IGameState state,
    ResolveChoiceCommand command,
    out ChoiceOption selectedOption)
  {
    selectedOption = default!;
    if (!state.PendingChoices.TryGetValue(command.ChoiceId, out var choice)
      || !StringComparer.Ordinal.Equals(choice.PlayerId, command.PlayerId)
      || !StringComparer.Ordinal.Equals(choice.ChoiceType, "DISCOVER")
      || !IsSupportedResultZone(choice.ResultZone))
    {
      return false;
    }

    selectedOption = choice.Options.SingleOrDefault(option =>
      StringComparer.Ordinal.Equals(option.OptionId, command.SelectedOptionId))!;
    return selectedOption is not null
      && StringComparer.Ordinal.Equals(selectedOption.ItemKind, "CARD")
      && !string.IsNullOrWhiteSpace(command.CardInstanceId);
  }

  private static bool TryGetSelectedEffectTargetOptions(
    IGameState state,
    ResolveChoiceCommand command,
    out IReadOnlyList<ChoiceOption> selectedOptions)
  {
    selectedOptions = [];
    if (!state.PendingChoices.TryGetValue(command.ChoiceId, out var choice)
      || !StringComparer.Ordinal.Equals(choice.PlayerId, command.PlayerId)
      || !StringComparer.Ordinal.Equals(choice.ChoiceType, "EFFECT_TARGET"))
    {
      return false;
    }

    var options = new List<ChoiceOption>();
    foreach (var optionId in command.SelectedOptionIds)
    {
      var option = choice.Options.SingleOrDefault(candidate =>
        StringComparer.Ordinal.Equals(candidate.OptionId, optionId));
      if (option is null || !StringComparer.Ordinal.Equals(option.ItemKind, "TARGET"))
      {
        return false;
      }

      options.Add(option);
    }

    selectedOptions = options;
    return selectedOptions.Count > 0;
  }

  private static string ResolveEffectTargetPhase(IGameState state, ResolveChoiceCommand command)
  {
    if (!state.PendingChoices.TryGetValue(command.ChoiceId, out var choice))
    {
      return "ON_PLAY";
    }

    var sourceParts = choice.SourceRef.Split('|');
    if (sourceParts.Length >= 2
      && StringComparer.Ordinal.Equals(sourceParts[0], "effect-choice-v1"))
    {
      return sourceParts[1] switch
      {
        "DEATHRATTLE" => "DEATHRATTLE_PHASE",
        "QUEST" => "AFTER_PLAY",
        _ => "ON_PLAY"
      };
    }

    return "ON_PLAY";
  }

  private static bool PlayerHasCard(PlayerState player, string cardInstanceId) =>
    player.Zones.Deck
      .Concat(player.Zones.Hand)
      .Concat(player.Zones.Graveyard)
      .Concat(player.Zones.SetAside)
      .Any(card => StringComparer.Ordinal.Equals(card.CardInstanceId, cardInstanceId));

  private static bool IsSupportedResultZone(string zone) =>
    zone is "HAND" or "DECK" or "SET_ASIDE";

  private static string ToCardTypeId(CardType cardType) =>
    cardType switch
    {
      CardType.MinionCard => "MINION_CARD",
      CardType.SpellCard => "SPELL_CARD",
      CardType.WeaponCard => "WEAPON_CARD",
      CardType.LocationCard => "LOCATION_CARD",
      CardType.HeroCard => "HERO_CARD",
      CardType.HeroPowerCard => "HERO_POWER_CARD",
      CardType.TokenCard => "TOKEN_CARD",
      _ => cardType.ToString().ToUpperInvariant()
    };
}
