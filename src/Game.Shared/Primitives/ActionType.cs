using System.Text.Json.Serialization;

namespace Game.Shared.Primitives;

[JsonConverter(typeof(JsonStringEnumConverter<ActionType>))]
public enum ActionType
{
  [JsonStringEnumMemberName("DEAL_DAMAGE")]
  DealDamage,

  [JsonStringEnumMemberName("DAMAGE_DISTRIBUTION")]
  DamageDistribution,

  [JsonStringEnumMemberName("DRAW_CARD")]
  DrawCard,

  [JsonStringEnumMemberName("HEAL")]
  Heal,

  [JsonStringEnumMemberName("FULLY_HEAL")]
  FullyHeal,

  [JsonStringEnumMemberName("DISCARD_CARD")]
  DiscardCard,

  [JsonStringEnumMemberName("SUMMON")]
  Summon,

  [JsonStringEnumMemberName("ADD_CARD_TO_HAND")]
  AddCardToHand,

  [JsonStringEnumMemberName("RETURN_TO_HAND")]
  ReturnToHand,

  [JsonStringEnumMemberName("RANDOM_SELECT")]
  RandomSelect,

  [JsonStringEnumMemberName("APPLY_MODIFIER")]
  ApplyModifier,

  [JsonStringEnumMemberName("APPLY_AURA")]
  ApplyAura,

  [JsonStringEnumMemberName("PREVENT_DAMAGE")]
  PreventDamage,

  [JsonStringEnumMemberName("REPLACE_EVENT")]
  ReplaceEvent,

  [JsonStringEnumMemberName("DISCOVER")]
  Discover,

  [JsonStringEnumMemberName("RESURRECT")]
  Resurrect,

  [JsonStringEnumMemberName("SHUFFLE_INTO_DECK")]
  ShuffleIntoDeck,

  [JsonStringEnumMemberName("TAKE_EXTRA_TURN")]
  TakeExtraTurn
}
