using System;
using System.Linq;
using CardGame.Unity.Client.Api;
using CardGame.Unity.Client.Audio;
using CardGame.Unity.Client.Commands;
using CardGame.Unity.Client.Config;
using CardGame.Unity.Client.Errors;
using CardGame.Unity.Client.Events;
using CardGame.Unity.Client.Match;
using CardGame.Unity.Client.Presentation;
using CardGame.Unity.Client.State;
using CardGame.Unity.Client.DebugTools;
using CardGame.Unity.Client.UI;
using CardGame.Unity.Client.UI.Board;
using CardGame.Unity.Client.UI.Cards;
using CardGame.Unity.Client.UI.Interaction;
using CardGame.Unity.Client.UI.Presentation;
using CardGame.Unity.Client.UI.Shell;
using CardGame.Unity.Client.UI.Status;
using CardGame.Unity.Client.UI.Theme;
using Game.UnityClient.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace CardGame.Unity.Client.Tests
{
  public sealed class UnityClientFoundationTests
  {
    [Test]
    public void PresentationAssetCatalog_HasAuthoredFrameVfxAndEveryBakedAudioCue()
    {
      Assert.That(Resources.Load<Texture2D>("CardFrames/frame-arcane-v1"), Is.Not.Null, "authored card frame");
      Assert.That(Resources.Load<Texture2D>("PresentationVfx/arcane-compass-v1"), Is.Not.Null, "authored mechanic decal");
      foreach (var cue in AudioCueRegistry.DefaultCues())
      {
        Assert.That(cue.AssetRef, Does.StartWith("resource://Audio/Presentation/"));
        var resource = cue.AssetRef.Substring("resource://".Length);
        Assert.That(Resources.Load<AudioClip>(resource), Is.Not.Null, cue.CueId);
      }
    }

    [Test]
    public void PremiumThemes_ResolveAFrameWithoutChangingThemeContract()
    {
      var catalog = new GameThemeCatalog();
      Assert.That(catalog.Themes.Count, Is.EqualTo(3));
      foreach (var selected in catalog.Themes)
      {
        Assert.That(selected.Version, Is.EqualTo(GameTheme.CurrentVersion));
        Assert.That(Resources.Load<Texture2D>(selected.Sprites.CardFrameResource), Is.Not.Null, selected.Id);
        Assert.That(Resources.Load<Texture2D>(selected.Sprites.ParticleResource), Is.Not.Null, selected.Id+" vfx");
      }
    }

    [Test]
    public void Localization_EnglishAndRussianResolvePlayerFacingKeysWithoutRawFallback()
    {
      var localization = new LocalizationService();
      localization.SetLocale("en");
      Assert.That(localization.Text("play"), Is.EqualTo("PLAY"));
      localization.SetLocale("ru");
      Assert.That(localization.Text("play"), Is.EqualTo("ИГРАТЬ"));
      Assert.That(localization.Text("settings.subtitle"), Does.Contain("игровой процесс"));
      Assert.That(localization.Text("missing.key"), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Localization_UnknownLocaleFallsBackToEnglishAndPersistsSelection()
    {
      var localization = new LocalizationService();
      localization.SetLocale("de");
      Assert.That(localization.Locale, Is.EqualTo("en"));
      Assert.That(localization.Text("collection"), Is.EqualTo("COLLECTION"));
      localization.SetLocale("ru");
      Assert.That(new LocalizationService().Locale, Is.EqualTo("ru"));
    }

    [Test]
    public void ProductionPlayerApplication_UsesCanvasViewInsteadOfImmediateModeGui()
    {
      var controllerSource = System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/PlayerApplicationController.cs");
      var viewSource = System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/PlayerApplicationView.cs");
      Assert.That(controllerSource, Does.Not.Contain("private void OnGUI()"));
      Assert.That(viewSource, Does.Contain("ScreenSpaceOverlay"));
      Assert.That(viewSource, Does.Contain("ScaleWithScreenSize"));
      Assert.That(viewSource, Does.Contain("referenceResolution = new Vector2(1920, 1080)"));
    }

    [Test]
    public void ProductionPlayerApplication_ConsumesInteractivePlayerContracts()
    {
      var controllerSource = System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/PlayerApplicationController.cs");
      var viewSource = System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/PlayerApplicationView.cs");
      Assert.That(controllerSource, Does.Contain("/player/collection/{Uri.EscapeDataString(cardVersionId)}/favorite"));
      Assert.That(controllerSource, Does.Contain("/player/decks/{Uri.EscapeDataString(deckId)}/select"));
      Assert.That(controllerSource, Does.Contain("DeleteAsync<object>"));
      Assert.That(controllerSource, Does.Contain("PollGenerationJobAsync"));
      Assert.That(controllerSource, Does.Contain("SaveProfileSettingsAsync"));
      Assert.That(controllerSource, Does.Contain("musicVolume, effectsVolume, reducedMotion"));
      Assert.That(viewSource, Does.Contain("CollectionScreen"));
      Assert.That(viewSource, Does.Contain("DeckBuilderScreen"));
      Assert.That(viewSource, Does.Contain("ScrollGrid"));
      Assert.That(viewSource, Does.Contain("CollectionCardTile"));
      Assert.That(viewSource, Does.Contain("DeckBuilderCardTile"));
      Assert.That(viewSource, Does.Contain("ProviderOption"));
      Assert.That(viewSource, Does.Contain("ThemeOption"));
      Assert.That(viewSource, Does.Contain("LanguageOption"));
      Assert.That(viewSource, Does.Contain("SettingsOption"));
      Assert.That(viewSource, Does.Contain("PlayScreen"));
      Assert.That(viewSource, Does.Contain("ReplayScreen"));
      Assert.That(viewSource, Does.Contain("ProfileScreen"));
      Assert.That(viewSource, Does.Contain("InputSystemUIInputModule"));
    }
    [Test]
    public void BackendConfig_DefaultUrl_IsValidLocalHttpUrl()
    {
      var config = UnityBackendConfig.CreateRuntimeDefault();

      Assert.That(Uri.TryCreate(config.BackendBaseUrl, UriKind.Absolute, out var uri), Is.True);
      Assert.That(uri.Scheme, Is.EqualTo(Uri.UriSchemeHttp));
      Assert.That(uri.Host, Is.EqualTo("localhost"));
    }

    [Test]
    public void BackendApiClient_ComposesRelativeUrls()
    {
      var client = new BackendApiClient(UnityBackendConfig.CreateRuntimeDefault());

      Assert.That(client.ComposeUrl("/engine/capabilities"), Is.EqualTo("http://localhost:5000/engine/capabilities"));
      Assert.That(client.ComposeUrl("matches/demo/state"), Is.EqualTo("http://localhost:5000/matches/demo/state"));
    }

    [Test]
    public void CommandBuilder_RefusesMissingLegalActionTemplate()
    {
      var builder = new CommandBuilder();

      Assert.Throws<InvalidOperationException>(() => builder.Build(null, "token", "hash"));
    }

    [Test]
    public void CommandBuilder_IncludesExpectedPreStateHash()
    {
      var builder = new CommandBuilder();
      var request = builder.Build(
        new MatchCommandDto { CommandType = "END_TURN", PlayerId = "player-1" },
        "session-token",
        "state-hash");

      Assert.That(request.ExpectedPreStateHash, Is.EqualTo("state-hash"));
      Assert.That(request.SessionToken, Is.EqualTo("session-token"));
      Assert.That(request.Command.CommandType, Is.EqualTo("END_TURN"));
    }

    [Test]
    public void CommandBuilder_UsesSelectedServerProvidedTargetRef()
    {
      var builder = new CommandBuilder();
      var request = builder.BuildWithSelectedRef(
        new MatchCommandDto
        {
          CommandType = "ATTACK",
          PlayerId = "player-1",
          Refs = new() { ["attackerEntityInstanceId"] = "entity-a" }
        },
        "session-token",
        "state-hash",
        "targetEntityInstanceId",
        "entity-b");

      Assert.That(request.Command.Refs["attackerEntityInstanceId"], Is.EqualTo("entity-a"));
      Assert.That(request.Command.Refs["targetEntityInstanceId"], Is.EqualTo("entity-b"));
    }

    [Test]
    public void LegalActionPresenter_CompleteTargetCommandSubmitsDirectly()
    {
      var presenter = new LegalActionPresenter();

      var view = presenter.Create(new MatchCommandDto
      {
        CommandType = "ATTACK",
        PlayerId = "player-1",
        Refs = new()
        {
          ["attackerEntityInstanceId"] = "entity-a",
          ["targetEntityInstanceId"] = "entity-b"
        }
      });

      Assert.That(view.Enabled, Is.True);
      Assert.That(view.RequiresTargetSelection, Is.False);
    }

    [Test]
    public void LegalActionPresenter_ServerCandidateRefsEnterTargetMode()
    {
      var presenter = new LegalActionPresenter();

      var view = presenter.Create(new MatchCommandDto
      {
        CommandType = "ATTACK",
        PlayerId = "player-1",
        Refs = new()
        {
          ["attackerEntityInstanceId"] = "entity-a",
          ["candidateTargetEntityInstanceIds"] = "entity-b, entity-c"
        }
      });

      Assert.That(view.Enabled, Is.True);
      Assert.That(view.RequiresTargetSelection, Is.True);
      Assert.That(view.CandidateTargetRefs, Is.EquivalentTo(new[] { "entity-b", "entity-c" }));
    }

    [Test]
    public void LegalActionPresenter_TargetedBattlecryCardEntersTargetMode()
    {
      var view=new LegalActionPresenter().Create(new MatchCommandDto{CommandType="PLAY_CARD",PlayerId="player-1",Refs=new(){["cardInstanceId"]="spark",["candidateTargetEntityInstanceIds"]="enemy-a,enemy-b"}});
      Assert.That(view.Enabled,Is.True);Assert.That(view.RequiresTargetSelection,Is.True);Assert.That(view.CandidateTargetRefs,Is.EquivalentTo(new[]{"enemy-a","enemy-b"}));
    }

    [Test]
    public void RussianRulesFormatterTranslatesBattlecryDamageText()
    {
      var formatted=CardRulesFormatter.Format("Battlecry: Deal 1 damage to a minion.",true);
      Assert.That(formatted,Does.Contain("БОЕВОЙ КЛИЧ"));Assert.That(formatted,Does.Contain("Нанесите"));Assert.That(formatted,Does.Not.Contain("Deal"));
    }

    [Test]
    public void EmptyRulesText_DoesNotInventARepeatedEffectSentence()
    {
      Assert.That(CardRulesFormatter.Format(string.Empty,false),Is.Empty);
      Assert.That(CardRulesFormatter.Format(string.Empty,true),Is.Empty);
    }

    [TestCase("Randomly split 3 damage among enemy minions.")]
    [TestCase("Prevent the next 2 damage dealt to a friendly minion.")]
    [TestCase("Activate: Resurrect a 2/2 friendly minion.")]
    [TestCase("Battlecry: Resurrect a 2/2 friendly minion. Other friendly minions have +1 Attack.")]
    [TestCase("Restore 3 Health to a friendly minion. Draw a card.")]
    public void RussianRulesFormatterCoversProductionStarterRules(string rulesText)
    {
      var formatted=CardRulesFormatter.Format(rulesText,true);
      Assert.That(formatted,Does.Not.Match(@"\b(Randomly|split|damage|enemy|Prevent|friendly|minion|Activate|Resurrect|Other|Attack|Restore|Health|Draw|card)\b"));
    }

    [Test]
    public void TargetSelection_SelectedServerRefIsMergedIntoCommand()
    {
      var presenter = new LegalActionPresenter();
      var action = presenter.Create(new MatchCommandDto
      {
        CommandType = "ATTACK",
        PlayerId = "player-1",
        Refs = new()
        {
          ["attackerEntityInstanceId"] = "entity-a",
          ["candidateTargetEntityInstanceIds"] = "entity-b"
        }
      });
      var controller = new TargetSelectionController();

      Assert.That(controller.Begin(action), Is.True);
      var selected = controller.Select("entity-b");

      Assert.That(selected.Refs["attackerEntityInstanceId"], Is.EqualTo("entity-a"));
      Assert.That(selected.Refs["targetEntityInstanceId"], Is.EqualTo("entity-b"));
      Assert.That(selected.Refs.ContainsKey("candidateTargetEntityInstanceIds"), Is.False);
      Assert.That(controller.Current.IsActive, Is.False);
    }

    [Test]
    public void TargetHighlight_DistinguishesSelectedSourceFromLegalTargets()
    {
      var action = new LegalActionPresenter().Create(new MatchCommandDto
      {
        CommandType = "ATTACK", PlayerId = "player-1",
        Refs = new() { ["attackerEntityInstanceId"] = "entity-a", ["candidateTargetEntityInstanceIds"] = "entity-b" }
      });
      var controller = new TargetSelectionController(); controller.Begin(action);
      var highlight = new TargetHighlightView(controller.Current);
      Assert.That(highlight.IsSource("entity-a"), Is.True);
      Assert.That(highlight.IsSource("entity-b"), Is.False);
      Assert.That(highlight.IsHighlighted("entity-b"), Is.True);
    }

    [Test]
    public void TargetSelection_RejectsNonHighlightedTarget()
    {
      var presenter = new LegalActionPresenter();
      var action = presenter.Create(new MatchCommandDto
      {
        CommandType = "ATTACK",
        PlayerId = "player-1",
        Refs = new()
        {
          ["attackerEntityInstanceId"] = "entity-a",
          ["candidateTargetEntityInstanceIds"] = "entity-b"
        }
      });
      var controller = new TargetSelectionController();

      controller.Begin(action);

      Assert.That(controller.Select("entity-c"), Is.Null);
      Assert.That(controller.Current.IsActive, Is.True);
    }

    [Test]
    public void TargetSelection_CancelClearsState()
    {
      var presenter = new LegalActionPresenter();
      var action = presenter.Create(new MatchCommandDto
      {
        CommandType = "ATTACK",
        PlayerId = "player-1",
        Refs = new()
        {
          ["attackerEntityInstanceId"] = "entity-a",
          ["candidateTargetEntityInstanceIds"] = "entity-b"
        }
      });
      var controller = new TargetSelectionController();

      controller.Begin(action);
      controller.Cancel();

      Assert.That(controller.Current.IsActive, Is.False);
    }

    [Test]
    public void GameplayCard_SourceClickRecognizesTargetedAttackWithoutTreatingCandidateAsSource()
    {
      var action = new LegalActionPresenter().Create(new MatchCommandDto
      {
        CommandType = "ATTACK", PlayerId = "player-1",
        Refs = new() { ["attackerEntityInstanceId"] = "entity-a", ["candidateTargetEntityInstanceIds"] = "entity-b,entity-c" }
      });
      Assert.That(CardGameScreen.OriginMatches(action, "entity-a"), Is.True);
      Assert.That(CardGameScreen.OriginMatches(action, "entity-b"), Is.False);
    }

    [Test]
    public void BoardCardsKeepBaseManaAndWeaponsRenderInHeroLoadout()
    {
      var source = System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/CardGameScreen.cs");
      Assert.That(source, Does.Contain("else if(displayedMana.HasValue)"));
      Assert.That(source, Does.Contain("Card Name Plate"));
      Assert.That(source, Does.Contain("RenderEquipped(target,player.Weapon"));
      Assert.That(source, Does.Contain("RenderEquipped(target,player.Location"));
      Assert.That(source, Does.Not.Contain("RenderEquipped(playerBoard, self?.Weapon"));
      Assert.That(source, Does.Contain("CancelTargeting"));
      Assert.That(source, Does.Not.Contain("Commands are disabled for this mode or reconnect state."));
    }

    [Test]
    public void HeroCombatUi_UsesHeroEntityReferenceAndAuthoredPortraits()
    {
      var player=new PublicPlayerStateView{PlayerId="player-2",Hero=new PublicEntityView{EntityInstanceId="hero-player-2",Health=30}};
      Assert.That(CardGameScreen.HeroTargetReference(player),Is.EqualTo("hero-player-2"));
      Assert.That(Resources.Load<Texture2D>("HeroArt/astral-warden"),Is.Not.Null);
      Assert.That(Resources.Load<Texture2D>("HeroArt/cinder-vanguard"),Is.Not.Null);
    }

    [Test]
    public void GameplayBoard_BuildsDedicatedHeroStagesAndRightCombatRail()
    {
      var root=new GameObject("screen-root");
      try
      {
        new CardGameScreen().Build(root.transform);
        var names=root.GetComponentsInChildren<Transform>(true).Select(item=>item.name).ToArray();
        Assert.That(names,Does.Contain("Opponent Hero Stage"));
        Assert.That(names,Does.Contain("Player Hero Stage"));
        Assert.That(names,Does.Contain("Right Combat Rail"));
        Assert.That(names,Does.Contain("Opponent Graveyard Slot"));
        Assert.That(names,Does.Contain("Premium Turn Action Button"));
        Assert.That(names,Does.Contain("Player Graveyard Slot"));
        Assert.That(names,Does.Contain("Legal Card Play Lane"));
        var opponentHero=root.GetComponentsInChildren<Transform>(true).Single(item=>item.name=="Opponent Hero Stage");
        var opponentHand=root.GetComponentsInChildren<Transform>(true).Single(item=>item.name=="Opponent Hand");
        var playerHero=root.GetComponentsInChildren<Transform>(true).Single(item=>item.name=="Player Hero Stage");
        var playerHand=root.GetComponentsInChildren<Transform>(true).Single(item=>item.name=="Player Hand");
        Assert.That(opponentHero.GetSiblingIndex(),Is.LessThan(opponentHand.GetSiblingIndex()));
        Assert.That(playerHero.GetSiblingIndex(),Is.GreaterThan(playerHand.GetSiblingIndex()));
      }
      finally{UnityEngine.Object.DestroyImmediate(root);}
    }

    [Test]
    public void HeroLoadout_PreservesVerticalCardShapeAndShowsDistinctPower()
    {
      var source=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/CardGameScreen.cs");
      Assert.That(source,Does.Contain("cardLayout.minWidth=78"));
      Assert.That(source,Does.Contain("cardLayout.minHeight=88"));
      Assert.That(source,Does.Contain("Player Hero Power"));
      Assert.That(source,Does.Contain("ASTRAL RESTORATION"));
      Assert.That(source,Does.Contain("CINDER SPARK"));
      Assert.That(source,Does.Contain("USE_HERO_POWER"));
      Assert.That(source,Does.Contain("FindHeroPowerInteraction(actions,source)"),"The visible power must resolve the authoritative hero-power action even if transport refs vary.");
      Assert.That(source,Does.Contain("ShowHeroPowerPreview(player)"));
      Assert.That(source,Does.Contain("ShowHeroPreview(player,local)"));
      Assert.That(source,Does.Contain("power.AddComponent<Button>()"));
      Assert.That(source,Does.Contain("preview.ConsumeClick()"),"Releasing a held preview must not also activate the power.");
      var visual=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/Presentation/PremiumCardVisualRig.cs");
      Assert.That(visual,Does.Contain("sortingOrder=10"),"Idle hero and card canvases must render above later battlefield panels.");
    }

    [Test]
    public void BoardReflow_UsesStableEntityRefsAndReducedMotionAwareSettle()
    {
      var screen=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/CardGameScreen.cs");
      var reflow=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/BoardReflowRig.cs");
      Assert.That(screen,Does.Contain("lastKnownWorldAnchors.TryGetValue(presentationRef"));
      Assert.That(screen,Does.Not.Contain("rig.PlayMove"),"Existing cards must not slide on every authoritative refresh.");
      Assert.That(screen,Does.Contain(".PlaySpawn"));
      Assert.That(reflow,Does.Contain("Time.unscaledDeltaTime"));
      Assert.That(reflow,Does.Contain("MotionTokens.Duration(MotionTokens.Normal,reducedMotion)"));
      Assert.That(reflow,Does.Contain("SuspendCardMotion"),"Reflow and hover rigs must not fight over the same RectTransform.");
    }


    [Test]
    public void HeldPreview_IsAbovePriorityCardsAndLegacyPresentationIsPlayerFacing()
    {
      var screen=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/CardGameScreen.cs");
      var view=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/PlayerApplicationView.cs");
      var effects=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/Presentation/PresentationEffectsHost.cs");
      Assert.That(screen,Does.Contain("previewCanvas.sortingOrder=32760"));
      Assert.That(screen,Does.Contain("pauseCanvas.sortingOrder=32750"));
      Assert.That(screen,Does.Contain("pauseOverlay.AddComponent<GraphicRaycaster>()"));
      Assert.That(screen,Does.Contain("Pause Illustrated Background"));
      Assert.That(screen,Does.Contain("typeof(RectTransform),typeof(RawImage)"),"The pause backdrop must not add RawImage to a Panel that already owns an Image graphic.");
      Assert.That(screen,Does.Contain("ConfigurePreviewBadges"));
      Assert.That(screen,Does.Contain("PreviewBadge(card.transform"));
      Assert.That(view,Does.Contain("CardArtLarge(tile.transform,card,234"),"Deck cards must use the collection card composition with a full square illustration.");
      Assert.That(view,Does.Contain("raw.uvRect=new Rect(0,0,1,1)"));
      Assert.That(PlayerApplicationView.PresentedCardName("legacy-a","Runesmith A123"),Does.Not.StartWith("Runesmith"));
      Assert.That(PlayerApplicationView.PresentedCardName("legacy-a","Runesmith A123"),Is.EqualTo(PlayerApplicationView.PresentedCardName("legacy-a","Runesmith A123")));
      Assert.That(PlayerApplicationView.PresentedCardName("modern","Nixara Quickflame"),Is.EqualTo("Nixara Quickflame"));
      var legacyNames=Enumerable.Range(1,15).Select(index=>PlayerApplicationView.PresentedCardName($"player_job_seed_{index:00}",$"Runesmith {index:00}")).ToArray();
      Assert.That(legacyNames.Distinct().Count(),Is.EqualTo(15));
      var legacyArt=Enumerable.Range(1,15).Select(index=>PlayerApplicationView.PackagedArtResourceFor($"player_job_seed_{index:00}",$"procedural://player_job_seed_{index:00}")).ToArray();
      Assert.That(legacyArt.Distinct().Count(),Is.EqualTo(15),"Every ordinal card in a generated set must receive unique packaged art.");
      Assert.That(effects,Does.Contain("EventChoreography.CardFlight or EventChoreography.DrawArc or EventChoreography.QuietRefresh"));
      Assert.That(effects,Does.Contain("semanticParticles"));
      Assert.That(effects,Does.Contain("EventChoreography.DeathDissolve or EventChoreography.DamageImpact"),"Dedicated combat/death cues must suppress duplicate host flashes and audio.");
    }

    [Test]
    public void AttackTargeting_HidesArrowAndAwaitsLungeBeforeSubmittingCommand()
    {
      var screen=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/CardGameScreen.cs");
      var hide=screen.IndexOf("targetingArrow?.Hide();",screen.IndexOf("private async void SelectTarget",System.StringComparison.Ordinal),System.StringComparison.Ordinal);
      var lunge=screen.IndexOf("await combatLunge.PlayAsync",hide,System.StringComparison.Ordinal);
      var submit=screen.IndexOf("TargetSelectedRequested?.Invoke(targetRef);",lunge,System.StringComparison.Ordinal);
      Assert.That(hide,Is.GreaterThanOrEqualTo(0));
      Assert.That(lunge,Is.GreaterThan(hide));
      Assert.That(submit,Is.GreaterThan(lunge));
    }

    [Test]
    public void DeathRemoval_ReactsBeforeRefreshThenTransfersToGraveyard()
    {
      var bootstrap=System.IO.File.ReadAllText("Assets/CardGame/Runtime/Debug/DebugMatchBootstrapper.cs");
      var transfer=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/ZoneTransferAnimationRig.cs");
      var reaction=bootstrap.IndexOf("await screen.AnimatePreRemovalDeathAsync",System.StringComparison.Ordinal);
      var refresh=bootstrap.IndexOf("await RefreshStateAsync()",reaction,System.StringComparison.Ordinal);
      var zone=bootstrap.IndexOf("screen.AnimateCommittedZoneEvent",refresh,System.StringComparison.Ordinal);
      Assert.That(reaction,Is.GreaterThanOrEqualTo(0));
      Assert.That(refresh,Is.GreaterThan(reaction));
      Assert.That(zone,Is.GreaterThan(refresh),"Removal/graveyard travel must start only after the lethal reaction and authoritative refresh.");
      Assert.That(transfer,Does.Contain("PlayDeathReaction"));
      Assert.That(transfer,Does.Contain("TaskCompletionSource<bool>"),"The event queue must await the mandatory local death reaction.");
      Assert.That(transfer,Does.Contain("MotionTokens.Duration(MotionTokens.Deliberate,reducedMotion)"));
    }

    [Test]
    public void TurnTransition_UsesCompactRibbonAndCoalescesQuietChainTail()
    {
      var screen=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/CardGameScreen.cs");
      var ribbon=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/TurnTransitionRig.cs");
      var host=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/Presentation/PresentationEffectsHost.cs");
      Assert.That(screen,Does.Contain("AnimateTurnEvent"));
      Assert.That(ribbon,Does.Contain("Compact Turn Transition"));
      Assert.That(ribbon,Does.Contain("group.blocksRaycasts=false"));
      Assert.That(ribbon,Does.Contain("MotionTokens.Duration(MotionTokens.Fast,reducedMotion)"));
      Assert.That(host,Does.Contain("resolved.Choreography==EventChoreography.TurnRibbon"));
      Assert.That(host,Does.Not.Contain("MatchOpening or EventChoreography.TurnRibbon"));
      Assert.That(host,Does.Contain("DroppedLowPriorityCount++"));
    }

    [Test]
    public void LegalStateReadability_UsesOracleOriginsAndPreservesOpponentNeutrality()
    {
      var board=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/CardGameScreen.cs");
      var visual=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/Presentation/PremiumCardVisualRig.cs");
      Assert.That(board,Does.Contain("ResolveInteractionState(true,true"));
      Assert.That(board,Does.Contain("FindOriginInteraction(actions"),"Availability must come from server legal-action origins.");
      Assert.That(board,Does.Contain("interactionEnabled&&targetSelection?.IsActive!=true"),"Unrelated direct handlers must be removed while target selection owns input.");
      Assert.That(board,Does.Contain("commandsEnabled&&!choosingTarget&&endTurn!=null"),"End turn must not remain clickable during target selection.");
      Assert.That(board,Does.Contain("if(!local)return CardInteractionState.Neutral"));
      Assert.That(board,Does.Not.Contain("EffectiveManaCost <="),"Unity must not infer playability from mana.");
      Assert.That(visual,Does.Contain("CardInteractionState.ReadyToAttack"));
      Assert.That(visual,Does.Contain("Interaction State Badge"));
      Assert.That(visual,Does.Contain("CardInteractionState.Unplayable or CardInteractionState.Exhausted"));
    }

    [Test]
    public void InvalidCardDrop_ExplainsRejectionBeforeTheLaneSettles()
    {
      var drag=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/CardDragToBoard.cs");
      var screen=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/CardGameScreen.cs");
      Assert.That(drag,Does.Contain("Action<bool> dragEnded"),"Drop resolution must distinguish accepted and rejected releases.");
      Assert.That(drag,Does.Contain("dragEnded?.Invoke(accepted)"));
      Assert.That(screen,Does.Contain("ResolvePlayDropLane"));
      Assert.That(screen,Does.Contain("CARD RETURNED — DROP INSIDE THE PLAY LANE"));
      Assert.That(drag,Does.Contain("MotionTokens.Normal-MotionTokens.Fast"),"Reject feedback must remain readable without blocking gameplay time.");
      Assert.That(drag,Does.Contain("rejectFeedbackEnded?.Invoke()"));
    }

    [Test]
    public void TargetingArrow_UsesCanvasLocalPointerGeometryAndManyDottedSegments()
    {
      var root=new GameObject("arrow-root");
      try
      {
        var canvas=root.AddComponent<Canvas>();var rig=root.AddComponent<TargetingArrowRig>();rig.Initialize(canvas);
        Assert.That(rig.SegmentCapacity,Is.GreaterThanOrEqualTo(48));
        var overlayCanvas=root.transform.Find("Targeting Arrow Canvas Host").GetComponent<Canvas>();
        Assert.That(overlayCanvas.overrideSorting,Is.True);
        Assert.That(overlayCanvas.sortingOrder,Is.GreaterThan(30000));
        var source=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/TargetingArrowRig.cs");
        Assert.That(source,Does.Contain("ScreenPointToLocalPointInRectangle"));
        Assert.That(source,Does.Contain("Mouse.current?.position.ReadValue()"));
        Assert.That(source,Does.Not.Contain("Input.mousePosition"),"The packaged player uses the new Input System and legacy Input.mousePosition throws every frame.");
        Assert.That(source,Does.Contain("AddTriangle"),"The targeting mesh must include a real arrowhead.");
        Assert.That(source,Does.Contain("useFallbackSource=source!=null"),"Dynamic UI redraws must retain the arrow's last valid source point.");
        Assert.That(source,Does.Contain("HideInteractive"));
        var screen=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/CardGameScreen.cs");
        Assert.That(screen,Does.Not.Contain("targetingArrow?.ShowFixed"),"The red target arrow must disappear before the awaited lunge, not reappear after committed damage.");
        var geometry=typeof(TargetingArrowRig).GetMethod("ApplyGeometry",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        Assert.That(geometry,Is.Not.Null);geometry.Invoke(rig,new object[]{new Vector2(-300,-140),new Vector2(310,170)});
        var host=root.transform.Find("Targeting Arrow Canvas Host");
        var graphic=host.GetComponent<TargetingArrowGraphic>();
        var populate=typeof(TargetingArrowGraphic).GetMethod("OnPopulateMesh",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(UnityEngine.UI.VertexHelper)},null);
        using(var vertices=new UnityEngine.UI.VertexHelper()){populate.Invoke(graphic,new object[]{vertices});Assert.That(rig.RenderedSegmentCount,Is.GreaterThan(20),"A long target vector must render an actual dotted line, not one red point.");Assert.That(vertices.currentVertCount,Is.GreaterThan(rig.RenderedSegmentCount*8),"The mesh must contain outlined dashes and an arrowhead.");}
      }
      finally{UnityEngine.Object.DestroyImmediate(root);}
    }

    [Test]
    public void CardDragAndPreview_RequireDropLaneAndDoNotAppendGlossary()
    {
      var drag=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/CardDragToBoard.cs");
      var board=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/CardGameScreen.cs");
      var application=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/PlayerApplicationView.cs");
      var applicationController=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/PlayerApplicationController.cs");
      Assert.That(drag,Does.Not.Contain("IPointerClickHandler"));
      Assert.That(drag,Does.Contain("SnapBack"));
      Assert.That(board,Does.Contain("Legal Card Play Lane"));
      Assert.That(board,Does.Contain("Compact Cancel Target Button"));
      Assert.That(board,Does.Contain("Preview Rules Surface"));
      Assert.That(board,Does.Contain("Preview Opaque Backing"));
      Assert.That(board,Does.Contain("var interactionEnabled=commandsEnabled&&!reconnecting"));
      Assert.That(board,Does.Contain("new List<LegalActionViewModel>()"),"Blocking client states must remove direct card/entity input handlers, not only disable the action dock.");
      Assert.That(board,Does.Not.Contain("CardEffectGlossary.Describe"));
      Assert.That(application,Does.Contain("Preview Rules Surface"));
      Assert.That(application,Does.Contain("Preview Opaque Backing"));
      Assert.That(application,Does.Not.Contain("CardEffectGlossary.Describe"));
      Assert.That(applicationController,Does.Contain("--record-seeded-match-evidence"),"Packaged gameplay evidence must not require an external generation provider.");
      Assert.That(applicationController,Does.Contain("DebugDemoSeeder.LoadSeedPresentations()"),"Seeded evidence must render the packaged catalog instead of placeholder names and rules.");
    }

    [Test]
    public void CombatDamageOverlay_StartsAfterStateRefreshAndRemainsReadable()
    {
      var bootstrap=System.IO.File.ReadAllText("Assets/CardGame/Runtime/Debug/DebugMatchBootstrapper.cs");
      var overlay=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/CombatDamageOverlayRig.cs");
      var refresh=bootstrap.LastIndexOf("await RefreshStateAsync()",System.StringComparison.Ordinal);
      var combat=bootstrap.LastIndexOf("screen.AnimateCommittedCombatEvent",System.StringComparison.Ordinal);
      Assert.That(refresh,Is.GreaterThanOrEqualTo(0));
      Assert.That(combat,Is.GreaterThan(refresh),"Damage numbers must start after refreshed health values are rendered.");
      Assert.That(overlay,Does.Contain("holdDuration=reducedMotion?.16f:.34f"),"Ordinary damage feedback must remain readable without stalling repeated effects.");
      Assert.That(overlay,Does.Contain("MotionTokens.Duration(MotionTokens.Fast,reducedMotion)"));
      Assert.That(overlay,Does.Contain("rect.position=worldPosition"));
      Assert.That(bootstrap,Does.Contain("screen.AnimateCommittedCombatEvent"));
      Assert.That(bootstrap,Does.Contain("CaptureCommittedAttack"));
      Assert.That(bootstrap,Does.Contain("committedAttackArrowPending=false"),"Only the primary committed attack impact should own the directional arrow.");
      var board=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/CardGameScreen.cs");
      Assert.That(board,Does.Contain("targetingArrow?.ShowFixed(sourceWorld,targetWorld,reducedMotion)"));
      Assert.That(board,Does.Contain("Resources.LoadAll<Texture2D>(\"CardArt\")"),"Seeded/runtime instance refs must still resolve packaged authored card art.");
      Assert.That(board,Does.Contain("entity?.SourceCardInstanceId??presentationRef"));
    }

    [Test]
    public void PersistedProductionDeckPath_SkipsDebugSeedAndLegacyCardsUsePackagedArt()
    {
      var bootstrap=System.IO.File.ReadAllText("Assets/CardGame/Runtime/Debug/DebugMatchBootstrapper.cs");
      var overloadStart=bootstrap.IndexOf("StartProductionMatchAsync(string playerId",System.StringComparison.Ordinal);
      var overloadEnd=bootstrap.IndexOf("private static async Task<bool> PrepareBackendAsync",overloadStart,System.StringComparison.Ordinal);
      Assert.That(overloadStart,Is.GreaterThanOrEqualTo(0));
      Assert.That(overloadEnd,Is.GreaterThan(overloadStart));
      var productionOverload=bootstrap.Substring(overloadStart,overloadEnd-overloadStart);
      Assert.That(productionOverload,Does.Contain("PrepareBackendAsync()"));
      Assert.That(productionOverload,Does.Not.Contain("PrepareProductionContentAsync()"),"Persisted player decks must not be blocked by developer seed revision drift.");

      var first=PlayerApplicationView.PackagedArtResourceFor("legacy-card-a","procedural://legacy-card-a");
      var second=PlayerApplicationView.PackagedArtResourceFor("legacy-card-b","procedural://legacy-card-b");
      Assert.That(first,Does.StartWith("CardArt/unity_test_"));
      Assert.That(second,Does.StartWith("CardArt/unity_test_"));
      Assert.That(Resources.Load<Texture2D>(first),Is.Not.Null);
      Assert.That(Resources.Load<Texture2D>(second),Is.Not.Null);
      var gameplay=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/CardGameScreen.cs");
      Assert.That(gameplay,Does.Contain("PlayerApplicationView.PackagedArtResourceFor(card.CardId,\"procedural://\"+card.CardId)"),"Legacy authored-art mapping must also cover the live match hand and board.");
      Assert.That(PlayerApplicationView.PackagedArtResourceFor("external-card","C:/generated/card.png"),Is.Empty);

      var view=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/PlayerApplicationView.cs");
      Assert.That(view,Does.Contain("CardArtAnchored(tile.transform, card"));
      Assert.That(view,Does.Contain("()=>ShowCollectionPreview(card,theme)"),"Deck art must expose an explicit click-to-preview path.");
      Assert.That(view,Does.Contain("close.onClick.AddListener(HideCollectionPreview)"),"Click-opened preview must have an obvious close path.");
      var controller=System.IO.File.ReadAllText("Assets/CardGame/Runtime/UI/PlayerApplicationController.cs");
      Assert.That(controller,Does.Contain("DebugMatchBootstrapper.LastStartupError"),"Player-visible match failures must report the actual client/backend error.");
    }

    [TestCase("SELF", "PRIVATE", true)]
    [TestCase("SELF", "VISIBLE", true)]
    [TestCase("SELF", "HIDDEN", false)]
    [TestCase("OPPONENT", "PRIVATE", false)]
    public void GameplayCard_VisibilityUsesProjectionSemantics(string relationship, string visibility, bool expected)
    {
      Assert.That(CardGameScreen.IsCardVisibleToPlayer(relationship, visibility), Is.EqualTo(expected));
    }

    [Test]
    public void CommandBuilder_ChoiceCommandUsesServerProvidedOptionId()
    {
      var builder = new CommandBuilder();
      var request = builder.BuildResolveChoice(
        new MatchCommandDto { CommandType = "RESOLVE_CHOICE", PlayerId = "player-1" },
        "session-token",
        "state-hash",
        "choice-1",
        "option-2");

      Assert.That(request.Command.CommandType, Is.EqualTo("RESOLVE_CHOICE"));
      Assert.That(request.Command.Refs["choiceId"], Is.EqualTo("choice-1"));
      Assert.That(request.Command.Refs["selectedOptionId"], Is.EqualTo("option-2"));
    }

    [Test]
    public void MatchStateStore_ReplacesStateOnReconnect()
    {
      var store = new MatchStateStore();
      store.ReplaceFromServer(new MatchStateView { MatchId = "old", StateHash = "old-hash", NextEventSeq = 3 });

      store.ReplaceAfterReconnect(new MatchStateView { MatchId = "new", StateHash = "new-hash", NextEventSeq = 8 }, 8);

      Assert.That(store.Current.MatchId, Is.EqualTo("new"));
      Assert.That(store.StateHash, Is.EqualTo("new-hash"));
      Assert.That(store.LastSeenEventSeq, Is.EqualTo(7));
    }

    [Test]
    public void MatchStateStore_DedupesEventsByEventSeq()
    {
      var store = new MatchStateStore();
      var evt = new PublicMatchEventRecord { EventSeq = 12, EventType = "TURN_ENDED" };

      Assert.That(store.TryAcceptEvent(evt), Is.True);
      Assert.That(store.TryAcceptEvent(evt), Is.False);
      Assert.That(store.LastSeenEventSeq, Is.EqualTo(12));
    }

    [Test]
    public void ReconnectReplacement_AllowsMissedEventsToBeAcceptedOnce()
    {
      var store = new MatchStateStore();
      store.ReplaceFromServer(new MatchStateView { MatchId = "match", StateHash = "old", NextEventSeq = 2 });
      store.TryAcceptEvent(new PublicMatchEventRecord { EventSeq = 1, EventType = "TURN_STARTED" });

      store.ReplaceAfterReconnect(new MatchStateView { MatchId = "match", StateHash = "new", NextEventSeq = 4 }, 4);

      Assert.That(store.TryAcceptEvent(new PublicMatchEventRecord { EventSeq = 2, EventType = "TURN_ENDED" }), Is.True);
      Assert.That(store.TryAcceptEvent(new PublicMatchEventRecord { EventSeq = 2, EventType = "TURN_ENDED" }), Is.False);
    }

    [TestCase("COMMAND_PRE_STATE_HASH_MISMATCH")]
    [TestCase("MATCH_SESSION_FORBIDDEN")]
    [TestCase("RATE_LIMITED")]
    [TestCase("SERVER_FAULT")]
    public void UnityErrorMapper_MapsKeyBackendCodes(string code)
    {
      var message = UnityErrorMapper.ToDebugMessage(new UnityError { Code = code, Message = "backend message" });

      Assert.That(message, Does.Contain(code));
      Assert.That(message, Does.Not.Contain("No API error"));
    }

    [Test]
    public void UnityErrorMapper_ReplayMismatchIsBlockingDiagnostic()
    {
      var message = UnityErrorMapper.ToDebugMessage(new UnityError { Code = "REPLAY_MISMATCH", Message = "hash mismatch" });

      Assert.That(message, Does.Contain("Replay verification failed"));
    }

    [Test]
    public void CapabilityChecker_SupportedCapabilityResponsePasses()
    {
      var result = CapabilityChecker.Validate(SupportedCapabilities());

      Assert.That(result.Supported, Is.True);
      Assert.That(result.MissingCapabilities, Is.Empty);
    }

    [Test]
    public void CapabilityChecker_MissingRequiredCommandFails()
    {
      var capabilities = SupportedCapabilities();
      capabilities.SupportedCommandTypes.Remove("ATTACK");

      var result = CapabilityChecker.Validate(capabilities);

      Assert.That(result.Supported, Is.False);
      Assert.That(result.MissingCapabilities, Does.Contain("command:ATTACK"));
    }

    [Test]
    public void CapabilityChecker_MissingRequiredEventFails()
    {
      var capabilities = SupportedCapabilities();
      capabilities.SupportedEventTypes.Remove("CARD_PLAYED");

      var result = CapabilityChecker.Validate(capabilities);

      Assert.That(result.Supported, Is.False);
      Assert.That(result.MissingCapabilities, Does.Contain("event:CARD_PLAYED"));
    }

    [Test]
    public void CapabilityChecker_MissingSchemaVersionFails()
    {
      var capabilities = SupportedCapabilities();
      capabilities.EventSchemaVersion = string.Empty;

      var result = CapabilityChecker.Validate(capabilities);

      Assert.That(result.Supported, Is.False);
      Assert.That(result.MissingCapabilities, Does.Contain("eventSchemaVersion"));
    }

    [Test]
    public void CapabilityChecker_HealthFailureBlocksBootResult()
    {
      var result = CapabilityCheckResult.Blocked("Health check failed");

      Assert.That(result.Supported, Is.False);
      Assert.That(result.MissingCapabilities, Does.Contain("Health check failed"));
    }

    [Test]
    public void LegalActionPresenter_GroupsActionsByType()
    {
      var presenter = new LegalActionPresenter();
      var groups = presenter.Group(new ExternalLegalActionsResult
      {
        LegalActions = new()
        {
          new MatchCommandDto { CommandType = "PLAY_CARD", PlayerId = "player-1" },
          new MatchCommandDto { CommandType = "END_TURN", PlayerId = "player-1" },
          new MatchCommandDto { CommandType = "PLAY_CARD", PlayerId = "player-1" }
        }
      });

      Assert.That(groups.Count, Is.EqualTo(2));
      Assert.That(groups.Single(group => group.CommandType == "PLAY_CARD").Actions.Count, Is.EqualTo(2));
    }

    [Test]
    public void LegalActionPresenter_DisablesUnsupportedTargetActionRatherThanInferring()
    {
      var presenter = new LegalActionPresenter();
      var view = presenter.Create(new MatchCommandDto
      {
        CommandType = "ATTACK",
        PlayerId = "player-1",
        Refs = new() { ["attackerEntityInstanceId"] = "entity-a" }
      });

      Assert.That(view.Enabled, Is.False);
      Assert.That(view.DisabledReason, Does.Contain("backend did not provide target candidates"));
      Assert.That(view.CandidateTargetRefs, Is.Empty);
    }

    [Test]
    public void TargetHighlightView_HighlightsOnlyServerProvidedRefs()
    {
      var state = new TargetSelectionState(
        true,
        new MatchCommandDto { CommandType = "ATTACK" },
        "targetEntityInstanceId",
        new[] { "entity-b" });
      var highlights = new TargetHighlightView(state);

      Assert.That(highlights.IsHighlighted("entity-b"), Is.True);
      Assert.That(highlights.IsHighlighted("entity-c"), Is.False);
    }

    [Test]
    public void ClientModeState_SpectatorDisablesCommands()
    {
      var mode = new ClientModeState();

      mode.SetMode(ClientViewMode.Spectator);

      Assert.That(mode.CommandsEnabled, Is.False);
      Assert.That(mode.ProjectionKey, Is.EqualTo("spectator-1"));
    }

    [Test]
    public void ClientModeState_DebugBothSwitchesProjectionWithoutMerging()
    {
      var mode = new ClientModeState();
      mode.SetMode(ClientViewMode.DebugBoth);

      Assert.That(mode.ProjectionKey, Is.EqualTo("player-1"));
      mode.SwitchDebugProjection();
      Assert.That(mode.ProjectionKey, Is.EqualTo("player-2"));
    }

    [Test]
    public void ClientModeState_DebugBothWarningStatesNoHiddenMerge()
    {
      Assert.That(ClientModeState.DebugBothWarning, Does.Contain("does not merge hidden state"));
    }

    [Test]
    public void LocalMatchFlowDriver_PrefersEndTurnCommand()
    {
      var command = LocalMatchFlowDriver.SelectSmokeCommand(new ExternalLegalActionsResult
      {
        LegalActions = new()
        {
          new MatchCommandDto { CommandType = "PLAY_CARD", PlayerId = "player-1", Refs = new() { ["cardInstanceId"] = "card-1" } },
          new MatchCommandDto { CommandType = "END_TURN", PlayerId = "player-1" }
        }
      });

      Assert.That(command.CommandType, Is.EqualTo("END_TURN"));
    }

    [Test]
    public void LocalMatchFlowDriver_DoesNotSelectTargetedFallback()
    {
      var command = LocalMatchFlowDriver.SelectSmokeCommand(new ExternalLegalActionsResult
      {
        LegalActions = new()
        {
          new MatchCommandDto { CommandType = "ATTACK", PlayerId = "player-1", Refs = new() { ["attackerEntityInstanceId"] = "entity-a" } }
        }
      });

      Assert.That(command, Is.Null);
    }

    [Test]
    public void LocalMatchFlowDriver_GeneratesUniqueCollisionSafeMatchIds()
    {
      var first = LocalMatchFlowDriver.GenerateSmokeMatchId();
      var second = LocalMatchFlowDriver.GenerateSmokeMatchId();

      Assert.That(first, Does.StartWith("unity-live-smoke-"));
      Assert.That(second, Does.StartWith("unity-live-smoke-"));
      Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public void DebugDemoSeeder_CompatibilityChecksExpectedSeedShape()
    {
      var profile = new ResolvedRulesetProfile
      {
        ProfileId = DebugDemoSeeder.RulesetProfileId,
        RulesetId = DebugDemoSeeder.RulesetProfileId,
        AllowedCommands = new() { "PLAY_CARD", "END_TURN", "ATTACK", "USE_HERO_POWER", "ACTIVATE_LOCATION" }
      };
      var catalog = new CatalogSnapshot
      {
        SnapshotId = DebugDemoSeeder.CatalogSnapshotId,
        RulesetId = DebugDemoSeeder.RulesetProfileId,
        Cards = Enumerable.Range(1, 15).Select(index => new CardVersionSummary
        {
          CardId = index == 1 ? "unity_test_zayara_draw_card_08" : index == 2 ? "unity_test_azavelle_vanilla_09" : $"card-{index}"
        }).ToList()
      };
      var deck = new DeckDefinition
      {
        DeckId = DebugDemoSeeder.PlayerOneDeckId,
        OwnerPlayerId = DebugDemoSeeder.PlayerOneId,
        CatalogSnapshotId = DebugDemoSeeder.CatalogSnapshotId,
        Cards = Enumerable.Range(1, 15).Select(index => new DeckCardRef { CardId = $"card-{index}", Count = 2 }).ToList()
      };

      Assert.That(DebugDemoSeeder.IsExpectedProfile(profile), Is.True);
      Assert.That(DebugDemoSeeder.IsExpectedCatalog(catalog), Is.True);
      Assert.That(DebugDemoSeeder.IsExpectedDeck(deck, DebugDemoSeeder.PlayerOneDeckId, DebugDemoSeeder.PlayerOneId), Is.True);

      deck.Cards.RemoveAt(0);
      Assert.That(DebugDemoSeeder.IsExpectedDeck(deck, DebugDemoSeeder.PlayerOneDeckId, DebugDemoSeeder.PlayerOneId), Is.False);
    }

    [Test]
    public void DebugDemoSeeder_CompatibilityChecksLegacyPublicSeedJsonAliases()
    {
      var profileJson = @"{""id"":""ruleset-production-core-v1"",""sourceProfileId"":""ruleset-production-core-v1"",""allowedActions"":[""PLAY_CARD"",""END_TURN"",""ATTACK"",""USE_HERO_POWER"",""ACTIVATE_LOCATION""]}";
      var catalogJson = @"{""snapshotId"":""catalog-unity-test-card-pack-v1"",""rulesetProfileId"":""ruleset-production-core-v1"",""cards"":[{""cardId"":""c1""},{""cardId"":""c2""},{""cardId"":""c3""},{""cardId"":""c4""},{""cardId"":""c5""},{""cardId"":""c6""},{""cardId"":""c7""},{""cardId"":""c8""},{""cardId"":""c9""},{""cardId"":""c10""},{""cardId"":""c11""},{""cardId"":""c12""},{""cardId"":""c13""},{""cardId"":""c14""},{""cardId"":""c15""}]}";
      var deckJson = @"{""deckId"":""unity-debug-deck-p1"",""playerId"":""player-1"",""catalogSnapshotId"":""catalog-unity-test-card-pack-v1"",""cards"":[{""cardId"":""c1"",""count"":2},{""cardId"":""c2"",""count"":2},{""cardId"":""c3"",""count"":2},{""cardId"":""c4"",""count"":2},{""cardId"":""c5"",""count"":2},{""cardId"":""c6"",""count"":2},{""cardId"":""c7"",""count"":2},{""cardId"":""c8"",""count"":2},{""cardId"":""c9"",""count"":2},{""cardId"":""c10"",""count"":2},{""cardId"":""c11"",""count"":2},{""cardId"":""c12"",""count"":2},{""cardId"":""c13"",""count"":2},{""cardId"":""c14"",""count"":2},{""cardId"":""c15"",""count"":2}]}";

      Assert.That(DebugDemoSeeder.IsExpectedProfile(new ResolvedRulesetProfile(), profileJson), Is.True);
      Assert.That(DebugDemoSeeder.IsExpectedCatalog(new CatalogSnapshot(), catalogJson), Is.True);
      Assert.That(DebugDemoSeeder.IsExpectedDeck(new DeckDefinition(), DebugDemoSeeder.PlayerOneDeckId, DebugDemoSeeder.PlayerOneId, deckJson), Is.True);
    }

    [Test]
    public void HiddenInformationAssertions_PlayerOpponentHiddenHandIdentityAbsent()
    {
      var state = PlayerProjection("player-1", opponentCardId: null);

      var result = HiddenInformationAssertions.CheckPlayerProjection(state, "player-1");

      Assert.That(result.Passed, Is.True, result.Message);
    }

    [Test]
    public void HiddenInformationAssertions_PlayerOpponentHiddenHandIdentityFails()
    {
      var state = PlayerProjection("player-1", opponentCardId: "leaked-card");

      var result = HiddenInformationAssertions.CheckPlayerProjection(state, "player-1");

      Assert.That(result.Passed, Is.False);
      Assert.That(result.Message, Does.Contain("Opponent hidden zone"));
    }

    [Test]
    public void HiddenInformationAssertions_SpectatorHandIdentityAbsent()
    {
      var result = HiddenInformationAssertions.CheckSpectatorProjection(SpectatorProjection(null));

      Assert.That(result.Passed, Is.True, result.Message);
    }

    [Test]
    public void HiddenInformationAssertions_ProjectionSwitchKeepsReplacementSemantics()
    {
      var store = new MatchStateStore();
      store.ReplaceFromServer(PlayerProjection("player-1", opponentCardId: null));
      store.ReplaceFromServer(PlayerProjection("player-2", opponentCardId: null));

      var result = HiddenInformationAssertions.CheckProjectionSwitch(store.Current, "player-2");

      Assert.That(result.Passed, Is.True, result.Message);
    }

    [Test]
    public void GameShellViewModelBuilder_OwnVisibleCardMapsToCardViewModel()
    {
      var state = PlayerProjection("player-1", opponentCardId: null);
      state.Players["player-1"].Zones.Hand.Cards.Add(new PublicCardInstanceView
      {
        CardInstanceId = "card-instance-1",
        CardId = "card-1",
        CardVersion = "card-1-v1",
        OwnerPlayerId = "player-1",
        CardType = "MINION_CARD",
        Visibility = "VISIBLE"
      });
      state.Players["player-1"].Zones.Hand.Count = 1;

      var shell = GameShellViewModelBuilder.Build(
        state,
        ClientViewMode.PlayerOne,
        TargetSelectionState.Inactive,
        commandsEnabled: true,
        disabledReasons: Array.Empty<string>(),
        isReconnecting: false,
        replayVerified: true,
        replayMismatch: false);

      var card = shell.HandZones.Single(zone => zone.PlayerId == "player-1").Cards.Single(candidate => candidate.CardInstanceId == "card-instance-1");
      Assert.That(card.IsHiddenPlaceholder, Is.False);
      Assert.That(card.CardId, Is.EqualTo("card-1"));
      Assert.That(card.CardInstanceId, Is.EqualTo("card-instance-1"));
    }

    [Test]
    public void GameShellViewModelBuilder_HiddenOpponentCardMapsToCardBackWithoutIdentity()
    {
      var state = PlayerProjection("player-1", opponentCardId: "leaked-card");
      state.Players["player-2"].Zones.Hand.Count = 3;

      var shell = GameShellViewModelBuilder.Build(
        state,
        ClientViewMode.PlayerOne,
        TargetSelectionState.Inactive,
        commandsEnabled: true,
        disabledReasons: Array.Empty<string>(),
        isReconnecting: false,
        replayVerified: false,
        replayMismatch: false);

      var placeholder = shell.HandZones.Single(zone => zone.PlayerId == "player-2").Cards.Single();
      Assert.That(placeholder.IsHiddenPlaceholder, Is.True);
      Assert.That(placeholder.Count, Is.EqualTo(3));
      Assert.That(placeholder.CardId, Is.Empty);
      Assert.That(placeholder.CardInstanceId, Is.Empty);
    }

    [Test]
    public void GameShellViewModelBuilder_SpectatorHiddenHandMapsToCountPlaceholder()
    {
      var state = SpectatorProjection("leaked-card");
      state.Players["player-1"].Zones.Hand.Count = 2;

      var shell = GameShellViewModelBuilder.Build(
        state,
        ClientViewMode.Spectator,
        TargetSelectionState.Inactive,
        commandsEnabled: false,
        disabledReasons: new[] { "Spectator mode" },
        isReconnecting: false,
        replayVerified: false,
        replayMismatch: false);

      var placeholder = shell.HandZones.Single(zone => zone.PlayerId == "player-1").Cards.Single();
      Assert.That(placeholder.IsHiddenPlaceholder, Is.True);
      Assert.That(placeholder.Count, Is.EqualTo(2));
      Assert.That(placeholder.CardId, Is.Empty);
    }

    [Test]
    public void GameShellViewModelBuilder_BoardEntityMapsCombatFieldsAndKeywords()
    {
      var state = PlayerProjection("player-1", opponentCardId: null);
      state.Players["player-1"].Zones.Board.Add(new PublicEntityView
      {
        EntityInstanceId = "entity-1",
        SourceCardInstanceId = "card-instance-1",
        ControllerPlayerId = "player-1",
        EntityType = "MINION",
        Attack = 3,
        Health = 4,
        Damage = 1,
        Keywords = new() { "TAUNT" }
      });

      var shell = GameShellViewModelBuilder.Build(
        state,
        ClientViewMode.PlayerOne,
        TargetSelectionState.Inactive,
        commandsEnabled: true,
        disabledReasons: Array.Empty<string>(),
        isReconnecting: false,
        replayVerified: false,
        replayMismatch: false);

      var entity = shell.BoardZones.Single(zone => zone.PlayerId == "player-1").Entities.Single();
      Assert.That(entity.Attack, Is.EqualTo(3));
      Assert.That(entity.Health, Is.EqualTo(4));
      Assert.That(entity.Damage, Is.EqualTo(1));
      Assert.That(entity.Keywords, Does.Contain("TAUNT"));
    }

    [Test]
    public void GameShellViewModelBuilder_CommandTargetAndReplayStatusesSurface()
    {
      var targetState = new TargetSelectionState(
        true,
        new MatchCommandDto { CommandType = "ATTACK" },
        "targetEntityInstanceId",
        new[] { "entity-a", "entity-b" });

      var shell = GameShellViewModelBuilder.Build(
        PlayerProjection("player-1", opponentCardId: null),
        ClientViewMode.PlayerOne,
        targetState,
        commandsEnabled: false,
        disabledReasons: new[] { "Target selection unavailable" },
        isReconnecting: true,
        replayVerified: false,
        replayMismatch: true);

      Assert.That(shell.CommandStatus.CommandsEnabled, Is.False);
      Assert.That(shell.CommandStatus.DisabledReasons, Does.Contain("Target selection unavailable"));
      Assert.That(shell.InteractionState.HighlightedRefs, Is.EquivalentTo(new[] { "entity-a", "entity-b" }));
      Assert.That(shell.InteractionState.CanCancel, Is.True);
      Assert.That(shell.ConnectionStatus.IsReconnecting, Is.True);
      Assert.That(shell.ReplayStatus.IsBlocking, Is.True);
    }

    [Test]
    public void CardViewModelFactory_OwnVisibleCardCreatesFaceUpIdentity()
    {
      var factory = new CardViewModelFactory();

      var model = factory.FromCard(new PublicCardInstanceView
      {
        CardInstanceId = "instance-1",
        CardId = "card-1",
        CardVersion = "v1",
        CardType = "MINION_CARD",
        Visibility = "VISIBLE"
      }, CardVisibilityPolicy.PlayerSelf, "HAND");

      Assert.That(model.IsFaceUp, Is.True);
      Assert.That(model.CardId, Is.EqualTo("card-1"));
      Assert.That(model.CardInstanceId, Is.EqualTo("instance-1"));
      Assert.That(model.DisplayIdentity, Is.EqualTo(CardDisplayIdentity.PrivateToViewer));
    }

    [Test]
    public void CardViewModelFactory_OpponentHiddenCardCreatesCardBackWithoutIdentity()
    {
      var factory = new CardViewModelFactory();

      var model = factory.FromCard(new PublicCardInstanceView
      {
        CardInstanceId = "leaked-instance",
        CardId = "leaked-card",
        CardVersion = "v1",
        Visibility = "HIDDEN"
      }, CardVisibilityPolicy.HiddenZone, "HAND");

      Assert.That(model.IsPlaceholder, Is.True);
      Assert.That(model.CardId, Is.Empty);
      Assert.That(model.CardInstanceId, Is.Empty);
      Assert.That(model.DisplayIdentity, Is.EqualTo(CardDisplayIdentity.None));
    }

    [Test]
    public void CardViewModelFactory_DeckZoneIsCountOnly()
    {
      var factory = new CardViewModelFactory();
      var model = factory.FromZone(new PublicZoneView
      {
        Zone = "DECK",
        Count = 7,
        Cards = new() { new PublicCardInstanceView { CardId = "should-not-render", Visibility = "VISIBLE" } }
      }, CardVisibilityPolicy.Deck);

      Assert.That(model.IsPlaceholder, Is.True);
      Assert.That(model.Count, Is.EqualTo(7));
      Assert.That(model.CardId, Is.Empty);
      Assert.That(model.DisplayLabel, Does.Contain("7"));
    }

    [Test]
    public void CardViewModelFactory_NullCardProducesSafePlaceholder()
    {
      var model = new CardViewModelFactory().FromCard(null, CardVisibilityPolicy.PlayerSelf, "HAND");

      Assert.That(model.IsPlaceholder, Is.True);
      Assert.That(model.CardId, Is.Empty);
      Assert.That(model.CardInstanceId, Is.Empty);
    }

    [Test]
    public void DebugBothWarningIsVisibleInModeViewModel()
    {
      var mode = ModeStatusViewModel.From(ClientViewMode.DebugBoth, "player-1");

      Assert.That(mode.IsDebugOnly, Is.True);
      Assert.That(mode.Warning, Does.Contain("does not merge hidden state"));
    }

    [Test]
    public void EntityViewModelFactory_MapsStatsAndKeywordsDeterministically()
    {
      var model = new EntityViewModelFactory().Create(new PublicEntityView
      {
        EntityInstanceId = "entity-1",
        ControllerPlayerId = "player-1",
        EntityType = "MINION",
        Attack = 4,
        Health = 6,
        Damage = 2,
        Keywords = new() { "WINDFURY", "TAUNT" }
      });

      Assert.That(model.Attack, Is.EqualTo(4));
      Assert.That(model.Health, Is.EqualTo(6));
      Assert.That(model.Damage, Is.EqualTo(2));
      Assert.That(model.RemainingHealth, Is.EqualTo(4));
      Assert.That(model.KeywordText, Is.EqualTo("TAUNT, WINDFURY"));
    }

    [Test]
    public void BoardZoneViewModelFactory_EmptyBoardMapsToPlaceholderState()
    {
      var zone = new BoardZoneViewModelFactory().Create("player-1", Array.Empty<PublicEntityView>());

      Assert.That(zone.IsEmpty, Is.True);
      Assert.That(zone.Entities, Is.Empty);
    }

    [Test]
    public void HeroAndResourceStatusFactories_MapPublicFields()
    {
      var player = new PublicPlayerStateView
      {
        PlayerId = "player-1",
        Mana = 3,
        MaxMana = 5,
        Armor = 2,
        FatigueCounter = 1,
        Hero = new PublicEntityView { EntityInstanceId = "hero-1", Health = 30, Damage = 4 },
        HeroPower = new PublicHeroPowerView { SourceCardInstanceId = "hp-1", Cost = 2, ActivationTiming = "MAIN" }
      };

      var hero = new HeroStatusViewModelFactory().Create(player);
      var resources = new ResourceStatusViewModelFactory().Create(player);

      Assert.That(hero.Hero.EntityInstanceId, Is.EqualTo("hero-1"));
      Assert.That(hero.HeroPowerSourceCardInstanceId, Is.EqualTo("hp-1"));
      Assert.That(resources.Mana, Is.EqualTo(3));
      Assert.That(resources.MaxMana, Is.EqualTo(5));
      Assert.That(resources.Armor, Is.EqualTo(2));
      Assert.That(resources.FatigueCounter, Is.EqualTo(1));
    }

    [Test]
    public void CommandSubmitGuard_DisablesSpectatorReconnectAndTerminalStates()
    {
      var guard = new CommandSubmitGuard(
        CommandAvailabilityReason.SpectatorMode,
        CommandAvailabilityReason.Reconnecting,
        CommandAvailabilityReason.Terminal);

      var intent = guard.CreateIntent(new MatchCommandDto { CommandType = "END_TURN" }, "hash-1");

      Assert.That(intent.Enabled, Is.False);
      Assert.That(intent.DisabledReason, Does.Contain("Spectator"));
      Assert.That(intent.DisabledReason, Does.Contain("Reconnecting"));
      Assert.That(intent.DisabledReason, Does.Contain("terminal"));
      Assert.That(intent.ExpectedPreStateHash, Is.EqualTo("hash-1"));
    }

    [Test]
    public void CommandSubmitGuard_RejectsLocalCommandConstruction()
    {
      var guard = new CommandSubmitGuard();

      Assert.That(guard.RejectsLocalCommand(null), Is.True);
      Assert.That(guard.CreateIntent(null, "hash").DisabledReason, Does.Contain("server-provided legal action"));
    }

    [Test]
    public void LegalActionViewModelFactory_GroupsServerTemplatesIntoCommandIntents()
    {
      var panel = new LegalActionViewModelFactory().Create(
        new ExternalLegalActionsResult
        {
          LegalActions = new()
          {
            new MatchCommandDto { CommandType = "PLAY_CARD", PlayerId = "player-1" },
            new MatchCommandDto { CommandType = "END_TURN", PlayerId = "player-1" }
          }
        },
        new CommandSubmitGuard(),
        "state-hash");

      Assert.That(panel.Commands.Count, Is.EqualTo(2));
      Assert.That(panel.Commands.All(command => command.Template != null), Is.True);
      Assert.That(panel.Commands.All(command => command.ExpectedPreStateHash == "state-hash"), Is.True);
    }

    [Test]
    public void TargetSelectionViewModelFactory_HighlightsCandidatesAndShowsBanner()
    {
      var model = new TargetSelectionViewModelFactory().Create(new TargetSelectionState(
        true,
        new MatchCommandDto { CommandType = "ATTACK" },
        "targetEntityInstanceId",
        new[] { "entity-a" }));

      Assert.That(model.Active, Is.True);
      Assert.That(model.CanCancel, Is.True);
      Assert.That(model.Banner, Does.Contain("highlighted"));
      Assert.That(model.Candidates.Single().RefId, Is.EqualTo("entity-a"));
      Assert.That(model.Candidates.Single().Highlighted, Is.True);
    }

    [Test]
    public void ChoiceViewModelFactory_RendersVisibleChoiceAndDisablesMissingOptionId()
    {
      var state = PlayerProjection("player-1", opponentCardId: null);
      state.PendingChoices["choice-1"] = new PublicChoiceView
      {
        ChoiceId = "choice-1",
        ChoiceType = "DISCOVER",
        SourceRef = "source-1",
        ResultZone = "HAND",
        VisibilityPolicy = "PRIVATE",
        Options = new()
        {
          new PublicChoiceOptionView { OptionId = "option-1", ItemKind = "CARD", ItemId = "card-1", Visibility = "VISIBLE" },
          new PublicChoiceOptionView { OptionId = "", ItemKind = "CARD", ItemId = "card-2", Visibility = "VISIBLE" }
        }
      };

      var panel = new ChoiceViewModelFactory().CreateVisible(state).Single();

      Assert.That(panel.ChoiceId, Is.EqualTo("choice-1"));
      Assert.That(panel.SourceRef, Is.EqualTo("source-1"));
      Assert.That(panel.ResultZone, Is.EqualTo("HAND"));
      Assert.That(panel.Options[0].Enabled, Is.True);
      Assert.That(panel.Options[1].Enabled, Is.False);
    }

    [Test]
    public void ChoiceResolveGuard_UsesServerProvidedIds()
    {
      var guard = new ChoiceResolveGuard();
      var choice = new ChoicePanelViewModel { ChoiceId = "choice-1" };
      var option = new ChoiceOptionViewModel { OptionId = "option-1" };

      var command = guard.BuildResolveTemplate("player-1", choice, option);

      Assert.That(command.CommandType, Is.EqualTo("RESOLVE_CHOICE"));
      Assert.That(command.Refs["choiceId"], Is.EqualTo("choice-1"));
      Assert.That(command.Refs["selectedOptionId"], Is.EqualTo("option-1"));
    }

    [Test]
    public void ConnectionAndReplayStatusViewModels_SurfaceRecoverableAndBlockingStates()
    {
      var backend = BackendHealthViewModel.FromResult(UnityBackendConfig.CreateRuntimeDefault(), false, "offline");
      var replay = ReplayVerificationViewModel.From(new MatchReplayVerificationView
      {
        Replay = new ReplayVerificationResult { IsVerified = false, FinalStateHash = "hash" }
      });

      Assert.That(backend.Available, Is.False);
      Assert.That(backend.Recoverable, Is.True);
      Assert.That(replay.Blocking, Is.True);
      Assert.That(replay.FinalStateHash, Is.EqualTo("hash"));
    }

    [Test]
    public void CapabilityStatusViewModel_MissingCapabilityBlocksBoot()
    {
      var capabilities = SupportedCapabilities();
      capabilities.SupportedCommandTypes.Remove("ATTACK");
      var status = CapabilityStatusViewModel.From(capabilities, CapabilityChecker.Validate(capabilities));

      Assert.That(status.BlocksBoot, Is.True);
      Assert.That(status.MissingCapabilities, Does.Contain("command:ATTACK"));
    }

    [Test]
    public void PresentationEventRouter_MapsKnownUnknownAndTerminalEvents()
    {
      var router = new PresentationEventRouter();

      var known = router.Route(new PublicMatchEventRecord { EventSeq = 1, EventType = "CARD_PLAYED" });
      var unknown = router.Route(new PublicMatchEventRecord { EventSeq = 2, EventType = "FUTURE_EVENT" });
      var terminal = router.Route(new PublicMatchEventRecord { EventSeq = 3, EventType = "ENGINE_FAULTED" });

      Assert.That(known.IntentType, Is.EqualTo(PresentationIntentType.CardZone));
      Assert.That(known.RequestsStateRefresh, Is.True);
      Assert.That(unknown.IntentType, Is.EqualTo(PresentationIntentType.Generic));
      Assert.That(unknown.RequestsStateRefresh, Is.True);
      Assert.That(unknown.Diagnostic, Does.Contain("Unsupported event type"));
      Assert.That(terminal.BlocksQueue, Is.True);
    }

    [Test]
    public void PresentationEventRouter_MapsEveryKnownPublicEvent()
    {
      var router = new PresentationEventRouter();

      foreach (var eventType in PresentationEventRouter.KnownPublicEventTypes)
      {
        var intent = router.Route(new PublicMatchEventRecord { EventSeq = 1, EventType = eventType });
        Assert.That(intent.IntentType, Is.Not.EqualTo(PresentationIntentType.Generic), eventType);
      }
    }

    [Test]
    public void PresentationIntentQueue_TerminalIntentBlocksNormalQueue()
    {
      var queue = new PresentationIntentQueue();
      queue.Enqueue(new PresentationIntent { IntentType = PresentationIntentType.CardZone, EventSeq = 1 });
      queue.Enqueue(new PresentationIntent { IntentType = PresentationIntentType.Fault, EventSeq = 2, BlocksQueue = true });

      Assert.That(queue.Status, Is.EqualTo(PresentationPlaybackStatus.Blocked));
      Assert.That(queue.Count, Is.EqualTo(1));
      Assert.That(queue.TryDequeue(out var intent), Is.True);
      Assert.That(intent.IntentType, Is.EqualTo(PresentationIntentType.Fault));
    }

    [Test]
    public void PresentationIntentQueue_DedupesAndQueuesReconnectMissedEventsInOrder()
    {
      var queue = new PresentationIntentQueue();
      var router = new PresentationEventRouter();

      var accepted = queue.EnqueueReconnectMissedEvents(new[]
      {
        new PublicMatchEventRecord { EventSeq = 3, EventType = "TURN_ENDED" },
        new PublicMatchEventRecord { EventSeq = 2, EventType = "TURN_STARTED" },
        new PublicMatchEventRecord { EventSeq = 2, EventType = "TURN_STARTED" }
      }, router);

      Assert.That(accepted, Is.EqualTo(2));
      Assert.That(queue.TryDequeue(out var first), Is.True);
      Assert.That(first.EventSeq, Is.EqualTo(2));
      Assert.That(queue.TryDequeue(out var second), Is.True);
      Assert.That(second.EventSeq, Is.EqualTo(3));
    }

    [Test]
    public void GameplayScreenStateMachine_TransitionsAndDisablesCommandsForBlockingStates()
    {
      var machine = new GameplayScreenStateMachine();

      Assert.That(machine.OnBootSucceeded().State, Is.EqualTo(GameplayScreenState.ReadyToSeed));
      Assert.That(machine.OnSeedSucceeded().State, Is.EqualTo(GameplayScreenState.ReadyToStart));
      Assert.That(machine.OnStartSucceeded(ClientViewMode.PlayerOne, "hash-1").CommandsEnabled, Is.True);

      var spectator = machine.Evaluate(GameplayScreenState.MatchActive, ClientViewMode.Spectator, "hash-1");
      Assert.That(spectator.State, Is.EqualTo(GameplayScreenState.Spectating));
      Assert.That(spectator.CommandsEnabled, Is.False);

      var stale = machine.OnStaleHash();
      Assert.That(stale.State, Is.EqualTo(GameplayScreenState.Reconnecting));
      Assert.That(stale.CommandsEnabled, Is.False);

      var mismatch = machine.OnReplayMismatch();
      Assert.That(mismatch.Blocking, Is.True);
      Assert.That(mismatch.CommandsEnabled, Is.False);

      var changed = machine.OnTargetCancelled(ClientViewMode.PlayerOne, "hash-2");
      Assert.That(changed.State, Is.EqualTo(GameplayScreenState.MatchActive));
      Assert.That(changed.ResetsTargetSelection, Is.True);
    }

    [Test]
    public void GameScreenLayoutBuilder_CreatesNamedGameplayRegions()
    {
      var root = new UnityEngine.GameObject("layout-root");
      try
      {
        var layout = new GameScreenLayoutBuilder().Build(root.transform);

        Assert.That(layout.RegionNames, Is.EquivalentTo(new[]
        {
          "Background",
          "Header",
          "OpponentPanel",
          "OpponentHand",
          "Board",
          "PlayerPanel",
          "PlayerHand",
          "InteractionPanel",
          "PresentationOverlay",
          "ChoiceModal",
          "TargetOverlay",
          "EventFeed",
          "DiagnosticsPanel",
          "AudioControls"
        }));
      }
      finally
      {
        UnityEngine.Object.DestroyImmediate(root);
      }
    }

    [Test]
    public void HandPanelBinder_SuppressesHiddenIdentityAndHighlightsServerPlayableRefs()
    {
      var obj = new UnityEngine.GameObject("hand-binder");
      try
      {
        var binder = obj.AddComponent<HandPanelBinder>();
        binder.Bind(new HandPanelViewModel
        {
          PlayerId = "player-2",
          OwnHand = false,
          SpectatorSafe = true,
          PlayableCardRefs = new[] { "card-instance-1" },
          Cards = new[]
          {
            new CardGame.Unity.Client.UI.Cards.CardViewModel
            {
              IsPlaceholder = true,
              DisplayLabel = "Card back",
              Count = 3
            }
          }
        });

        Assert.That(binder.BoundCardLabels.Single(), Does.Contain("Card back"));
        Assert.That(binder.BoundCardLabels.Single(), Does.Not.Contain("leaked-card"));
        Assert.That(binder.HighlightedRefs, Is.EquivalentTo(new[] { "card-instance-1" }));
      }
      finally
      {
        UnityEngine.Object.DestroyImmediate(obj);
      }
    }

    [Test]
    public void BoardPanelBinder_CreatesDeterministicEntitySurfacesAndHighlightsCandidates()
    {
      var obj = new UnityEngine.GameObject("board-binder");
      try
      {
        var binder = obj.AddComponent<BoardPanelBinder>();
        binder.Bind(new BoardPanelViewModel
        {
          PlayerBoard = new BoardZoneViewModel
          {
            Entities = new[]
            {
              new CardGame.Unity.Client.UI.Board.EntityViewModel { EntityInstanceId = "entity-b", EntityType = "MINION", Attack = 2, RemainingHealth = 3 },
              new CardGame.Unity.Client.UI.Board.EntityViewModel { EntityInstanceId = "entity-a", EntityType = "MINION", Attack = 1, RemainingHealth = 2 }
            }
          },
          HighlightedTargetRefs = new[] { "entity-b" }
        });

        Assert.That(binder.EntitySurfaces.Select(surface => surface.RefId), Is.EqualTo(new[] { "entity-a", "entity-b" }));
        Assert.That(binder.EntitySurfaces.Single(surface => surface.RefId == "entity-b").Highlighted, Is.True);
        Assert.That(binder.EntitySurfaces.Single(surface => surface.RefId == "entity-a").Highlighted, Is.False);
      }
      finally
      {
        UnityEngine.Object.DestroyImmediate(obj);
      }
    }

    [Test]
    public void LegalActionPanelBinder_RendersDisabledReasonsAndExpectedHash()
    {
      var obj = new UnityEngine.GameObject("legal-action-binder");
      try
      {
        var binder = obj.AddComponent<CardGame.Unity.Client.UI.Interaction.LegalActionPanelBinder>();
        binder.Bind(new LegalActionPanelViewModel
        {
          Commands = new[]
          {
            new CommandIntentViewModel
            {
              CommandType = "END_TURN",
              Enabled = false,
              DisabledReason = "State hash is stale; reconnect and refresh legal actions.",
              ExpectedPreStateHash = "abcdef123456"
            }
          }
        });

        Assert.That(binder.Buttons.Single().Label, Is.EqualTo("END_TURN"));
        Assert.That(binder.Buttons.Single().Enabled, Is.False);
        Assert.That(binder.Buttons.Single().DisabledReason, Does.Contain("stale"));
        Assert.That(binder.Buttons.Single().ExpectedPreStateHashShort, Is.EqualTo("abcdef12"));
      }
      finally
      {
        UnityEngine.Object.DestroyImmediate(obj);
      }
    }

    [Test]
    public void PresentationPlaybackRunner_QueuesEventsAndBlocksOnTerminalWithoutMutatingState()
    {
      var state = PlayerProjection("player-1", opponentCardId: null);
      var originalHash = "hash-1";
      state.StateHash = originalHash;
      var runner = new PresentationPlaybackRunner();

      Assert.That(runner.EnqueueEvents(new[]
      {
        new PublicMatchEventRecord { EventSeq = 1, EventType = "TURN_STARTED" },
        new PublicMatchEventRecord { EventSeq = 1, EventType = "TURN_STARTED" },
        new PublicMatchEventRecord { EventSeq = 2, EventType = "FUTURE_EVENT" },
        new PublicMatchEventRecord { EventSeq = 3, EventType = "GAME_ENDED" }
      }), Is.EqualTo(3));

      Assert.That(runner.Tick(1f), Is.True);
      Assert.That(runner.CurrentIntent.EventType, Is.EqualTo("GAME_ENDED"));
      Assert.That(runner.Status, Is.EqualTo(PresentationPlaybackStatus.Blocked));
      Assert.That(PresentationPanelViewModel.From(runner).Blocking, Is.True);
      Assert.That(state.StateHash, Is.EqualTo(originalHash));
    }

    [Test]
    public void PresentationPlaybackRunner_UsesDeterministicDurationsAndStatusIntents()
    {
      var clock = new PresentationPlaybackClock();
      var runner = new PresentationPlaybackRunner(clock: clock);

      runner.EnqueueEvents(new[]
      {
        new PublicMatchEventRecord { EventSeq = 1, EventType = "CARD_PLAYED" }
      });
      runner.EnqueueStatus("RECONNECT_COMPLETED", PresentationIntentType.ConnectionStatus, "missed=0");

      Assert.That(runner.Tick(0f), Is.True);
      Assert.That(runner.CurrentIntent.DurationSeconds, Is.EqualTo(0.4f));
      Assert.That(runner.CurrentIntent.ChoreographyLabel, Is.EqualTo("card movement"));
      Assert.That(runner.Tick(0.39f), Is.False);
      Assert.That(runner.Tick(0.02f), Is.True);
      Assert.That(runner.CurrentIntent.IntentType, Is.EqualTo(PresentationIntentType.ConnectionStatus));
      Assert.That(runner.Report.Played, Is.EqualTo(2));
      Assert.That(runner.Report.Lines.Last(), Does.Contain(nameof(ReconnectAnimator)));
    }

    [Test]
    public void GameTheme_DefaultPaletteProvidesReadableIntentionalStates()
    {
      var theme = GameTheme.CreateDefault();
      var playable = GameThemeStyles.CardFace(theme, playable: true, disabled: false);
      var disabled = GameThemeStyles.CardFace(theme, playable: false, disabled: true);
      var back = GameThemeStyles.CardBack(theme);

      Assert.That(theme.Version, Is.EqualTo(GameTheme.CurrentVersion));
      Assert.That(playable.State, Is.EqualTo("playable"));
      Assert.That(playable.AccentColor, Is.EqualTo(theme.Palette.Playable));
      Assert.That(disabled.State, Is.EqualTo("disabled"));
      Assert.That(back.State, Is.EqualTo("hidden"));
    }

    [Test]
    public void CardFaceViewsExposePlayableAndDisabledPresentationWithoutHiddenLeak()
    {
      var model = new CardGame.Unity.Client.UI.Cards.CardViewModel
      {
        IsFaceUp = true,
        CardId = "card-1",
        Title = "card-1",
        Cost = 2,
        Attack = 3,
        Health = 4,
        RulesText = "MINION_CARD",
        Keywords = new[] { "TAUNT" },
        Playable = true,
        Style = GameThemeStyles.CardFace(GameTheme.CreateDefault(), playable: true, disabled: false)
      };

      var glow = new CardGlowView();
      var stats = new CardStatsView();
      var text = new CardRulesTextView();
      glow.Bind(model);
      stats.Bind(model);
      text.Bind(model);

      Assert.That(glow.Visible, Is.True);
      Assert.That(stats.Attack, Is.EqualTo(3));
      Assert.That(stats.Health, Is.EqualTo(4));
      Assert.That(text.Text, Is.EqualTo("MINION_CARD"));

      var hidden = new CardGame.Unity.Client.UI.Cards.CardViewModel { IsPlaceholder = true, DisplayLabel = "Card back" };
      Assert.That(hidden.DisplayLabel, Does.Not.Contain("card-1"));
    }

    [Test]
    public void BoardEntityPresentationMarksDamageAndTargetRing()
    {
      var entity = new CardGame.Unity.Client.UI.Board.EntityViewModel
      {
        EntityInstanceId = "entity-1",
        Damage = 2,
        TargetCandidate = true,
        Style = GameThemeStyles.Entity(GameTheme.CreateDefault(), damaged: true, highlighted: true)
      };
      var damage = new DamageBadgeView();
      var ring = new TargetRingView();

      damage.Bind(entity);
      ring.Bind(entity);

      Assert.That(damage.Visible, Is.True);
      Assert.That(damage.Damage, Is.EqualTo(2));
      Assert.That(ring.Visible, Is.True);
    }

    [Test]
    public void PresentationAnimationRegistry_BindsMinimumKnownEvents()
    {
      var registry = new PresentationAnimationRegistry();

      foreach (var eventType in new[]
      {
        "GAME_STARTED", "TURN_STARTED", "TURN_ENDED", "CARD_PLAYED", "CARD_DRAWN",
        "CARD_MOVED_ZONE", "MINION_SUMMONED", "ENTITY_DAMAGED", "ENTITY_HEALED",
        "ENTITY_DIED", "ENTITY_DESTROYED", "CHOICE_CREATED", "CHOICE_RESOLVED",
        "WEAPON_EQUIPPED", "LOCATION_ACTIVATED", "HERO_POWER_USED", "GAME_ENDED",
        "ENGINE_FAULTED"
      })
      {
        var binding = registry.Resolve(new PresentationIntent { EventType = eventType, IntentType = PresentationIntentType.Generic });
        Assert.That(binding.AnimatorName, Is.Not.Empty, eventType);
        Assert.That(binding.DurationSeconds, Is.GreaterThan(0f), eventType);
      }
    }

    [Test]
    public void AudioRouting_MuteMissingCueAndVolumesAreSafe()
    {
      var settings = new AudioVolumeSettings { MasterVolume = 2f };
      settings.SetCategoryVolume(AudioCategory.Card, -1f);
      Assert.That(settings.MasterVolume, Is.EqualTo(1f));
      Assert.That(settings.CategoryVolume(AudioCategory.Card), Is.EqualTo(0f));

      var controller = new AudioPlaybackController(settings: settings);
      var router = new AudioEventRouter();
      var cardCue = controller.Request(router.CueForEvent("CARD_PLAYED"));
      Assert.That(cardCue.Suppressed, Is.True);

      settings.Muted = true;
      var muted = controller.Request(router.CueForEvent("TURN_STARTED"));
      Assert.That(muted.Suppressed, Is.True);

      var missing = controller.Request("missing-cue");
      Assert.That(missing.Suppressed, Is.True);
      Assert.That(missing.Reason, Is.EqualTo("missing-cue"));
    }

    [Test]
    public void PlaytestEvidenceRecorder_RecordsAndExporterWritesJsonWithoutTokens()
    {
      var recorder = new PlaytestEvidenceRecorder();
      recorder.Start("http://localhost:5000", "match-1");
      recorder.LogModeSwitch("PlayerTwo");
      recorder.LogCommand("END_TURN");
      recorder.LogTargetSelection("entity-1", true);
      recorder.LogChoice("choice-1", "option-1");
      recorder.LogReconnect(true, 0);
      recorder.LogReplay(true);
      recorder.LogPresentationIntent("1:TURN_STARTED:TurnStatus");
      recorder.LogPresentationPlayed("1:TURN_STARTED:TurnStatus");
      recorder.LogAnimationPlayed();
      recorder.LogAudioCue("turn.start");
      recorder.SetAudioEnabled(true);
      var report = recorder.Complete("pass-with-limitations");
      var tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cardgame-playtest-" + Guid.NewGuid().ToString("N"));

      try
      {
        var path = new PlaytestEvidenceExporter().Export(report, tempRoot);
        var json = System.IO.File.ReadAllText(path);

        Assert.That(json, Does.Contain("END_TURN"));
        Assert.That(json, Does.Contain("PlaytestId"));
        Assert.That(json, Does.Contain("TargetsSelected"));
        Assert.That(json, Does.Contain("VisualThemeVersion"));
        Assert.That(json, Does.Contain("AudioCuesRequested"));
        Assert.That(json, Does.Contain("AnimationsPlayed"));
        Assert.That(path, Does.Contain(tempRoot));
        Assert.That(json, Does.Contain("PlayerTwo"));
        Assert.That(json, Does.Not.Contain("sessionToken"));
        Assert.That(json, Does.Not.Contain("token-"));
      }
      finally
      {
        if (System.IO.Directory.Exists(tempRoot))
        {
          System.IO.Directory.Delete(tempRoot, recursive: true);
        }
      }
    }

    [Test]
    public void EventVisualDispatcher_MapsEveryKnownEvent()
    {
      var dispatcher = new EventVisualDispatcher();

      foreach (var eventType in EventVisualDispatcher.KnownEventTypes)
      {
        var visual = dispatcher.Dispatch(new PublicMatchEventRecord { EventSeq = 1, EventType = eventType });
        Assert.That(visual.Message, Is.Not.Empty, eventType);
      }
    }

    [Test]
    public void BackendDeclaredPublicEvents_HaveVisualIntentAndAnimationCoverage()
    {
      var declared = new[]
      {
        "GAME_STARTED", "TURN_STARTED", "EXTRA_TURN_SCHEDULED", "TURN_ENDED", "CARD_PLAYED",
        "CARD_MOVED_ZONE", "CARD_DRAWN", "CARD_DISCARDED", "CARD_RETURNED_TO_HAND", "MINION_SUMMONED",
        "ENTITY_DAMAGED", "DAMAGE_WOULD_BE_DEALT", "REPLACEMENT_RESOLVED", "ENTITY_HEALED", "MANA_CHANGED",
        "ENTITY_DESTROYED", "ENTITY_DIED", "DEATHRATTLE_TRIGGERED", "SECRET_REVEALED", "QUEST_PROGRESS_CHANGED",
        "MODIFIER_APPLIED", "MODIFIER_EXPIRED", "AURA_REFRESHED", "WEAPON_EQUIPPED", "LOCATION_PLAYED",
        "LOCATION_ACTIVATED", "HERO_CARD_PLAYED", "HERO_POWER_USED", "HERO_POWER_REPLACED", "CHOICE_CREATED",
        "CHOICE_RESOLVED", "RNG_ROLLED", "GAME_ENDED", "ENGINE_FAULTED"
      };
      var dispatcher = new EventVisualDispatcher();
      var router = new PresentationEventRouter();
      var animations = new PresentationAnimationRegistry();
      var audioRouter = new AudioEventRouter();
      var audioPlayback = new AudioPlaybackController();

      foreach (var eventType in declared)
      {
        Assert.That(EventVisualDispatcher.KnownEventTypes, Does.Contain(eventType), eventType);
        Assert.That(PresentationEventRouter.KnownPublicEventTypes, Does.Contain(eventType), eventType);
        var intent = router.Route(new PublicMatchEventRecord { EventSeq = 1, EventType = eventType });
        Assert.That(intent.Diagnostic, Is.Empty, eventType);
        Assert.That(animations.Resolve(intent).EventType, Is.EqualTo(eventType), eventType);
        var cue = audioRouter.CueForIntent(intent);
        Assert.That(cue, Is.Not.Empty, eventType);
        Assert.That(audioPlayback.Request(cue).Suppressed, Is.False, eventType);
        Assert.That(dispatcher.Dispatch(new PublicMatchEventRecord { EventSeq = 1, EventType = eventType }).Message, Is.Not.Empty, eventType);
      }
    }

    [Test]
    public void PresentationEffectsHost_AttachesToCanvasAndDoesNotBlockInput()
    {
      var parent = new GameObject("effects-parent", typeof(RectTransform));
      try
      {
        var host = PresentationEffectsHost.Attach(parent.transform);
        Assert.That(host, Is.Not.Null);
        Assert.That(host.transform.parent, Is.EqualTo(parent.transform));
        Assert.That(host.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
      }
      finally { UnityEngine.Object.DestroyImmediate(parent); }
    }

    [Test]
    public void EventVisualDispatcher_UnknownEventReturnsGenericFallback()
    {
      var dispatcher = new EventVisualDispatcher();

      var visual = dispatcher.Dispatch(new PublicMatchEventRecord { EventSeq = 1, EventType = "FUTURE_EVENT" });

      Assert.That(visual.Message, Does.Contain("FUTURE_EVENT"));
      Assert.That(visual.RefreshState, Is.True);
    }

    [Test]
    public void EventVisualQueue_EnqueuesBySequence()
    {
      var queue = new EventVisualQueue(new EventVisualDispatcher());

      queue.Enqueue(new PublicMatchEventRecord { EventSeq = 3, EventType = "TURN_ENDED" });
      queue.Enqueue(new PublicMatchEventRecord { EventSeq = 2, EventType = "TURN_STARTED" });

      Assert.That(queue.Advance().EventType, Is.EqualTo("TURN_STARTED"));
      Assert.That(queue.Advance().EventType, Is.EqualTo("TURN_ENDED"));
    }

    [Test]
    public void EventVisualQueue_DuplicateEventIsIgnoredBeforeQueue()
    {
      var queue = new EventVisualQueue(new EventVisualDispatcher());
      var evt = new PublicMatchEventRecord { EventSeq = 2, EventType = "TURN_STARTED" };

      Assert.That(queue.Enqueue(evt), Is.True);
      Assert.That(queue.Enqueue(evt), Is.False);
    }

    [Test]
    public void EventVisualQueue_TerminalEventInterruptsAndRemainsVisible()
    {
      var queue = new EventVisualQueue(new EventVisualDispatcher());

      queue.Enqueue(new PublicMatchEventRecord { EventSeq = 2, EventType = "TURN_STARTED" });
      queue.Enqueue(new PublicMatchEventRecord { EventSeq = 3, EventType = "ENGINE_FAULTED" });

      Assert.That(queue.Current.EventType, Is.EqualTo("ENGINE_FAULTED"));
      Assert.That(queue.Advance().EventType, Is.EqualTo("ENGINE_FAULTED"));
    }

    [Test]
    public void EventVisualTiming_UnknownEventUsesFallback()
    {
      var timing = new EventVisualTiming { FallbackSeconds = 0.42f };

      var duration = timing.DurationSeconds(new EventVisualMessage("FUTURE_EVENT", "future", "unknown", false, false, true, string.Empty));

      Assert.That(duration, Is.EqualTo(0.42f));
    }

    [Test]
    public void EventVisualDispatcher_TerminalFaultEventsAreBlocking()
    {
      var dispatcher = new EventVisualDispatcher();

      Assert.That(dispatcher.Dispatch(new PublicMatchEventRecord { EventSeq = 1, EventType = "GAME_ENDED" }).Blocking, Is.True);
      Assert.That(dispatcher.Dispatch(new PublicMatchEventRecord { EventSeq = 2, EventType = "ENGINE_FAULTED" }).Blocking, Is.True);
    }

    [Test]
    public void EventVisualDispatcher_ChoiceEventsRequestUiRefresh()
    {
      var dispatcher = new EventVisualDispatcher();

      Assert.That(dispatcher.Dispatch(new PublicMatchEventRecord { EventSeq = 1, EventType = "CHOICE_CREATED" }).RequestUiRefresh, Is.True);
      Assert.That(dispatcher.Dispatch(new PublicMatchEventRecord { EventSeq = 2, EventType = "CHOICE_RESOLVED" }).RequestUiRefresh, Is.True);
    }

    [Test]
    public void CardView_DoesNotRequireHiddenCardIdentity()
    {
      var label = CardView.Label(new PublicCardInstanceView { Visibility = "HIDDEN" });

      Assert.That(label, Does.Contain("card"));
      Assert.That(label, Does.Not.Contain("opponent-secret-card"));
    }

    [Test]
    public void GameThemeCatalog_ProvidesThreeStableExtensibleThemes()
    {
      var catalog = new GameThemeCatalog();

      Assert.That(catalog.Themes.Select(theme => theme.Id), Is.EqualTo(new[] { "arcane", "cyberpunk", "zen" }));
      Assert.That(catalog.Themes.All(theme => !string.IsNullOrWhiteSpace(theme.BoardTextureResource)), Is.True);
      Assert.That(catalog.Next("arcane").Id, Is.EqualTo("cyberpunk"));
      Assert.That(catalog.Next("zen").Id, Is.EqualTo("arcane"));
    }

    [Test]
    public void FullMatchCommandSelection_PrioritizesChoiceAttackPlayAndEndTurn()
    {
      var actions = new ExternalLegalActionsResult
      {
        LegalActions = new()
        {
          new MatchCommandDto { CommandType = "END_TURN" },
          new MatchCommandDto { CommandType = "ATTACK" },
          new MatchCommandDto { CommandType = "PLAY_CARD" },
          new MatchCommandDto { CommandType = "RESOLVE_CHOICE" }
        }
      };

      Assert.That(LocalMatchFlowDriver.SelectFullMatchCommand(actions).CommandType, Is.EqualTo("RESOLVE_CHOICE"));
      actions.LegalActions.RemoveAt(3);
      Assert.That(LocalMatchFlowDriver.SelectFullMatchCommand(actions).CommandType, Is.EqualTo("ATTACK"));
      actions.LegalActions.RemoveAt(1);
      Assert.That(LocalMatchFlowDriver.SelectFullMatchCommand(actions).CommandType, Is.EqualTo("PLAY_CARD"));
    }

    private static EngineCapabilityRegistry SupportedCapabilities()
    {
      return new EngineCapabilityRegistry
      {
        ApiContractVersion = CapabilityChecker.UnityClientContractVersion,
        StateSchemaVersion = "match-state-v1",
        EventSchemaVersion = "match-events-v1",
        MinimumUnityClientContractVersion = CapabilityChecker.UnityClientContractVersion,
        SupportedCommandTypes = new()
        {
          "END_TURN",
          "PLAY_CARD",
          "ATTACK",
          "RESOLVE_CHOICE"
        },
        SupportedEventTypes = new()
        {
          "GAME_STARTED",
          "TURN_STARTED",
          "TURN_ENDED",
          "CARD_PLAYED",
          "CARD_MOVED_ZONE",
          "CARD_DRAWN",
          "MINION_SUMMONED",
          "ENTITY_DAMAGED",
          "ENTITY_DIED",
          "CHOICE_CREATED",
          "CHOICE_RESOLVED",
          "GAME_ENDED"
        }
      };
    }

    private static MatchStateView PlayerProjection(string viewerPlayerId, string opponentCardId)
    {
      var opponentId = viewerPlayerId == "player-1" ? "player-2" : "player-1";
      return new MatchStateView
      {
        MatchId = "match",
        ViewKind = "PLAYER",
        ViewerPlayerId = viewerPlayerId,
        Players = new()
        {
          [viewerPlayerId] = new PublicPlayerStateView
          {
            PlayerId = viewerPlayerId,
            Relationship = "SELF",
            Zones = new PublicPlayerZonesView
            {
              Hand = new PublicZoneView
              {
                Zone = "HAND",
                Count = 1,
                Cards = new() { new PublicCardInstanceView { CardId = "own-card", Visibility = "VISIBLE" } }
              },
              Deck = new PublicZoneView { Zone = "DECK", Count = 9 }
            }
          },
          [opponentId] = new PublicPlayerStateView
          {
            PlayerId = opponentId,
            Relationship = "OPPONENT",
            Zones = new PublicPlayerZonesView
            {
              Hand = new PublicZoneView
              {
                Zone = "HAND",
                Count = 1,
                Cards = new() { new PublicCardInstanceView { CardId = opponentCardId, Visibility = "HIDDEN" } }
              },
              Deck = new PublicZoneView { Zone = "DECK", Count = 9 }
            }
          }
        }
      };
    }

    private static MatchStateView SpectatorProjection(string cardId)
    {
      return new MatchStateView
      {
        MatchId = "match",
        ViewKind = "SPECTATOR",
        Players = new()
        {
          ["player-1"] = new PublicPlayerStateView
          {
            PlayerId = "player-1",
            Relationship = "SPECTATED",
            Zones = new PublicPlayerZonesView
            {
              Hand = new PublicZoneView
              {
                Zone = "HAND",
                Count = 1,
                Cards = new() { new PublicCardInstanceView { CardId = cardId, Visibility = "HIDDEN" } }
              },
              Deck = new PublicZoneView { Zone = "DECK", Count = 9 }
            }
          }
        }
      };
    }
  }
}
