using Game.Domain.Mutation;
using Game.Shared.Events;

using Game.Domain.State;

namespace Game.Domain.Replacements;

public sealed record ReplacementResolutionRequest(
  IGameState State,
  ProposedMutationBatch ProposedBatch,
  IReadOnlyList<ProposedEventRecord> ProposedEvents,
  ReplacementResolutionPolicy? Policy = null);
