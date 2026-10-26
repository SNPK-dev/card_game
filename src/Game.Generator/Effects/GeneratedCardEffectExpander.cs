using Game.Generator.Validation;
using Game.Shared.Dsl;

namespace Game.Generator.Effects;

public sealed class GeneratedCardEffectExpander
{
  public GeneratedCardEffectExpansionResult Expand(GeneratedCardEffectExpansionRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.Candidate);
    ArgumentNullException.ThrowIfNull(request.Context);
    ArgumentNullException.ThrowIfNull(request.Effect);

    var diagnostics = new List<GeneratedCardEffectExpansionDiagnostic>();
    if (string.IsNullOrWhiteSpace(request.ExpansionId))
    {
      diagnostics.Add(new GeneratedCardEffectExpansionDiagnostic(
        "EFFECT_EXPANSION_ID_REQUIRED",
        "expansionId",
        string.Empty,
        "Effect expansion id is required."));
    }

    if (request.Candidate.Effects.Count + 1 > request.Context.Limits.MaxEffectsPerCard)
    {
      diagnostics.Add(new GeneratedCardEffectExpansionDiagnostic(
        "EFFECTS_PER_CARD_LIMIT_EXCEEDED",
        "card.effects",
        (request.Candidate.Effects.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        "Adding this generated effect would exceed the generation context effect limit."));
    }

    if (request.Effect.Actions.Count > request.Context.Limits.MaxActionsPerEffect)
    {
      diagnostics.Add(new GeneratedCardEffectExpansionDiagnostic(
        "ACTIONS_PER_EFFECT_LIMIT_EXCEEDED",
        "effect.actions",
        request.Effect.Actions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "Generated effect action count exceeds the generation context action limit."));
    }

    if (diagnostics.Count > 0)
    {
      return GeneratedCardEffectExpansionResult.Rejected(diagnostics);
    }

    var candidate = request.Candidate with
    {
      Effects = [..request.Candidate.Effects, request.Effect],
      CardText = request.Candidate.CardText with
      {
        RulesText = request.RulesText
      },
      BalanceMetadata = request.Candidate.BalanceMetadata with
      {
        ComplexityScore = request.ComplexityScore,
        RandomnessScore = request.RandomnessScore,
        RiskFlags = request.RiskFlags
      },
      AuthoringMetadata = request.Candidate.AuthoringMetadata with
      {
        GenerationPipelineVersion =
          request.Candidate.AuthoringMetadata.GenerationPipelineVersion ?? "pipeline-v1",
        SourceRefs =
        [
          ..request.Candidate.AuthoringMetadata.SourceRefs,
          $"generator:effect-expander:{request.ExpansionId}"
        ]
      }
    };

    var validation = new GeneratedCardCandidateValidator().Validate(candidate, request.Context);
    if (validation.IsValid)
    {
      return GeneratedCardEffectExpansionResult.Success(candidate);
    }

    return GeneratedCardEffectExpansionResult.Rejected(validation.Diagnostics
      .Select(diagnostic => new GeneratedCardEffectExpansionDiagnostic(
        diagnostic.Code.ToString(),
        diagnostic.Path,
        diagnostic.ActualValue,
        diagnostic.Message))
      .ToArray());
  }
}
