using System.Collections.ObjectModel;
using Game.Shared.Engine;

namespace Game.Application.Capabilities;

public sealed class RuntimeEngineCapabilityRegistryBuilder(
  EngineCapabilityRegistryHasher? hasher = null)
{
  private readonly EngineCapabilityRegistryHasher _hasher = hasher ?? new EngineCapabilityRegistryHasher();

  public EngineCapabilityRegistryBuildResult Build(
    string engineVersion,
    IEnumerable<RuntimeCapabilityDeclaration> declarations,
    EngineCapabilityMetadata metadata)
  {
    if (string.IsNullOrWhiteSpace(engineVersion))
    {
      throw new ArgumentException("Engine version is required.", nameof(engineVersion));
    }

    ArgumentNullException.ThrowIfNull(declarations);
    ArgumentNullException.ThrowIfNull(metadata);

    var declarationList = declarations.ToArray();
    ValidateDeclarationIds(declarationList);
    ValidateUniqueRuntimeSupport(declarationList, declaration => declaration.MutationTypes, "mutation type");
    ValidateRequiredMutationCoverage(declarationList);
    ValidateEventMappingCoverage(declarationList);

    var registry = new EngineCapabilityRegistry(
      engineVersion,
      Merge(declarationList, declaration => declaration.SchemaVersions),
      Merge(declarationList, declaration => declaration.PrimitiveRegistryVersions),
      Merge(declarationList, declaration => declaration.RulesetProfileVersions),
      Merge(declarationList, declaration => declaration.ActionTypes),
      Merge(declarationList, declaration => declaration.EffectKinds),
      Merge(declarationList, declaration => declaration.Mechanics),
      Merge(declarationList, declaration => declaration.Phases),
      Merge(declarationList, declaration => declaration.SourceZones),
      Merge(declarationList, declaration => declaration.ResolutionPolicies),
      Merge(declarationList, declaration => declaration.FizzlePolicies),
      Merge(declarationList, declaration => declaration.AssetManifestVersions),
      metadata with
      {
        SourceReferences = MergeWithMetadata(metadata.SourceReferences, declarationList, declaration => declaration.SourceReferences),
        Notes = MergeWithMetadata(metadata.Notes, declarationList, declaration => declaration.Notes)
      });

    return new EngineCapabilityRegistryBuildResult(registry, _hasher.ComputeHash(registry));
  }

  private static void ValidateDeclarationIds(IReadOnlyList<RuntimeCapabilityDeclaration> declarations)
  {
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (var declaration in declarations)
    {
      ArgumentNullException.ThrowIfNull(declaration);

      if (string.IsNullOrWhiteSpace(declaration.DeclarationId))
      {
        throw new InvalidOperationException("Runtime capability declaration id is required.");
      }

      if (!seen.Add(declaration.DeclarationId))
      {
        throw new InvalidOperationException(
          $"Duplicate runtime capability declaration '{declaration.DeclarationId}'.");
      }
    }
  }

  private static void ValidateUniqueRuntimeSupport(
    IReadOnlyList<RuntimeCapabilityDeclaration> declarations,
    Func<RuntimeCapabilityDeclaration, IReadOnlyList<string>?> selector,
    string supportType)
  {
    var owners = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var declaration in declarations)
    {
      foreach (var value in selector(declaration) ?? [])
      {
        if (string.IsNullOrWhiteSpace(value))
        {
          throw new InvalidOperationException(
            $"Runtime capability declaration '{declaration.DeclarationId}' contains an empty {supportType}.");
        }

        if (owners.TryGetValue(value, out var existingOwner))
        {
          throw new InvalidOperationException(
            $"Duplicate {supportType} '{value}' declared by '{existingOwner}' and '{declaration.DeclarationId}'.");
        }

        owners[value] = declaration.DeclarationId;
      }
    }
  }

  private static void ValidateRequiredMutationCoverage(IReadOnlyList<RuntimeCapabilityDeclaration> declarations)
  {
    var mutationTypes = declarations
      .SelectMany(declaration => declaration.MutationTypes ?? [])
      .ToHashSet(StringComparer.Ordinal);
    var requiredMutationTypes = declarations
      .SelectMany(declaration => declaration.RequiredMutationTypes ?? [])
      .Order(StringComparer.Ordinal)
      .ToArray();

    foreach (var mutationType in requiredMutationTypes)
    {
      if (!mutationTypes.Contains(mutationType))
      {
        throw new InvalidOperationException(
          $"Required mutation type '{mutationType}' has no registered mutation handler capability.");
      }
    }
  }

  private static void ValidateEventMappingCoverage(IReadOnlyList<RuntimeCapabilityDeclaration> declarations)
  {
    var eventMappedMutationTypes = declarations
      .SelectMany(declaration => declaration.EventMappedMutationTypes ?? [])
      .ToHashSet(StringComparer.Ordinal);
    var mutationTypes = declarations
      .SelectMany(declaration => declaration.MutationTypes ?? [])
      .Order(StringComparer.Ordinal)
      .ToArray();

    foreach (var mutationType in mutationTypes)
    {
      if (!eventMappedMutationTypes.Contains(mutationType))
      {
        throw new InvalidOperationException(
          $"Mutation type '{mutationType}' has no committed event mapping capability.");
      }
    }
  }

  private static ReadOnlyCollection<string> Merge(
    IEnumerable<RuntimeCapabilityDeclaration> declarations,
    Func<RuntimeCapabilityDeclaration, IReadOnlyList<string>?> selector) =>
    Freeze(declarations.SelectMany(declaration => selector(declaration) ?? []));

  private static ReadOnlyCollection<string> MergeWithMetadata(
    IReadOnlyList<string> metadataValues,
    IEnumerable<RuntimeCapabilityDeclaration> declarations,
    Func<RuntimeCapabilityDeclaration, IReadOnlyList<string>?> selector) =>
    Freeze(metadataValues.Concat(declarations.SelectMany(declaration => selector(declaration) ?? [])));

  private static ReadOnlyCollection<string> Freeze(IEnumerable<string> values)
  {
    var ordered = values
      .Where(value => !string.IsNullOrWhiteSpace(value))
      .Distinct(StringComparer.Ordinal)
      .Order(StringComparer.Ordinal)
      .ToArray();
    return Array.AsReadOnly(ordered);
  }
}
