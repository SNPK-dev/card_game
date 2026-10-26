namespace Game.Domain.State;

public sealed record PlayerState(
  string PlayerId,
  PlayerZones Zones,
  EntityInstance? Hero,
  int Mana,
  int MaxMana,
  int Armor,
  int FatigueCounter,
  IReadOnlyList<PlayerModifier> Modifiers,
  EntityInstance? Weapon = null,
  EntityInstance? Location = null,
  HeroPowerState? HeroPower = null,
  IReadOnlyList<PlayerQuest>? Quests = null,
  IReadOnlyList<PlayerSecret>? Secrets = null,
  IReadOnlyList<PlayerAura>? Auras = null)
{
  public static PlayerState Empty(string playerId)
  {
    return new PlayerState(playerId, PlayerZones.Empty, null, 0, 0, 0, 0, [], null, null, null, [], [], []);
  }
}
