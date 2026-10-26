namespace Game.Domain.Pooling;

public interface IPoolResolver
{
  PoolResolutionResult Resolve(PoolResolutionRequest request);
}
