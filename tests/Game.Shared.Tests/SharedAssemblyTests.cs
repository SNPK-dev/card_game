using Game.Shared;
using Xunit;

namespace Game.Shared.Tests;

public sealed class SharedAssemblyTests
{
  [Fact]
  public void SharedAssemblyMarkerIsAvailable()
  {
    Assert.Equal("Game.Shared", typeof(AssemblyMarker).Namespace);
  }
}
