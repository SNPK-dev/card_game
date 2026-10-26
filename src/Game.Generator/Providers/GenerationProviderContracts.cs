using Game.Shared.Dsl;
using Game.Shared.Primitives;

namespace Game.Generator.Providers;

public interface IStructuredCardGenerationProvider
{
  StructuredCardGenerationResult Generate(StructuredCardGenerationRequest request);
}

public interface IGeneratedAssetProvider
{
  GeneratedAssetProviderResult Generate(GeneratedAssetProviderRequest request);
}

public sealed record StructuredCardGenerationRequest(
  string GenerationId,
  string PromptVersion,
  string ModelId,
  string Prompt,
  IReadOnlyDictionary<string, string> ContextRefs,
  TimeSpan Timeout);

public sealed record StructuredCardGenerationResult(
  bool Succeeded,
  StructuredCardOutput? Output,
  string Provider,
  string ModelId,
  string RawStructuredOutputRef,
  string? ErrorCode,
  string? ErrorMessage)
{
  public static StructuredCardGenerationResult Success(
    StructuredCardOutput output,
    string provider,
    string modelId,
    string rawStructuredOutputRef) =>
    new(true, output, provider, modelId, rawStructuredOutputRef, null, null);

  public static StructuredCardGenerationResult Failure(
    string provider,
    string modelId,
    string rawStructuredOutputRef,
    string errorCode,
    string errorMessage) =>
    new(false, null, provider, modelId, rawStructuredOutputRef, errorCode, errorMessage);
}

public sealed record StructuredCardOutput(
  string Name,
  CardType CardType,
  IReadOnlyList<string> ClassTags,
  Rarity Rarity,
  int ManaCost,
  IReadOnlyList<string> Tribes,
  IReadOnlyList<string> Keywords,
  bool Collectible,
  IReadOnlyList<string> FormatTags,
  int? Attack,
  int? Health,
  bool CanAttack,
  bool RequiresTarget,
  string RulesText,
  IReadOnlyList<EffectDefinition> Effects,
  int ComplexityScore,
  int RandomnessScore,
  IReadOnlyList<string> RiskFlags,
  AssetGenerationPrompt VisualPrompt,
  AssetGenerationPrompt AnimationPrompt,
  AssetGenerationPrompt? AudioPrompt = null,
  IReadOnlyList<string>? RequestedAudioCueTypes = null,
  string? RequestedAudioAssetMode = null,
  int HeroArmorGain = 0,
  string? HeroPortraitAssetRef = null,
  int? HeroPowerCost = null,
  string HeroPowerActivationTiming = "ACTIVE_PLAYER_MAIN");

public sealed record AssetGenerationPrompt(
  string Prompt,
  string StyleGuideRef,
  string SafetyPolicyRef);

public sealed record GeneratedAssetProviderRequest(
  string GenerationId,
  string CardId,
  string CardVersionId,
  string AssetKind,
  string Prompt,
  string StyleGuideRef,
  string SafetyPolicyRef,
  TimeSpan Timeout);

public sealed record GeneratedAssetProviderResult(
  bool Succeeded,
  string Provider,
  string ModelId,
  string AssetRef,
  string Format,
  string ContentHash,
  string ModerationStatus,
  string RawAssetRef,
  string? ErrorCode,
  string? ErrorMessage)
{
  public static GeneratedAssetProviderResult Success(
    string provider,
    string modelId,
    string assetRef,
    string format,
    string contentHash,
    string moderationStatus,
    string rawAssetRef) =>
    new(true, provider, modelId, assetRef, format, contentHash, moderationStatus, rawAssetRef, null, null);

  public static GeneratedAssetProviderResult Failure(
    string provider,
    string modelId,
    string rawAssetRef,
    string errorCode,
    string errorMessage) =>
    new(false, provider, modelId, string.Empty, string.Empty, string.Empty, "REJECTED", rawAssetRef, errorCode, errorMessage);
}
