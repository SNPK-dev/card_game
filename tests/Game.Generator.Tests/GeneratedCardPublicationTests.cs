using Game.Generator.Generation;
using Game.Generator.Publication;
using Game.Generator.Validation;
using Game.Shared.Dsl;
using Game.Shared.Mechanics;
using Xunit;

namespace Game.Generator.Tests;

public sealed class GeneratedCardPublicationTests
{
  [Fact]
  public void PublishCreatesCardVersionAndSnapshotForAcceptedValidationReport()
  {
    var request = CreateValidationRequest();
    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);
    var publicationRequest = new GeneratedCardPublicationRequest(
      request,
      report,
      "card_arcane_apprentice_v1",
      "catalog-alpha",
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      TestEngineCapabilityRegistryFactory.Create());

    var result = new GeneratedCardPublisher().Publish(publicationRequest);

    Assert.True(result.Succeeded);
    Assert.Empty(result.Diagnostics);
    Assert.NotNull(result.CardVersion);
    Assert.NotNull(result.CatalogSnapshot);
    Assert.Equal("card_arcane_apprentice", result.CardVersion.CardId);
    Assert.Equal("card_arcane_apprentice_v1", result.CardVersion.CardVersionId);
    Assert.Equal("pipeline-v1", result.CardVersion.GenerationPipelineVersion);
    Assert.Equal("ruleset_mvp_core_v1", result.CardVersion.RulesetProfileId);
    Assert.Equal("resolved-hash", result.CardVersion.RulesetProfileHash);
    Assert.Equal("catalog-alpha", result.CatalogSnapshot.SnapshotId);
    Assert.Equal("resolved-hash", result.CatalogSnapshot.RulesetProfileHash);
    Assert.Same(result.CardVersion, result.CatalogSnapshot.Cards.Single());
    Assert.Equal("card_arcane_apprentice_v1", result.CardVersion.Definition.CardVersion);
    Assert.Equal("catalog-alpha", result.CardVersion.Definition.CatalogSnapshotId);
  }

  [Fact]
  public void PublishRejectsRejectedValidationReport()
  {
    var request = CreateValidationRequest(TestFixtures.CreateBattlecryCandidate(effectTags: ["SMART_OFFER"]));
    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);
    var publicationRequest = new GeneratedCardPublicationRequest(
      request,
      report,
      "card_arcane_apprentice_v1",
      "catalog-alpha",
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      TestEngineCapabilityRegistryFactory.Create());

    var result = new GeneratedCardPublisher().Publish(publicationRequest);

    Assert.False(result.Succeeded);
    Assert.Null(result.CardVersion);
    Assert.Null(result.CatalogSnapshot);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardPublicationDiagnosticCode.ValidationRejected);
  }

  [Fact]
  public void PublishRejectsUnsupportedEngineCapability()
  {
    var request = CreateValidationRequest();
    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);
    var registry = TestEngineCapabilityRegistryFactory.Create() with
    {
      SupportedActionTypes = ["DRAW_CARD"]
    };
    var publicationRequest = new GeneratedCardPublicationRequest(
      request,
      report,
      "card_arcane_apprentice_v1",
      "catalog-alpha",
      DateTimeOffset.Parse("2026-06-24T00:00:00Z"),
      registry);

    var result = new GeneratedCardPublisher().Publish(publicationRequest);

    Assert.False(result.Succeeded);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardPublicationDiagnosticCode.UnsupportedEngineCapability
      && diagnostic.Path == "card.effects[0].actions[0]");
  }

  [Fact]
  public void PublishRejectsUnsupportedMechanicAndFutureDslTokenEvenWithAcceptedReport()
  {
    var futureAction = new FutureDslActionDefinition("future_token_001");
    var request = CreateValidationRequest(TestFixtures.CreateBattlecryCandidate(
      effectTags: ["FUTURE_TRIGGER_LIFECYCLE"],
      extraAction: futureAction));
    var report = new GeneratedCardValidationPipelineReport(
      request.GenerationId,
      request.PipelineVersion,
      request.ContextVersion,
      request.Context.RulesetProfileId,
      request.Context.RulesetProfileHash,
      GeneratedCardValidationDecision.Accepted,
      []);
    var publicationRequest = new GeneratedCardPublicationRequest(
      request,
      report,
      "card_arcane_apprentice_v1",
      "catalog-alpha",
      DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
      TestEngineCapabilityRegistryFactory.Create());

    var result = new GeneratedCardPublisher().Publish(publicationRequest);

    Assert.False(result.Succeeded);
    Assert.Null(result.CardVersion);
    Assert.Null(result.CatalogSnapshot);
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardPublicationDiagnosticCode.UnsupportedEngineCapability
      && diagnostic.Path == "card.effects[0].tags");
    Assert.Contains(result.Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardPublicationDiagnosticCode.UnsupportedEngineCapability
      && diagnostic.Path == "card.effects[0].actions[1]");
  }

  private static GeneratedCardValidationRequest CreateValidationRequest(
    Game.Shared.Dsl.CardDefinition? candidate = null)
  {
    var profile = TestFixtures.CreateProfile(allowedActions: ["DEAL_DAMAGE", "DRAW_CARD"]);
    var context = new GenerationContextBuilder().Build(
      profile,
      [TestFixtures.Mechanic("BATTLECRY", MechanicStatus.ApprovedForRuleset)]);

    return new GeneratedCardValidationRequest(
      "generation-001",
      "pipeline-v1",
      "context-v1",
      NormalizeForPublication(candidate ?? TestFixtures.CreateBattlecryCandidate()),
      context,
      TestFixtures.CreateBalanceReport(),
      TestFixtures.CreateSimulationReport());
  }

  private static Game.Shared.Dsl.CardDefinition NormalizeForPublication(Game.Shared.Dsl.CardDefinition candidate) =>
    candidate with
    {
      SchemaVersion = "card-dsl-0.2.0",
      EngineVersion = "engine-bootstrap-0.3.0",
      PrimitiveRegistryVersion = "primitive-registry-bootstrap-0.2.0"
    };

  private sealed record FutureDslActionDefinition(string FutureActionId) : ActionDefinition(FutureActionId);
}
