using Game.Domain.Commands;
using Xunit;

namespace Game.Domain.Tests;

public sealed class CommandValidationTests
{
  private readonly DeterministicCommandValidator _validator = new();

  [Fact]
  public void ValidatorAcceptsStructurallyValidPlayerCommand()
  {
    var payload = new DeterministicCommandPayload(1, "player-1", "draw");

    var result = _validator.Validate(payload, CommandValidationOptions.Production);

    Assert.True(result.IsAccepted);
    Assert.Empty(result.Errors);
  }

  [Theory]
  [MemberData(nameof(InvalidPayloads))]
  public void ValidatorRejectsStructuralCommandErrors(
    DeterministicCommandPayload payload,
    CommandValidationCode expectedCode)
  {
    var result = _validator.Validate(payload, CommandValidationOptions.Production);

    Assert.False(result.IsAccepted);
    Assert.Contains(result.Errors, error => error.Code == expectedCode);
  }

  [Fact]
  public void ValidatorAllowsSystemCommandWithoutPlayerId()
  {
    var payload = new DeterministicCommandPayload(
      1,
      null,
      "START_GAME",
      commandCategory: CommandCategory.System);

    var result = _validator.Validate(payload, CommandValidationOptions.Production);

    Assert.True(result.IsAccepted);
  }

  [Fact]
  public void ValidatorAllowsFlaggedDebugCommandOnlyWhenDebugReplayIsAllowed()
  {
    var payload = new DeterministicCommandPayload(
      1,
      "admin",
      "SET_STATE",
      commandCategory: CommandCategory.Debug,
      debugReplayFlag: true);

    var production = _validator.Validate(payload, CommandValidationOptions.Production);
    var debugReplay = _validator.Validate(payload, CommandValidationOptions.DebugReplay);

    Assert.False(production.IsAccepted);
    Assert.Contains(production.Errors, error => error.Code == CommandValidationCode.DebugCommandNotAllowed);
    Assert.True(debugReplay.IsAccepted);
  }

