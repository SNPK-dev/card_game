namespace Game.Generator.Validation;

public sealed class GeneratedCardValidationPipeline
{
  private readonly IReadOnlyList<IGeneratedCardValidationStage> stages;

  public GeneratedCardValidationPipeline(IEnumerable<IGeneratedCardValidationStage> stages)
  {
    ArgumentNullException.ThrowIfNull(stages);
    this.stages = stages.ToArray();

    if (this.stages.Count == 0)
    {
      throw new ArgumentException("At least one validation stage is required.", nameof(stages));
    }
  }

  public static GeneratedCardValidationPipeline CreateDefault() =>
    new(
      [
        new SchemaValidationStage(),
        new SemanticValidationStage(),
        new RuleValidationStage(new GeneratedCardCandidateValidator()),
        new StaticAnalysisValidationStage(),
        new BalanceValidationStage(),
        new SimulationValidationStage(),
        new ReplayValidationStage()
      ]);

  public GeneratedCardValidationPipelineReport Validate(GeneratedCardValidationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var reports = stages
      .Select(stage => stage.Validate(request))
      .ToArray();

    var finalDecision = reports.Any(report => report.Decision is GeneratedCardValidationDecision.Rejected)
      ? GeneratedCardValidationDecision.Rejected
      : GeneratedCardValidationDecision.Accepted;

    return new GeneratedCardValidationPipelineReport(
      request.GenerationId,
      request.PipelineVersion,
      request.ContextVersion,
      request.Context.RulesetProfileId,
      request.Context.RulesetProfileHash,
      finalDecision,
      reports);
  }
}
