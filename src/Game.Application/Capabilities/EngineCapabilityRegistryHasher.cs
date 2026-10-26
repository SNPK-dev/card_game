using System.Security.Cryptography;
using System.Text;
using Game.Shared.Engine;

namespace Game.Application.Capabilities;

public sealed class EngineCapabilityRegistryHasher
{
  public string ComputeHash(EngineCapabilityRegistry registry)
  {
    ArgumentNullException.ThrowIfNull(registry);

    var canonical = string.Join(
      "\n",
      [
        $"engine={registry.EngineVersion}",
        $"schema={Join(registry.SupportedSchemaVersions)}",
        $"primitive={Join(registry.SupportedPrimitiveRegistryVersions)}",
        $"ruleset={Join(registry.SupportedRulesetProfileVersions)}",
        $"actions={Join(registry.SupportedActionTypes)}",
        $"effects={Join(registry.SupportedEffectKinds)}",
        $"mechanics={Join(registry.SupportedMechanics)}",
        $"phases={Join(registry.SupportedPhases)}",
        $"zones={Join(registry.SupportedSourceZones)}",
        $"resolution={Join(registry.SupportedResolutionPolicies)}",
        $"fizzle={Join(registry.SupportedFizzlePolicies)}",
        $"assets={Join(registry.SupportedAssetManifestVersions)}"
      ]);
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
  }

  private static string Join(IReadOnlyList<string> values) =>
    string.Join("|", values.Order(StringComparer.Ordinal));
}
