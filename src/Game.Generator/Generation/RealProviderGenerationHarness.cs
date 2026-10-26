using Game.Generator.Providers;

namespace Game.Generator.Generation;

public sealed record RealProviderGenerationHarnessRequest(
  GenerationProviderConfiguration StructuredOutputProvider,
  GenerationProviderConfiguration ImageProvider,
  GenerationProviderConfiguration AnimationProvider,
  ProductionCardGenerationRequest GenerationRequest,
  GenerationProviderConfiguration? AudioProvider = null);

public sealed record RealProviderGenerationHarnessResult(
  ProductionCardGenerationStatus Status,
  ProductionCardGenerationResult? GenerationResult,
  IReadOnlyList<ProductionCardGenerationDiagnostic> Diagnostics,
  IReadOnlyList<GenerationProviderConfiguration> RedactedProviderConfigurations);

public sealed class RealProviderGenerationHarness(
  IStructuredCardGenerationProvider structuredProvider,
  IGeneratedAssetProvider imageProvider,
  IGeneratedAssetProvider animationProvider,
  IGeneratedAssetProvider? audioProvider = null,
  GenerationProviderConfigurationValidator? configurationValidator = null)
{
  private readonly GenerationProviderConfigurationValidator _configurationValidator = configurationValidator ?? new();

  public RealProviderGenerationHarnessResult Run(RealProviderGenerationHarnessRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var validations = new List<(string Name, GenerationProviderConfigurationValidationResult Result)>
    {
      ("structured", _configurationValidator.Validate(request.StructuredOutputProvider)),
      ("image", _configurationValidator.Validate(request.ImageProvider)),
      ("animation", _configurationValidator.Validate(request.AnimationProvider))
    };
    if (request.AudioProvider is not null)
    {
      validations.Add(("audio", _configurationValidator.Validate(request.AudioProvider)));
    }

    var diagnostics = validations
      .Where(validation => !validation.Result.IsConfigured)
      .SelectMany(validation => validation.Result.MissingFields.Select(field => new ProductionCardGenerationDiagnostic(
        "PROVIDER_CONFIGURATION_MISSING",
        $"providers.{validation.Name}.{field}",
        validation.Name,
        $"Provider '{validation.Name}' is missing required configuration field '{field}'.")))
      .ToArray();

    if (diagnostics.Length > 0)
    {
      return new RealProviderGenerationHarnessResult(
        ProductionCardGenerationStatus.Blocked,
        GenerationResult: null,
        diagnostics,
        validations.Select(validation => validation.Result.RedactedConfiguration).ToArray());
    }

    var generation = new ProductionCardGenerationPipeline(
      structuredProvider,
      imageProvider,
      animationProvider,
      audioProvider).Run(request.GenerationRequest);

    return new RealProviderGenerationHarnessResult(
      generation.Status,
      generation,
      generation.Diagnostics,
      validations.Select(validation => validation.Result.RedactedConfiguration).ToArray());
  }
}
