using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Unity.Client.DebugTools;
using CardGame.Unity.Client.Match;
using CardGame.Unity.Client.UI.Presentation;
using CardGame.Unity.Client.UI.Theme;
using Game.UnityClient.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame.Unity.Client.UI
{
  public sealed class CardGameScreen
  {
    private readonly LegalActionPresenter legalActionPresenter = new();
    private readonly Dictionary<string, GameplayCardPresentation> cardCatalog = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GameObject> dynamicObjects = new();
    private readonly List<Image> themedPanels = new();
    private readonly List<Text> themedTexts = new();
    private readonly Dictionary<string, Texture2D> proceduralArt = new(StringComparer.OrdinalIgnoreCase);
    private Texture2D[] authoredCardArt;
    private readonly Dictionary<string, RectTransform> presentationAnchors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int?> currentHandCosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> lastManaCosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> lastAttackValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> lastHealthValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly GameThemeCatalog themeCatalog = new();
    private Sprite roundedUiSprite;

    private GameTheme theme = GameTheme.CreateDefault();
    private RawImage boardBackground;
    private Text themeText;

    private Transform root;
    private Transform opponentArea;
    private Transform opponentHeroArea;
    private Transform opponentBoard;
    private Transform playerBoard;
    private Transform playerHeroArea;
    private Transform handArea;
    private Transform actionArea;
    private Transform choiceArea;
    private Transform opponentGraveyardArea;
    private Transform playerGraveyardArea;
    private Text bootText;
    private Text headerText;
    private Text eventLogText;
    private Text errorText;
    private Text visualText;
    private Text modeText;
    private Button endTurnButton;
    private Text endTurnButtonText;
    private Text endTurnStatusText;
    private Image endTurnGlow;
    private GameObject pauseOverlay;
    private GameObject cardPreviewOverlay;
    private RawImage cardPreviewArt;
    private Text cardPreviewTitle;
    private Text cardPreviewStats;
    private Text cardPreviewManaBadge;
    private Text cardPreviewAttackBadge;
    private Text cardPreviewHealthBadge;
    private Text cardPreviewRules;
    private RectTransform playDropArea;
    private Image playDropSurface;
    private Text playDropLabel;
    private Canvas gameplayCanvas;
    private TargetingArrowRig targetingArrow;
    private CombatLungeRig combatLunge;
    private ZoneTransferAnimationRig zoneTransfer;
    private CombatDamageOverlayRig combatDamage;
    private TurnTransitionRig turnTransition;
    private readonly Dictionary<string,RectTransform> graveyardAnchors=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,Vector3> lastKnownWorldAnchors=new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(RectTransform Rect,Vector3? PreviousWorld)> pendingBoardSettles=new();
    private string currentViewerPlayerId=string.Empty;
    private string currentOpponentPlayerId=string.Empty;
    private bool developerTools;
    public Transform PresentationEffectsParent => gameplayCanvas == null ? null : gameplayCanvas.transform;

    public event Action BootRequested;
    public event Action SeedRequested;
    public event Action StartRequested;
    public event Action ModeCycleRequested;
    public event Action SwitchViewerRequested;
    public event Action RefreshStateRequested;
    public event Action RefreshLegalActionsRequested;
    public event Action<MatchCommandDto> SubmitCommandRequested;
    public event Action<LegalActionViewModel> TargetSelectionRequested;
    public event Action<string> TargetSelectedRequested;
    public event Action TargetSelectionCancelledRequested;
    public event Action<PublicChoiceView, PublicChoiceOptionView, MatchCommandDto> ResolveChoiceRequested;

    public void SetCardCatalog(IEnumerable<GameplayCardPresentation> cards)
    {
      cardCatalog.Clear();
      foreach (var card in cards ?? Array.Empty<GameplayCardPresentation>())
      {
        if (!string.IsNullOrWhiteSpace(card.CardId)) cardCatalog[card.CardId] = card;
        if (!string.IsNullOrWhiteSpace(card.CardVersionId)) cardCatalog[card.CardVersionId] = card;
      }
    }
    public event Action ReconnectRequested;
    public event Action ReplayRequested;
    public event Action RestartRequested;
    public event Action MainMenuRequested;
    public event Action SettingsRequested;
    public event Action TogglePollingRequested;
    public event Action ExportPlaytestReportRequested;

    public void Build(Transform parent, bool developerTools = false)
    {
      theme = themeCatalog.Find(PlayerPrefs.GetString("cardgame.board-theme", "arcane"));
      this.developerTools = developerTools;
      var canvasObject = new GameObject("CardGame Playable Board Canvas");
      canvasObject.transform.SetParent(parent, false);
      var canvas = canvasObject.AddComponent<Canvas>();
      gameplayCanvas = canvas;
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      var scaler = canvasObject.AddComponent<CanvasScaler>();
      scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
      scaler.referenceResolution = new Vector2(1440, 900);
      scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
      canvasObject.AddComponent<GraphicRaycaster>();
      targetingArrow=canvasObject.AddComponent<TargetingArrowRig>();targetingArrow.Initialize(canvas);
      combatLunge=canvasObject.AddComponent<CombatLungeRig>();
      zoneTransfer=canvasObject.AddComponent<ZoneTransferAnimationRig>();
      combatDamage=canvasObject.AddComponent<CombatDamageOverlayRig>();
      turnTransition=canvasObject.AddComponent<TurnTransitionRig>();turnTransition.Initialize(canvasObject.transform);

      var backgroundObject = new GameObject("Theme Board Background");
      backgroundObject.transform.SetParent(canvasObject.transform, false);
      var backgroundRect = backgroundObject.AddComponent<RectTransform>();
      backgroundRect.anchorMin = Vector2.zero;
      backgroundRect.anchorMax = Vector2.one;
      backgroundRect.offsetMin = Vector2.zero;
      backgroundRect.offsetMax = Vector2.zero;
      boardBackground = backgroundObject.AddComponent<RawImage>();
      boardBackground.raycastTarget = false;

      var vignette = Panel(canvasObject.transform, "Gameplay Vignette", new Color(0f, 0f, 0f, 0.24f));
      var vignetteRect = vignette.GetComponent<RectTransform>();
      vignetteRect.anchorMin = Vector2.zero;
      vignetteRect.anchorMax = Vector2.one;
      vignetteRect.offsetMin = Vector2.zero;
      vignetteRect.offsetMax = Vector2.zero;

      var rootObject = Panel(canvasObject.transform, "Board Root", new Color(0.07f, 0.08f, 0.09f, 0.22f));
      root = rootObject.transform;
      var rootRect = rootObject.GetComponent<RectTransform>();
      rootRect.anchorMin = Vector2.zero;
      rootRect.anchorMax = Vector2.one;
      rootRect.offsetMin = Vector2.zero;
      rootRect.offsetMax = Vector2.zero;
      var rootLayout = rootObject.AddComponent<VerticalLayoutGroup>();
      rootLayout.padding = new RectOffset(24, 184, 12, 14);
      rootLayout.spacing = 3;
      rootLayout.childControlHeight = true;
      rootLayout.childControlWidth = true;

      var top = Row(root, "Utility Rail", 32);
      themeText = TextBlock(top.transform, "Theme", 13);
      themeText.alignment = TextAnchor.MiddleLeft;
      if (developerTools)
      {
        Button(top.transform, "Theme", CycleTheme, 92);
        Button(top.transform, "Connect", () => BootRequested?.Invoke(), 92);
        Button(top.transform, "Prepare", () => SeedRequested?.Invoke(), 92);
        Button(top.transform, "Play", () => StartRequested?.Invoke(), 80);
        Button(top.transform, "View", () => ModeCycleRequested?.Invoke(), 72);
        Button(top.transform, "Switch", () => SwitchViewerRequested?.Invoke(), 78);
        Button(top.transform, "Sync", () => RefreshStateRequested?.Invoke(), 68);
        Button(top.transform, "Evidence", () => ExportPlaytestReportRequested?.Invoke(), 88);
      }
      Button(top.transform, L("MENU", "МЕНЮ"), TogglePause, 96);

      bootText = TextPanel(root, "Connection Status", 32, 11);
      modeText = TextPanel(root, "Mode Status", 30, 11);
      headerText = TextPanel(root, "Match Header", 32, 14);
      headerText.alignment = TextAnchor.MiddleCenter;
      visualText = TextPanel(root, "Event Visual", 38, 17);
      visualText.alignment = TextAnchor.MiddleCenter;

      opponentHeroArea = Row(root, "Opponent Hero Stage", 96).transform;
      opponentArea = Row(root, "Opponent Hand", 64).transform;
      opponentArea.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
      opponentBoard = Row(root, "Opponent Battlefield", 126).transform;
      playerBoard = Row(root, "Player Battlefield", 126).transform;
      handArea = Row(root, "Player Hand", 154).transform;
      handArea.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
      playerHeroArea = Row(root, "Player Hero Stage", 96).transform;
      actionArea = Row(root, "Action Dock", 40).transform;
      choiceArea = Row(root, "Choice Overlay", 64).transform;

      BuildPlayDropLane(canvasObject.transform);
      BuildRightCombatRail(canvasObject.transform);

      var bottom = Row(root, "Event Feed And Diagnostics", 88);
      eventLogText = TextBlock(bottom.transform, "Events", 12);
      errorText = TextBlock(bottom.transform, "Errors", 12);
      if (!developerTools)
      {
        bootText.transform.parent.gameObject.SetActive(false);
        modeText.transform.parent.gameObject.SetActive(false);
        visualText.transform.parent.gameObject.SetActive(false);
        bottom.SetActive(false);
      }
      BuildCardPreview(canvasObject.transform);
      BuildPauseOverlay(canvasObject.transform);
      ApplyTheme();
    }

    public void SetVisible(bool visible)
    {
      if (visible)
      {
        var selectedTheme = themeCatalog.Find(PlayerPrefs.GetString("cardgame.board-theme", "arcane"));
        if (!string.Equals(selectedTheme.Id, theme.Id, StringComparison.OrdinalIgnoreCase)) { theme = selectedTheme; ApplyTheme(); }
      }
      if (gameplayCanvas != null) gameplayCanvas.enabled = visible;
    }

    public void Render(
      string backendUrl,
      CapabilityChecker capabilityChecker,
      MatchStateView state,
      string activeViewerPlayerId,
      ClientViewMode mode,
      TargetSelectionState targetSelection,
      ClientUiState uiState,
      ExternalLegalActionsResult legalActions,
      IReadOnlyList<string> eventLines,
      string error,
      string visual,
      bool polling,
      bool commandsEnabled,
      bool reconnecting)
    {
      ClearDynamic();
      lastTargetSelection=targetSelection;
      themeText.text = $"{L("BATTLEFIELD", "ПОЛЕ БОЯ")}  •  {theme.DisplayName}";
      bootText.text =
        $"Backend: {backendUrl}\n" +
        $"{capabilityChecker?.BootStatus ?? "Not checked"}\n" +
        $"Polling: {(polling ? "running" : "stopped")}";
      modeText.text =
        $"Mode: {mode} | State: {uiState} | Projection: {activeViewerPlayerId} | Commands: {(commandsEnabled ? "enabled" : "disabled")}" +
        (reconnecting ? " | Reconnecting" : string.Empty) +
        (mode == ClientViewMode.DebugBoth ? "\n" + ClientModeState.DebugBothWarning : string.Empty);

      headerText.text = state == null
        ? L("MATCH NOT STARTED", "МАТЧ НЕ НАЧАТ")
        : developerTools
          ? $"Match {state.MatchId} | Viewer {activeViewerPlayerId} | Active {state.ActivePlayerId} | Turn {state.TurnNumber} | {state.Phase} | Hash {ShortHash(state.StateHash)} | Commands {state.CommandCount} | Next event {state.NextEventSeq}"
          : $"{L("TURN", "ХОД")} {state.TurnNumber}  •  {LocalizePhase(state.Phase)}  •  {(state.ActivePlayerId == activeViewerPlayerId ? L("YOUR MOVE", "ВАШ ХОД") : L("OPPONENT'S MOVE", "ХОД СОПЕРНИКА"))}";
      visualText.text = string.IsNullOrWhiteSpace(visual) ? "No event visual." : visual;

      var self = FindPlayer(state, activeViewerPlayerId);
      var opponent = state?.Players?.Values.FirstOrDefault(player => player.PlayerId != activeViewerPlayerId);
      currentViewerPlayerId=self?.PlayerId??activeViewerPlayerId??string.Empty;
      currentOpponentPlayerId=opponent?.PlayerId??string.Empty;
      var grouped = legalActionPresenter.Group(legalActions);
      var interactionEnabled=commandsEnabled&&!reconnecting;
      // The board itself is an input surface. Do not merely disable the action dock:
      // suppress card, entity, hero-power, and target handlers while the client state
      // machine owns input (reconnect, replay mismatch, terminal, or another block).
      var actionViewModels = interactionEnabled&&targetSelection?.IsActive!=true
        ? grouped.SelectMany(group => group.Actions).ToList()
        : new List<LegalActionViewModel>();
      var targetHighlight = new TargetHighlightView(targetSelection);

      RenderOpponent(opponent);
      RenderHeroLoadout(opponentHeroArea,opponent,false,actionViewModels,targetHighlight);
      RenderBoard(opponentBoard, opponent, actionViewModels, targetHighlight, L("Opponent board", "Поле соперника"));
      RenderGraveyard(opponentGraveyardArea,opponent,false);
      RenderBoard(playerBoard, self, actionViewModels, targetHighlight, L("Your board", "Ваше поле"));
      RenderPlayer(self, actionViewModels, targetHighlight);
      RenderHeroLoadout(playerHeroArea,self,true,actionViewModels,targetHighlight);
      RenderGraveyard(playerGraveyardArea,self,true);
      RenderActions(grouped, targetSelection, interactionEnabled);
      RenderChoices(state, activeViewerPlayerId, legalActions, interactionEnabled);
      UpdateTargetingArrow(targetSelection);

      eventLogText.text = "Events\n" + string.Join("\n", (eventLines ?? Array.Empty<string>()).Reverse().Take(10).Reverse());
      errorText.text = string.IsNullOrWhiteSpace(error) ? "Errors\nNone" : "Errors\n" + error;
      // Dynamic cards are rebuilt from authoritative state. Resolve all nested row
      // layouts in the same frame so new entities never flash at their prefab origin.
      if(root is RectTransform boardRoot)LayoutRebuilder.ForceRebuildLayoutImmediate(boardRoot);
      Canvas.ForceUpdateCanvases();
      foreach(var settle in pendingBoardSettles)
      {
        if(settle.Rect==null)continue;
        // Existing entities are rebuilt from authoritative snapshots. Re-animating
        // their layout position on every refresh made the whole board twitch at the
        // start of each turn. Only a genuinely new entity receives an entrance.
        if(!settle.PreviousWorld.HasValue)settle.Rect.gameObject.AddComponent<BoardReflowRig>().PlaySpawn(PlayerPrefs.GetInt("cardgame.reduced-motion",0)==1);
      }
      pendingBoardSettles.Clear();
    }

    private void RenderOpponent(PublicPlayerStateView opponent)
    {
      if (opponent == null)
      {
        return;
      }

      LabelCard(opponentArea, L("Deck", "Колода") + "\n" + opponent.Zones.Deck.Count, false, false, null);
      for (var i = 0; i < Math.Max(1, opponent.Zones.Hand.Count); i++)
      {
        LabelCard(opponentArea, L("Hidden\nCard", "Скрытая\nкарта"), false, false, null);
      }
    }

    private void RenderPlayer(PublicPlayerStateView player, IReadOnlyList<LegalActionViewModel> actions, TargetHighlightView targetHighlight)
    {
      currentHandCosts.Clear();
      if (player == null)
      {
        return;
      }

      LabelCard(handArea, L("Deck", "Колода") + "\n" + player.Zones.Deck.Count, false, false, null);
      foreach (var card in player.Zones.Hand.Cards)
      {
        var visible = IsCardVisibleToPlayer(player.Relationship, card.Visibility);
        var refId = card.CardInstanceId ?? card.CardId ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(refId)) currentHandCosts[refId] = card.EffectiveManaCost ?? card.BaseManaCost;
        var highlighted = targetHighlight.IsHighlighted(refId) || legalActionPresenter.IsHighlighted(actions, refId);
        var label = visible
          ? CardLabel(card)
          : L("Hidden\nCard", "Скрытая\nкарта");
        var action = FindOriginInteraction(actions, refId);
        var state=ResolveInteractionState(true,true,targetHighlight.IsSource(refId),targetHighlight.IsHighlighted(refId),action);
        VisualCard(handArea, visible ? FindPresentation(card) : null, null, refId, visible, highlighted, targetHighlight.IsSource(refId), false,
          action != null && !action.RequiresTargetSelection ? action.Template : null,
          targetHighlight.IsHighlighted(refId) ? refId : null,
          action != null && action.RequiresTargetSelection ? () => TargetSelectionRequested?.Invoke(action) : null,
          label,state,InteractionStateLabel(state));
      }
    }

    private void RenderBoard(Transform target, PublicPlayerStateView player, IReadOnlyList<LegalActionViewModel> actions, TargetHighlightView targetHighlight, string emptyLabel)
    {
      if (player?.Zones?.Board == null || player.Zones.Board.Count == 0)
      {
        return;
      }

      foreach (var entity in player.Zones.Board)
      {
        var selectableTarget = targetHighlight.IsHighlighted(entity.EntityInstanceId);
        var highlighted = selectableTarget || legalActionPresenter.IsHighlighted(actions, entity.EntityInstanceId);
        var action = FindOriginInteraction(actions, entity.EntityInstanceId);
        var local=string.Equals(entity.ControllerPlayerId,currentViewerPlayerId,StringComparison.OrdinalIgnoreCase);
        var state=ResolveInteractionState(false,local,targetHighlight.IsSource(entity.EntityInstanceId),selectableTarget,action);
        VisualCard(target, FindPresentation(entity), entity, entity.EntityInstanceId, true, highlighted, targetHighlight.IsSource(entity.EntityInstanceId), selectableTarget,
          action != null && !action.RequiresTargetSelection ? action.Template : null,
          selectableTarget ? entity.EntityInstanceId : null,
          action != null && action.RequiresTargetSelection ? () => TargetSelectionRequested?.Invoke(action) : null,
          EntityLabel(entity),state,InteractionStateLabel(state));
      }
    }

    private void RenderGraveyard(Transform target,PublicPlayerStateView player,bool local)
    {
      if(target==null||player?.Zones?.Graveyard==null)return;
      var count=player.Zones.Graveyard.Count;
      var top=local?player.Zones.Graveyard.Cards?.LastOrDefault():null;
      var pile=Panel(target,local?"Player Graveyard Pile":"Opponent Graveyard Pile",new Color(.035f,.04f,.065f,.98f));
      dynamicObjects.Add(pile);
      var layout=pile.AddComponent<LayoutElement>();layout.minWidth=166;layout.preferredWidth=166;layout.minHeight=104;layout.preferredHeight=104;layout.flexibleWidth=0;
      var pileRect=pile.GetComponent<RectTransform>();
      var shadow=Panel(pile.transform,"Graveyard Card Shadow",new Color(.01f,.01f,.018f,.9f));
      var shadowRect=shadow.GetComponent<RectTransform>();shadowRect.anchorMin=shadowRect.anchorMax=new Vector2(.5f,.5f);shadowRect.pivot=new Vector2(.5f,.5f);shadowRect.sizeDelta=new Vector2(72,88);shadowRect.anchoredPosition=new Vector2(10,-5);shadowRect.localRotation=Quaternion.Euler(0,0,6);
      var card=Panel(pile.transform,"Graveyard Top Card",new Color(.11f,.075f,.15f,1f));
      var cardRect=card.GetComponent<RectTransform>();cardRect.anchorMin=cardRect.anchorMax=new Vector2(.5f,.5f);cardRect.pivot=new Vector2(.5f,.5f);cardRect.sizeDelta=new Vector2(72,88);cardRect.anchoredPosition=new Vector2(-5,0);cardRect.localRotation=Quaternion.Euler(0,0,-4);
      var artNode=new GameObject("Graveyard Card Art");artNode.transform.SetParent(card.transform,false);var artRect=artNode.AddComponent<RectTransform>();SetRect(artRect,new Vector2(.07f,.07f),new Vector2(.93f,.93f));var art=artNode.AddComponent<RawImage>();
      var topPresentation=local?FindPresentation(top):null;art.texture=local&&topPresentation!=null?ResolveArt(topPresentation,top?.CardId):Resources.Load<Texture2D>("CardArt/card-back-celestial-forge-v1");art.color=art.texture==null?new Color(.08f,.1f,.16f):Color.white;art.raycastTarget=false;
      var title=TextBlock(pile.transform,"Graveyard Title",12);title.fontStyle=FontStyle.Bold;title.alignment=TextAnchor.UpperCenter;title.text=local?L("YOUR DISCARD","ВАШ СБРОС"):L("ENEMY DISCARD","СБРОС ВРАГА");SetRect(title.rectTransform,new Vector2(.02f,.77f),new Vector2(.98f,.98f));
      var countBadge=Badge(pile.transform,"Graveyard Count",count.ToString(),new Color(.42f,.12f,.55f,1f));AnchorBadge(countBadge,new Vector2(1,0),new Vector2(-7,7));
      var topName=TextBlock(pile.transform,"Graveyard Top Card Name",9);topName.alignment=TextAnchor.LowerCenter;topName.fontStyle=FontStyle.Bold;topName.text=count==0?L("EMPTY","ПУСТО"):local?(topPresentation?.DisplayName??L("FALLEN CARD","ПАВШАЯ КАРТА")):L("HIDDEN","СКРЫТО");SetRect(topName.rectTransform,new Vector2(.03f,.02f),new Vector2(.97f,.23f));topName.resizeTextForBestFit=true;topName.resizeTextMinSize=7;topName.resizeTextMaxSize=9;
      graveyardAnchors[player.PlayerId]=pileRect;
    }

    public void AnimateCommittedZoneEvent(PublicMatchEventRecord evt,bool reducedMotion)
    {
      if(evt==null||zoneTransfer==null)return;
      var playerId=Payload(evt,"playerId");var targetRef=evt.TargetRefs?.FirstOrDefault()?.RefId;
      var cardVersion=Payload(evt,"cardVersion");var presentation=FindPresentationFromReference(cardVersion)??FindPresentationFromReference(Payload(evt,"sourceCardInstanceId"))??FindPresentationFromReference(targetRef);
      var texture=presentation==null?null:ResolveArt(presentation,presentation.CardId);
      if(evt.EventType=="ENTITY_DIED"&&graveyardAnchors.TryGetValue(playerId,out var grave)&&grave!=null&&TryGetPresentationWorldAnchor(targetRef,out var deadWorld))
        zoneTransfer.Play(deadWorld,grave.TransformPoint(grave.rect.center),"☠",reducedMotion,texture,presentation?.DisplayName);
      if(evt.EventType=="CARD_MOVED_ZONE"&&string.Equals(Payload(evt,"fromZone"),"GRAVEYARD",StringComparison.OrdinalIgnoreCase)&&graveyardAnchors.TryGetValue(playerId,out grave)&&grave!=null)
      {
        var local=string.Equals(playerId,currentViewerPlayerId,StringComparison.OrdinalIgnoreCase);
        var toHand=string.Equals(Payload(evt,"toZone"),"HAND",StringComparison.OrdinalIgnoreCase);
        var destination=(toHand?(local?handArea:opponentArea):(local?playerBoard:opponentBoard)) as RectTransform;
        if(destination!=null)zoneTransfer.Play(grave.TransformPoint(grave.rect.center),destination.TransformPoint(destination.rect.center),"↟",reducedMotion,texture,presentation?.DisplayName);
      }
    }

    public System.Threading.Tasks.Task AnimatePreRemovalDeathAsync(PublicMatchEventRecord evt,bool reducedMotion)
    {
      if(evt==null||zoneTransfer==null||!string.Equals(evt.EventType,"ENTITY_DIED",StringComparison.OrdinalIgnoreCase))return System.Threading.Tasks.Task.CompletedTask;
      var targetRef=evt.TargetRefs?.FirstOrDefault()?.RefId;
      return TryGetPresentationWorldAnchor(targetRef,out var world)
        ? zoneTransfer.PlayDeathReaction(world,reducedMotion)
        : System.Threading.Tasks.Task.CompletedTask;
    }

    public void AnimateTurnEvent(PublicMatchEventRecord evt,string title,string subtitle,bool localTurn,bool reducedMotion)
    {
      if(evt==null||turnTransition==null||
        (!string.Equals(evt.EventType,"TURN_STARTED",StringComparison.OrdinalIgnoreCase)&&!string.Equals(evt.EventType,"EXTRA_TURN_SCHEDULED",StringComparison.OrdinalIgnoreCase)))return;
      turnTransition.Play(title,subtitle,localTurn,reducedMotion);
    }

    public void AnimateCommittedCombatEvent(PublicMatchEventRecord evt,bool reducedMotion)
    {
      if(evt==null||combatDamage==null||!string.Equals(evt.EventType,"ENTITY_DAMAGED",StringComparison.OrdinalIgnoreCase))return;
      var amount=IntPayload(evt,"amount");if(!amount.HasValue||amount.Value<=0)return;
      // Render() has just rebuilt the dynamic board. Resolve layout immediately so
      // combat cues use the new card positions instead of the previous frame's row origin.
      if(root is RectTransform boardRoot)LayoutRebuilder.ForceRebuildLayoutImmediate(boardRoot);
      Canvas.ForceUpdateCanvases();
      foreach(var target in evt.TargetRefs??new List<PublicEventRef>())
        if(TryGetPresentationWorldAnchor(target?.RefId,out var world))combatDamage.Play(world,-amount.Value,reducedMotion);
    }

    private bool TryGetPresentationWorldAnchor(string reference,out Vector3 world)
    {
      world=Vector3.zero;if(string.IsNullOrWhiteSpace(reference))return false;
      if(presentationAnchors.TryGetValue(reference,out var live)&&live!=null){world=live.TransformPoint(live.rect.center);lastKnownWorldAnchors[reference]=world;return true;}
      return lastKnownWorldAnchors.TryGetValue(reference,out world);
    }

    private GameplayCardPresentation FindPresentationFromReference(string reference)
    {
      if(string.IsNullOrWhiteSpace(reference))return null;
      if(cardCatalog.TryGetValue(reference,out var direct))return direct;
      return cardCatalog.Values.FirstOrDefault(card=>(!string.IsNullOrWhiteSpace(card.CardVersionId)&&reference.IndexOf(card.CardVersionId,StringComparison.OrdinalIgnoreCase)>=0)||(!string.IsNullOrWhiteSpace(card.CardId)&&reference.IndexOf(card.CardId,StringComparison.OrdinalIgnoreCase)>=0));
    }

    private static string Payload(PublicMatchEventRecord evt,string key)=>evt?.Payload?.FirstOrDefault(entry=>string.Equals(entry.Key,key,StringComparison.OrdinalIgnoreCase))?.Value?.StringValue??string.Empty;
    private static int? IntPayload(PublicMatchEventRecord evt,string key)=>evt?.Payload?.FirstOrDefault(entry=>string.Equals(entry.Key,key,StringComparison.OrdinalIgnoreCase))?.Value?.IntValue;

    private GameplayCardPresentation FindPresentation(PublicCardInstanceView card)
    {
      if (card == null) return null;
      if (!string.IsNullOrWhiteSpace(card.CardVersion) && cardCatalog.TryGetValue(card.CardVersion, out var found)) return found;
      return !string.IsNullOrWhiteSpace(card.CardId) && cardCatalog.TryGetValue(card.CardId, out found) ? found : null;
    }

    private void RenderEquipped(Transform target, PublicEntityView equippedEntity, IReadOnlyList<LegalActionViewModel> actions, TargetHighlightView targetHighlight)
    {
      if (equippedEntity == null) return;
      var reference = equippedEntity.EntityInstanceId;
      var selectableTarget = targetHighlight.IsHighlighted(reference);
      var action = FindOriginInteraction(actions, reference);
      var local=string.Equals(equippedEntity.ControllerPlayerId,currentViewerPlayerId,StringComparison.OrdinalIgnoreCase);
      var state=ResolveInteractionState(false,local,targetHighlight.IsSource(reference),selectableTarget,action);
      VisualCard(target, FindPresentation(equippedEntity), equippedEntity, reference, true,
        selectableTarget || legalActionPresenter.IsHighlighted(actions, reference),
        targetHighlight.IsSource(reference), selectableTarget,
        action != null && !action.RequiresTargetSelection ? action.Template : null,
        selectableTarget ? reference : null,
        action != null && action.RequiresTargetSelection ? () => TargetSelectionRequested?.Invoke(action) : null,
        EntityLabel(equippedEntity),state,InteractionStateLabel(state));
    }

    private GameplayCardPresentation FindPresentation(PublicEntityView entity) => cardCatalog.Values.FirstOrDefault(card =>
      !string.IsNullOrWhiteSpace(card.CardId) && entity?.SourceCardInstanceId?.IndexOf(":" + card.CardId + ":", StringComparison.OrdinalIgnoreCase) >= 0);

    private void RenderHeroLoadout(Transform target,PublicPlayerStateView player,bool local,IReadOnlyList<LegalActionViewModel> actions,TargetHighlightView highlight)
    {
      if(target==null||player==null)return;
      RenderHeroResources(target,player);
      RenderHeroCard(target,player,local,highlight);
      RenderHeroPower(target,player,local,actions,highlight);
      RenderEquipped(target,player.Weapon,actions,highlight);
      RenderEquipped(target,player.Location,actions,highlight);
    }

    private void RenderHeroResources(Transform target,PublicPlayerStateView player)
    {
      var plaque=Panel(target,"Hero Resource Plaque",new Color(.025f,.035f,.055f,.96f));dynamicObjects.Add(plaque);
      var layout=plaque.AddComponent<LayoutElement>();layout.minWidth=104;layout.preferredWidth=112;layout.minHeight=54;layout.preferredHeight=58;layout.flexibleWidth=0;
      var outline=plaque.AddComponent<Outline>();outline.effectColor=new Color(.18f,.42f,.68f,.5f);outline.effectDistance=new Vector2(1,-1);
      var resources=TextBlock(plaque.transform,"Hero Resources",12);resources.alignment=TextAnchor.MiddleCenter;resources.fontStyle=FontStyle.Bold;resources.text=$"{player.Mana}/{player.MaxMana} {L("MANA","МАНА")}\n{player.Zones.Deck.Count} {L("DECK","КОЛОДА")}";SetRect(resources.rectTransform,new Vector2(.05f,.08f),new Vector2(.95f,.92f));
    }

    private void RenderHeroPower(Transform target,PublicPlayerStateView player,bool local,IReadOnlyList<LegalActionViewModel> actions,TargetHighlightView highlight)
    {
      if(player?.HeroPower==null)return;
      var source=player.HeroPower.SourceCardInstanceId??string.Empty;
      var action=local?FindHeroPowerInteraction(actions,source):null;
      var selectedSource=highlight.IsSource(source);
      var enabled=action?.Enabled==true;
      var color=selectedSource?new Color(.08f,.55f,.9f,1f):enabled?new Color(.17f,.38f,.62f,1f):new Color(.08f,.1f,.15f,1f);
      var power=Panel(target,local?"Player Hero Power":"Opponent Hero Power",color);dynamicObjects.Add(power);
      var powerLayout=power.AddComponent<LayoutElement>();powerLayout.minWidth=76;powerLayout.preferredWidth=82;powerLayout.minHeight=68;powerLayout.preferredHeight=74;powerLayout.flexibleWidth=0;
      var powerImage=power.GetComponent<Image>();powerImage.sprite=RoundedUiSprite();powerImage.type=Image.Type.Sliced;
      var outline=power.AddComponent<Outline>();outline.effectColor=enabled?new Color(.3f,.8f,1f,.92f):new Color(.26f,.3f,.38f,.75f);outline.effectDistance=new Vector2(3,-3);
      var sigil=TextBlock(power.transform,"Hero Power Sigil",27);sigil.text=source.IndexOf("cinder_spark",StringComparison.OrdinalIgnoreCase)>=0?"✦":"✧";sigil.alignment=TextAnchor.MiddleCenter;sigil.color=source.IndexOf("cinder_spark",StringComparison.OrdinalIgnoreCase)>=0?new Color(1f,.34f,.08f):new Color(.36f,.78f,1f);SetRect(sigil.rectTransform,new Vector2(.08f,.3f),new Vector2(.92f,.94f));
      var name=TextBlock(power.transform,"Hero Power Name",9);name.text=HeroPowerName(source);name.fontStyle=FontStyle.Bold;name.alignment=TextAnchor.MiddleCenter;name.resizeTextForBestFit=true;name.resizeTextMinSize=7;name.resizeTextMaxSize=9;SetRect(name.rectTransform,new Vector2(.08f,.05f),new Vector2(.92f,.34f));
      var cost=Badge(power.transform,"Hero Power Cost",player.HeroPower.Cost.ToString(),new Color(.05f,.4f,.94f,1f));AnchorBadge(cost,Vector2.zero,new Vector2(3,3));
      Action usePower=enabled?(action.RequiresTargetSelection?()=>TargetSelectionRequested?.Invoke(action):()=>SubmitCommandRequested?.Invoke(action.Template)):null;
      var preview=power.AddComponent<CardLongPressPreview>();preview.Configure(()=>ShowHeroPowerPreview(player),HideCardPreview);
      var button=power.AddComponent<Button>();button.onClick.AddListener(()=>{if(!preview.ConsumeClick())usePower?.Invoke();});
      var rect=power.GetComponent<RectTransform>();if(!string.IsNullOrWhiteSpace(source))presentationAnchors[source]=rect;
      power.AddComponent<PremiumCardVisualRig>().Configure(enabled||selectedSource,false);
    }

    private void RenderHeroCard(Transform target,PublicPlayerStateView player,bool local,TargetHighlightView highlight)
    {
      if(target==null||player?.Hero==null)return;
      var heroRef=HeroTargetReference(player);
      var selectable=highlight.IsHighlighted(heroRef)||highlight.IsHighlighted(player.PlayerId);
      var selectedSource=highlight.IsSource(heroRef);
      var border=selectable?new Color(1f,.16f,.08f,1f):selectedSource?new Color(.1f,.72f,1f,1f):new Color(.78f,.52f,.14f,1f);
      var card=Panel(target,local?"Player Hero Card":"Opponent Hero Card",border);dynamicObjects.Add(card);
      var cardLayout=card.AddComponent<LayoutElement>();cardLayout.minWidth=78;cardLayout.preferredWidth=84;cardLayout.minHeight=88;cardLayout.preferredHeight=92;cardLayout.flexibleWidth=0;
      var face=Panel(card.transform,"Hero Card Face",new Color(.025f,.035f,.06f,1f));var faceRect=face.GetComponent<RectTransform>();SetRect(faceRect,new Vector2(.025f,.035f),new Vector2(.975f,.965f));
      var heroArtNode=new GameObject("Hero Identity Art");heroArtNode.transform.SetParent(face.transform,false);var artRect=heroArtNode.AddComponent<RectTransform>();SetRect(artRect,new Vector2(.05f,.13f),new Vector2(.95f,.96f));var heroArt=heroArtNode.AddComponent<RawImage>();heroArt.texture=ResolveHeroArt(player);heroArt.color=heroArt.texture==null?HeroFallbackColor(player):Color.white;heroArt.raycastTarget=false;heroArt.uvRect=new Rect(.08f,0,.84f,1f);
      var shade=Panel(face.transform,"Hero Portrait Shade",new Color(0,0,0,.18f));var shadeRect=shade.GetComponent<RectTransform>();SetRect(shadeRect,Vector2.zero,Vector2.one);shade.GetComponent<Image>().raycastTarget=false;
      var frameNode=new GameObject("Hero Card Frame");frameNode.transform.SetParent(face.transform,false);var frameRect=frameNode.AddComponent<RectTransform>();frameRect.anchorMin=Vector2.zero;frameRect.anchorMax=Vector2.one;frameRect.offsetMin=new Vector2(-5,-5);frameRect.offsetMax=new Vector2(5,5);var frame=frameNode.AddComponent<RawImage>();frame.texture=Resources.Load<Texture2D>(theme?.Sprites?.CardFrameResource??"CardFrames/frame-arcane-v1");frame.raycastTarget=false;
      var namePlate=Panel(face.transform,"Hero Name Plate",new Color(.035f,.025f,.02f,.94f));var nameRect=namePlate.GetComponent<RectTransform>();SetRect(nameRect,new Vector2(.08f,.02f),new Vector2(.92f,.23f));namePlate.GetComponent<Image>().raycastTarget=false;
      var name=TextBlock(face.transform,"Hero Name",12);name.text=HeroDisplayName(player,local?L("YOUR HERO","ВАШ ГЕРОЙ"):L("ENEMY HERO","ГЕРОЙ ВРАГА"));name.fontStyle=FontStyle.Bold;name.alignment=TextAnchor.MiddleCenter;name.resizeTextForBestFit=true;name.resizeTextMinSize=8;name.resizeTextMaxSize=12;SetRect(name.rectTransform,new Vector2(.12f,.035f),new Vector2(.88f,.21f));name.raycastTarget=false;
      var health=Badge(card.transform,"Hero Health",HeroHealth(player).ToString(),new Color(.76f,.025f,.07f,1f));AnchorBadge(health,new Vector2(1,0),new Vector2(-2,2));health.fontSize=15;health.transform.parent.GetComponent<RectTransform>().sizeDelta=new Vector2(32,32);
      if(player.Armor>0){var armor=Badge(card.transform,"Hero Armor",player.Armor.ToString(),new Color(.18f,.42f,.68f,1f));AnchorBadge(armor,Vector2.zero,new Vector2(2,2));armor.fontSize=16;armor.transform.parent.GetComponent<RectTransform>().sizeDelta=new Vector2(34,34);}
      var preview=card.AddComponent<CardLongPressPreview>();preview.Configure(()=>ShowHeroPreview(player,local),HideCardPreview);
      var button=card.AddComponent<Button>();button.onClick.AddListener(()=>{if(!preview.ConsumeClick()&&selectable)SelectTarget(heroRef);});
      var cardRect=card.GetComponent<RectTransform>();presentationAnchors[heroRef]=cardRect;presentationAnchors[player.PlayerId]=cardRect;
      card.AddComponent<PremiumCardVisualRig>().Configure(selectable||selectedSource,selectable);
    }

    private string HeroPowerName(string source)=>source.IndexOf("cinder_spark",StringComparison.OrdinalIgnoreCase)>=0
      ? L("CINDER SPARK","ИСКРА ПЕПЛА")
      : L("ASTRAL RESTORATION","АСТРАЛЬНОЕ ИСЦЕЛЕНИЕ");

    private string HeroPowerRules(string source)=>source.IndexOf("cinder_spark",StringComparison.OrdinalIgnoreCase)>=0
      ? L("Deal 1 damage to an enemy character.","Нанесите 1 урон вражескому персонажу.")
      : L("Restore 2 Health to a friendly character.","Восстановите 2 здоровья союзному персонажу.");

    private LegalActionViewModel FindHeroPowerInteraction(IReadOnlyList<LegalActionViewModel> actions,string source)
    {
      var powers=actions?.Where(action=>action.Enabled&&string.Equals(action.CommandType,"USE_HERO_POWER",StringComparison.OrdinalIgnoreCase)).ToList();
      if(powers==null||powers.Count==0)return null;
      var exact=powers.Where(action=>OriginMatches(action,source)).ToList();
      return FindOriginInteraction(exact.Count>0?exact:powers,source)??BuildTargetInteraction(exact.Count>0?exact:powers);
    }

    private LegalActionViewModel BuildTargetInteraction(IReadOnlyList<LegalActionViewModel> matches)
    {
      if(matches==null||matches.Count==0)return null;
      var alreadySelectable=matches.FirstOrDefault(action=>action.RequiresTargetSelection);if(alreadySelectable!=null)return alreadySelectable;
      var targeted=matches.Where(action=>action.Template?.Refs!=null&&action.Template.Refs.Keys.Any(IsConcreteTargetRef)).ToList();if(targeted.Count==0)return matches[0];
      var first=targeted[0].Template;var concreteKey=first.Refs.Keys.First(IsConcreteTargetRef);var candidates=targeted.Select(action=>action.Template.Refs.GetValueOrDefault(concreteKey)).Where(value=>!string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();if(candidates.Count==0)return matches[0];
      var refs=new Dictionary<string,string>(first.Refs,StringComparer.OrdinalIgnoreCase);refs.Remove(concreteKey);var playerTarget=concreteKey.IndexOf("Player",StringComparison.OrdinalIgnoreCase)>=0;refs[playerTarget?"candidateTargetPlayerIds":"candidateTargetEntityInstanceIds"]=string.Join(",",candidates);
      return new LegalActionViewModel(new MatchCommandDto{CommandType=first.CommandType,PlayerId=first.PlayerId,Payload=first.Payload,Refs=refs},first.CommandType,true,string.Empty,candidates,true,concreteKey,candidates);
    }

    private Texture2D ResolveHeroArt(PublicPlayerStateView player)
    {
      var source=player?.Hero?.SourceCardInstanceId??string.Empty;
      var resource=source.EndsWith(":cinder-vanguard",StringComparison.OrdinalIgnoreCase)?"HeroArt/cinder-vanguard":"HeroArt/astral-warden";
      var authored=Resources.Load<Texture2D>(resource);if(authored!=null)return authored;
      return source.EndsWith(":cinder-vanguard",StringComparison.OrdinalIgnoreCase)
        ? Resources.Load<Texture2D>("CardArt/unity_test_nixara_quickflame_deal_damage_01")
        : Resources.Load<Texture2D>("CardArt/fallback-sentinel-v1");
    }

    private static Color HeroFallbackColor(PublicPlayerStateView player)
    {
      var source=player?.Hero?.SourceCardInstanceId??string.Empty;
      return source.EndsWith(":cinder-vanguard",StringComparison.OrdinalIgnoreCase)?new Color(.68f,.16f,.06f,1f):new Color(.08f,.32f,.68f,1f);
    }

    private static int HeroHealth(PublicPlayerStateView player) => player?.Hero == null ? 0 : Math.Max(0, player.Hero.Health-player.Hero.Damage);
    public static string HeroTargetReference(PublicPlayerStateView player)=>player?.Hero?.EntityInstanceId??string.Empty;

    private static string HeroDisplayName(PublicPlayerStateView player,string fallback)
    {
      var source=player?.Hero?.SourceCardInstanceId??string.Empty;
      if(source.EndsWith(":cinder-vanguard",StringComparison.OrdinalIgnoreCase))return "VARG";
      if(source.EndsWith(":astral-warden",StringComparison.OrdinalIgnoreCase))return "ASTRA";
      return fallback;
    }

    private static string CombatRelics(PublicPlayerStateView player)
    {
      var weapon=player?.Weapon==null?string.Empty:$"   ⚔ {player.Weapon.Attack}/{Math.Max(0,player.Weapon.Health-player.Weapon.Damage)}";
      var location=player?.Location==null?string.Empty:"   ⌂";
      var secrets=player?.Secrets?.Count>0?$"   ?×{player.Secrets.Count}":string.Empty;
      var quests=player?.Quests?.Count>0?$"   ◇×{player.Quests.Count}":string.Empty;
      return weapon+location+secrets+quests;
    }

    private string CardLabel(PublicCardInstanceView card)
    {
      if (card != null && ((!string.IsNullOrWhiteSpace(card.CardVersion) && cardCatalog.TryGetValue(card.CardVersion, out var byVersion))
        || (!string.IsNullOrWhiteSpace(card.CardId) && cardCatalog.TryGetValue(card.CardId, out byVersion))))
      {
        return $"{byVersion.ManaCost}  {byVersion.DisplayName}\n{byVersion.CardType}\n{byVersion.RulesText}";
      }

      return CardView.Label(card);
    }

    private string EntityLabel(PublicEntityView entity)
    {
      var presentation = cardCatalog.Values.FirstOrDefault(card =>
        !string.IsNullOrWhiteSpace(card.CardId)
        && entity?.SourceCardInstanceId?.IndexOf(":" + card.CardId + ":", StringComparison.OrdinalIgnoreCase) >= 0);
      if (presentation == null) return EntityView.Label(entity);
      var health = Math.Max(0, entity.Health - entity.Damage);
      var keywords = entity.Keywords == null || entity.Keywords.Count == 0 ? string.Empty : "\n" + string.Join(", ", entity.Keywords);
      return $"{presentation.DisplayName}\n{entity.Attack}/{health}{keywords}";
    }

    private void RenderActions(IReadOnlyList<LegalActionGroup> grouped, TargetSelectionState targetSelection, bool commandsEnabled)
    {
      var endTurn=grouped?.FirstOrDefault(group=>string.Equals(group.CommandType,"END_TURN",StringComparison.OrdinalIgnoreCase))?.Actions?.FirstOrDefault(action=>action.Enabled);
      if(endTurnButton!=null)
      {
        var choosingTarget=targetSelection?.IsActive==true;
        endTurnButton.interactable=commandsEnabled&&!choosingTarget&&endTurn!=null;
        endTurnButtonText.text=endTurnButton.interactable?L("END TURN","ЗАВЕРШИТЬ ХОД"):choosingTarget?L("CHOOSE TARGET","ВЫБЕРИТЕ ЦЕЛЬ"):L("OPPONENT TURN","ХОД ПРОТИВНИКА");
        if(endTurnStatusText!=null)endTurnStatusText.text=endTurnButton.interactable?L("YOUR MOVE","ВАШ ХОД"):choosingTarget?L("TARGETING","ВЫБОР ЦЕЛИ"):L("PLEASE WAIT","ОЖИДАНИЕ");
        var image=endTurnButton.GetComponent<Image>();if(image!=null)image.color=endTurnButton.interactable?new Color(1f,.63f,.045f,1f):new Color(.24f,.27f,.32f,1f);
        if(endTurnGlow!=null)endTurnGlow.color=endTurnButton.interactable?new Color(1f,.52f,.04f,.36f):new Color(.28f,.34f,.42f,.15f);
        endTurnButtonText.color=endTurnButton.interactable?new Color(.08f,.035f,.008f,1f):new Color(.78f,.82f,.88f,1f);
        if(endTurnStatusText!=null)endTurnStatusText.color=endTurnButton.interactable?new Color(.18f,.08f,.01f,.8f):new Color(.62f,.68f,.76f,.9f);
        pendingEndTurn=endTurn?.Template;
      }
      if (targetSelection?.IsActive == true)
      {
        RenderCancelTargetButton();
        return;
      }

      if (grouped == null || grouped.Count == 0)
      {
        return;
      }

      foreach (var group in grouped)
      {
        if (string.Equals(group.CommandType, "PLAY_CARD", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(group.CommandType, "ATTACK", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(group.CommandType, "USE_HERO_POWER", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(group.CommandType, "END_TURN", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(group.CommandType, "CONCEDE", StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }
        foreach (var action in group.Actions)
        {
          var enabled = action.Enabled && commandsEnabled;
          var commandLabel = LocalizeCommand(group.CommandType);
          var label = enabled
            ? commandLabel
            : commandLabel + L("\nDisabled", "\nНедоступно");
          if (action.RequiresTargetSelection)
          {
            label = commandLabel + L("\nSelect target", "\nВыберите цель");
          }

          LabelCard(
            actionArea,
            label,
            true,
            enabled,
            enabled && !action.RequiresTargetSelection ? action.Template : null,
            enabled ? string.Empty : string.IsNullOrWhiteSpace(action.DisabledReason) ? L("Unavailable in the current match state.", "Недоступно в текущем состоянии матча.") : action.DisabledReason,
            null,
            enabled && action.RequiresTargetSelection ? () => TargetSelectionRequested?.Invoke(action) : null);
        }
      }
    }

    private void RenderCancelTargetButton()
    {
      var cancel=Panel(actionArea,"Compact Cancel Target Button",new Color(.34f,.075f,.075f,1f));dynamicObjects.Add(cancel);
      var layout=cancel.AddComponent<LayoutElement>();layout.minWidth=184;layout.preferredWidth=184;layout.minHeight=32;layout.preferredHeight=34;layout.flexibleWidth=0;
      var image=cancel.GetComponent<Image>();image.sprite=RoundedUiSprite();image.type=Image.Type.Sliced;
      var outline=cancel.AddComponent<Outline>();outline.effectColor=new Color(.92f,.28f,.18f,.9f);outline.effectDistance=new Vector2(2,-2);
      var button=cancel.AddComponent<Button>();button.onClick.AddListener(CancelTargeting);
      var label=TextBlock(cancel.transform,"Cancel Target",13);label.text=L("CANCEL TARGET","ОТМЕНИТЬ ЦЕЛЬ");label.alignment=TextAnchor.MiddleCenter;label.fontStyle=FontStyle.Bold;label.resizeTextForBestFit=true;label.resizeTextMinSize=10;label.resizeTextMaxSize=13;SetRect(label.rectTransform,new Vector2(.07f,.08f),new Vector2(.93f,.92f));label.raycastTarget=false;
    }

    private void RenderChoices(MatchStateView state, string viewerPlayerId, ExternalLegalActionsResult legalActions, bool commandsEnabled)
    {
      var choices = state?.PendingChoices?.Values
        .Where(choice => string.Equals(choice.PlayerId, viewerPlayerId, StringComparison.OrdinalIgnoreCase) ||
          string.Equals(choice.VisibilityPolicy, "PUBLIC", StringComparison.OrdinalIgnoreCase))
        .ToList();

      if (choices == null || choices.Count == 0)
      {
        choiceArea.gameObject.SetActive(false);
        return;
      }

      choiceArea.gameObject.SetActive(true);

      var resolveTemplate = legalActions?.LegalActions?.FirstOrDefault(action =>
        string.Equals(action.CommandType, "RESOLVE_CHOICE", StringComparison.OrdinalIgnoreCase));
      foreach (var choice in choices)
      {
        var optionNumber = 0;
        foreach (var option in choice.Options)
        {
          optionNumber++;
          var hasOptionId = !string.IsNullOrWhiteSpace(option.OptionId);
          var enabled = commandsEnabled && resolveTemplate != null && hasOptionId;
          var russian = PlayerPrefs.GetString(LocalizationService.LocaleKey, "en") == "ru";
          var matchingCard = cardCatalog.Values.FirstOrDefault(card =>
            string.Equals(card.CardId, option.OptionId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(card.CardVersionId, option.OptionId, StringComparison.OrdinalIgnoreCase));
          var label = matchingCard == null
            ? (russian ? $"ВАРИАНТ {optionNumber}\nВыберите этот исход" : $"OPTION {optionNumber}\nChoose this outcome")
            : $"{matchingCard.ManaCost}  {matchingCard.DisplayName}\n{CardRulesFormatter.Format(matchingCard.RulesText, russian)}";
          var card = LabelCard(choiceArea, label, true, enabled, null, hasOptionId ? string.Empty : "Missing option id.");
          if (enabled)
          {
            var button = card.GetComponent<Button>() ?? card.AddComponent<Button>();
            button.onClick.AddListener(() => ResolveChoiceRequested?.Invoke(choice, option, resolveTemplate));
          }
        }
      }
    }

    private void BuildPlayDropLane(Transform parent)
    {
      var lane=Panel(parent,"Legal Card Play Lane",new Color(.08f,.36f,.42f,.28f));playDropArea=lane.GetComponent<RectTransform>();playDropArea.anchorMin=new Vector2(.09f,.29f);playDropArea.anchorMax=new Vector2(.77f,.71f);playDropArea.offsetMin=Vector2.zero;playDropArea.offsetMax=Vector2.zero;
      playDropSurface=lane.GetComponent<Image>();playDropSurface.sprite=RoundedUiSprite();playDropSurface.type=Image.Type.Sliced;playDropSurface.raycastTarget=false;
      var outline=lane.AddComponent<Outline>();outline.effectColor=new Color(.24f,.82f,1f,.86f);outline.effectDistance=new Vector2(4,-4);outline.useGraphicAlpha=true;
      playDropLabel=TextBlock(lane.transform,"Play Lane",24);playDropLabel.text=L("RELEASE TO PLAY","ОТПУСТИТЕ, ЧТОБЫ РАЗЫГРАТЬ");playDropLabel.alignment=TextAnchor.MiddleCenter;playDropLabel.fontStyle=FontStyle.Bold;playDropLabel.color=new Color(.72f,.94f,1f,.9f);SetRect(playDropLabel.rectTransform,new Vector2(.16f,.39f),new Vector2(.84f,.61f));playDropLabel.raycastTarget=false;
      lane.SetActive(false);
    }

    private void ShowPlayDropLane(bool inside)
    {
      if(playDropArea==null)return;playDropArea.gameObject.SetActive(true);playDropArea.SetAsLastSibling();
      if(playDropSurface!=null)playDropSurface.color=inside?new Color(.06f,.52f,.28f,.42f):new Color(.08f,.34f,.46f,.34f);
      if(playDropLabel!=null){playDropLabel.text=inside?L("RELEASE TO PLAY","ОТПУСТИТЕ, ЧТОБЫ РАЗЫГРАТЬ"):L("DRAG INTO THE PLAY LANE","ПЕРЕТАЩИТЕ В ЗОНУ РОЗЫГРЫША");playDropLabel.color=inside?new Color(.64f,1f,.72f,.96f):new Color(.72f,.94f,1f,.9f);}
    }

    private void ResolvePlayDropLane(bool accepted)
    {
      if(playDropArea==null)return;
      if(accepted){playDropArea.gameObject.SetActive(false);return;}
      playDropArea.gameObject.SetActive(true);playDropArea.SetAsLastSibling();
      if(playDropSurface!=null)playDropSurface.color=new Color(.62f,.08f,.06f,.48f);
      if(playDropLabel!=null){playDropLabel.text=L("CARD RETURNED — DROP INSIDE THE PLAY LANE","КАРТА ВОЗВРАЩЕНА — ОТПУСТИТЕ В ЗОНЕ РОЗЫГРЫША");playDropLabel.color=new Color(1f,.78f,.72f,1f);}
    }

    private void HidePlayDropLane(){if(playDropArea!=null)playDropArea.gameObject.SetActive(false);}

    private void BuildRightCombatRail(Transform parent)
    {
      var rail=Panel(parent,"Right Combat Rail",new Color(.018f,.022f,.036f,.82f));
      var railRect=rail.GetComponent<RectTransform>();railRect.anchorMin=railRect.anchorMax=new Vector2(1,.5f);railRect.pivot=new Vector2(1,.5f);railRect.sizeDelta=new Vector2(166,390);railRect.anchoredPosition=new Vector2(-14,0);
      var railImage=rail.GetComponent<Image>();railImage.sprite=RoundedUiSprite();railImage.type=Image.Type.Sliced;
      var railLayout=rail.AddComponent<VerticalLayoutGroup>();railLayout.padding=new RectOffset(14,14,20,20);railLayout.spacing=14;railLayout.childAlignment=TextAnchor.MiddleCenter;railLayout.childControlHeight=true;railLayout.childControlWidth=true;railLayout.childForceExpandHeight=false;

      opponentGraveyardArea=RailSlot(rail.transform,"Opponent Graveyard Slot",112,true).transform;
      var turnSlot=RailSlot(rail.transform,"Turn Control Slot",116,false);
      playerGraveyardArea=RailSlot(rail.transform,"Player Graveyard Slot",112,true).transform;

      endTurnGlow=Panel(turnSlot.transform,"Turn Button Glow",new Color(1f,.52f,.04f,.34f)).GetComponent<Image>();
      endTurnGlow.sprite=RoundedUiSprite();endTurnGlow.type=Image.Type.Sliced;endTurnGlow.raycastTarget=false;
      var glowRect=endTurnGlow.rectTransform;glowRect.anchorMin=glowRect.anchorMax=new Vector2(.5f,.5f);glowRect.pivot=new Vector2(.5f,.5f);glowRect.sizeDelta=new Vector2(148,78);glowRect.anchoredPosition=new Vector2(0,-2);
      var shadow=Panel(turnSlot.transform,"Turn Button Shadow",new Color(.005f,.008f,.015f,.9f));var shadowImage=shadow.GetComponent<Image>();shadowImage.sprite=RoundedUiSprite();shadowImage.type=Image.Type.Sliced;shadowImage.raycastTarget=false;var shadowRect=shadow.GetComponent<RectTransform>();shadowRect.anchorMin=shadowRect.anchorMax=new Vector2(.5f,.5f);shadowRect.pivot=new Vector2(.5f,.5f);shadowRect.sizeDelta=new Vector2(146,70);shadowRect.anchoredPosition=new Vector2(0,-6);
      var buttonObject=Panel(turnSlot.transform,"Premium Turn Action Button",new Color(.96f,.59f,.035f,1f));var buttonImage=buttonObject.GetComponent<Image>();buttonImage.sprite=RoundedUiSprite();buttonImage.type=Image.Type.Sliced;var buttonRect=buttonObject.GetComponent<RectTransform>();buttonRect.anchorMin=buttonRect.anchorMax=new Vector2(.5f,.5f);buttonRect.pivot=new Vector2(.5f,.5f);buttonRect.sizeDelta=new Vector2(142,70);buttonRect.anchoredPosition=Vector2.zero;
      var outline=buttonObject.AddComponent<Outline>();outline.effectColor=new Color(1f,.82f,.3f,.82f);outline.effectDistance=new Vector2(2,-2);
      endTurnButton=buttonObject.AddComponent<Button>();endTurnButton.onClick.AddListener(SubmitEndTurn);
      endTurnStatusText=TextBlock(buttonObject.transform,"Turn State",10);endTurnStatusText.alignment=TextAnchor.MiddleCenter;endTurnStatusText.fontStyle=FontStyle.Bold;endTurnStatusText.color=new Color(.18f,.09f,.015f,.82f);SetRect(endTurnStatusText.rectTransform,new Vector2(.08f,.61f),new Vector2(.92f,.91f));endTurnStatusText.raycastTarget=false;
      endTurnButtonText=TextBlock(buttonObject.transform,"Turn Action",16);endTurnButtonText.alignment=TextAnchor.MiddleCenter;endTurnButtonText.fontStyle=FontStyle.Bold;endTurnButtonText.color=new Color(.075f,.035f,.01f,1f);SetRect(endTurnButtonText.rectTransform,new Vector2(.06f,.12f),new Vector2(.94f,.68f));endTurnButtonText.raycastTarget=false;endTurnButtonText.resizeTextForBestFit=true;endTurnButtonText.resizeTextMinSize=12;endTurnButtonText.resizeTextMaxSize=16;
    }

    private GameObject RailSlot(Transform parent,string name,int height,bool layoutChildren)
    {
      var slot=Panel(parent,name,new Color(0,0,0,0));var slotLayout=slot.AddComponent<LayoutElement>();slotLayout.minHeight=height;slotLayout.preferredHeight=height;slotLayout.flexibleHeight=0;
      if(layoutChildren){var layout=slot.AddComponent<HorizontalLayoutGroup>();layout.childAlignment=TextAnchor.MiddleCenter;layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandWidth=false;layout.childForceExpandHeight=false;}
      return slot;
    }

    private Sprite RoundedUiSprite()
    {
      if(roundedUiSprite!=null)return roundedUiSprite;
      const int width=64,height=32,radius=12;var texture=new Texture2D(width,height,TextureFormat.RGBA32,false){name="Runtime Rounded UI",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
      for(var y=0;y<height;y++)for(var x=0;x<width;x++)
      {
        var dx=x<radius?radius-x:x>=width-radius?x-(width-radius-1):0;var dy=y<radius?radius-y:y>=height-radius?y-(height-radius-1):0;
        var alpha=dx==0||dy==0||dx*dx+dy*dy<=radius*radius?1f:0f;texture.SetPixel(x,y,new Color(1,1,1,alpha));
      }
      texture.Apply(false,true);roundedUiSprite=Sprite.Create(texture,new Rect(0,0,width,height),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(14,14,14,14));roundedUiSprite.name="Runtime Rounded UI Sprite";return roundedUiSprite;
    }

    private LegalActionViewModel FindOriginAction(IReadOnlyList<LegalActionViewModel> actions, string refId)
    {
      return actions?.FirstOrDefault(action => action.Enabled && OriginMatches(action, refId));
    }

    private LegalActionViewModel FindOriginInteraction(IReadOnlyList<LegalActionViewModel> actions,string refId)
    {
      var matches=actions?.Where(action=>action.Enabled&&OriginMatches(action,refId)).ToList();
      if(matches==null||matches.Count==0)return null;
      return BuildTargetInteraction(matches);
    }
    private static bool IsConcreteTargetRef(string key)=>!string.IsNullOrWhiteSpace(key)&&key.StartsWith("target",StringComparison.OrdinalIgnoreCase)&&key.IndexOf("candidate",StringComparison.OrdinalIgnoreCase)<0;

    public static bool OriginMatches(LegalActionViewModel action, string refId) => action?.Template?.Refs != null &&
      action.Template.Refs.Any(pair => !IsTargetOrCandidateRef(pair.Key) &&
        string.Equals(pair.Value, refId, StringComparison.OrdinalIgnoreCase));

    public static bool IsCardVisibleToPlayer(string relationship, string visibility) =>
      string.Equals(relationship, "SELF", StringComparison.OrdinalIgnoreCase) &&
      !string.Equals(visibility, "HIDDEN", StringComparison.OrdinalIgnoreCase);

    private static bool IsTargetOrCandidateRef(string key) => string.IsNullOrWhiteSpace(key) ||
      key.IndexOf("candidate", StringComparison.OrdinalIgnoreCase) >= 0 ||
      key.StartsWith("target", StringComparison.OrdinalIgnoreCase);

    private GameObject VisualCard(Transform parent, GameplayCardPresentation presentation, PublicEntityView entity, string presentationRef,
      bool faceUp, bool highlighted, bool selectedSource, bool selectableTarget, MatchCommandDto command, string selectedTargetRef,
      Action customClick, string fallbackLabel, CardInteractionState interactionState=CardInteractionState.Neutral, string interactionLabel="")
    {
      var accent = selectableTarget ? new Color(.96f,.24f,.16f,1f)
        : selectedSource ? new Color(.12f,.68f,1f,1f)
        : highlighted ? ThemeColorParser.ParseOrWhite(theme.Palette.Playable)
        : new Color(.48f,.34f,.16f,1f);
      var card = Panel(parent, "Gameplay Card", accent);
      dynamicObjects.Add(card);
      if(!string.IsNullOrWhiteSpace(presentationRef))presentationAnchors[presentationRef]=card.GetComponent<RectTransform>();
      if(entity!=null&&!string.IsNullOrWhiteSpace(presentationRef))
      {
        var cardRect=card.GetComponent<RectTransform>();
        pendingBoardSettles.Add((cardRect,lastKnownWorldAnchors.TryGetValue(presentationRef,out var previousWorld)?previousWorld:null));
      }
      var size = card.AddComponent<LayoutElement>();
      size.minWidth = entity == null ? 122 : 94; size.preferredWidth = entity == null ? 132 : 102;
      size.minHeight = entity == null ? 154 : 106; size.preferredHeight = size.minHeight; size.flexibleWidth = 0;

      var shell = Panel(card.transform, "Card Face", faceUp ? new Color(.055f,.065f,.09f,1f) : Color.white);
      var shellRect = shell.GetComponent<RectTransform>(); shellRect.anchorMin=Vector2.zero; shellRect.anchorMax=Vector2.one;
      shellRect.offsetMin=new Vector2(3,3); shellRect.offsetMax=new Vector2(-3,-3);

      var artNode=new GameObject("Card Art"); artNode.transform.SetParent(shell.transform,false); artNode.AddComponent<RectTransform>();
      var artRect=artNode.GetComponent<RectTransform>(); artRect.anchorMin=entity==null?new Vector2(.12f,.28f):new Vector2(.12f,.14f);
      artRect.anchorMax=entity==null?new Vector2(.88f,.84f):new Vector2(.88f,.74f); artRect.offsetMin=Vector2.zero;artRect.offsetMax=Vector2.zero;
      var art=artNode.AddComponent<RawImage>();
      var artReference=presentation?.CardId??entity?.SourceCardInstanceId??presentationRef;
      art.texture=faceUp?ResolveArt(presentation,artReference):Resources.Load<Texture2D>("CardArt/card-back-celestial-forge-v1");
      art.color=art.texture==null?new Color(.08f,.12f,.2f,1f):Color.white; art.uvRect=new Rect(0,0,1,1);

      var previewMana=entity==null&&currentHandCosts.TryGetValue(presentationRef,out var previewProjectedCost)?previewProjectedCost:presentation?.ManaCost;
      if(faceUp)
      {
        var frameNode=new GameObject("Authored Card Frame");frameNode.transform.SetParent(shell.transform,false);
        var frameRect=frameNode.AddComponent<RectTransform>();frameRect.anchorMin=Vector2.zero;frameRect.anchorMax=Vector2.one;
        frameRect.offsetMin=new Vector2(-5,-5);frameRect.offsetMax=new Vector2(5,5);
        var frame=frameNode.AddComponent<RawImage>();frame.raycastTarget=false;frame.texture=Resources.Load<Texture2D>(theme?.Sprites?.CardFrameResource??"CardFrames/frame-arcane-v1");
        frame.color=Color.white;
        var displayedMana = entity == null && currentHandCosts.TryGetValue(presentationRef, out var projectedCost)
          ? projectedCost
          : presentation?.ManaCost;
        Text manaBadge=null;
        if(entity==null)
        {
          manaBadge=Badge(shell.transform,"Mana",displayedMana.HasValue?displayedMana.Value.ToString():"?",new Color(.06f,.42f,.95f,1f));
          AnchorBadge(manaBadge,new Vector2(0,1),new Vector2(6,-7));
          if(displayedMana.HasValue&&!string.IsNullOrWhiteSpace(presentationRef))
          {
            if(lastManaCosts.TryGetValue(presentationRef,out var oldMana)&&oldMana!=displayedMana.Value)manaBadge.transform.parent.gameObject.AddComponent<CardStatChangeRig>().Configure(displayedMana.Value-oldMana);
            lastManaCosts[presentationRef]=displayedMana.Value;
          }
        }
        else if(displayedMana.HasValue)
        {
          manaBadge=Badge(shell.transform,"Mana",displayedMana.Value.ToString(),new Color(.06f,.42f,.95f,1f));
          AnchorBadge(manaBadge,new Vector2(0,1),new Vector2(6,-7));
        }
        var titlePlate=Panel(shell.transform,"Card Name Plate",new Color(.055f,.045f,.03f,.94f));
        var titlePlateRect=titlePlate.GetComponent<RectTransform>();titlePlateRect.anchorMin=new Vector2(.14f,.74f);titlePlateRect.anchorMax=new Vector2(.9f,.91f);titlePlateRect.offsetMin=Vector2.zero;titlePlateRect.offsetMax=Vector2.zero;
        var title=TextBlock(shell.transform,"Card Name",entity==null?11:10); title.alignment=TextAnchor.MiddleCenter; title.fontStyle=FontStyle.Bold;
        title.text=DisplayCardTitle(presentation?.DisplayName??FirstLine(fallbackLabel)); var titleRect=title.rectTransform;titleRect.anchorMin=new Vector2(.18f,.75f);titleRect.anchorMax=new Vector2(.88f,.9f);titleRect.offsetMin=Vector2.zero;titleRect.offsetMax=Vector2.zero;
        title.color=theme?.Id=="cyberpunk"?new Color(.78f,.98f,1f,1f):new Color(.96f,.88f,.72f,1f);title.resizeTextForBestFit=true;title.resizeTextMinSize=6;title.resizeTextMaxSize=11;title.horizontalOverflow=HorizontalWrapMode.Wrap;title.verticalOverflow=VerticalWrapMode.Truncate;
        if(entity==null)
        {
          var rulesPlate=Panel(shell.transform,"Rules Parchment",new Color(.055f,.045f,.03f,.86f));
          var plateRect=rulesPlate.GetComponent<RectTransform>();plateRect.anchorMin=new Vector2(.11f,.155f);plateRect.anchorMax=new Vector2(.89f,.325f);plateRect.offsetMin=Vector2.zero;plateRect.offsetMax=Vector2.zero;
          var rules=TextBlock(shell.transform,"Rules",9); rules.alignment=TextAnchor.MiddleCenter;
          rules.supportRichText=true; rules.text=CardRulesFormatter.Format(presentation?.RulesText??fallbackLabel,PlayerPrefs.GetString(LocalizationService.LocaleKey,"en")=="ru");
          var rulesRect=rules.rectTransform;rulesRect.anchorMin=new Vector2(.14f,.16f);rulesRect.anchorMax=new Vector2(.86f,.32f);rulesRect.offsetMin=Vector2.zero;rulesRect.offsetMax=Vector2.zero;
          rules.resizeTextForBestFit=true;rules.resizeTextMinSize=6;rules.resizeTextMaxSize=9;rules.color=new Color(.92f,.84f,.65f,1f);
          if(presentation!=null&&presentation.Health>0)
          {
            AnchorBadge(Badge(shell.transform,"Attack",presentation.Attack.ToString(),new Color(.9f,.48f,.04f,1f)),Vector2.zero,new Vector2(5,5));
            AnchorBadge(Badge(shell.transform,"Health",presentation.Health.ToString(),new Color(.82f,.06f,.1f,1f)),new Vector2(1,0),new Vector2(-5,5));
          }
          else if(presentation!=null&&presentation.CardType.IndexOf("WEAPON",StringComparison.OrdinalIgnoreCase)>=0)
          {
            AnchorBadge(Badge(shell.transform,"Attack",presentation.Attack.ToString(),new Color(.9f,.48f,.04f,1f)),Vector2.zero,new Vector2(5,5));
            AnchorBadge(Badge(shell.transform,"Durability",presentation.Durability.ToString(),new Color(.45f,.45f,.52f,1f)),new Vector2(1,0),new Vector2(-5,5));
          }
          else if(presentation!=null&&presentation.CardType.IndexOf("LOCATION",StringComparison.OrdinalIgnoreCase)>=0)
            AnchorBadge(Badge(shell.transform,"Durability",presentation.Durability.ToString(),new Color(.25f,.65f,.58f,1f)),new Vector2(1,0),new Vector2(-5,5));
          else if(presentation!=null&&presentation.ArmorGain>0)
            AnchorBadge(Badge(shell.transform,"Armor",presentation.ArmorGain.ToString(),new Color(.45f,.65f,.82f,1f)),new Vector2(1,0),new Vector2(-5,5));
        }
        else
        {
          var currentHealth=CurrentHealth(entity);
          var attackBadge=Badge(shell.transform,"Attack",entity.Attack.ToString(),new Color(.9f,.48f,.04f,1f));
          var healthBadge=Badge(shell.transform,"Health",currentHealth.ToString(),new Color(.82f,.06f,.1f,1f));
          AnchorBadge(attackBadge,Vector2.zero,new Vector2(5,5));
          AnchorBadge(healthBadge,new Vector2(1,0),new Vector2(-5,5));
          if(!string.IsNullOrWhiteSpace(presentationRef))
          {
            if(lastAttackValues.TryGetValue(presentationRef,out var oldAttack)&&oldAttack!=entity.Attack)attackBadge.transform.parent.gameObject.AddComponent<CardStatChangeRig>().Configure(entity.Attack-oldAttack);
            if(lastHealthValues.TryGetValue(presentationRef,out var oldHealth)&&oldHealth!=currentHealth)healthBadge.transform.parent.gameObject.AddComponent<CardStatChangeRig>().Configure(currentHealth-oldHealth);
            lastAttackValues[presentationRef]=entity.Attack;lastHealthValues[presentationRef]=currentHealth;
          }
        }
        var rig=card.AddComponent<PremiumCardVisualRig>();rig.Configure(highlighted||selectableTarget,selectableTarget,interactionState,interactionLabel);
      }
      if(command!=null||selectedTargetRef!=null||customClick!=null)
      {
        if(entity==null&&(command!=null||customClick!=null))
        {
          var drag=card.AddComponent<CardDragToBoard>();
          drag.Configure(playDropArea,command!=null?()=>SubmitCommandRequested?.Invoke(command):customClick,ShowPlayDropLane,ResolvePlayDropLane,HidePlayDropLane);
        }
        else
        {
        var button=card.AddComponent<Button>();
        if(command!=null)button.onClick.AddListener(()=>SubmitCommandRequested?.Invoke(command));
        else if(selectedTargetRef!=null)button.onClick.AddListener(()=>SelectTarget(selectedTargetRef));
        else button.onClick.AddListener(()=>customClick());
        }
      }
      if(faceUp&&presentation!=null)card.AddComponent<CardLongPressPreview>().Configure(
        ()=>ShowCardPreview(presentation,entity,previewMana),HideCardPreview);
      return card;
    }

    public static CardInteractionState ResolveInteractionState(bool hand,bool local,bool selectedSource,bool validTarget,LegalActionViewModel action)
    {
      if(validTarget)return CardInteractionState.ValidTarget;
      if(selectedSource)return CardInteractionState.SelectedSource;
      if(!local)return CardInteractionState.Neutral;
      if(action!=null)return hand?CardInteractionState.Playable:
        string.Equals(action.CommandType,"ATTACK",StringComparison.OrdinalIgnoreCase)?CardInteractionState.ReadyToAttack:CardInteractionState.ReadyForAction;
      return hand?CardInteractionState.Unplayable:CardInteractionState.Exhausted;
    }

    private string InteractionStateLabel(CardInteractionState state)=>state switch
    {
      CardInteractionState.Playable=>L("PLAY","МОЖНО"),
      CardInteractionState.ReadyToAttack=>L("ATTACK","АТАКА"),
      CardInteractionState.ReadyForAction=>L("READY","ГОТОВ"),
      CardInteractionState.SelectedSource=>L("SOURCE","ИСТОЧНИК"),
      CardInteractionState.ValidTarget=>L("TARGET","ЦЕЛЬ"),
      _=>string.Empty
    };

    private void BuildCardPreview(Transform parent)
    {
      cardPreviewOverlay=Panel(parent,"Held Card Preview Backdrop",new Color(0,0,0,.82f));
      var previewCanvas=cardPreviewOverlay.AddComponent<Canvas>();previewCanvas.overrideSorting=true;previewCanvas.sortingOrder=32760;
      cardPreviewOverlay.AddComponent<CanvasGroup>().blocksRaycasts=false;
      var overlayRect=cardPreviewOverlay.GetComponent<RectTransform>();overlayRect.anchorMin=Vector2.zero;overlayRect.anchorMax=Vector2.one;overlayRect.offsetMin=Vector2.zero;overlayRect.offsetMax=Vector2.zero;
      var card=Panel(cardPreviewOverlay.transform,"Held Card Preview",new Color(.018f,.014f,.01f,1f));
      var rect=card.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=new Vector2(430,680);
      var shadow=card.AddComponent<Shadow>();shadow.effectColor=new Color(0,0,0,.94f);shadow.effectDistance=new Vector2(12,-14);
      var outline=card.AddComponent<Outline>();outline.effectColor=new Color(.86f,.58f,.18f,.94f);outline.effectDistance=new Vector2(5,-5);
      var backingNode=new GameObject("Preview Opaque Backing");backingNode.transform.SetParent(card.transform,false);var backingRect=backingNode.AddComponent<RectTransform>();SetRect(backingRect,new Vector2(.025f,.018f),new Vector2(.975f,.982f));var backing=backingNode.AddComponent<RawImage>();backing.texture=Texture2D.whiteTexture;backing.color=new Color(.018f,.014f,.01f,1f);backing.raycastTarget=false;
      var frameNode=new GameObject("Preview Authored Frame");frameNode.transform.SetParent(card.transform,false);var frameRect=frameNode.AddComponent<RectTransform>();SetRect(frameRect,new Vector2(-.025f,-.018f),new Vector2(1.025f,1.018f));var frame=frameNode.AddComponent<RawImage>();frame.texture=Resources.Load<Texture2D>(theme?.Sprites?.CardFrameResource??"CardFrames/frame-arcane-v1");frame.raycastTarget=false;
      var artNode=new GameObject("Preview Art");artNode.transform.SetParent(card.transform,false);var artRect=artNode.AddComponent<RectTransform>();artRect.anchorMin=new Vector2(.08f,.37f);artRect.anchorMax=new Vector2(.92f,.9f);artRect.offsetMin=Vector2.zero;artRect.offsetMax=Vector2.zero;cardPreviewArt=artNode.AddComponent<RawImage>();cardPreviewArt.uvRect=new Rect(0,0,1,1);var artOutline=artNode.AddComponent<Outline>();artOutline.effectColor=new Color(.48f,.33f,.14f,.78f);artOutline.effectDistance=new Vector2(3,-3);
      cardPreviewTitle=TextBlock(card.transform,"Preview Title",28);cardPreviewTitle.alignment=TextAnchor.MiddleCenter;cardPreviewTitle.fontStyle=FontStyle.Bold;cardPreviewTitle.resizeTextForBestFit=true;cardPreviewTitle.resizeTextMinSize=18;cardPreviewTitle.resizeTextMaxSize=28;SetRect(cardPreviewTitle.rectTransform,new Vector2(.08f,.9f),new Vector2(.92f,.98f));
      cardPreviewStats=TextBlock(card.transform,"Preview Stats",20);cardPreviewStats.alignment=TextAnchor.MiddleCenter;cardPreviewStats.fontStyle=FontStyle.Bold;SetRect(cardPreviewStats.rectTransform,new Vector2(.08f,.355f),new Vector2(.92f,.37f));
      var rulesSurface=Panel(card.transform,"Preview Rules Surface",new Color(.028f,.021f,.014f,1f));var rulesRect=rulesSurface.GetComponent<RectTransform>();SetRect(rulesRect,new Vector2(.065f,.045f),new Vector2(.935f,.355f));rulesSurface.GetComponent<Image>().raycastTarget=false;var rulesOutline=rulesSurface.AddComponent<Outline>();rulesOutline.effectColor=new Color(.48f,.33f,.14f,.78f);rulesOutline.effectDistance=new Vector2(2,-2);
      cardPreviewRules=TextBlock(card.transform,"Preview Rules",18);cardPreviewRules.alignment=TextAnchor.MiddleCenter;cardPreviewRules.supportRichText=true;cardPreviewRules.resizeTextForBestFit=true;cardPreviewRules.resizeTextMinSize=13;cardPreviewRules.resizeTextMaxSize=18;SetRect(cardPreviewRules.rectTransform,new Vector2(.095f,.065f),new Vector2(.905f,.335f));
      cardPreviewManaBadge=PreviewBadge(card.transform,"Mana",new Vector2(.015f,.84f),new Vector2(.16f,.93f),new Color(.08f,.38f,.92f));
      cardPreviewAttackBadge=PreviewBadge(card.transform,"Attack",new Vector2(.015f,.005f),new Vector2(.16f,.095f),new Color(.92f,.58f,.08f));
      cardPreviewHealthBadge=PreviewBadge(card.transform,"Health",new Vector2(.84f,.005f),new Vector2(.985f,.095f),new Color(.78f,.08f,.12f));
      cardPreviewOverlay.SetActive(false);
    }

    private void ShowCardPreview(GameplayCardPresentation presentation,PublicEntityView entity,int? mana)
    {
      if(cardPreviewOverlay==null||presentation==null)return;
      var russian=PlayerPrefs.GetString(LocalizationService.LocaleKey,"en")=="ru";
      cardPreviewTitle.text=presentation.DisplayName;
      cardPreviewStats.text=string.Empty;
      cardPreviewRules.text=CardRulesFormatter.Format(presentation.RulesText,russian);
      cardPreviewArt.texture=ResolveArt(presentation,presentation.CardId);cardPreviewArt.color=cardPreviewArt.texture==null?new Color(.08f,.12f,.2f):Color.white;cardPreviewArt.uvRect=new Rect(0,0,1,1);ConfigurePreviewBadges(presentation,entity,mana.GetValueOrDefault());
      cardPreviewOverlay.SetActive(true);cardPreviewOverlay.transform.SetAsLastSibling();
    }

    private void ShowHeroPreview(PublicPlayerStateView player,bool local)
    {
      if(cardPreviewOverlay==null||player?.Hero==null)return;var russian=PlayerPrefs.GetString(LocalizationService.LocaleKey,"en")=="ru";
      cardPreviewTitle.text=HeroDisplayName(player,local?L("YOUR HERO","ВАШ ГЕРОЙ"):L("ENEMY HERO","ГЕРОЙ ВРАГА"));cardPreviewStats.text=$"{HeroHealth(player)} {(russian?"ЗДОРОВЬЯ":"HEALTH")}"+(player.Armor>0?$"   {player.Armor} {(russian?"БРОНИ":"ARMOR")}":string.Empty);cardPreviewRules.text=local?L("Your hero.","Ваш герой."):L("Enemy hero.","Вражеский герой.");cardPreviewArt.texture=ResolveHeroArt(player);cardPreviewArt.color=cardPreviewArt.texture==null?HeroFallbackColor(player):Color.white;cardPreviewArt.uvRect=new Rect(0,0,1,1);HidePreviewBadges();cardPreviewOverlay.SetActive(true);cardPreviewOverlay.transform.SetAsLastSibling();
    }

    private void ShowHeroPowerPreview(PublicPlayerStateView player)
    {
      if(cardPreviewOverlay==null||player?.HeroPower==null)return;var source=player.HeroPower.SourceCardInstanceId??string.Empty;
      cardPreviewTitle.text=HeroPowerName(source);cardPreviewStats.text=$"{player.HeroPower.Cost} {L("MANA","МАНА")}";cardPreviewRules.text=HeroPowerRules(source);cardPreviewArt.texture=ResolveHeroArt(player);cardPreviewArt.color=cardPreviewArt.texture==null?HeroFallbackColor(player):Color.white;cardPreviewArt.uvRect=new Rect(0,0,1,1);HidePreviewBadges();cardPreviewOverlay.SetActive(true);cardPreviewOverlay.transform.SetAsLastSibling();
    }

    private void HideCardPreview(){if(cardPreviewOverlay!=null)cardPreviewOverlay.SetActive(false);}
    private void UpdateTargetingArrow(TargetSelectionState selection)
    {
      if(targetingArrow==null)return;
      var source=selection?.Template?.Refs?.FirstOrDefault(pair=>!IsTargetOrCandidateRef(pair.Key)).Value;
      if(selection?.IsActive==true&&!string.IsNullOrWhiteSpace(source)&&presentationAnchors.TryGetValue(source,out var anchor)&&anchor!=null)targetingArrow.Show(anchor);
      else targetingArrow.HideInteractive();
    }
    private async void SelectTarget(string targetRef)
    {
      var source=lastTargetSelection?.Template?.Refs?.FirstOrDefault(pair=>!IsTargetOrCandidateRef(pair.Key)).Value;
      targetingArrow?.Hide();
      if(lastTargetSelection?.Template?.CommandType=="ATTACK"&&!string.IsNullOrWhiteSpace(source)&&presentationAnchors.TryGetValue(source,out var a)&&presentationAnchors.TryGetValue(targetRef,out var b)&&combatLunge!=null)
        await combatLunge.PlayAsync(a,b,PlayerPrefs.GetInt("cardgame.reduced-motion",0)==1);
      TargetSelectedRequested?.Invoke(targetRef);
    }
    private void CancelTargeting(){targetingArrow?.Hide();TargetSelectionCancelledRequested?.Invoke();}
    private TargetSelectionState lastTargetSelection;
    private MatchCommandDto pendingEndTurn;
    private void SubmitEndTurn(){if(pendingEndTurn!=null)SubmitCommandRequested?.Invoke(pendingEndTurn);}

    private void BuildPauseOverlay(Transform parent)
    {
      pauseOverlay=Panel(parent,"Match Pause Overlay",new Color(0,0,0,.84f));
      var pauseCanvas=pauseOverlay.AddComponent<Canvas>();pauseCanvas.overrideSorting=true;pauseCanvas.sortingOrder=32750;pauseOverlay.AddComponent<GraphicRaycaster>();
      var full=pauseOverlay.GetComponent<RectTransform>();full.anchorMin=Vector2.zero;full.anchorMax=Vector2.one;full.offsetMin=Vector2.zero;full.offsetMax=Vector2.zero;
      var backdrop=new GameObject("Pause Illustrated Background",typeof(RectTransform),typeof(RawImage));backdrop.transform.SetParent(pauseOverlay.transform,false);var backdropRect=backdrop.GetComponent<RectTransform>();backdropRect.anchorMin=Vector2.zero;backdropRect.anchorMax=Vector2.one;backdropRect.offsetMin=Vector2.zero;backdropRect.offsetMax=Vector2.zero;var backdropRaw=backdrop.GetComponent<RawImage>();backdropRaw.texture=Resources.Load<Texture2D>(theme?.BoardTextureResource);backdropRaw.color=new Color(.32f,.35f,.42f,.72f);backdropRaw.raycastTarget=false;backdrop.transform.SetAsFirstSibling();
      var panel=Panel(pauseOverlay.transform,"Pause Menu",new Color(.045f,.055f,.075f,.99f));
      var rect=panel.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=new Vector2(430,480);
      var layout=panel.AddComponent<VerticalLayoutGroup>();layout.padding=new RectOffset(54,54,44,44);layout.spacing=18;layout.childControlHeight=true;layout.childControlWidth=true;
      var title=TextBlock(panel.transform,"Paused",28);title.text=L("MATCH PAUSED","МАТЧ ПРИОСТАНОВЛЕН");title.alignment=TextAnchor.MiddleCenter;
      Button(panel.transform,L("RESUME","ПРОДОЛЖИТЬ"),TogglePause,280);
      Button(panel.transform,L("RESTART MATCH","НАЧАТЬ ЗАНОВО"),()=>{TogglePause();RestartRequested?.Invoke();},280);
      Button(panel.transform,L("SETTINGS","НАСТРОЙКИ"),()=>{pauseOverlay.SetActive(false);SettingsRequested?.Invoke();},280);
      Button(panel.transform,L("EXIT TO MAIN MENU","ВЫЙТИ В ГЛАВНОЕ МЕНЮ"),()=>{pauseOverlay.SetActive(false);MainMenuRequested?.Invoke();},280);
      pauseOverlay.SetActive(false);
    }
    private Text PreviewBadge(Transform parent,string name,Vector2 min,Vector2 max,Color color){var node=Panel(parent,"Preview "+name+" Badge",color);SetRect(node.GetComponent<RectTransform>(),min,max);var text=TextBlock(node.transform,"Preview "+name,20);text.alignment=TextAnchor.MiddleCenter;text.fontStyle=FontStyle.Bold;return text;}
    private void ConfigurePreviewBadges(GameplayCardPresentation card,PublicEntityView entity,int mana){var minion=card.CardType.IndexOf("MINION",StringComparison.OrdinalIgnoreCase)>=0;var weapon=card.CardType.IndexOf("WEAPON",StringComparison.OrdinalIgnoreCase)>=0;var location=card.CardType.IndexOf("LOCATION",StringComparison.OrdinalIgnoreCase)>=0;cardPreviewManaBadge.transform.parent.gameObject.SetActive(true);cardPreviewManaBadge.text=mana.ToString();cardPreviewAttackBadge.transform.parent.gameObject.SetActive(minion||weapon);cardPreviewAttackBadge.text=(entity?.Attack??card.Attack).ToString();cardPreviewHealthBadge.transform.parent.gameObject.SetActive(minion||weapon||location);cardPreviewHealthBadge.text=(minion?(entity==null?card.Health:CurrentHealth(entity)):card.Durability).ToString();}
    private void HidePreviewBadges(){cardPreviewManaBadge.transform.parent.gameObject.SetActive(false);cardPreviewAttackBadge.transform.parent.gameObject.SetActive(false);cardPreviewHealthBadge.transform.parent.gameObject.SetActive(false);}
    private void TogglePause(){if(pauseOverlay!=null){HideCardPreview();pauseOverlay.SetActive(!pauseOverlay.activeSelf);pauseOverlay.transform.SetAsLastSibling();}}
    private static string PreviewStats(GameplayCardPresentation card,PublicEntityView entity,int mana)
    {
      if(entity!=null)return $"{entity.Attack} ATK     {CurrentHealth(entity)} HP";
      if(card.CardType.IndexOf("MINION",StringComparison.OrdinalIgnoreCase)>=0)return $"{mana} MANA     {card.Attack} ATK     {card.Health} HP";
      if(card.CardType.IndexOf("WEAPON",StringComparison.OrdinalIgnoreCase)>=0)return $"{mana} MANA     {card.Attack} ATK     {card.Durability} DURABILITY";
      if(card.CardType.IndexOf("LOCATION",StringComparison.OrdinalIgnoreCase)>=0)return $"{mana} MANA     {card.Durability} DURABILITY";
      if(card.ArmorGain>0)return $"{mana} MANA     +{card.ArmorGain} ARMOR";
      return $"{mana} MANA";
    }
    private static void SetRect(RectTransform rect,Vector2 min,Vector2 max){rect.anchorMin=min;rect.anchorMax=max;rect.offsetMin=Vector2.zero;rect.offsetMax=Vector2.zero;}

    public static int CurrentHealth(PublicEntityView entity)=>entity==null?0:Math.Max(0,entity.Health-entity.Damage);
    public static int? DisplayedManaCost(PublicCardInstanceView card,GameplayCardPresentation presentation)=>card?.EffectiveManaCost??card?.BaseManaCost??presentation?.ManaCost;

    public Vector3? FindPresentationWorldAnchor(string reference)
    {
      if(string.IsNullOrWhiteSpace(reference)||!presentationAnchors.TryGetValue(reference,out var rect)||rect==null)return null;
      return rect.TransformPoint(rect.rect.center);
    }

    private Text Badge(Transform parent,string name,string value,Color color)
    {
      var node=Panel(parent,name+" Gem",color); var rect=node.GetComponent<RectTransform>(); rect.sizeDelta=new Vector2(30,30);
      var text=TextBlock(node.transform,name,15); text.alignment=TextAnchor.MiddleCenter; text.fontStyle=FontStyle.Bold; text.text=value; return text;
    }

    private static void AnchorBadge(Text badge,Vector2 anchor,Vector2 position)
    {
      var rect=badge.transform.parent.GetComponent<RectTransform>(); rect.anchorMin=anchor; rect.anchorMax=anchor; rect.pivot=anchor;
      rect.anchoredPosition=position; rect.sizeDelta=new Vector2(30,30);
    }

    private Texture2D ResolveArt(GameplayCardPresentation card,string seed)
    {
      if(card?.Art!=null)return card.Art;
      if(card!=null)
      {
        var resource=Resources.Load<Texture2D>("CardArt/"+card.CardId);if(resource!=null)return resource;seed=card.CardId;
        // Persisted catalogs created before authored art was packaged only carry
        // opaque/procedural card ids. Use the same stable authored-art mapping as
        // the deck builder so those cards never regress to technical gradients in
        // the actual match hand, board, or preview.
        var packaged=PlayerApplicationView.PackagedArtResourceFor(card.CardId,"procedural://"+card.CardId);
        resource=Resources.Load<Texture2D>(packaged);if(resource!=null)return resource;
      }
      seed=string.IsNullOrWhiteSpace(seed)?"unknown-card":seed;
      // Runtime refs wrap the card id in an instance/version token. Prefer the
      // packaged authored texture before creating a procedural placeholder.
      authoredCardArt??=Resources.LoadAll<Texture2D>("CardArt");
      foreach(var authored in authoredCardArt)
        if(authored!=null&&seed.IndexOf(authored.name,StringComparison.OrdinalIgnoreCase)>=0)return authored;
      if(proceduralArt.TryGetValue(seed,out var cached))return cached;
      var hash=seed.Aggregate(17,(value,ch)=>unchecked(value*31+ch)); var a=Color.HSVToRGB(Mathf.Abs(hash%360)/360f,.7f,.82f);
      var b=Color.HSVToRGB(Mathf.Abs((hash/7+151)%360)/360f,.8f,.22f);
      var texture=new Texture2D(96,64,TextureFormat.RGBA32,false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
      for(var y=0;y<64;y++)for(var x=0;x<96;x++){var d=Mathf.Clamp01(Vector2.Distance(new Vector2(x,y),new Vector2(48,30))/56f);var glow=((x+y+Mathf.Abs(hash))%19<2)?0.18f:0f;texture.SetPixel(x,y,Color.Lerp(a,b,d)+new Color(glow,glow*.7f,glow*.25f,0));}
      texture.Apply(false,true);proceduralArt[seed]=texture;return texture;
    }

    private static string FirstLine(string value)=>string.IsNullOrWhiteSpace(value)?"Card":value.Split('\n')[0];
    private static string DisplayCardTitle(string value)
    {
      var title=FirstLine(value).Trim();
      if(title.Length<=22)return title;
      var separator=title.IndexOfAny(new[]{':','/','_'});
      if(separator>2&&separator<22)title=title.Substring(0,separator);
      return title.Length<=22?title:title.Substring(0,20)+"…";
    }

    private GameObject LabelCard(
      Transform parent,
      string label,
      bool faceUp,
      bool highlighted,
      MatchCommandDto command,
      string tooltip = "",
      string selectedTargetRef = null,
      Action customClick = null)
    {
      var card = Panel(parent, "Card", highlighted
        ? ThemeColorParser.ParseOrWhite(theme.Palette.Playable)
        : faceUp
          ? ThemeColorParser.ParseOrWhite(theme.Palette.PanelRaised)
          : ThemeColorParser.ParseOrWhite(theme.Palette.CardBack));
      dynamicObjects.Add(card);
      var layout = card.AddComponent<LayoutElement>();
      layout.minWidth = faceUp ? 104 : 66;
      layout.preferredWidth = faceUp ? 122 : 66;
      layout.minHeight = faceUp ? 42 : 84;
      layout.preferredHeight = faceUp ? 42 : 84;
      layout.flexibleWidth = 0f;
      var text = TextBlock(card.transform, "Label", 11);
      text.alignment = TextAnchor.MiddleCenter;
      text.text = string.IsNullOrWhiteSpace(tooltip) ? label : label + "\n" + tooltip;
      if(!faceUp)
      {
        var backNode=new GameObject("Authored Card Back");backNode.transform.SetParent(card.transform,false);
        var backRect=backNode.AddComponent<RectTransform>();backRect.anchorMin=Vector2.zero;backRect.anchorMax=Vector2.one;backRect.offsetMin=new Vector2(2,2);backRect.offsetMax=new Vector2(-2,-2);
        var back=backNode.AddComponent<RawImage>();back.texture=Resources.Load<Texture2D>("CardArt/card-back-celestial-forge-v1");back.raycastTarget=false;
        text.transform.SetAsLastSibling();text.color=Color.white;text.fontStyle=FontStyle.Bold;
      }
      if (command != null || selectedTargetRef != null || customClick != null)
      {
        var button = card.AddComponent<Button>();
        if (command != null)
        {
          button.onClick.AddListener(() => SubmitCommandRequested?.Invoke(command));
        }
        else if (selectedTargetRef != null)
        {
          button.onClick.AddListener(() => TargetSelectedRequested?.Invoke(selectedTargetRef));
        }
        else
        {
          button.onClick.AddListener(() => customClick());
        }
      }

      return card;
    }

    private void ClearDynamic()
    {
      foreach(var anchor in presentationAnchors)
        if(anchor.Value!=null)lastKnownWorldAnchors[anchor.Key]=anchor.Value.TransformPoint(anchor.Value.rect.center);
      foreach (var obj in dynamicObjects)
      {
        if (obj != null)
        {
          UnityEngine.Object.Destroy(obj);
        }
      }

      dynamicObjects.Clear();
      presentationAnchors.Clear();
      pendingBoardSettles.Clear();
    }

    private static PublicPlayerStateView FindPlayer(MatchStateView state, string playerId)
    {
      if (state?.Players == null)
      {
        return null;
      }

      return state.Players.TryGetValue(playerId, out var player) ? player : state.Players.Values.FirstOrDefault();
    }

    private GameObject Row(Transform parent, string name, int height)
    {
      var row = Panel(parent, name, ThemeColorParser.ParseOrWhite(theme.Palette.Panel));
      var layout = row.AddComponent<HorizontalLayoutGroup>();
      layout.spacing = 6;
      layout.padding = new RectOffset(6, 6, 5, 5);
      layout.childControlWidth = true;
      layout.childControlHeight = true;
      layout.childForceExpandWidth = false;
      layout.childAlignment = TextAnchor.MiddleCenter;
      var element = row.AddComponent<LayoutElement>();
      element.minHeight = height;
      element.preferredHeight = height;
      return row;
    }

    private Text TextPanel(Transform parent, string name, int height, int fontSize)
    {
      var panel = Row(parent, name, height);
      return TextBlock(panel.transform, name, fontSize);
    }

    private Text TextBlock(Transform parent, string name, int fontSize)
    {
      var textObject = new GameObject(name + " Text");
      textObject.transform.SetParent(parent, false);
      var text = textObject.AddComponent<Text>();
      text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
      text.color = Color.white;
      text.fontSize = fontSize;
      text.alignment = TextAnchor.UpperLeft;
      text.horizontalOverflow = HorizontalWrapMode.Wrap;
      text.verticalOverflow = VerticalWrapMode.Truncate;
      var element = textObject.AddComponent<LayoutElement>();
      element.flexibleWidth = 1f;
      element.flexibleHeight = 1f;
      themedTexts.Add(text);
      return text;
    }

    private Text Button(Transform parent, string label, Action action, int width = 88)
    {
      var buttonObject = Panel(parent, label + " Button", new Color(0.22f, 0.26f, 0.30f, 1f));
      var layout = buttonObject.AddComponent<LayoutElement>();
      layout.minWidth = width;
      layout.preferredWidth = width;
      layout.minHeight = 30;
      var button = buttonObject.AddComponent<Button>();
      button.onClick.AddListener(() => action());
      var text = TextBlock(buttonObject.transform, label, 12);
      text.alignment = TextAnchor.MiddleCenter;
      text.text = label;
      return text;
    }

    private GameObject Panel(Transform parent, string name, Color color)
    {
      var panel = new GameObject(name);
      panel.transform.SetParent(parent, false);
      panel.AddComponent<RectTransform>();
      var image = panel.AddComponent<Image>();
      image.color = color;
      themedPanels.Add(image);
      return panel;
    }

    private void CycleTheme()
    {
      theme = themeCatalog.Next(theme.Id);
      PlayerPrefs.SetString("cardgame.board-theme", theme.Id);
      PlayerPrefs.Save();
      ApplyTheme();
      if (themeText != null)
      {
        themeText.text = $"BOARD  •  {theme.DisplayName}";
      }
    }

    private void ApplyTheme()
    {
      var panelColor = ThemeColorParser.ParseOrWhite(theme.Palette.Panel);
      var textColor = ThemeColorParser.ParseOrWhite(theme.Palette.TextPrimary);
      foreach (var panel in themedPanels)
      {
        if (panel != null && panel.gameObject.name != "Gameplay Vignette" && !IsDynamicSurface(panel.transform))
        {
          var name = panel.gameObject.name;
          var alpha = name.EndsWith("Slot",StringComparison.Ordinal) ? 0f
            : name == "Right Combat Rail" ? .82f
            : name == "Turn Button Shadow" ? .92f
            : name == "Board Root" ? 0.08f
            : name == "Hero Portrait" ? 0.92f
            : name.EndsWith("Button", StringComparison.Ordinal) ? 0.92f
            : name == "Utility Rail" ? 0.62f
            : 0.16f;
          var surface=name=="Turn Button Shadow"?new Color(.005f,.008f,.015f,1f)
            : name=="Right Combat Rail"?new Color(.018f,.022f,.036f,1f)
            : name=="Hero Portrait"?ThemeColorParser.ParseOrWhite(theme.Palette.Playable):panelColor;
          panel.color = new Color(surface.r, surface.g, surface.b, alpha);
        }
      }

      foreach (var text in themedTexts)
      {
        if (text != null && !IsDynamicSurface(text.transform))
        {
          text.color = textColor;
        }
      }

      if (boardBackground != null)
      {
        boardBackground.color = Color.white;
        boardBackground.texture = Resources.Load<Texture2D>(theme.BoardTextureResource);
        if (boardBackground.texture == null)
        {
          boardBackground.color = ThemeColorParser.ParseOrWhite(theme.Palette.Background);
        }
      }
      if(themeText!=null)themeText.text=$"{L("BATTLEFIELD", "ПОЛЕ БОЯ")}  •  {theme.DisplayName}";
      if(root!=null)
      {
        var frameTexture=Resources.Load<Texture2D>(theme.Sprites.CardFrameResource);
        foreach(var raw in root.GetComponentsInChildren<RawImage>(true))
          if(raw.gameObject.name=="Authored Card Frame")raw.texture=frameTexture;
        foreach(var rig in root.GetComponentsInChildren<PremiumCardVisualRig>(true))rig.ApplyTheme(theme);
        foreach(var item in dynamicObjects)
        {
          if(item==null||item.name!="Card"||item.GetComponent<Button>()==null)continue;
          var image=item.GetComponent<Image>();if(image!=null)image.color=ThemeColorParser.ParseOrWhite(theme.Palette.Playable);
        }
      }
    }

    private bool IsDynamicSurface(Transform candidate)
    {
      foreach(var item in dynamicObjects)
        if(item!=null&&(candidate==item.transform||candidate.IsChildOf(item.transform)))return true;
      return false;
    }

    private static string ShortHash(string hash) =>
      string.IsNullOrWhiteSpace(hash) ? "(none)" : hash.Substring(0, Math.Min(12, hash.Length));

    private static string L(string english, string russian) =>
      PlayerPrefs.GetString(LocalizationService.LocaleKey, "en") == "ru" ? russian : english;

    private static string LocalizeCommand(string command) => command switch
    {
      "END_TURN" => L("END TURN", "ЗАВЕРШИТЬ ХОД"),
      "CONCEDE" => L("CONCEDE", "СДАТЬСЯ"),
      "PLAY_CARD" => L("PLAY CARD", "РАЗЫГРАТЬ"),
      "ATTACK" => L("ATTACK", "АТАКОВАТЬ"),
      "RESOLVE_CHOICE" => L("CHOOSE", "ВЫБРАТЬ"),
      _ => command?.Replace('_', ' ') ?? string.Empty
    };

    private static string LocalizePhase(string phase) => phase switch
    {
      "TURN_START" => L("TURN START", "НАЧАЛО ХОДА"),
      "MAIN" => L("MAIN PHASE", "ОСНОВНАЯ ФАЗА"),
      "TURN_END" => L("TURN END", "КОНЕЦ ХОДА"),
      _ => phase?.Replace('_', ' ') ?? string.Empty
    };
  }

  public static class CardView
  {
    public static string Label(PublicCardInstanceView card)
    {
      if (card == null)
      {
        return "Card\nmissing";
      }

      return $"{card.CardId ?? "card"}\n{card.CardType}\n{card.Visibility}";
    }
  }

  public static class EntityView
  {
    public static string Label(PublicEntityView entity)
    {
      if (entity == null)
      {
        return "none";
      }

      var health = Math.Max(0, entity.Health - entity.Damage);
      var keywords = entity.Keywords == null || entity.Keywords.Count == 0 ? string.Empty : "\n" + string.Join(",", entity.Keywords);
      return $"{entity.EntityInstanceId}\n{entity.EntityType} {entity.Attack}/{health} dmg {entity.Damage}\nctrl {entity.ControllerPlayerId}{keywords}";
    }
  }
}
