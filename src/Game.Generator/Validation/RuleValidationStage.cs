namespace Game.Generator.Validation;

public sealed class RuleValidationStage(GeneratedCardCandidateValidator validator) : IGeneratedCardValidationStage
{
  public GeneratedCardValidationStageKind Stage => GeneratedCardValidationStageKind.Rule;

  public GeneratedCardValidationStageReport Validate(GeneratedCardValidationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var result = validator.Validate(request.Candidate, request.Context);

    return new GeneratedCardValidationStageReport(
      Stage,
      result.IsValid ? GeneratedCardValidationDecision.Accepted : GeneratedCardValidationDecision.Rejected,
      result.Diagnostics);
  }
}
