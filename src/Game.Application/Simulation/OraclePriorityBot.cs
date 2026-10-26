using Game.Application.Legality;
using Game.Domain.Commands;
using Game.Domain.Randomness;

namespace Game.Application.Simulation;

public sealed class OraclePriorityBot(
  ILegalActionOracle? legalActionOracle = null) : IBotAgent
{
  private readonly ILegalActionOracle _legalActionOracle = legalActionOracle ?? new LegalActionOracle();

  public BotDecision Decide(BotDecisionContext context)
  {
    ArgumentNullException.ThrowIfNull(context);

    var legalActions = _legalActionOracle.GetLegalActions(new LegalActionQuery(
      context.State,
      context.PlayerId,
      context.CardVersionsByVersionId,
      context.RulesetProfile,
      context.EngineCapabilities));

    var candidates = HighestPriorityCandidates(legalActions, context.PlayerId);
    if (candidates.Count == 0)
    {
      return new BotDecision(new EndTurnCommand(context.PlayerId));
    }

    if (context.BotRngRoller is null || candidates.Count == 1)
    {
      return new BotDecision(candidates[0]);
    }

    var selection = context.BotRngRoller.SelectOne(
      candidates,
      RngStreamKind.BotDecision,
      "BOT_COMMAND_SELECTION",
      context.CommandSeq,
      $"bot:{context.PlayerId}:command");
    return new BotDecision(selection.Selected, selection.Roll);
  }

  private static IReadOnlyList<IGameCommand> HighestPriorityCandidates(
    LegalActionResult legalActions,
    string playerId)
  {
    var playCards = legalActions.Commands
      .OfType<PlayCardCommand>()
      .OrderBy(command => command.CardInstanceId, StringComparer.Ordinal)
      .Cast<IGameCommand>()
      .ToArray();
    if (playCards.Length > 0)
    {
      return playCards;
    }

    var heroPowers = legalActions.Commands
      .OfType<UseHeroPowerCommand>()
      .OrderBy(command => command.SourceCardInstanceId, StringComparer.Ordinal)
      .ThenBy(command => command.TargetPlayerId, StringComparer.Ordinal)
      .ThenBy(command => command.TargetEntityInstanceId, StringComparer.Ordinal)
      .Cast<IGameCommand>()
      .ToArray();
    if (heroPowers.Length > 0)
    {
      return heroPowers;
    }

    var attacks = legalActions.Commands
      .OfType<AttackCommand>()
      .OrderBy(command => IsHeroTarget(command.TargetEntityInstanceId) ? 0 : 1)
      .ThenBy(command => command.AttackerEntityInstanceId, StringComparer.Ordinal)
      .ThenBy(command => command.TargetPlayerId, StringComparer.Ordinal)
      .ThenBy(command => command.TargetEntityInstanceId, StringComparer.Ordinal)
      .Cast<IGameCommand>()
      .ToArray();
    if (attacks.Length > 0)
    {
      return attacks;
    }

    var endTurn = legalActions.Commands
      .OfType<EndTurnCommand>()
      .Where(command => StringComparer.Ordinal.Equals(command.PlayerId, playerId))
      .Cast<IGameCommand>()
      .ToArray();
    if (endTurn.Length > 0)
    {
      return endTurn;
    }

    return legalActions.Commands
      .OfType<ConcedeCommand>()
      .Where(command => StringComparer.Ordinal.Equals(command.PlayerId, playerId))
      .Cast<IGameCommand>()
      .ToArray();
  }

  private static bool IsHeroTarget(string entityInstanceId) =>
    entityInstanceId.StartsWith("hero-", StringComparison.Ordinal)
    || entityInstanceId.StartsWith("hero:", StringComparison.Ordinal);
}
