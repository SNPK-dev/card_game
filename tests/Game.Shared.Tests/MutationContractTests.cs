using System.Reflection;
using System.Text.Json;
using Game.Shared.Mutation;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Shared.Tests;

public sealed class MutationContractTests
{
  [Fact]
  public void ProposedMutationBatchCarriesPreStateHashOnly()
  {
    var batch = new ProposedMutationBatchRecord(
      "batch-1",
      CommandSeq: 7,
      ParentEventSeq: null,
      Phase.OnPlay,
      "command:PLAY_CARD",
      "PLAY_CARD_ACTION_EXECUTOR",
      [CreateDamageMutation()],
      "sha256:pre",
      ReplacementPolicyRef: null,
      FizzlePolicyRef: "FIZZLE");

    var propertyNames = typeof(ProposedMutationBatchRecord)
      .GetProperties()
      .Select(property => property.Name)
      .ToArray();

    Assert.Equal("sha256:pre", batch.PreStateHash);
    Assert.DoesNotContain("PostStateHash", propertyNames);
  }

  [Fact]
  public void AppliedMutationBatchCarriesPostStateHashAndDerivedEventRefs()
  {
    var mutation = CreateDamageMutation();
    var batch = new AppliedMutationBatchRecord(
      "batch-1",
      CommandSeq: 7,
      ParentEventSeq: null,
      Phase.OnPlay,
      "command:PLAY_CARD",
      "STATE_MUTATOR",
      [mutation],
      [],
      "sha256:pre",
      "sha256:post",
      ["event-1"],
      AppliedAtStep: 1);

    Assert.Equal("sha256:post", batch.PostStateHash);
    Assert.Equal("event-1", batch.DerivedEventRefs.Single());
    Assert.Equal(mutation.MutationId, batch.AppliedMutations.Single().MutationId);
  }

  [Fact]
  public void MutationContractsSerializeReviewableEnumValues()
  {
    var batch = new ProposedMutationBatchRecord(
      "batch-1",
      CommandSeq: 7,
      ParentEventSeq: null,
      Phase.OnPlay,
      "effect:effect-1",
      "EFFECT_ACTION_COMPILER",
      [CreateDamageMutation()],
      "sha256:pre",
      ReplacementPolicyRef: null,
      FizzlePolicyRef: null);

    var json = JsonSerializer.Serialize(batch);

    Assert.Contains("\"Phase\":\"ON_PLAY\"", json);
    Assert.Contains("\"MutationType\":\"APPLY_DAMAGE\"", json);
    Assert.Contains("\"Category\":\"STAT_MUTATION\"", json);
    Assert.Contains("\"PreStateHash\":\"sha256:pre\"", json);
  }

  [Fact]
  public void PresentationOnlyMutationTypesAreReviewableButBehaviorFree()
  {
    var mutation = CreateDamageMutation() with
    {
      MutationType = MutationType.PlayAudio,
      Category = MutationCategory.PresentationOnly,
      SourceRef = "presentation:cue-1",
      TargetRef = "asset:audio-1"
    };

    Assert.Equal(MutationCategory.PresentationOnly, mutation.Category);
    Assert.Equal(MutationType.PlayAudio, mutation.MutationType);
  }

  [Fact]
  public void MutationContractsDoNotExposeRuntimeBehaviorServices()
  {
    var contractTypes = typeof(StateMutationRecord).Assembly
      .GetExportedTypes()
      .Where(type => type.Namespace is "Game.Shared.Mutation")
      .ToArray();

    Assert.NotEmpty(contractTypes);
    Assert.All(contractTypes, type =>
    {
      Assert.True(type.IsEnum || IsRecord(type));
      Assert.False(type.Name.EndsWith("Service", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Resolver", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Validator", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Executor", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Handler", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Mutator", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Factory", StringComparison.Ordinal));

      var declaredMethods = type
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(method => !method.IsSpecialName)
        .Select(method => method.Name)
        .ToArray();

      Assert.DoesNotContain("Apply", declaredMethods);
      Assert.DoesNotContain("Execute", declaredMethods);
      Assert.DoesNotContain("Resolve", declaredMethods);
      Assert.DoesNotContain("Validate", declaredMethods);
      Assert.DoesNotContain("Mutate", declaredMethods);
      Assert.DoesNotContain("CalculateHash", declaredMethods);
      Assert.DoesNotContain("CreateEvent", declaredMethods);
    });
  }

  private static StateMutationRecord CreateDamageMutation()
  {
    return new StateMutationRecord(
      "mutation-damage-001",
      MutationType.ApplyDamage,
      MutationCategory.StatMutation,
      "effect:effect-1",
      "entity-2",
      [new MutationPayloadEntry("amount", new MutationPayloadValue("INT", null, 3, null, []))],
      Phase.OnPlay,
      CausedByCommandSeq: 7,
      CausedByEventSeq: null,
      "effect:effect-1:0001",
      [new MutationMetadataEntry("playerId", "player-2")]);
  }

  private static bool IsRecord(Type type)
  {
    return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
      .Any(method => method.Name == "<Clone>$");
  }
}
