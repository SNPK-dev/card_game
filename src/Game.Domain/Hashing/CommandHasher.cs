using System.Security.Cryptography;
using Game.Domain.Commands;
using Game.Domain.Serialization;

namespace Game.Domain.Hashing;

public sealed class CommandHasher(ICanonicalSerializer serializer) : ICommandHasher
{
  public string HashCommand(DeterministicCommandPayload command)
  {
    ArgumentNullException.ThrowIfNull(command);

    var projection = new CommandHashProjection(
      command.CommandSeq,
      command.PlayerId,
      command.CommandType,
      command.Payload
        .OrderBy(entry => entry.Key, StringComparer.Ordinal)
        .Select(entry => new CommandPayloadHashProjection(entry.Key, entry.Value))
        .ToArray(),
      command.Refs
        .OrderBy(entry => entry.Key, StringComparer.Ordinal)
        .Select(entry => new CommandRefHashProjection(entry.Key, entry.Value))
        .ToArray(),
      command.ExpectedPreStateHash,
      command.CommandCategory.ToString(),
      command.DebugReplayFlag);

    return HashCanonicalBytes(serializer.SerializeToCanonicalBytes(projection));
  }

  private static string HashCanonicalBytes(byte[] canonicalBytes)
  {
    var hash = SHA256.HashData(canonicalBytes);
    return Convert.ToHexString(hash);
  }

  private sealed record CommandHashProjection(
    int CommandSeq,
    string? PlayerId,
    string CommandType,
    IReadOnlyList<CommandPayloadHashProjection> Payload,
    IReadOnlyList<CommandRefHashProjection> Refs,
    string? ExpectedPreStateHash,
    string CommandCategory,
    bool DebugReplayFlag);

  private sealed record CommandPayloadHashProjection(
    string Key,
    int Value);

  private sealed record CommandRefHashProjection(
    string Key,
    string Value);
}
