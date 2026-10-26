using Game.Domain.Actions;
using Game.Domain.Commands;
using Game.Domain.Hashing;
using Game.Domain.Mutation;
using Game.Domain.Serialization;
using Game.Domain.State;
using Xunit;

namespace Game.Domain.Tests;

public sealed class ActionExecutorTests
{
  private readonly IStateHasher _hasher = new StateHasher(new CanonicalJsonSerializer());

  [Fact]
  public void CommandCounterActionExecutorReturnsProposedBatchWithoutMutatingState()
  {
    var state = new InMemoryGameState();
    var executor = new CommandCounterActionExecutor();
    var context = new ActionExecutionContext(
      state,
      new NoOpCommand("draw"),
      1,
      _hasher.HashGameplayState(state));

    var batch = executor.Execute(context);

    Assert.Equal(0, state.Revision);
    Assert.Equal("batch:1:draw", batch.BatchId);
    Assert.Equal(1, batch.CommandSeq);
    Assert.Equal(_hasher.HashGameplayState(state), batch.PreStateHash);
    Assert.Single(batch.Mutations);
    Assert.DoesNotContain(
      "PostStateHash",
      typeof(ProposedMutationBatch).GetProperties().Select(property => property.Name));
  }

  [Fact]
  public void ActionExecutorRegistryRejectsDuplicateActionTypes()
  {
    Assert.Throws<InvalidOperationException>(() =>
      new ActionExecutorRegistry(
      [
        new CommandCounterActionExecutor(),
        new CommandCounterActionExecutor()
      ]));
  }

  [Fact]
  public void ActionExecutorRegistryRejectsMissingActionType()
  {
    var registry = new ActionExecutorRegistry([new CommandCounterActionExecutor()]);

    Assert.Throws<InvalidOperationException>(() => registry.GetRequiredExecutor("MISSING_ACTION"));
  }
}
