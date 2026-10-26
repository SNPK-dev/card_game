using Game.Application.Catalog;
using Game.Application.Decks;
using Game.Application.Presentation;
using Game.Application.Rulesets;
using Game.Generator.Acceptance;
using Game.Shared.Assets;
using Game.Shared.Catalog;
using Game.Shared.Decks;
using Game.Shared.Ruleset;

public static class StorageEndpointHandlers
{
  public static IResult StoreCatalogSnapshot(CatalogSnapshot snapshot, ICatalogSnapshotStore store)
  {
    var result = store.Store(snapshot);
    return result.Succeeded ? Results.Ok(snapshot) : Results.Conflict(result);
  }

  public static IResult StoreDeck(DeckDefinition deck, IDeckStore store)
  {
    var result = store.Store(deck);
    return result.Succeeded ? Results.Ok(deck) : Results.Conflict(result);
  }

  public static IResult StoreResolvedRulesetProfile(ResolvedRulesetProfile profile, IResolvedRulesetProfileStore store)
  {
    var result = store.Store(profile);
    return result.Succeeded ? Results.Ok(profile) : Results.Conflict(result);
  }

  public static IResult StoreAssetManifest(AssetManifest manifest, IAssetManifestStore store)
  {
    var result = store.Store(manifest);
    return result.Succeeded ? Results.Ok(manifest) : Results.Conflict(result);
  }

  public static IResult StoreProductionAcceptanceReport(
    ProductionAcceptanceReport report,
    IProductionAcceptanceReportStore store)
  {
    var result = store.Store(report);
    return result.Succeeded ? Results.Ok(report) : Results.Conflict(result);
  }

  public static IResult StoreMatchEvidenceArtifact(
    MatchEvidenceArtifact artifact,
    IMatchEvidenceArtifactStore store)
  {
    var result = store.Store(artifact);
    if (result.Succeeded)
    {
      return Results.Ok(artifact);
    }

    return IsInvalidMatchEvidenceArtifactInput(result)
      ? Results.BadRequest(result)
      : Results.Conflict(result);
  }

  public static IResult GetMatchEvidenceArtifact(
    string artifactRef,
    IMatchEvidenceArtifactStore store)
  {
    if (!MatchEvidenceArtifactRefValidator.IsValid(artifactRef))
    {
      return Results.BadRequest(MatchEvidenceArtifactStoreResult.InvalidArtifactRef(artifactRef));
    }

    var artifact = store.FindByRef(artifactRef);
    return artifact is null ? Results.NotFound() : Results.Ok(artifact);
  }

  public static IResult GetMatchEvidenceArtifacts(
    string matchId,
    IMatchEvidenceArtifactStore store) =>
    Results.Ok(store.FindByMatchId(matchId));

  public static IResult GetProductionAcceptanceReportMatchEvidence(
    string reportId,
    IProductionAcceptanceReportStore store)
  {
    var report = store.FindById(reportId);
    return report is null ? Results.NotFound() : Results.Ok(report.Match);
  }

  private static bool IsInvalidMatchEvidenceArtifactInput(MatchEvidenceArtifactStoreResult result) =>
    result.ErrorCode?.StartsWith("INVALID_MATCH_EVIDENCE_ARTIFACT_REF:", StringComparison.Ordinal) == true
    || result.ErrorCode?.StartsWith("INVALID_MATCH_EVIDENCE_CONTENT_HASH:", StringComparison.Ordinal) == true;
}
