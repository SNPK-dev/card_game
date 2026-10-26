using Game.Generator.Validation;
using Game.Shared.Primitives;

namespace Game.Generator.Skeletons;

public sealed class GeneratedCardSkeletonBuilder
{
  public SkeletonGenerationResult Build(SkeletonGenerationRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var diagnostics = new List<SkeletonGenerationDiagnostic>();

    AddMissing(diagnostics, request.SkeletonId, "skeleton.skeletonId");
    AddMissing(diagnostics, request.SchemaVersion, "skeleton.schemaVersion");
    AddMissing(diagnostics, request.EngineVersion, "skeleton.engineVersion");
    AddMissing(diagnostics, request.PrimitiveRegistryVersion, "skeleton.primitiveRegistryVersion");
    AddMissing(diagnostics, request.Name, "skeleton.name");
    AddMissing(diagnostics, request.CreatedBy, "skeleton.createdBy");

    AddIfDisallowed(
      diagnostics,
      request.Context.AllowedCardTypes,
      EnumToken.ToToken(request.CardType),
      SkeletonGenerationDiagnosticCode.CardTypeNotAllowed,
      "skeleton.cardType");
    AddIfDisallowed(
      diagnostics,
      request.Context.AllowedRarities,
      EnumToken.ToToken(request.Rarity),
      SkeletonGenerationDiagnosticCode.RarityNotAllowed,
      "skeleton.rarity");

    foreach (var classTag in request.ClassTags)
    {
      AddIfDisallowed(diagnostics, request.Context.AllowedClasses, classTag, SkeletonGenerationDiagnosticCode.ClassTagNotAllowed, "skeleton.classTags");
    }

    foreach (var tribe in request.Tribes)
    {
      AddIfDisallowed(diagnostics, request.Context.AllowedTribes, tribe, SkeletonGenerationDiagnosticCode.TribeNotAllowed, "skeleton.tribes");
    }

    foreach (var keyword in request.Keywords)
    {
      AddIfDisallowed(diagnostics, request.Context.AllowedKeywords, keyword, SkeletonGenerationDiagnosticCode.KeywordNotAllowed, "skeleton.keywords");
    }

    if (request.ManaCost < 0)
    {
      diagnostics.Add(new SkeletonGenerationDiagnostic(
        SkeletonGenerationDiagnosticCode.InvalidManaCost,
        "skeleton.manaCost",
        request.ManaCost.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "Mana cost must be zero or greater."));
    }

    var isMinionLike = request.CardType is CardType.MinionCard or CardType.TokenCard;
    var isHeroCard = request.CardType is CardType.HeroCard;
    if (isMinionLike && (request.Attack is null or < 0 || request.Health is null or <= 0))
    {
      diagnostics.Add(new SkeletonGenerationDiagnostic(
        SkeletonGenerationDiagnosticCode.InvalidMinionStats,
        "skeleton.entity.minion",
        $"{request.Attack?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}/{request.Health?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}",
        "Minion-like skeletons require non-negative attack and positive health."));
    }

    if(isHeroCard&&(request.HeroArmorGain<0||request.HeroPowerCost is <0 or >10||request.HeroPowerCost.HasValue&&string.IsNullOrWhiteSpace(request.HeroPowerActivationTiming)))
    {
      diagnostics.Add(new SkeletonGenerationDiagnostic(
        SkeletonGenerationDiagnosticCode.InvalidHeroCardDefinition,
        "skeleton.entity.heroCard",
        $"armor={request.HeroArmorGain};powerCost={request.HeroPowerCost?.ToString()??"none"};timing={request.HeroPowerActivationTiming}",
        "Hero cards require non-negative armor and a valid optional hero power cost/timing."));
    }

    if (request.CardType is not (CardType.MinionCard or CardType.SpellCard or CardType.TokenCard or CardType.HeroCard))
    {
      diagnostics.Add(new SkeletonGenerationDiagnostic(
        SkeletonGenerationDiagnosticCode.UnsupportedCardType,
        "skeleton.cardType",
        EnumToken.ToToken(request.CardType),
        "Only MINION_CARD, SPELL_CARD, TOKEN_CARD, and HERO_CARD skeletons are supported by the production builder."));
    }

    if (diagnostics.Count > 0)
    {
      return SkeletonGenerationResult.Failure(diagnostics);
    }

    var skeleton = new GeneratedCardSkeleton(
      request.SkeletonId,
      request.SchemaVersion,
      request.EngineVersion,
      request.PrimitiveRegistryVersion,
      request.Context.RulesetProfileId,
      request.Context.CatalogSnapshotId,
      request.Name,
      request.CardType,
      request.ClassTags,
      request.Rarity,
      request.ManaCost,
      request.Tribes,
      request.Keywords,
      request.Collectible,
      request.FormatTags,
      isMinionLike ? new MinionSkeleton(request.Attack!.Value, request.Health!.Value, request.CanAttack) : null,
      request.CardType is CardType.SpellCard ? new SpellSkeleton(request.RequiresTarget) : null,
      request.CreatedBy,
      request.CreatedAt,
      isHeroCard?new HeroCardSkeleton(
        request.HeroArmorGain,
        request.HeroPortraitAssetRef,
        request.HeroPowerCost.HasValue?new HeroPowerSkeleton(request.HeroPowerCost.Value,request.HeroPowerActivationTiming):null):null);

    return SkeletonGenerationResult.Success(skeleton);
  }

  private static void AddMissing(
    ICollection<SkeletonGenerationDiagnostic> diagnostics,
    string? value,
    string path)
  {
    if (!string.IsNullOrWhiteSpace(value))
    {
      return;
    }

    diagnostics.Add(new SkeletonGenerationDiagnostic(
      SkeletonGenerationDiagnosticCode.RequiredFieldMissing,
      path,
      string.Empty,
      $"Required field '{path}' is missing."));
  }

  private static void AddIfDisallowed(
    ICollection<SkeletonGenerationDiagnostic> diagnostics,
    IReadOnlySet<string> allowed,
    string actual,
    SkeletonGenerationDiagnosticCode code,
    string path)
  {
    if (allowed.Contains(actual))
    {
      return;
    }

    diagnostics.Add(new SkeletonGenerationDiagnostic(
      code,
      path,
      actual,
      $"Value '{actual}' is not allowed by the generation context."));
  }
}
