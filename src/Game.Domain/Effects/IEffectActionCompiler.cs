namespace Game.Domain.Effects;

public interface IEffectActionCompiler
{
  EffectActionCompilationResult Compile(EffectActionCompilationRequest request);
}
