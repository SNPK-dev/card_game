using Game.Domain.Commands;

namespace Game.Domain.Replay;

public sealed class CanonicalCommandLog
{
  private readonly DeterministicCommandPayload[] _payloads;

  public CanonicalCommandLog(IEnumerable<DeterministicCommandPayload> payloads)
  {
    ArgumentNullException.ThrowIfNull(payloads);

    _payloads = payloads
      .OrderBy(payload => payload.CommandSeq)
      .ToArray();

    ValidateContiguousCommandSequence(_payloads);
  }

  public IReadOnlyList<DeterministicCommandPayload> Payloads => _payloads;

  private static void ValidateContiguousCommandSequence(
    IReadOnlyList<DeterministicCommandPayload> payloads)
  {
    for (var index = 0; index < payloads.Count; index++)
    {
      var expectedSeq = index + 1;
      var actualSeq = payloads[index].CommandSeq;

      if (actualSeq != expectedSeq)
      {
        throw new ArgumentException(
          $"Canonical command log must have contiguous commandSeq values starting at 1. Expected {expectedSeq}, got {actualSeq}.",
          nameof(payloads));
      }
    }
  }
}
