using Game.Application.Execution;
using Game.Domain.Events;
using Game.Domain.Mutation;
using Xunit;

namespace Game.Application.Tests;

public sealed class ApplicationAssemblyTests
{
  [Fact]
  public void DeterministicExecutorCanBeConstructed()
  {
    var executor = new DeterministicCommandExecutor(new StateMutator(), new DerivedEventLog());

    Assert.NotNull(executor);
  }
}
