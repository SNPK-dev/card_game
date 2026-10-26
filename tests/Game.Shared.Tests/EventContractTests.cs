using System.Reflection;
using System.Text.Json;
using Game.Shared.Events;
using Game.Shared.Primitives;
using Xunit;

namespace Game.Shared.Tests;

public sealed class EventContractTests
{
  [Fact]
  public void ProposedEventSerializesAsPreMutationIntention()
  {
    var proposed = new ProposedEventRecord(
      "event-proposed-001",
      12,
      5,
      11,
      Phase.OnPlay,
      ProposedEventType.DamageWouldBeDealt,
      [new EventRef("CARD_INSTANCE", "card-instance-1", "BOARD", "player-1")],
      [new EventRef("ENTITY_INSTANCE", "entity-2", "BOARD", "player-2")],
      "mutation-damage-001",
      [new EventPayloadEntry("amount", new EventPayloadValue("INT", null, 3, null, []))],
      "DAMAGE_PREVENTION",
      PreventionAllowed: true,
      ["rng-roll-7"],
      EventVisibility.Public);

    var json = JsonSerializer.Serialize(proposed);

    Assert.Contains("\"ProposedEventType\":\"DAMAGE_WOULD_BE_DEALT\"", json);
    Assert.Contains("\"Phase\":\"ON_PLAY\"", json);
    Assert.Contains("\"Visibility\":\"PUBLIC\"", json);
    Assert.Contains("\"PreventionAllowed\":true", json);
    Assert.Contains("\"RngRollRefs\":[\"rng-roll-7\"]", json);
  }

  [Fact]
  public void CommittedEventCarriesMutationAndStateHashRefsWithoutCalculatingThem()
  {
    var committed = CreateCommittedEvent();

    Assert.Equal(14, committed.EventSeq);
    Assert.Equal(CommittedEventType.EntityDamaged, committed.EventType);
    Assert.Equal(EventCategory.Committed, committed.Category);
    Assert.Equal("batch-5", committed.MutationBatchRef);
    Assert.Contains("mutation-damage-001", committed.MutationRefs);
    Assert.Equal("sha256:pre", committed.PreStateHash);
    Assert.Equal("sha256:post", committed.PostStateHash);
  }

  [Fact]
  public void CommittedEventSerializesDocumentedEnumValues()
  {
    var committed = CreateCommittedEvent();

    var json = JsonSerializer.Serialize(committed);

    Assert.Contains("\"EventType\":\"ENTITY_DAMAGED\"", json);
    Assert.Contains("\"Category\":\"COMMITTED\"", json);
    Assert.Contains("\"Visibility\":\"PUBLIC\"", json);
    Assert.Contains("\"PreStateHash\":\"sha256:pre\"", json);
    Assert.Contains("\"PostStateHash\":\"sha256:post\"", json);
  }

  [Fact]
  public void EventContractsDoNotExposeRuntimeBehaviorServices()
  {
    var contractTypes = typeof(ProposedEventRecord).Assembly
      .GetExportedTypes()
      .Where(type => type.Namespace is "Game.Shared.Events")
      .ToArray();

    Assert.NotEmpty(contractTypes);
    Assert.All(contractTypes, type =>
    {
      Assert.True(type.IsEnum || IsRecord(type));
      Assert.False(type.Name.EndsWith("Service", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Resolver", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Validator", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Executor", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Factory", StringComparison.Ordinal));
      Assert.False(type.Name.EndsWith("Queue", StringComparison.Ordinal));

      var declaredMethods = type
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(method => !method.IsSpecialName)
        .Select(method => method.Name)
        .ToArray();

      Assert.DoesNotContain("Enqueue", declaredMethods);
      Assert.DoesNotContain("Derive", declaredMethods);
      Assert.DoesNotContain("Resolve", declaredMethods);
      Assert.DoesNotContain("Validate", declaredMethods);
      Assert.DoesNotContain("Mutate", declaredMethods);
      Assert.DoesNotContain("CalculateHash", declaredMethods);
      Assert.DoesNotContain("CompareReplay", declaredMethods);
    });
  }

  private static CommittedEventRecord CreateCommittedEvent()
  {
    return new CommittedEventRecord(
      14,
      12,
      5,
      CommittedEventType.EntityDamaged,
      EventCategory.Committed,
      Phase.OnPlay,
      [new EventRef("CARD_INSTANCE", "card-instance-1", "BOARD", "player-1")],
      [new EventRef("ENTITY_INSTANCE", "entity-2", "BOARD", "player-2")],
      [
        new EventPayloadEntry("amount", new EventPayloadValue("INT", null, 3, null, [])),
        new EventPayloadEntry("damageSchool", new EventPayloadValue("STRING", "ARCANE", null, null, []))
      ],
      "batch-5",
      ["mutation-damage-001"],
      ["rng-roll-7"],
      "sha256:pre",
      "sha256:post",
      EventVisibility.Public);
  }

  private static bool IsRecord(Type type)
  {
    return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
      .Any(method => method.Name == "<Clone>$");
  }
}
