namespace Game.Generator.Validation;

public interface IGeneratedCardValidationStage
{
  GeneratedCardValidationStageKind Stage { get; }

  GeneratedCardValidationStageReport Validate(GeneratedCardValidationRequest request);
}