  [Fact]
  public void PlayCardPayloadCanMaterializeSpellCommandWithoutEntityRef()
  {
    var payload = new DeterministicCommandPayload(
      1,
      "player-1",
      PlayCardCommand.CommandKind,
      payload: new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["manaCost"] = 2
      },
      refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["cardInstanceId"] = "spell-instance-1"
      });

    var command = Assert.IsType<PlayCardCommand>(payload.ToCommand());

    Assert.Equal("player-1", command.PlayerId);
    Assert.Equal("spell-instance-1", command.CardInstanceId);
    Assert.Empty(command.EntityInstanceId);
    Assert.Equal(0, command.Attack);
    Assert.Equal(0, command.Health);
    Assert.Equal(2, command.ManaCost);
  }

  [Fact]
  public void PlayCardPayloadPreservesChosenBattlecryTarget()
  {
    var payload=new DeterministicCommandPayload(1,"player-1",PlayCardCommand.CommandKind,refs:new SortedDictionary<string,string>(StringComparer.Ordinal){["cardInstanceId"]="spark-herald",["entityInstanceId"]="spark-entity",["targetPlayerId"]="player-2",["targetEntityInstanceId"]="enemy-minion"});
    var command=Assert.IsType<PlayCardCommand>(payload.ToCommand());
    Assert.Equal("player-2",command.TargetPlayerId);Assert.Equal("enemy-minion",command.TargetEntityInstanceId);
  }

  [Fact]
  public void PlayCardPayloadCanMaterializeWeaponCommandWithDurability()
  {
    var payload = new DeterministicCommandPayload(
      1,
      "player-1",
      PlayCardCommand.CommandKind,
      payload: new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["attack"] = 3,
        ["durability"] = 2,
        ["manaCost"] = 4
      },
      refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["cardInstanceId"] = "weapon-instance-1",
        ["entityInstanceId"] = "weapon:weapon-instance-1"
      });

    var command = Assert.IsType<PlayCardCommand>(payload.ToCommand());

    Assert.Equal("weapon-instance-1", command.CardInstanceId);
    Assert.Equal("weapon:weapon-instance-1", command.EntityInstanceId);
    Assert.Equal(3, command.Attack);
    Assert.Equal(0, command.Health);
    Assert.Equal(2, command.Durability);
    Assert.Equal(4, command.ManaCost);
  }

  [Fact]
  public void PlayCardPayloadCanMaterializeLocationCommandWithDurability()
  {
    var payload = new DeterministicCommandPayload(
      1,
      "player-1",
      PlayCardCommand.CommandKind,
      payload: new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["durability"] = 3,
        ["manaCost"] = 2
      },
      refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["cardInstanceId"] = "location-instance-1",
        ["entityInstanceId"] = "location:location-instance-1"
      });

    var command = Assert.IsType<PlayCardCommand>(payload.ToCommand());

    Assert.Equal("location-instance-1", command.CardInstanceId);
    Assert.Equal("location:location-instance-1", command.EntityInstanceId);
    Assert.Equal(0, command.Attack);
    Assert.Equal(0, command.Health);
    Assert.Equal(3, command.Durability);
    Assert.Equal(2, command.ManaCost);
  }

  [Fact]
  public void PlayCardPayloadCanMaterializeHeroCardCommandWithHeroPowerReplacement()
  {
    var payload = new DeterministicCommandPayload(
      1,
      "player-1",
      PlayCardCommand.CommandKind,
      payload: new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["armorGain"] = 5,
        ["heroPowerCost"] = 2,
        ["manaCost"] = 7
      },
      refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["cardInstanceId"] = "hero-card-instance-1",
        ["heroPowerActivationTiming"] = "MAIN_PHASE"
      });

    var command = Assert.IsType<PlayCardCommand>(payload.ToCommand());

    Assert.Equal("hero-card-instance-1", command.CardInstanceId);
    Assert.Empty(command.EntityInstanceId);
    Assert.Equal(5, command.ArmorGain);
    Assert.Equal(2, command.HeroPowerCost);
    Assert.Equal("MAIN_PHASE", command.HeroPowerActivationTiming);
    Assert.Equal(7, command.ManaCost);
  }

  [Fact]
  public void UseHeroPowerPayloadCanMaterializeCommandWithTargetRefs()
  {
    var payload = new DeterministicCommandPayload(
      1,
      "player-1",
      UseHeroPowerCommand.CommandKind,
      payload: new SortedDictionary<string, int>(StringComparer.Ordinal)
      {
        ["manaCost"] = 2
      },
      refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["sourceCardInstanceId"] = "hero-card-instance-1",
        ["targetEntityInstanceId"] = "hero-player-2",
        ["targetPlayerId"] = "player-2"
      });

    var command = Assert.IsType<UseHeroPowerCommand>(payload.ToCommand());

    Assert.Equal("player-1", command.PlayerId);
    Assert.Equal("hero-card-instance-1", command.SourceCardInstanceId);
    Assert.Equal(2, command.ManaCost);
    Assert.Equal("player-2", command.TargetPlayerId);
    Assert.Equal("hero-player-2", command.TargetEntityInstanceId);
  }

  [Fact]
  public void ResolveChoicePayloadCanMaterializeCanonicalSelectedOptionList()
  {
    var payload = new DeterministicCommandPayload(
      1,
      "player-1",
      ResolveChoiceCommand.CommandKind,
      refs: new SortedDictionary<string, string>(StringComparer.Ordinal)
      {
        ["cardInstanceId"] = "effect-target-placeholder-card",
        ["cardType"] = "SPELL_CARD",
        ["cardVersion"] = "effect_target_placeholder_v1",
        ["choiceId"] = "choice-effect-target",
        ["selectedOptionId"] = "choice-effect-target:option:0002",
        ["selectedOptionIds"] = "choice-effect-target:option:0002,choice-effect-target:option:0001,choice-effect-target:option:0001"
      },
      commandCategory: CommandCategory.Choice);

    var command = Assert.IsType<ResolveChoiceCommand>(payload.ToCommand());

    Assert.Equal("choice-effect-target:option:0002", command.SelectedOptionId);
    Assert.Equal(
      ["choice-effect-target:option:0001", "choice-effect-target:option:0002"],
      command.SelectedOptionIds);
  }

  public static IEnumerable<object[]> InvalidPayloads()
  {
    yield return
    [
      new DeterministicCommandPayload(0, "player-1", "draw"),
      CommandValidationCode.InvalidCommandSequence
    ];

    yield return
    [
      new DeterministicCommandPayload(1, "player-1", ""),
      CommandValidationCode.MissingCommandType
    ];

    yield return
    [
      new DeterministicCommandPayload(1, null, "draw"),
      CommandValidationCode.MissingPlayerId
    ];

    yield return
    [
      new DeterministicCommandPayload(
        1,
        "player-1",
        "draw",
        debugReplayFlag: true),
      CommandValidationCode.DebugReplayFlagNotAllowed
    ];
  }
}
