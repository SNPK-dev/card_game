using Game.Generator.Audit;
using Game.Generator.Generation;
using Game.Generator.Validation;
using Game.Shared.Mechanics;
using Xunit;

namespace Game.Generator.Tests;

public sealed class GenerationAuditLogTests
{
  [Fact]
  public void BuildPreservesValidationReportIdentityAndDiagnostics()
  {
    var request = CreateValidationRequest(TestFixtures.CreateBattlecryCandidate(effectTags: ["SMART_OFFER"]));
    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);
    var metadata = CreateMetadata();

    var entry = new GenerationAuditLogEntryBuilder().Build(request, report, metadata);

    Assert.Equal("generation-001", entry.GenerationId);
    Assert.Equal(DateTimeOffset.Parse("2026-06-23T10:15:00Z"), entry.CreatedAt);
    Assert.Equal("pipeline-v1", entry.PipelineVersion);
    Assert.Equal("context-v1", entry.ContextVersion);
    Assert.Equal("ruleset_mvp_core_v1", entry.RulesetProfileId);
    Assert.Equal("resolved-hash", entry.RulesetProfileHash);
    Assert.Equal(GeneratedCardValidationDecision.Rejected, entry.FinalDecision);
    Assert.Equal(7, entry.ValidationResults.Count);
    Assert.Contains(entry.ValidationResults.Single(stage => stage.Stage == GeneratedCardValidationStageKind.Rule).Diagnostics, diagnostic =>
      diagnostic.Code == GeneratedCardValidationCode.MechanicNotAvailable
      && diagnostic.ActualValue == "SMART_OFFER");
    Assert.Contains(entry.ValidationResults, stage => stage.Stage == GeneratedCardValidationStageKind.Replay);
    Assert.Equal("intent://generation-001", entry.IntentRef);
    Assert.Equal("raw://generation-001", entry.RawStructuredOutputRef);
    Assert.Equal("replay://generation-001", entry.ReplayReportRef);
    Assert.Empty(entry.RepairAttemptRefs);
  }

  [Fact]
  public void StoreAppendsEntriesInOrderAndRejectsDuplicateGenerationId()
  {
    var firstEntry = CreateAuditEntry("generation-001");
    var duplicateEntry = firstEntry with { PipelineVersion = "pipeline-v2" };
    var secondEntry = CreateAuditEntry("generation-002");
    var store = new InMemoryGenerationAuditLogStore();

    var firstResult = store.Append(firstEntry);
    var duplicateResult = store.Append(duplicateEntry);
    var secondResult = store.Append(secondEntry);

    Assert.True(firstResult.Succeeded);
    Assert.False(duplicateResult.Succeeded);
    Assert.Contains("generation-001", duplicateResult.Error, StringComparison.Ordinal);
    Assert.True(secondResult.Succeeded);
    Assert.Equal(["generation-001", "generation-002"], store.Entries.Select(entry => entry.GenerationId));
    Assert.Equal("pipeline-v1", store.FindByGenerationId("generation-001")?.PipelineVersion);
  }

  [Fact]
  public void BuildRejectsMismatchedValidationReportGenerationId()
  {
    var request = CreateValidationRequest(TestFixtures.CreateBattlecryCandidate());
    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request) with
    {
      GenerationId = "other-generation"
    };

    var exception = Assert.Throws<ArgumentException>(() =>
      new GenerationAuditLogEntryBuilder().Build(request, report, CreateMetadata()));

    Assert.Contains("generation id", exception.Message, StringComparison.OrdinalIgnoreCase);
  }

  private static GenerationAuditLogEntry CreateAuditEntry(string generationId)
  {
    var request = CreateValidationRequest(TestFixtures.CreateBattlecryCandidate(), generationId);
    var report = GeneratedCardValidationPipeline.CreateDefault().Validate(request);
    return new GenerationAuditLogEntryBuilder().Build(request, report, CreateMetadata());
  }

  private static GeneratedCardValidationRequest CreateValidationRequest(
    Game.Shared.Dsl.CardDefinition candidate,
    string generationId = "generation-001")
  {
    var profile = TestFixtures.CreateProfile(allowedActions: ["DEAL_DAMAGE", "DRAW_CARD"]);
    var context = new GenerationContextBuilder().Build(
      profile,
      [TestFixtures.Mechanic("BATTLECRY", MechanicStatus.ApprovedForRuleset)]);

    return new GeneratedCardValidationRequest(
      generationId,
      "pipeline-v1",
      "context-v1",
      candidate,
      context,
      TestFixtures.CreateBalanceReport(),
      TestFixtures.CreateSimulationReport());
  }

  private static GenerationAuditLogEntryMetadata CreateMetadata() =>
    new(
      DateTimeOffset.Parse("2026-06-23T10:15:00Z"),
      "intent://generation-001",
      "raw://generation-001",
      "skeleton://generation-001",
      "effect-slots://generation-001",
      "expanded-effects://generation-001",
      [],
      null,
      null,
      "replay://generation-001");
}
