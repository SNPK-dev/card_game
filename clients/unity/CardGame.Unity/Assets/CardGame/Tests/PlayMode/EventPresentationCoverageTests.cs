#if UNITY_INCLUDE_TESTS
using System.Linq;
using System.Threading.Tasks;
using CardGame.Unity.Client.Audio;
using CardGame.Unity.Client.DebugTools;
using CardGame.Unity.Client.Match;
using CardGame.Unity.Client.Presentation;
using CardGame.Unity.Client.UI;
using CardGame.Unity.Client.UI.Presentation;
using Game.UnityClient.Contracts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CardGame.Unity.Client.Tests.PlayMode
{
  public sealed class EventPresentationCoverageTests
  {
    [Test]
    public void EverySupportedMatchEventHasAuthoredVisualAndAudioPresentation()
    {
      var director = new EventPresentationDirector();
      var audio = new AudioEventRouter();
      CollectionAssert.AreEquivalent(PresentationEventRouter.KnownPublicEventTypes,EventPresentationDirector.CoveredEventTypes);
      foreach (var eventType in EventPresentationDirector.CoveredEventTypes)
      {
        var descriptor = director.Resolve(new PublicMatchEventRecord { EventType = eventType });
        Assert.That(descriptor.IsGeneric, Is.False, eventType);
        Assert.That(descriptor.EventType, Is.EqualTo(eventType), eventType);
        Assert.That(audio.CueForEvent(eventType), Is.Not.EqualTo("event.generic"), eventType);
        Assert.That(descriptor.AnimationSignature, Is.Not.Empty, eventType);
      }
      var signatures=EventPresentationDirector.CoveredEventTypes
        .Select(eventType=>director.Resolve(new PublicMatchEventRecord { EventType=eventType }).AnimationSignature)
        .ToArray();
      Assert.That(signatures.Distinct().Count(),Is.EqualTo(signatures.Length),"Every public event must drive a distinct deterministic motion signature.");
    }

    [Test]
    public void DeathrattleIsNotPresentedAsOrdinaryDeath()
    {
      var director = new EventPresentationDirector();
      var death = director.Resolve(new PublicMatchEventRecord { EventType = "ENTITY_DIED" });
      var deathrattle = director.Resolve(new PublicMatchEventRecord { EventType = "DEATHRATTLE_TRIGGERED" });
      Assert.That(deathrattle.Choreography, Is.EqualTo(EventChoreography.DeathrattleEcho));
      Assert.That(deathrattle.Choreography, Is.Not.EqualTo(death.Choreography));
      Assert.That(new AudioEventRouter().CueForEvent("DEATHRATTLE_TRIGGERED"), Is.Not.EqualTo(new AudioEventRouter().CueForEvent("ENTITY_DIED")));
    }

    [Test]
    public void PayloadAndReferencesDriveModifierPresentation()
    {
      var evt = new PublicMatchEventRecord
      {
        EventType = "MODIFIER_APPLIED",
        SourceRefs = { new PublicEventRef { RefId = "source-minion" } },
        TargetRefs = { new PublicEventRef { RefId = "target-minion" } },
        Payload =
        {
          new PublicEventPayloadEntry { Key="mutationType", Value=new PublicEventPayloadValue { ValueKind="STRING", StringValue="MODIFY_ATTACK" } },
          new PublicEventPayloadEntry { Key="amount", Value=new PublicEventPayloadValue { ValueKind="INT", IntValue=3 } }
        }
      };
      var descriptor = new EventPresentationDirector().Resolve(evt);
      Assert.That(descriptor.Choreography, Is.EqualTo(EventChoreography.StatRise));
      Assert.That(descriptor.Title, Does.Contain("+3"));
      Assert.That(descriptor.SourceRef, Is.EqualTo("source-minion"));
      Assert.That(descriptor.TargetRef, Is.EqualTo("target-minion"));
    }

    [Test]
    public void PlayerRulesTextUsesKeywordTypographyWithoutTechnicalIdentifiers()
    {
      var formatted = CardRulesFormatter.Format("Deathrattle: Deal 2 damage.", false);
      Assert.That(formatted, Does.Contain("<color=").And.Contain("<b>DEATHRATTLE</b>"));
      Assert.That(CardRulesFormatter.ContainsRawTechnicalIdentifier(formatted), Is.False);
      Assert.That(CardRulesFormatter.ContainsRawTechnicalIdentifier("card_version_id=debug-1"), Is.True);
    }

    [Test]
    public void UnknownEventsRemainSilentAndNeverExposeDebugCopy()
    {
      var descriptor = new EventPresentationDirector().Resolve(new PublicMatchEventRecord { EventType="FUTURE_INTERNAL_EVENT" });
      Assert.That(descriptor.IsGeneric, Is.True);
      Assert.That(descriptor.Choreography, Is.EqualTo(EventChoreography.QuietRefresh));
      Assert.That(descriptor.Title, Is.Empty);
      Assert.That(descriptor.Subtitle, Is.Empty);
    }

    [Test]
    public void CardNumbersUseAuthoritativeDynamicProjection()
    {
      var presentation=new GameplayCardPresentation("card","card-v1","Card",7,"MINION_CARD","",null);
      var card=new PublicCardInstanceView { BaseManaCost=7,EffectiveManaCost=4 };
      Assert.That(CardGameScreen.DisplayedManaCost(card,presentation),Is.EqualTo(4));
      card.EffectiveManaCost=null;
      Assert.That(CardGameScreen.DisplayedManaCost(card,presentation),Is.EqualTo(7));

      var entity=new PublicEntityView { Attack=6,Health=9,Damage=4 };
      Assert.That(entity.Attack,Is.EqualTo(6));
      Assert.That(CardGameScreen.CurrentHealth(entity),Is.EqualTo(5));
      entity.Damage=12;
      Assert.That(CardGameScreen.CurrentHealth(entity),Is.Zero);
    }

    [Test]
    public async Task HoldingCardOpensPreviewAndSuppressesClickUntilRelease()
    {
      var node=new GameObject("card");var button=node.AddComponent<Button>();var preview=node.AddComponent<CardLongPressPreview>();
      var opened=false;var closed=false;preview.Configure(()=>opened=true,()=>closed=true);
      var existingEventSystem=Object.FindFirstObjectByType<EventSystem>();
      var eventSystem=existingEventSystem ?? new GameObject("events").AddComponent<EventSystem>();
      var pointer=new PointerEventData(eventSystem);
      preview.OnPointerDown(pointer);await Task.Delay(500);
      Assert.That(opened,Is.True);Assert.That(button.interactable,Is.True);
      preview.OnPointerExit(pointer);await Task.Delay(50);
      Assert.That(opened,Is.True,"The preview must remain open while the original left-button press is held.");
      preview.OnPointerUp(pointer);await Task.Delay(50);
      Assert.That(closed,Is.True);Assert.That(button.interactable,Is.True);
      Object.DestroyImmediate(node);
      if(existingEventSystem==null)Object.DestroyImmediate(eventSystem.gameObject);
    }

    [Test]
    public async Task HandCardSubmitsOnlyOnBattlefieldDrop()
    {
      var canvasNode=new GameObject("canvas");var canvas=canvasNode.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
      var boardNode=new GameObject("board");boardNode.transform.SetParent(canvasNode.transform,false);var board=boardNode.AddComponent<RectTransform>();board.anchorMin=board.anchorMax=new Vector2(.5f,.5f);board.sizeDelta=new Vector2(600,300);
      var cardNode=new GameObject("card");cardNode.transform.SetParent(canvasNode.transform,false);cardNode.AddComponent<RectTransform>().sizeDelta=new Vector2(140,194);
      var drag=cardNode.AddComponent<CardDragToBoard>();var submitted=0;drag.Configure(board,()=>submitted++);
      Assert.That(cardNode.GetComponent<CanvasGroup>(),Is.Not.Null,"Drag configuration must keep runtime-created cards pointer-safe without prefab setup.");
      var existingEventSystem=Object.FindFirstObjectByType<EventSystem>();
      var events=existingEventSystem ?? new GameObject("events").AddComponent<EventSystem>();
      var pointer=new PointerEventData(events){position=new Vector2(Screen.width/2f,Screen.height/2f)};
      drag.OnBeginDrag(pointer);drag.OnEndDrag(pointer);Assert.That(submitted,Is.Zero,"Accepted play keeps a brief readable commit pose before submission.");
      await Task.Delay(180);Assert.That(submitted,Is.EqualTo(1));
      pointer.position=Vector2.zero;drag.OnBeginDrag(pointer);drag.OnEndDrag(pointer);Assert.That(submitted,Is.EqualTo(1));
      Assert.That(drag,Is.Not.InstanceOf<IPointerClickHandler>(),"A simple click must select or preview a hand card, never cast it.");
      Object.DestroyImmediate(canvasNode);
      if(existingEventSystem==null)Object.DestroyImmediate(events.gameObject);
    }

    [Test]
    public void MotionTokensKeepHierarchyAndReducedMotionReadable()
    {
      Assert.That(MotionTokens.Instant,Is.LessThan(MotionTokens.Micro));
      Assert.That(MotionTokens.Micro,Is.LessThan(MotionTokens.Fast));
      Assert.That(MotionTokens.Fast,Is.LessThan(MotionTokens.Normal));
      Assert.That(MotionTokens.Normal,Is.LessThan(MotionTokens.Deliberate));
      Assert.That(MotionTokens.Deliberate,Is.LessThan(MotionTokens.Dramatic));
      Assert.That(MotionTokens.Duration(MotionTokens.Dramatic,true),Is.EqualTo(MotionTokens.Micro));
      Assert.That(MotionTokens.OutCubic(.5f),Is.GreaterThan(.5f));
      Assert.That(MotionTokens.InCubic(.5f),Is.LessThan(.5f));
    }

    [Test]
    public void PremiumCardPriorityCanvasKeepsPointerRaycasts()
    {
      var root=new GameObject("canvas");root.AddComponent<Canvas>();root.AddComponent<GraphicRaycaster>();
      var node=new GameObject("interactive-card");node.transform.SetParent(root.transform,false);node.AddComponent<RectTransform>();node.AddComponent<Image>();
      var rig=node.AddComponent<PremiumCardVisualRig>();rig.Configure(true,false);
      Assert.That(node.GetComponent<Canvas>()?.overrideSorting,Is.True);
      Assert.That(node.GetComponent<GraphicRaycaster>(),Is.Not.Null,"A nested sorting canvas must not make cards pointer-inert.");
      Object.DestroyImmediate(root);
    }

    [Test]
    public void LegalActionStatesRemainDistinctAndServerDriven()
    {
      var attack=new LegalActionViewModel(null,"ATTACK",true,string.Empty,System.Array.Empty<string>(),false,string.Empty,System.Array.Empty<string>());
      var play=new LegalActionViewModel(null,"PLAY_CARD",true,string.Empty,System.Array.Empty<string>(),false,string.Empty,System.Array.Empty<string>());
      Assert.That(CardGameScreen.ResolveInteractionState(true,true,false,false,play),Is.EqualTo(CardInteractionState.Playable));
      Assert.That(CardGameScreen.ResolveInteractionState(true,true,false,false,null),Is.EqualTo(CardInteractionState.Unplayable));
      Assert.That(CardGameScreen.ResolveInteractionState(false,true,false,false,attack),Is.EqualTo(CardInteractionState.ReadyToAttack));
      Assert.That(CardGameScreen.ResolveInteractionState(false,true,false,false,null),Is.EqualTo(CardInteractionState.Exhausted));
      Assert.That(CardGameScreen.ResolveInteractionState(false,false,false,false,null),Is.EqualTo(CardInteractionState.Neutral),"Opponent objects must not be presented as exhausted.");
      Assert.That(CardGameScreen.ResolveInteractionState(false,true,true,false,attack),Is.EqualTo(CardInteractionState.SelectedSource));
      Assert.That(CardGameScreen.ResolveInteractionState(false,true,true,true,attack),Is.EqualTo(CardInteractionState.ValidTarget),"Target state must win while targeting.");

      var node=new GameObject("state-card");node.AddComponent<RectTransform>();node.AddComponent<Image>();
      var rig=node.AddComponent<PremiumCardVisualRig>();rig.Configure(true,false,CardInteractionState.ReadyToAttack,"ATTACK");
      Assert.That(node.GetComponent<CanvasGroup>().alpha,Is.EqualTo(1f));
      Assert.That(node.transform.Find("Interaction State Badge"),Is.Not.Null);
      Object.DestroyImmediate(node);

      node=new GameObject("unplayable-card");node.AddComponent<RectTransform>();node.AddComponent<Image>();
      rig=node.AddComponent<PremiumCardVisualRig>();rig.Configure(false,false,CardInteractionState.Unplayable);
      Assert.That(node.GetComponent<CanvasGroup>().alpha,Is.EqualTo(.68f).Within(.001f));
      Assert.That(node.transform.Find("Interaction State Badge"),Is.Null,"Unavailable objects stay quiet instead of adding label clutter.");
      Object.DestroyImmediate(node);
    }

    [Test]
    public void OrdinaryEventsStayLocalWhileMajorEventsOwnScreenStaging()
    {
      Assert.That(PresentationEffectsHost.UsesFullScreenStaging(EventChoreography.DamageImpact),Is.False);
      Assert.That(PresentationEffectsHost.UsesFullScreenStaging(EventChoreography.DeathDissolve),Is.False);
      Assert.That(PresentationEffectsHost.UsesFullScreenStaging(EventChoreography.StatRise),Is.False);
      Assert.That(PresentationEffectsHost.UsesFullScreenStaging(EventChoreography.TurnRibbon),Is.False,"Ordinary turn changes use the compact dedicated ribbon, not a full-screen wash.");
      Assert.That(PresentationEffectsHost.HasVisualAnchor(new EventPresentationDescriptor{Choreography=EventChoreography.DamageImpact}),Is.False,"An undirected ordinary event must not imply a fake target at the canvas fallback point.");
      Assert.That(PresentationEffectsHost.HasVisualAnchor(new EventPresentationDescriptor{Choreography=EventChoreography.DamageImpact,TargetRef="entity-1"}),Is.True);
      Assert.That(PresentationEffectsHost.UsesFullScreenStaging(EventChoreography.SecretReveal),Is.True);
      Assert.That(PresentationEffectsHost.UsesFullScreenStaging(EventChoreography.Victory),Is.True);
    }

    [Test]
    public void GraveyardTransferCreatesAuthoredCardFlight()
    {
      var root=new GameObject("zone-transfer-root");
      var rig=root.AddComponent<ZoneTransferAnimationRig>();
      rig.Play(Vector3.zero,new Vector3(240f,80f,0f),"☠",false,null,"Fallen Card");

      var card=root.transform.Find("Zone Transfer Card");
      var glow=root.transform.Find("Zone Transfer Trail Glow");
      Assert.That(card,Is.Not.Null,"Committed graveyard motion must create a visible card, not only mutate the pile count.");
      Assert.That(glow,Is.Not.Null,"The card flight must include its authored motion trail.");
      Assert.That(card.Find("Moving Card Frame"),Is.Not.Null);
      Assert.That(card.Find("Moving Card Name"),Is.Not.Null);

      Object.DestroyImmediate(root);
    }

    [Test]
    public void EffectGlossaryExplainsDeathrattleWithoutTechnicalIdentifiers()
    {
      var description=CardEffectGlossary.Describe("Deathrattle: Draw a card.",null,false);
      Assert.That(description,Does.Contain("resolves after this minion dies"));
      Assert.That(CardRulesFormatter.ContainsRawTechnicalIdentifier(description),Is.False);
    }
  }
}
#endif
