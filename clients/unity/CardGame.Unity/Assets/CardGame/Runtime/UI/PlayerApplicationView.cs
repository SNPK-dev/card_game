using System;
using System.Linq;
using CardGame.Unity.Client.UI.Theme;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CardGame.Unity.Client.UI
{
  public sealed class PlayerApplicationView
  {
    private static readonly System.Collections.Generic.Dictionary<string, Texture2D> ProceduralArtCache = new(StringComparer.Ordinal);
    private static readonly string[] CuratedOfflineArt =
    {
      "unity_test_ceraxa_draw_card_13","unity_test_cinderwake_damage_distribution_15","unity_test_cyravel_resurrect_14",
      "unity_test_fenna_alley_chirurgeon_draw_card_10","unity_test_fulgurite_cascade_damage_distribution_03",
      "unity_test_jadayra_draw_card_08","unity_test_jexara_returns_deal_damage_06","unity_test_juxia_intervenes_prevent_damage_04",
      "unity_test_nixara_quickflame_deal_damage_01","unity_test_pazca_rooftop_astrologer_draw_card_12",
      "unity_test_peregrine_vanilla_11","unity_test_sira_market_tower_draw_card_02","unity_test_vaelbrand_vanilla_09",
      "unity_test_vexara_apply_aura_05","unity_test_veyra_cinderglass_draw_card_07"
    };
    private static readonly string[] CuratedLegacyNames =
    {
      "Emberblade Initiate","Ashen Gatekeeper","Cindercoil Adept","Moonlit Sentinel","Forgeheart Guardian",
      "Stormglass Seer","Verdant Reliquary","Astral Duelist","Duskwing Protector","Lanternbound Knight",
      "Runebloom Mystic","Starfall Envoy","Ironroot Vanguard","Celestial Archivist","Gilded Wayfinder"
    };
    private readonly PlayerApplicationController controller;
    private readonly LocalizationService localization;
    private readonly GameThemeCatalog themes = new();
    private Canvas canvas;
    private Transform content;
    private PlayerRoute renderedRoute = (PlayerRoute)(-1);
    private string renderedLocale = "";
    private string renderedTheme = "";

    public PlayerApplicationView(PlayerApplicationController controller, LocalizationService localization)
    {
      this.controller = controller; this.localization = localization;
    }

    public void Build()
    {
      EnsureEventSystem();
      var root = new GameObject("Production Application Canvas");
      UnityEngine.Object.DontDestroyOnLoad(root);
      canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 500;
      var scaler = root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
      root.AddComponent<GraphicRaycaster>();
      content = root.transform;
      localization.Changed += Invalidate;
      RenderNow();
    }

    public void Tick()
    {
      var gameplay = controller.Route == PlayerRoute.Gameplay;
      if (canvas.enabled == gameplay) canvas.enabled = !gameplay;
      if (!gameplay && (renderedRoute != controller.Route || renderedLocale != localization.Locale || renderedTheme != controller.ThemeId)) RenderNow();
    }

    public void Invalidate() => renderedRoute = (PlayerRoute)(-1);

    private void RenderNow()
    {
      foreach (Transform child in content) UnityEngine.Object.Destroy(child.gameObject);
      renderedRoute = controller.Route; renderedLocale = localization.Locale; renderedTheme = controller.ThemeId;
      var theme = themes.Find(renderedTheme);
      Background(theme);
      if (renderedRoute == PlayerRoute.MainMenu) MainMenu(theme); else Screen(theme);
    }

    private void Background(GameTheme theme)
    {
      var bg = Node(content, "Application Background", Vector2.zero, Vector2.one);
      var image = bg.AddComponent<RawImage>(); image.texture = Resources.Load<Texture2D>($"AppShell/main-hall-{theme.Id}-v1"); image.color = Color.white;
      image.uvRect = new Rect(0, 0, 1, 1);
      var shade = Node(content, "Cinematic Vignette", Vector2.zero, Vector2.one).AddComponent<Image>(); shade.color = new Color(0.01f, 0.02f, 0.05f, renderedRoute == PlayerRoute.MainMenu ? 0.14f : 0.62f); shade.raycastTarget = false;
    }

    private void MainMenu(GameTheme theme)
    {
      TopBar(theme, false);
      var hub = Node(content, "Navigation Hub", new Vector2(.10f,.08f), new Vector2(.90f,.88f));
      var welcome = Node(hub.transform, "Hall Welcome", new Vector2(.25f,.73f), new Vector2(.75f,.94f));
      var welcomeImage = welcome.AddComponent<Image>(); var welcomeColor = ThemeColorParser.ParseOrWhite(theme.Palette.Panel); welcomeColor.a = .72f; welcomeImage.color = welcomeColor;
      var welcomeOutline = welcome.AddComponent<Outline>(); welcomeOutline.effectColor = ThemeColorParser.ParseOrWhite(theme.Palette.Border); welcomeOutline.effectDistance = new Vector2(0,-2);
      var eyebrow = Label(welcome.transform, L("YOUR CHRONICLE AWAITS", "ВАША ХРОНИКА ЖДЁТ"), 15, FontStyle.Bold, TextAnchor.MiddleCenter, ThemeColorParser.ParseOrWhite(theme.Palette.Playable)); Stretch(eyebrow.rectTransform,new Vector2(.08f,.58f),new Vector2(.92f,.88f));
      var heading = Label(welcome.transform, L("Choose your path", "Выберите свой путь"), 34, FontStyle.Bold, TextAnchor.MiddleCenter, ThemeColorParser.ParseOrWhite(theme.Palette.TextPrimary)); Stretch(heading.rectTransform,new Vector2(.05f,.20f),new Vector2(.95f,.62f));
      var subtitle = Label(welcome.transform, L("Forge a collection, shape a deck, then enter the arena.", "Создайте коллекцию, соберите колоду и выходите на арену."), 16, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(.75f,.82f,.88f)); Stretch(subtitle.rectTransform,new Vector2(.05f,.02f),new Vector2(.95f,.28f));
      HubButton(hub.transform, "✦", "play", PlayerRoute.DeckSelection, new Vector2(.41f,.43f), new Vector2(.59f,.56f), theme, true);
      HubButton(hub.transform, "◆", "collection", PlayerRoute.Collection, new Vector2(.08f,.52f), new Vector2(.31f,.65f), theme, false);
      HubButton(hub.transform, "⚒", "forge", PlayerRoute.CardGeneration, new Vector2(.08f,.30f), new Vector2(.31f,.43f), theme, false);
      HubButton(hub.transform, "▤", "decks", PlayerRoute.DeckBuilder, new Vector2(.69f,.52f), new Vector2(.92f,.65f), theme, false);
      HubButton(hub.transform, "◉", "replays", PlayerRoute.Replay, new Vector2(.69f,.30f), new Vector2(.92f,.43f), theme, false);
      HubButton(hub.transform, "⚙", "settings", PlayerRoute.Settings, new Vector2(.41f,.075f), new Vector2(.59f,.18f), theme, false);
    }

    private void Screen(GameTheme theme)
    {
      TopBar(theme, renderedRoute is PlayerRoute.Login or PlayerRoute.Register or PlayerRoute.Bootstrap);
      var authRoute = renderedRoute is PlayerRoute.Login or PlayerRoute.Register;
      var panel = Node(content, "Route Panel", authRoute ? new Vector2(.27f,.16f) : new Vector2(.07f,.025f), authRoute ? new Vector2(.73f,.82f) : new Vector2(.93f,.87f));
      var panelImage = panel.AddComponent<Image>(); var panelColor = ThemeColorParser.ParseOrWhite(theme.Palette.Panel); panelColor.a = .96f; panelImage.color = panelColor;
      var outline = panel.AddComponent<Outline>(); outline.effectColor = ThemeColorParser.ParseOrWhite(theme.Palette.Border); outline.effectDistance = new Vector2(2,-2);
      var accent = Node(panel.transform, "Route Accent", new Vector2(0,.986f), Vector2.one); accent.AddComponent<Image>().color = ThemeColorParser.ParseOrWhite(theme.Palette.Playable); accent.AddComponent<LayoutElement>().ignoreLayout = true;
      var layout = panel.AddComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(authRoute?54:34,authRoute?54:34,authRoute?38:24,authRoute?38:24); layout.spacing = authRoute?14:10; layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
      Title(panel.transform, RouteTitle());
      switch (renderedRoute)
      {
        case PlayerRoute.Bootstrap: Body(panel.transform, localization.Text("loading")); Action(panel.transform, localization.Text("refresh"), () => controller.RetryConnection(), theme); break;
        case PlayerRoute.Login: Auth(panel.transform, false, theme); break;
        case PlayerRoute.Register: Auth(panel.transform, true, theme); break;
        case PlayerRoute.CardGeneration: GenerationScreen(panel.transform, theme); break;
        case PlayerRoute.Collection: CollectionScreen(panel.transform, theme); break;
        case PlayerRoute.DeckBuilder: DeckBuilderScreen(panel.transform, theme); break;
        case PlayerRoute.DeckSelection: DeckSelectionScreen(panel.transform, theme); break;
        case PlayerRoute.Play: PlayScreen(panel.transform, theme); break;
        case PlayerRoute.Results: ResultsScreen(panel.transform, theme); break;
        case PlayerRoute.Replay: ReplayScreen(panel.transform, theme); break;
        case PlayerRoute.Settings: Settings(panel.transform, theme); break;
        case PlayerRoute.Profile: ProfileScreen(panel.transform, theme); break;
      }
      if (!string.IsNullOrWhiteSpace(controller.LocalizedStatusText)) { var status = Body(panel.transform, controller.LocalizedStatusText); status.color = controller.StatusIsError?new Color(1f,.34f,.28f):new Color(.58f,.72f,.84f); status.fontSize = 14; }
    }

    private void TopBar(GameTheme theme, bool anonymous)
    {
      var bar = Node(content, "Top Bar", new Vector2(0, .88f), Vector2.one); var img = bar.AddComponent<Image>(); var barColor = ThemeColorParser.ParseOrWhite(theme.Palette.PanelRaised); barColor.a = .98f; img.color = barColor;
      var brand = Label(bar.transform, localization.Text("brand"), 24, FontStyle.Bold, TextAnchor.MiddleLeft, ThemeColorParser.ParseOrWhite(theme.Palette.TextPrimary)); Stretch(brand.rectTransform,new Vector2(.035f,0),new Vector2(.34f,1));
      if (!anonymous && renderedRoute != PlayerRoute.MainMenu) SmallButton(bar.transform, L("‹  MAIN HALL", "‹  ГЛАВНЫЙ ЗАЛ"), () => controller.Navigate(PlayerRoute.MainMenu), new Vector2(.38f,.18f),new Vector2(.53f,.82f),theme);
      if (!anonymous) SmallButton(bar.transform, localization.Text("profile"), () => controller.Navigate(PlayerRoute.Profile), new Vector2(.70f,.18f),new Vector2(.82f,.82f),theme);
      SmallButton(bar.transform, localization.Locale == "ru" ? "EN" : "RU", () => controller.ToggleLocale(), new Vector2(.84f,.18f),new Vector2(.90f,.82f),theme);
      SmallButton(bar.transform, "×", Application.Quit, new Vector2(.92f,.18f),new Vector2(.965f,.82f),theme);
    }

    private void Auth(Transform parent, bool register, GameTheme theme)
    {
      Input(parent, controller.Email, value=>controller.Email=value, false); Input(parent, controller.Password, value=>controller.Password=value, false, true);
      if(register)
      {
        Input(parent, controller.DisplayName, value=>controller.DisplayName=value, false);
        var hero=Body(parent,L("STARTER HERO: ","СТАРТОВЫЙ ГЕРОЙ: ")+controller.StarterHeroName);hero.fontStyle=FontStyle.Bold;
        StarterHeroSelector(parent,theme);
        var identity=Body(parent,controller.StarterHeroId=="cinder-vanguard"?L("Aggressive Cinder Vanguard deck.","Агрессивная колода Пламенного авангарда."):L("Resilient Astral Warden deck.","Стойкая колода Звёздного стража."));identity.fontSize=14;
      }
      Action(parent, register ? localization.Text("createAccount") : localization.Text("enter"), () => controller.AuthenticateFromUi(register), theme);
      Action(parent, register ? localization.Text("haveAccount") : localization.Text("createAccount"), () => controller.Navigate(register ? PlayerRoute.Login : PlayerRoute.Register), theme, true);
    }

    private void GenerationScreen(Transform parent, GameTheme theme)
    {
      var intro = Body(parent, localization.Text("forge.subtitle")); intro.fontSize = 17; intro.color = new Color(.76f,.83f,.9f);
      Input(parent, controller.Prompt, value => controller.Prompt = value, true);
      var providers = Row(parent); providers.GetComponent<LayoutElement>().preferredHeight = 154;
      ProviderOption(providers.transform, L("LOCAL FORGE", "ЛОКАЛЬНАЯ КУЗНИЦА"), L("Local models · Offline", "Локальные модели · Без сети"), L("Qwen creates each schema and Stable Diffusion renders unique art. Local gateways and model cache are required.", "Qwen создаёт схему каждой карты, Stable Diffusion рисует уникальный арт. Нужны локальные шлюзы и кеш моделей."), "local", theme);
      ProviderOption(providers.transform, "OPENAI", L("Premium art · Online", "Премиальный арт · Онлайн"), L("Unique schemas and a 1024×1024 illustration for every card.", "Уникальная схема и иллюстрация 1024×1024 для каждой карты."), "openai", theme);
      if (controller.GenerationState != "idle")
      {
        Body(parent, $"{controller.GenerationState.ToUpperInvariant()}  •  {controller.GenerationProgress}%");
        ProgressBar(parent, controller.GenerationProgress, theme);
      }
      Action(parent, controller.GenerationState is "failed" or "cancelled" ? L("RETRY GENERATION", "ПОВТОРИТЬ ГЕНЕРАЦИЮ") : localization.Text("generate"), controller.GenerationState is "failed" or "cancelled" ? controller.RetryGenerationFromUi : controller.GenerateFromUi, theme);
      if (controller.GenerationState is "queued" or "running" or "validating") Action(parent, L("CANCEL GENERATION", "ОТМЕНИТЬ ГЕНЕРАЦИЮ"), controller.CancelGenerationFromUi, theme, true);
    }

    private void CollectionScreen(Transform parent, GameTheme theme)
    {
      var filters = Row(parent);
      var input = Input(filters.transform, controller.CollectionSearch, value => controller.CollectionSearch = value, false);
      input.gameObject.GetComponent<LayoutElement>().preferredWidth = 520;
      SmallFlowButton(filters.transform, L("SEARCH", "НАЙТИ"), controller.ApplyCollectionFilter, theme);
      SmallFlowButton(filters.transform, controller.FavoritesOnly ? L("★ FAVORITES", "★ ИЗБРАННОЕ") : L("☆ ALL CARDS", "☆ ВСЕ КАРТЫ"), controller.ToggleFavoritesFilter, theme);
      var list = ScrollGrid(parent, 470, theme, 254, 424);
      if (controller.CollectionCards.Count == 0) EmptyState(list, "◇", localization.Text("collection.empty"), L("Generate cards in the Forge, then return here to search, favorite and build decks.", "Создайте карты в Кузнице, затем вернитесь сюда для поиска, избранного и сборки колоды."), theme);
      foreach (var card in controller.CollectionCards)
      {
        CollectionCardTile(list, card, theme);
      }
    }

    private void DeckBuilderScreen(Transform parent, GameTheme theme)
    {
      var header = Row(parent);
      var name = Input(header.transform, controller.DeckName, value => controller.DeckName = value, false); name.gameObject.GetComponent<LayoutElement>().preferredWidth = 480;
      Body(header.transform, $"{controller.DeckCardCount} / 30");
      SmallFlowButton(header.transform, L("NEW", "НОВАЯ"), controller.NewDeck, theme);
      var list = ScrollGrid(parent, 470, theme, 254, 430);
      if (controller.CollectionCards.Count == 0) EmptyState(list, "+", L("NO CARDS AVAILABLE", "НЕТ ДОСТУПНЫХ КАРТ"), L("Open the Forge to create your first validated card set.", "Откройте Кузницу, чтобы создать первый проверенный набор карт."), theme);
      foreach (var card in controller.CollectionCards)
      {
        DeckBuilderCardTile(list, card, theme);
      }
      Action(parent, localization.Text("saveDeck"), controller.SaveDeckFromUi, theme);
    }

    private void DeckSelectionScreen(Transform parent, GameTheme theme)
    {
      var top = Row(parent); SmallFlowButton(top.transform, L("CREATE DECK", "СОЗДАТЬ КОЛОДУ"), () => { controller.NewDeck(); controller.Navigate(PlayerRoute.DeckBuilder); }, theme);
      var list = ScrollList(parent, 460, theme);
      if (controller.Decks.Count == 0) EmptyState(list, "30", L("NO BATTLE DECKS", "НЕТ БОЕВЫХ КОЛОД"), L("Create a legal 30-card deck to unlock matchmaking.", "Создайте легальную колоду из 30 карт, чтобы открыть игру."), theme);
      foreach (var deck in controller.Decks)
      {
        var row = ListRow(list, theme);
        var marker = deck.Active ? "◆ " : "◇ ";
        var label = Label(row.transform, $"{marker}{deck.Name}\n{deck.Count} / 30", 17, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white); label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        SmallFlowButton(row.transform, L("EDIT", "ИЗМЕНИТЬ"), () => controller.EditDeck(deck.Id), theme);
        SmallFlowButton(row.transform, deck.Active ? L("ACTIVE", "АКТИВНА") : L("SELECT", "ВЫБРАТЬ"), () => controller.SelectDeck(deck.Id), theme);
        SmallFlowButton(row.transform, L("DELETE", "УДАЛИТЬ"), () => controller.DeleteDeck(deck.Id), theme);
      }
      if (!string.IsNullOrWhiteSpace(controller.ActiveDeckId)) Action(parent, localization.Text("continue"), () => controller.Navigate(PlayerRoute.Play), theme);
    }

    private void PlayScreen(Transform parent, GameTheme theme)
    {
      var versus = Node(parent,"Match Briefing",Vector2.zero,Vector2.one);var image=versus.AddComponent<Image>();var surface=ThemeColorParser.ParseOrWhite(theme.Palette.BoardSurface);surface.a=.9f;image.color=surface;var outline=versus.AddComponent<Outline>();outline.effectColor=ThemeColorParser.ParseOrWhite(theme.Palette.Playable);outline.effectDistance=new Vector2(2,-2);var layout=versus.AddComponent<HorizontalLayoutGroup>();layout.padding=new RectOffset(28,28,22,22);layout.spacing=24;layout.childControlHeight=true;layout.childControlWidth=true;layout.childForceExpandWidth=true;versus.AddComponent<LayoutElement>().preferredHeight=190;
      MatchSide(versus.transform,L("YOUR DECK", "ВАША КОЛОДА"),string.IsNullOrWhiteSpace(controller.ActiveDeckName)?L("No deck selected", "Колода не выбрана"):controller.ActiveDeckName,$"{controller.ActiveDeckCardCount} / 30",theme);
      var vs=Label(versus.transform,"VS",32,FontStyle.Bold,TextAnchor.MiddleCenter,ThemeColorParser.ParseOrWhite(theme.Palette.Playable));vs.gameObject.AddComponent<LayoutElement>().preferredWidth=100;
      MatchSide(versus.transform,L("OPPONENT", "СОПЕРНИК"),L("Chronicle Warden", "Страж Хроник"),L("Adaptive bot · Standard", "Адаптивный бот · Стандарт"),theme);
      var notes=Body(parent,L("STANDARD DUEL  ·  30 HEALTH  ·  VERIFIED REPLAY  ·  NO GENERATION DURING MATCH", "СТАНДАРТНАЯ ДУЭЛЬ  ·  30 ЗДОРОВЬЯ  ·  ПРОВЕРЯЕМЫЙ ПОВТОР  ·  БЕЗ ГЕНЕРАЦИИ В МАТЧЕ"));notes.alignment=TextAnchor.MiddleCenter;notes.fontSize=14;notes.color=new Color(.72f,.81f,.87f);
      Action(parent,localization.Text("playBot"),controller.StartMatchFromUi,theme);
      Action(parent,L("CHANGE DECK", "СМЕНИТЬ КОЛОДУ"),()=>controller.Navigate(PlayerRoute.DeckSelection),theme,true);
    }

    private void MatchSide(Transform parent,string eyebrow,string title,string meta,GameTheme theme)
    {
      var node=Node(parent,title,Vector2.zero,Vector2.one);var layout=node.AddComponent<VerticalLayoutGroup>();layout.spacing=5;layout.childAlignment=TextAnchor.MiddleCenter;layout.childControlHeight=true;layout.childControlWidth=true;layout.childForceExpandHeight=false;
      var a=Label(node.transform,eyebrow,12,FontStyle.Bold,TextAnchor.MiddleCenter,ThemeColorParser.ParseOrWhite(theme.Palette.Playable));a.gameObject.AddComponent<LayoutElement>().preferredHeight=24;
      var b=Label(node.transform,title,22,FontStyle.Bold,TextAnchor.MiddleCenter,ThemeColorParser.ParseOrWhite(theme.Palette.TextPrimary));b.gameObject.AddComponent<LayoutElement>().preferredHeight=42;
      var c=Label(node.transform,meta,14,FontStyle.Normal,TextAnchor.MiddleCenter,new Color(.7f,.79f,.85f));c.gameObject.AddComponent<LayoutElement>().preferredHeight=28;
    }

    private void ResultsScreen(Transform parent, GameTheme theme)
    {
      var verdict=controller.LastReplayVerified?L("MATCH VERIFIED", "МАТЧ ПРОВЕРЕН"):L("MATCH COMPLETE", "МАТЧ ЗАВЕРШЁН");
      var detail=$"{L("Commands", "Команды")}: {controller.LastCompletedCommandCount}   •   {L("Result", "Результат")}: {controller.LastTerminalReason}\n{L("Match", "Матч")}: {controller.LastCompletedMatchId}";
      EmptyState(parent,controller.LastReplayVerified?"✓":"◆",verdict,detail,theme);
      var row=Row(parent);row.GetComponent<LayoutElement>().preferredHeight=74;SmallFlowButton(row.transform,L("OPEN REPLAY", "ОТКРЫТЬ ПОВТОР"),()=>controller.Navigate(PlayerRoute.Replay),theme);SmallFlowButton(row.transform,L("PLAY AGAIN", "ИГРАТЬ СНОВА"),()=>controller.Navigate(PlayerRoute.Play),theme);
    }

    private void ReplayScreen(Transform parent, GameTheme theme)
    {
      EmptyState(parent,"▶",controller.LastReplayVerified?L("VERIFIED REPLAY", "ПРОВЕРЕННЫЙ ПОВТОР"):L("REPLAY", "ПОВТОР"),string.IsNullOrWhiteSpace(controller.LastCompletedMatchId)?L("Complete a match to create a canonical replay.", "Завершите матч, чтобы создать канонический повтор."):$"{controller.LastCompletedMatchId}\n{controller.LastCompletedCommandCount} {L("canonical commands", "канонических команд")}",theme);
      var evidence=Node(parent,"Replay Evidence",Vector2.zero,Vector2.one);var image=evidence.AddComponent<Image>();var color=ThemeColorParser.ParseOrWhite(theme.Palette.PanelRaised);color.a=.9f;image.color=color;var layout=evidence.AddComponent<HorizontalLayoutGroup>();layout.padding=new RectOffset(24,24,16,16);layout.spacing=12;layout.childControlWidth=true;layout.childForceExpandWidth=true;evidence.AddComponent<LayoutElement>().preferredHeight=92;
      ReplayBadge(evidence.transform,L("SOURCE", "ИСТОЧНИК"),L("COMMAND LOG", "ЖУРНАЛ КОМАНД"),theme);ReplayBadge(evidence.transform,L("INTEGRITY", "ЦЕЛОСТНОСТЬ"),controller.LastReplayVerified?L("HASH VERIFIED", "ХЕШ ПРОВЕРЕН"):L("NOT VERIFIED", "НЕ ПРОВЕРЕН"),theme);ReplayBadge(evidence.transform,L("COMMANDS", "КОМАНДЫ"),controller.LastCompletedCommandCount.ToString(),theme);
      Action(parent,L("START A MATCH", "НАЧАТЬ МАТЧ"),()=>controller.Navigate(PlayerRoute.DeckSelection),theme);
    }

    private void ReplayBadge(Transform parent,string title,string value,GameTheme theme){var node=Node(parent,title,Vector2.zero,Vector2.one);var layout=node.AddComponent<VerticalLayoutGroup>();layout.childAlignment=TextAnchor.MiddleCenter;layout.childControlHeight=true;layout.childControlWidth=true;var a=Label(node.transform,title,11,FontStyle.Bold,TextAnchor.MiddleCenter,new Color(.62f,.72f,.8f));a.gameObject.AddComponent<LayoutElement>().preferredHeight=24;var b=Label(node.transform,value,15,FontStyle.Bold,TextAnchor.MiddleCenter,ThemeColorParser.ParseOrWhite(theme.Palette.Playable));b.gameObject.AddComponent<LayoutElement>().preferredHeight=30;}

    private void ProfileScreen(Transform parent, GameTheme theme)
    {
      var identity=Node(parent,"Wayfinder Identity",Vector2.zero,Vector2.one);var image=identity.AddComponent<Image>();var color=ThemeColorParser.ParseOrWhite(theme.Palette.BoardSurface);color.a=.9f;image.color=color;var layout=identity.AddComponent<HorizontalLayoutGroup>();layout.padding=new RectOffset(28,28,20,20);layout.spacing=28;layout.childControlHeight=true;layout.childControlWidth=true;layout.childForceExpandWidth=true;identity.AddComponent<LayoutElement>().preferredHeight=154;
      var crest=Label(identity.transform,"◆",52,FontStyle.Bold,TextAnchor.MiddleCenter,ThemeColorParser.ParseOrWhite(theme.Palette.Playable));crest.gameObject.AddComponent<LayoutElement>().preferredWidth=110;
      var copy=Label(identity.transform,$"{controller.PlayerDisplayName}\n{controller.PlayerEmail}\n{L("SECURE PLAYER SESSION", "ЗАЩИЩЁННАЯ СЕССИЯ")}",18,FontStyle.Bold,TextAnchor.MiddleLeft,ThemeColorParser.ParseOrWhite(theme.Palette.TextPrimary));copy.gameObject.AddComponent<LayoutElement>().flexibleWidth=1;
      var stats=Row(parent);stats.GetComponent<LayoutElement>().preferredHeight=84;ProfileStat(stats.transform,L("COLLECTION", "КОЛЛЕКЦИЯ"),controller.CollectionCount.ToString(),theme);ProfileStat(stats.transform,L("DECKS", "КОЛОДЫ"),controller.DeckCount.ToString(),theme);ProfileStat(stats.transform,L("ACTIVE DECK", "АКТИВНАЯ КОЛОДА"),string.IsNullOrWhiteSpace(controller.ActiveDeckName)?"—":controller.ActiveDeckName,theme);
      Action(parent,localization.Text("logout"),controller.LogoutFromUi,theme,true);
    }

    private void ProfileStat(Transform parent,string title,string value,GameTheme theme){var node=Node(parent,title,Vector2.zero,Vector2.one);var image=node.AddComponent<Image>();image.color=ThemeColorParser.ParseOrWhite(theme.Palette.PanelRaised);var text=Label(node.transform,$"{title}\n{value}",14,FontStyle.Bold,TextAnchor.MiddleCenter,ThemeColorParser.ParseOrWhite(theme.Palette.TextPrimary));Stretch(text.rectTransform,new Vector2(.05f,.08f),new Vector2(.95f,.92f));}

    private void Settings(Transform parent, GameTheme theme)
    {
      Body(parent, localization.Text("settings.subtitle")); Body(parent, localization.Text("language"));
      var languages = Row(parent); languages.GetComponent<LayoutElement>().preferredHeight = 74;
      LanguageOption(languages.transform,"English","en",theme); LanguageOption(languages.transform,"Русский","ru",theme);
      Body(parent, localization.Text("theme"));
      var row = Row(parent); row.GetComponent<LayoutElement>().preferredHeight = 92; foreach(var item in themes.Themes) ThemeOption(row.transform,item,theme);
      var audio = Row(parent); audio.GetComponent<LayoutElement>().preferredHeight = 70;
      SettingsOption(audio.transform,L("MUSIC", "МУЗЫКА"),$"{Mathf.RoundToInt(controller.MusicVolume*100)}%",controller.CycleMusicVolume,theme);
      SettingsOption(audio.transform,L("EFFECTS", "ЭФФЕКТЫ"),$"{Mathf.RoundToInt(controller.EffectsVolume*100)}%",controller.CycleEffectsVolume,theme);
      SettingsOption(audio.transform,L("MOTION", "АНИМАЦИЯ"),controller.ReducedMotion?L("REDUCED", "СНИЖЕНА"):L("FULL", "ПОЛНАЯ"),controller.ToggleReducedMotion,theme);
      var display = Row(parent); display.GetComponent<LayoutElement>().preferredHeight = 70;
      SettingsOption(display.transform,L("RESOLUTION","РАЗРЕШЕНИЕ"),controller.ResolutionLabel,controller.CycleResolution,theme);
      SettingsOption(display.transform,L("DISPLAY MODE","РЕЖИМ ЭКРАНА"),controller.DisplayModeLabel,controller.CycleDisplayMode,theme);
      if(controller.ReturnToActiveMatch) Action(parent,L("RETURN TO MATCH","ВЕРНУТЬСЯ В МАТЧ"),controller.ReturnToMatch,theme);
    }

    private void ProviderOption(Transform parent, string title, string meta, string description, string mode, GameTheme theme)
    {
      var selected = controller.ProviderMode == mode;
      var node = Node(parent, mode + " Provider", Vector2.zero, Vector2.one);
      var image = node.AddComponent<Image>(); var color = ThemeColorParser.ParseOrWhite(selected ? theme.Palette.BoardSurface : theme.Palette.PanelRaised); color.a = selected ? .98f : .82f; image.color = color;
      var outline = node.AddComponent<Outline>(); outline.effectColor = ThemeColorParser.ParseOrWhite(selected ? theme.Palette.Playable : theme.Palette.Disabled); outline.effectDistance = selected ? new Vector2(3,-3) : new Vector2(1,-1);
      var button = node.AddComponent<Button>(); button.onClick.AddListener(() => controller.SelectProviderMode(mode));
      var badge = Label(node.transform, selected ? L("SELECTED", "ВЫБРАНО") : L("AVAILABLE", "ДОСТУПНО"), 12, FontStyle.Bold, TextAnchor.MiddleRight, ThemeColorParser.ParseOrWhite(selected ? theme.Palette.Playable : theme.Palette.TextPrimary)); Stretch(badge.rectTransform,new Vector2(.62f,.76f),new Vector2(.94f,.94f));
      var heading = Label(node.transform,title,22,FontStyle.Bold,TextAnchor.MiddleLeft,ThemeColorParser.ParseOrWhite(theme.Palette.TextPrimary)); Stretch(heading.rectTransform,new Vector2(.06f,.58f),new Vector2(.68f,.90f));
      var metadata = Label(node.transform,meta,14,FontStyle.Bold,TextAnchor.MiddleLeft,ThemeColorParser.ParseOrWhite(theme.Palette.Playable)); Stretch(metadata.rectTransform,new Vector2(.06f,.38f),new Vector2(.94f,.60f));
      var copy = Label(node.transform,description,13,FontStyle.Normal,TextAnchor.UpperLeft,new Color(.72f,.80f,.86f)); Stretch(copy.rectTransform,new Vector2(.06f,.08f),new Vector2(.94f,.38f));
    }

    private void ThemeOption(Transform parent, GameTheme option, GameTheme activeTheme)
    {
      var selected = controller.ThemeId == option.Id;
      var node = ButtonNode(parent, (selected ? "◆  " : "◇  ") + option.DisplayName, () => controller.SetTheme(option.Id), activeTheme, selected);
      var image = node.GetComponent<Image>(); var color = ThemeColorParser.ParseOrWhite(option.Palette.BoardSurface); color.a = .96f; image.color = color;
      node.GetComponentInChildren<Text>().color = ThemeColorParser.ParseOrWhite(option.Palette.TextPrimary);
      var outline = node.AddComponent<Outline>(); outline.effectColor = ThemeColorParser.ParseOrWhite(selected ? option.Palette.Playable : option.Palette.Border); outline.effectDistance = selected ? new Vector2(3,-3) : new Vector2(1,-1);
      node.AddComponent<LayoutElement>().preferredHeight = 82;
    }

    private void LanguageOption(Transform parent, string title, string locale, GameTheme theme)
    {
      var selected = localization.Locale == locale;
      var node = ButtonNode(parent,(selected ? L("◆  ", "◆  ") : "◇  ") + title,()=>controller.SetLocale(locale),theme,selected);
      var outline = node.AddComponent<Outline>(); outline.effectColor = ThemeColorParser.ParseOrWhite(selected ? theme.Palette.Playable : theme.Palette.Disabled); outline.effectDistance = selected ? new Vector2(3,-3) : new Vector2(1,-1);
      node.AddComponent<LayoutElement>().preferredHeight = 68;
    }

    private void SettingsOption(Transform parent, string title, string value, Action action, GameTheme theme)
    {
      var node = ButtonNode(parent,$"{title}\n{value}",action,theme,false);
      var text=node.GetComponentInChildren<Text>();text.fontSize=15;text.color=ThemeColorParser.ParseOrWhite(theme.Palette.TextPrimary);
      var outline=node.AddComponent<Outline>();outline.effectColor=ThemeColorParser.ParseOrWhite(theme.Palette.Border);outline.effectDistance=new Vector2(1,-1);
      node.AddComponent<LayoutElement>().preferredHeight=66;
    }

    private void CollectionCardTile(Transform parent, PlayerApplicationController.CollectionUiCard card, GameTheme theme)
    {
      var tile = Node(parent, "Collection Card " + card.CardId, Vector2.zero, Vector2.one);
      var image = tile.AddComponent<Image>(); image.color = ThemeColorParser.ParseOrWhite(theme.Palette.CardFace);
      var outline = tile.AddComponent<Outline>(); outline.effectColor = ThemeColorParser.ParseOrWhite(card.Favorite ? theme.Palette.Playable : theme.Palette.Border); outline.effectDistance = card.Favorite ? new Vector2(3,-3) : new Vector2(1,-1);
      var layout = tile.AddComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(10,10,10,10); layout.spacing = 7; layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
      CardArtLarge(tile.transform, card, 234);
      var title = Label(tile.transform, card.DisplayName, 18, FontStyle.Bold, TextAnchor.MiddleCenter, ThemeColorParser.ParseOrWhite(theme.Palette.TextOnCard)); title.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
      var statCopy=CollectionStats(card);
      var meta = Label(tile.transform, $"{statCopy}  ·  {card.Rarity.ToUpperInvariant()}", 12, FontStyle.Bold, TextAnchor.MiddleCenter, ThemeColorParser.ParseOrWhite(theme.Palette.TextOnCard)); meta.gameObject.AddComponent<LayoutElement>().preferredHeight = 22;
      var rules = Label(tile.transform, CardRulesFormatter.Format(string.IsNullOrWhiteSpace(card.RulesText) ? L("Battle-ready minion", "Готовый к бою миньон") : card.RulesText, string.Equals(localization.Locale, "ru", StringComparison.OrdinalIgnoreCase)), 13, FontStyle.Normal, TextAnchor.UpperCenter, ThemeColorParser.ParseOrWhite(theme.Palette.TextOnCard)); rules.supportRichText=true; rules.gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
      var footer = ButtonNode(tile.transform, (card.Favorite ? "★  " : "☆  ") + L($"OWNED {card.Owned}", $"В КОЛЛЕКЦИИ {card.Owned}"), () => controller.ToggleFavorite(card.VersionId), theme, card.Favorite); footer.AddComponent<LayoutElement>().preferredHeight = 38;
      tile.AddComponent<CardLongPressPreview>().Configure(()=>ShowCollectionPreview(card,theme),HideCollectionPreview);
    }

    private void DeckBuilderCardTile(Transform parent, PlayerApplicationController.CollectionUiCard card, GameTheme theme)
    {
      var tile = Node(parent, "Deck Card " + card.CardId, Vector2.zero, Vector2.one);
      var image = tile.AddComponent<Image>(); image.color = ThemeColorParser.ParseOrWhite(theme.Palette.CardFace);
      var outline = tile.AddComponent<Outline>(); outline.effectColor = ThemeColorParser.ParseOrWhite(card.Selected > 0 ? theme.Palette.Playable : theme.Palette.Border); outline.effectDistance = card.Selected > 0 ? new Vector2(3,-3) : new Vector2(1,-1);
      var layout = tile.AddComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(10,10,10,10); layout.spacing = 7; layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
      CardArtLarge(tile.transform,card,234,()=>ShowCollectionPreview(card,theme));
      var title=Label(tile.transform,card.DisplayName,18,FontStyle.Bold,TextAnchor.MiddleCenter,ThemeColorParser.ParseOrWhite(theme.Palette.TextOnCard));title.gameObject.AddComponent<LayoutElement>().preferredHeight=30;
      var meta=Label(tile.transform,$"{CollectionStats(card)}  ·  {card.Rarity.ToUpperInvariant()}",12,FontStyle.Bold,TextAnchor.MiddleCenter,ThemeColorParser.ParseOrWhite(theme.Palette.TextOnCard));meta.gameObject.AddComponent<LayoutElement>().preferredHeight=22;
      var rules=Label(tile.transform,CardRulesFormatter.Format(card.RulesText,localization.Locale=="ru"),13,FontStyle.Normal,TextAnchor.UpperCenter,ThemeColorParser.ParseOrWhite(theme.Palette.TextOnCard));rules.supportRichText=true;rules.gameObject.AddComponent<LayoutElement>().preferredHeight=38;
      var controls=Node(tile.transform,"Deck Quantity Controls",Vector2.zero,Vector2.one);var controlsLayout=controls.AddComponent<LayoutElement>();controlsLayout.preferredHeight=42;controlsLayout.minHeight=42;
      var minus=ButtonNode(controls.transform,"−",()=>controller.RemoveDraftCard(card.VersionId),theme,false);Stretch(minus.GetComponent<RectTransform>(),new Vector2(0,0),new Vector2(.31f,1));
      var count=Label(controls.transform,card.Selected.ToString(),20,FontStyle.Bold,TextAnchor.MiddleCenter,ThemeColorParser.ParseOrWhite(card.Selected>0?theme.Palette.Playable:theme.Palette.TextOnCard));Stretch(count.rectTransform,new Vector2(.33f,0),new Vector2(.67f,1));
      var plus=ButtonNode(controls.transform,"+",()=>controller.AddDraftCard(card.VersionId),theme,true);Stretch(plus.GetComponent<RectTransform>(),new Vector2(.69f,0),new Vector2(1,1));
      tile.AddComponent<CardLongPressPreview>().Configure(()=>ShowCollectionPreview(card,theme),HideCollectionPreview);
    }

    private void ShowCollectionPreview(PlayerApplicationController.CollectionUiCard card,GameTheme theme)
    {
      HideCollectionPreview();
      var overlay=Node(content,"Collection Held Card Preview",Vector2.zero,Vector2.one);overlay.transform.SetAsLastSibling();
      var shade=overlay.AddComponent<Image>();shade.color=new Color(0,0,0,.86f);
      var close=overlay.AddComponent<Button>();close.onClick.AddListener(HideCollectionPreview);
      var closeHint=Label(overlay.transform,L("CLICK ANYWHERE TO CLOSE","НАЖМИТЕ, ЧТОБЫ ЗАКРЫТЬ"),13,FontStyle.Bold,TextAnchor.UpperRight,new Color(.82f,.86f,.9f));Stretch(closeHint.rectTransform,new Vector2(.68f,.93f),new Vector2(.97f,.985f));closeHint.raycastTarget=false;
      var panel=Node(overlay.transform,"Preview Card",new Vector2(.5f,.5f),new Vector2(.5f,.5f));var panelRect=panel.GetComponent<RectTransform>();panelRect.pivot=new Vector2(.5f,.5f);panelRect.sizeDelta=new Vector2(430,680);var panelImage=panel.AddComponent<Image>();panelImage.color=new Color(.018f,.014f,.01f,1f);
      var shadow=panel.AddComponent<Shadow>();shadow.effectColor=new Color(0,0,0,.9f);shadow.effectDistance=new Vector2(10,-12);
      var outline=panel.AddComponent<Outline>();outline.effectColor=ThemeColorParser.ParseOrWhite(theme.Palette.Playable);outline.effectDistance=new Vector2(5,-5);
      var backing=Node(panel.transform,"Preview Opaque Backing",new Vector2(.025f,.018f),new Vector2(.975f,.982f));var backingImage=backing.AddComponent<RawImage>();backingImage.texture=Texture2D.whiteTexture;backingImage.color=new Color(.018f,.014f,.01f,1f);backingImage.raycastTarget=false;
      var frameNode=Node(panel.transform,"Preview Authored Frame",new Vector2(-.025f,-.018f),new Vector2(1.025f,1.018f));var frame=frameNode.AddComponent<RawImage>();frame.texture=Resources.Load<Texture2D>(theme?.Sprites?.CardFrameResource??"CardFrames/frame-arcane-v1");frame.raycastTarget=false;
      var artNode=Node(panel.transform,"Preview Art",new Vector2(.08f,.37f),new Vector2(.92f,.9f));var art=artNode.AddComponent<RawImage>();art.texture=ResolveCardArt(card);art.color=art.texture==null?new Color(.08f,.12f,.2f):Color.white;art.uvRect=new Rect(0,0,1,1);
      var artOutline=artNode.AddComponent<Outline>();artOutline.effectColor=ThemeColorParser.ParseOrWhite(theme.Palette.Border);artOutline.effectDistance=new Vector2(3,-3);
      var title=Label(panel.transform,card.DisplayName,28,FontStyle.Bold,TextAnchor.MiddleCenter,ThemeColorParser.ParseOrWhite(theme.Palette.TextOnCard));Stretch(title.rectTransform,new Vector2(.08f,.9f),new Vector2(.92f,.98f));
      var stats=Label(panel.transform,CollectionStats(card),18,FontStyle.Bold,TextAnchor.MiddleCenter,ThemeColorParser.ParseOrWhite(theme.Palette.TextOnCard));Stretch(stats.rectTransform,new Vector2(.08f,.36f),new Vector2(.92f,.43f));
      var rulesSurface=Node(panel.transform,"Preview Rules Surface",new Vector2(.065f,.045f),new Vector2(.935f,.355f));var rulesImage=rulesSurface.AddComponent<Image>();rulesImage.color=new Color(.028f,.021f,.014f,1f);rulesImage.raycastTarget=false;var rulesOutline=rulesSurface.AddComponent<Outline>();rulesOutline.effectColor=ThemeColorParser.ParseOrWhite(theme.Palette.Border);rulesOutline.effectDistance=new Vector2(2,-2);
      var rules=Label(panel.transform,CardRulesFormatter.Format(card.RulesText,localization.Locale=="ru"),17,FontStyle.Normal,TextAnchor.MiddleCenter,ThemeColorParser.ParseOrWhite(theme.Palette.TextOnCard));rules.supportRichText=true;rules.resizeTextForBestFit=true;rules.resizeTextMinSize=12;Stretch(rules.rectTransform,new Vector2(.095f,.065f),new Vector2(.905f,.335f));
      PreviewBadge(panel.transform,"Mana",card.ManaCost.ToString(),new Vector2(.015f,.84f),new Vector2(.16f,.93f),new Color(.08f,.38f,.92f),theme);
      if(card.CardType.IndexOf("MINION",StringComparison.OrdinalIgnoreCase)>=0)
      {
        PreviewBadge(panel.transform,"Attack",card.Attack.ToString(),new Vector2(.015f,.005f),new Vector2(.16f,.095f),new Color(.92f,.58f,.08f),theme);
        PreviewBadge(panel.transform,"Health",card.Health.ToString(),new Vector2(.84f,.005f),new Vector2(.985f,.095f),new Color(.78f,.08f,.12f),theme);
      }
      else if(card.CardType.IndexOf("WEAPON",StringComparison.OrdinalIgnoreCase)>=0)
      {
        PreviewBadge(panel.transform,"Attack",card.Attack.ToString(),new Vector2(.015f,.005f),new Vector2(.16f,.095f),new Color(.92f,.58f,.08f),theme);
        PreviewBadge(panel.transform,"Durability",card.Durability.ToString(),new Vector2(.84f,.005f),new Vector2(.985f,.095f),new Color(.42f,.5f,.58f),theme);
      }
      else if(card.CardType.IndexOf("LOCATION",StringComparison.OrdinalIgnoreCase)>=0)
        PreviewBadge(panel.transform,"Durability",card.Durability.ToString(),new Vector2(.84f,.005f),new Vector2(.985f,.095f),new Color(.42f,.5f,.58f),theme);
    }

    private static void PreviewBadge(Transform parent,string name,string value,Vector2 min,Vector2 max,Color color,GameTheme theme)
    {
      var badge=Node(parent,name+" Badge",min,max);badge.AddComponent<Image>().color=color;
      var shadow=badge.AddComponent<Shadow>();shadow.effectColor=new Color(0,0,0,.9f);shadow.effectDistance=new Vector2(3,-3);
      var outline=badge.AddComponent<Outline>();outline.effectColor=ThemeColorParser.ParseOrWhite(theme.Palette.Border);outline.effectDistance=new Vector2(2,-2);
      var label=Label(badge.transform,value,30,FontStyle.Bold,TextAnchor.MiddleCenter,Color.white);Stretch(label.rectTransform,Vector2.zero,Vector2.one);
    }

    private void HideCollectionPreview(){var existing=content==null?null:content.Find("Collection Held Card Preview");if(existing!=null)UnityEngine.Object.Destroy(existing.gameObject);}

    public bool ShowCollectionCardPreviewForCapture(int index)
    {
      var cards=controller.CollectionCards;
      var card=index>=0&&index<cards.Count?cards[index]:cards.FirstOrDefault();
      if(card==null||content==null)return false;
      ShowCollectionPreview(card,themes.Find(controller.ThemeId));
      return true;
    }
    private static string CollectionStats(PlayerApplicationController.CollectionUiCard card)
    {
      if(card==null)return string.Empty;
      if(card.CardType.IndexOf("MINION",StringComparison.OrdinalIgnoreCase)>=0)return $"{card.ManaCost} MANA · {card.Attack} ATK · {card.Health} HP";
      if(card.CardType.IndexOf("WEAPON",StringComparison.OrdinalIgnoreCase)>=0)return $"{card.ManaCost} MANA · {card.Attack} ATK · {card.Durability} DURABILITY";
      if(card.CardType.IndexOf("LOCATION",StringComparison.OrdinalIgnoreCase)>=0)return $"{card.ManaCost} MANA · {card.Durability} DURABILITY";
      if(card.CardType.IndexOf("HERO",StringComparison.OrdinalIgnoreCase)>=0&&card.ArmorGain>0)return $"{card.ManaCost} MANA · +{card.ArmorGain} ARMOR";
      return $"{card.ManaCost} MANA";
    }

    private string RouteTitle() => renderedRoute switch { PlayerRoute.Login=>localization.Text("login.title"), PlayerRoute.Register=>localization.Text("register.title"), PlayerRoute.CardGeneration=>localization.Text("forge"), PlayerRoute.Collection=>localization.Text("collection"), PlayerRoute.DeckBuilder=>localization.Text("decks"), PlayerRoute.DeckSelection=>localization.Text("decks"), PlayerRoute.Play=>localization.Text("play"), PlayerRoute.Replay=>localization.Text("replays"), PlayerRoute.Settings=>localization.Text("settings"), PlayerRoute.Profile=>localization.Text("profile"), _=>localization.Text("brand") };
    private void HubButton(Transform p,string icon,string key,PlayerRoute route,Vector2 min,Vector2 max,GameTheme t,bool primary){var b=ButtonNode(p,icon+"  "+localization.Text(key),()=>controller.Navigate(route),t,false);Stretch(b.GetComponent<RectTransform>(),min,max);var image=b.GetComponent<Image>();var color=ThemeColorParser.ParseOrWhite(t.Palette.PanelRaised);color.a=.96f;image.color=color;var outline=b.AddComponent<Outline>();outline.effectColor=ThemeColorParser.ParseOrWhite(primary?t.Palette.Playable:t.Palette.Border);outline.effectDistance=primary?new Vector2(4,-4):new Vector2(2,-2);var text=b.GetComponentInChildren<Text>();text.color=primary?ThemeColorParser.ParseOrWhite(t.Palette.Playable):ThemeColorParser.ParseOrWhite(t.Palette.TextPrimary);text.fontSize=primary?23:19;var shadow=text.gameObject.AddComponent<Shadow>();shadow.effectColor=new Color(0,0,0,.8f);shadow.effectDistance=new Vector2(2,-2);}
    private void Action(Transform p,string text,Action action,GameTheme t,bool secondary=false){var b=ButtonNode(p,text,action,t,!secondary); var e=b.AddComponent<LayoutElement>();e.preferredHeight=58;e.minHeight=48;}
    private GameObject ButtonNode(Transform p,string text,Action action,GameTheme t,bool primary){var n=Node(p,text+" Button",Vector2.zero,Vector2.one);var img=n.AddComponent<Image>();img.color=ThemeColorParser.ParseOrWhite(primary?t.Palette.Playable:t.Palette.PanelRaised);var button=n.AddComponent<Button>();var colors=button.colors;colors.highlightedColor=ThemeColorParser.ParseOrWhite(t.Palette.Target);colors.pressedColor=ThemeColorParser.ParseOrWhite(t.Palette.Connection);button.colors=colors;button.onClick.AddListener(()=>action());var label=Label(n.transform,text,primary?21:18,FontStyle.Bold,TextAnchor.MiddleCenter,primary?ThemeColorParser.ParseOrWhite(t.Palette.TextOnCard):ThemeColorParser.ParseOrWhite(t.Palette.TextPrimary));Stretch(label.rectTransform,Vector2.zero,Vector2.one);return n;}
    private void SmallButton(Transform p,string text,Action action,Vector2 min,Vector2 max,GameTheme t){var b=ButtonNode(p,text,action,t,false);Stretch(b.GetComponent<RectTransform>(),min,max);}
    private void SmallFlowButton(Transform p,string text,Action action,GameTheme t){var b=ButtonNode(p,text,action,t,false);var e=b.AddComponent<LayoutElement>();e.preferredWidth=190;e.preferredHeight=52;}
    private void Selector(Transform p,string a,string b,Action aa,Action bb,GameTheme t){var row=Row(p);SmallFlowButton(row.transform,a,aa,t);SmallFlowButton(row.transform,b,bb,t);}
    private void StarterHeroSelector(Transform parent,GameTheme theme)
    {
      var row=Row(parent);var rowLayout=row.GetComponent<LayoutElement>();rowLayout.preferredHeight=136;rowLayout.minHeight=136;
      StarterHeroOption(row.transform,"astral-warden",L("ASTRA","АСТРА"),L("CONTROL · POWER (2): RESTORE 2 HEALTH","КОНТРОЛЬ · СИЛА (2): ЛЕЧЕНИЕ 2"),"HeroArt/astral-warden",theme);
      StarterHeroOption(row.transform,"cinder-vanguard",L("VARG","ВАРГ"),L("PRESSURE · POWER (2): DEAL 1 DAMAGE","НАТИСК · СИЛА (2): 1 УРОН"),"HeroArt/cinder-vanguard",theme);
    }
    private void StarterHeroOption(Transform parent,string heroId,string title,string subtitle,string artResource,GameTheme theme)
    {
      var selected=controller.StarterHeroId==heroId;var option=new GameObject(title+" Hero Option");option.transform.SetParent(parent,false);var rect=option.AddComponent<RectTransform>();var image=option.AddComponent<Image>();image.color=selected?new Color(.11f,.2f,.3f,.98f):new Color(.035f,.045f,.065f,.96f);var element=option.AddComponent<LayoutElement>();element.preferredWidth=260;element.minWidth=230;element.preferredHeight=132;element.minHeight=132;element.flexibleWidth=1;
      var outline=option.AddComponent<Outline>();outline.effectColor=selected?ThemeColorParser.ParseOrWhite(theme.Palette.Playable):ThemeColorParser.ParseOrWhite(theme.Palette.Border);outline.effectDistance=selected?new Vector2(4,-4):new Vector2(2,-2);
      var button=option.AddComponent<Button>();button.onClick.AddListener(()=>controller.SelectStarterHero(heroId));
      var artNode=Node(option.transform,"Hero Portrait",new Vector2(.025f,.04f),new Vector2(.39f,.96f));var art=artNode.AddComponent<RawImage>();art.texture=Resources.Load<Texture2D>(artResource);art.color=art.texture==null?new Color(.12f,.22f,.36f):Color.white;art.uvRect=new Rect(0,.12f,1,.72f);art.raycastTarget=false;
      var heroTitle=Label(option.transform,title,20,FontStyle.Bold,TextAnchor.MiddleLeft,ThemeColorParser.ParseOrWhite(theme.Palette.TextPrimary));Stretch(heroTitle.rectTransform,new Vector2(.43f,.54f),new Vector2(.96f,.9f));heroTitle.raycastTarget=false;
      var detail=Label(option.transform,subtitle,11,FontStyle.Bold,TextAnchor.UpperLeft,selected?ThemeColorParser.ParseOrWhite(theme.Palette.Playable):new Color(.7f,.76f,.84f));Stretch(detail.rectTransform,new Vector2(.43f,.2f),new Vector2(.96f,.58f));detail.resizeTextForBestFit=true;detail.resizeTextMinSize=8;detail.raycastTarget=false;
      var marker=Label(option.transform,selected?"✓  "+L("SELECTED","ВЫБРАН"):L("CHOOSE","ВЫБРАТЬ"),11,FontStyle.Bold,TextAnchor.MiddleLeft,selected?new Color(1f,.72f,.14f):new Color(.56f,.62f,.7f));Stretch(marker.rectTransform,new Vector2(.43f,.03f),new Vector2(.96f,.23f));marker.raycastTarget=false;
    }
    private GameObject Row(Transform p){var n=Node(p,"Button Row",Vector2.zero,Vector2.one);var l=n.AddComponent<HorizontalLayoutGroup>();l.spacing=16;l.childForceExpandWidth=true;l.childControlWidth=true;l.childForceExpandHeight=false;l.childControlHeight=true;l.childAlignment=TextAnchor.MiddleCenter;var e=n.AddComponent<LayoutElement>();e.preferredHeight=56;e.minHeight=52;e.flexibleHeight=0;return n;}
    private Transform ScrollList(Transform p,int height,GameTheme theme){var viewport=Node(p,"Scroll View",Vector2.zero,Vector2.one);viewport.AddComponent<Image>().color=new Color(0.02f,0.03f,0.05f,.72f);viewport.AddComponent<Mask>().showMaskGraphic=true;var e=viewport.AddComponent<LayoutElement>();e.preferredHeight=height;e.flexibleHeight=1;var contentNode=Node(viewport.transform,"Content",new Vector2(0,1),new Vector2(1,1));var rt=contentNode.GetComponent<RectTransform>();rt.pivot=new Vector2(.5f,1);var layout=contentNode.AddComponent<VerticalLayoutGroup>();layout.spacing=8;layout.padding=new RectOffset(12,12,12,12);layout.childControlHeight=true;layout.childControlWidth=true;layout.childForceExpandHeight=false;var fitter=contentNode.AddComponent<ContentSizeFitter>();fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;var scroll=viewport.AddComponent<ScrollRect>();scroll.viewport=viewport.GetComponent<RectTransform>();scroll.content=rt;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;return contentNode.transform;}
    private Transform ScrollGrid(Transform p,int height,GameTheme theme,int cellWidth,int cellHeight){var viewport=Node(p,"Card Grid View",Vector2.zero,Vector2.one);var background=viewport.AddComponent<Image>();var color=ThemeColorParser.ParseOrWhite(theme.Palette.Background);color.a=.78f;background.color=color;viewport.AddComponent<Mask>().showMaskGraphic=true;var e=viewport.AddComponent<LayoutElement>();e.preferredHeight=height;e.flexibleHeight=1;var contentNode=Node(viewport.transform,"Card Grid",new Vector2(0,1),new Vector2(1,1));var rt=contentNode.GetComponent<RectTransform>();rt.pivot=new Vector2(.5f,1);var grid=contentNode.AddComponent<GridLayoutGroup>();grid.padding=new RectOffset(14,14,14,14);grid.spacing=new Vector2(14,14);grid.cellSize=new Vector2(cellWidth,cellHeight);grid.constraint=GridLayoutGroup.Constraint.Flexible;grid.childAlignment=TextAnchor.UpperCenter;var fitter=contentNode.AddComponent<ContentSizeFitter>();fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;var scroll=viewport.AddComponent<ScrollRect>();scroll.viewport=viewport.GetComponent<RectTransform>();scroll.content=rt;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Elastic;scroll.scrollSensitivity=34;return contentNode.transform;}
    private GameObject ListRow(Transform p,GameTheme theme){var n=Node(p,"List Item",Vector2.zero,Vector2.one);var color=ThemeColorParser.ParseOrWhite(theme.Palette.PanelRaised);color.a=.82f;n.AddComponent<Image>().color=color;var l=n.AddComponent<HorizontalLayoutGroup>();l.padding=new RectOffset(18,12,8,8);l.spacing=10;l.childAlignment=TextAnchor.MiddleCenter;l.childControlHeight=true;l.childControlWidth=true;l.childForceExpandWidth=false;var e=n.AddComponent<LayoutElement>();e.preferredHeight=96;e.minHeight=88;return n;}
    private static void CardArt(Transform parent, PlayerApplicationController.CollectionUiCard card){var art=ResolveCardArt(card);var node=Node(parent,"Card Art",Vector2.zero,Vector2.one);var raw=node.AddComponent<RawImage>();raw.texture=art;raw.color=art==null?new Color(.12f,.15f,.22f,1):Color.white;raw.uvRect=new Rect(0,0,1,1);var e=node.AddComponent<LayoutElement>();e.preferredWidth=80;e.minWidth=80;e.preferredHeight=80;}
    private static void CardArtLarge(Transform parent, PlayerApplicationController.CollectionUiCard card, int height,Action preview=null){var art=ResolveCardArt(card);var node=Node(parent,"Card Illustration",Vector2.zero,Vector2.one);var raw=node.AddComponent<RawImage>();raw.texture=art;raw.color=art==null?new Color(.12f,.15f,.22f,1):Color.white;raw.uvRect=new Rect(0,0,1,1);var e=node.AddComponent<LayoutElement>();e.preferredHeight=height;e.minHeight=height;if(preview!=null){var button=node.AddComponent<Button>();button.onClick.AddListener(()=>preview());}}
    private static void CardArtAnchored(Transform parent, PlayerApplicationController.CollectionUiCard card, Vector2 min, Vector2 max,Action preview=null){var node=Node(parent,"Card Illustration",min,max);var raw=node.AddComponent<RawImage>();raw.texture=ResolveCardArt(card);raw.color=raw.texture==null?new Color(.12f,.15f,.22f,1):Color.white;raw.uvRect=new Rect(0,0,1,1);if(raw.texture!=null){var aspect=node.AddComponent<AspectRatioFitter>();aspect.aspectMode=AspectRatioFitter.AspectMode.FitInParent;aspect.aspectRatio=raw.texture.width/(float)Math.Max(1,raw.texture.height);}if(preview!=null){var button=node.AddComponent<Button>();button.onClick.AddListener(()=>preview());var hint=Label(node.transform,"VIEW",10,FontStyle.Bold,TextAnchor.LowerRight,Color.white);Stretch(hint.rectTransform,new Vector2(.05f,.04f),new Vector2(.93f,.28f));var shadow=hint.gameObject.AddComponent<Shadow>();shadow.effectColor=Color.black;shadow.effectDistance=new Vector2(2,-2);}}
    private static Texture2D ResolveCardArt(PlayerApplicationController.CollectionUiCard card)
    {
      var art=card.Art!=null?card.Art:Resources.Load<Texture2D>("CardArt/"+card.CardId);
      if(art==null&&card.VisualAssetRef.StartsWith("procedural://",StringComparison.Ordinal))
      {
        art=Resources.Load<Texture2D>(PackagedArtResourceFor(card.CardId,card.VisualAssetRef));
      }
      return art??ProceduralArt(card.CardId);
    }
    public static string PackagedArtResourceFor(string cardId,string visualAssetRef)
    {
      if(string.IsNullOrWhiteSpace(visualAssetRef)||!visualAssetRef.StartsWith("procedural://",StringComparison.Ordinal))return string.Empty;
      var suffix=(cardId??string.Empty).Split('_').LastOrDefault();
      var index=int.TryParse(suffix,out var ordinal)&&ordinal>0?(ordinal-1)%CuratedOfflineArt.Length:(StableHash(cardId)&int.MaxValue)%CuratedOfflineArt.Length;
      return "CardArt/"+CuratedOfflineArt[index];
    }
    public static string PresentedCardName(string cardId,string displayName)
    {
      if(!string.IsNullOrWhiteSpace(displayName)&&!displayName.StartsWith("Runesmith",StringComparison.OrdinalIgnoreCase))return displayName;
      var seed=string.IsNullOrWhiteSpace(cardId)?displayName:cardId;var suffix=(seed??string.Empty).Split('_').LastOrDefault();int index;
      if(int.TryParse(suffix,out var ordinal)&&ordinal>0)index=(ordinal-1)%CuratedLegacyNames.Length;
      else{var hash=17;foreach(var ch in seed??string.Empty)hash=unchecked(hash*31+ch);index=Math.Abs(hash==int.MinValue?0:hash)%CuratedLegacyNames.Length;}
      return CuratedLegacyNames[index];
    }
    private static int StableHash(string value){unchecked{var hash=23;foreach(var ch in value??string.Empty)hash=hash*31+ch;return hash;}}
    private static Texture2D ProceduralArt(string seed){if(ProceduralArtCache.TryGetValue(seed,out var cached))return cached;var hash=seed.Aggregate(17,(value,ch)=>unchecked(value*31+ch));var a=Color.HSVToRGB(Mathf.Abs(hash%360)/360f,.72f,.78f);var b=Color.HSVToRGB(Mathf.Abs((hash/7+130)%360)/360f,.82f,.34f);var texture=new Texture2D(64,64,TextureFormat.RGBA32,false){name="Procedural Card Art "+seed,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};for(var y=0;y<64;y++)for(var x=0;x<64;x++){var radial=Mathf.Clamp01(Vector2.Distance(new Vector2(x,y),new Vector2(32,30))/45f);var rune=((x+y+Mathf.Abs(hash))%17<2||(x-y+Mathf.Abs(hash))%23<2)?0.24f:0f;texture.SetPixel(x,y,Color.Lerp(a,b,radial)+new Color(rune,rune*.72f,rune*.2f,0));}texture.Apply(false,true);ProceduralArtCache[seed]=texture;return texture;}
    private void ProgressBar(Transform p,int percent,GameTheme theme){var track=Node(p,"Progress Track",Vector2.zero,Vector2.one);track.AddComponent<Image>().color=new Color(.03f,.04f,.06f,.9f);var e=track.AddComponent<LayoutElement>();e.preferredHeight=28;var fill=Node(track.transform,"Progress Fill",Vector2.zero,new Vector2(Mathf.Clamp01(percent/100f),1));fill.AddComponent<Image>().color=ThemeColorParser.ParseOrWhite(theme.Palette.Playable);}
    private void EmptyState(Transform p,string icon,string title,string subtitle,GameTheme theme){var n=Node(p,"Empty State",Vector2.zero,Vector2.one);var image=n.AddComponent<Image>();image.color=new Color(.025f,.04f,.07f,.76f);var outline=n.AddComponent<Outline>();outline.effectColor=ThemeColorParser.ParseOrWhite(theme.Palette.Border);outline.effectDistance=new Vector2(1,-1);var layout=n.AddComponent<VerticalLayoutGroup>();layout.padding=new RectOffset(30,30,18,18);layout.spacing=4;layout.childAlignment=TextAnchor.MiddleCenter;layout.childControlHeight=true;layout.childControlWidth=true;layout.childForceExpandHeight=false;var e=n.AddComponent<LayoutElement>();e.preferredHeight=156;e.minHeight=140;var glyph=Label(n.transform,icon,30,FontStyle.Bold,TextAnchor.MiddleCenter,ThemeColorParser.ParseOrWhite(theme.Palette.Playable));glyph.gameObject.AddComponent<LayoutElement>().preferredHeight=42;var head=Label(n.transform,title,18,FontStyle.Bold,TextAnchor.MiddleCenter,Color.white);head.gameObject.AddComponent<LayoutElement>().preferredHeight=30;var copy=Label(n.transform,subtitle,14,FontStyle.Normal,TextAnchor.MiddleCenter,new Color(.68f,.76f,.84f));copy.gameObject.AddComponent<LayoutElement>().preferredHeight=42;}
    private InputField Input(Transform p,string value,Action<string> changed,bool multiline,bool password=false){var n=Node(p,"Input",Vector2.zero,Vector2.one);var img=n.AddComponent<Image>();img.color=new Color(.04f,.06f,.10f,.92f);var f=n.AddComponent<InputField>();var txt=Label(n.transform,value,18,FontStyle.Normal,TextAnchor.MiddleLeft,Color.white);Stretch(txt.rectTransform,new Vector2(.025f,.08f),new Vector2(.975f,.92f));f.textComponent=txt;f.text=value;f.lineType=multiline?InputField.LineType.MultiLineNewline:InputField.LineType.SingleLine;f.contentType=password?InputField.ContentType.Password:InputField.ContentType.Standard;f.onValueChanged.AddListener(v=>changed(v));var e=n.AddComponent<LayoutElement>();e.preferredHeight=multiline?100:54;return f;}
    private Text Title(Transform p,string value){var t=Label(p,value,32,FontStyle.Bold,TextAnchor.MiddleCenter,Color.white);var e=t.gameObject.AddComponent<LayoutElement>();e.preferredHeight=54;return t;}
    private Text Body(Transform p,string value){var t=Label(p,value,18,FontStyle.Normal,TextAnchor.MiddleLeft,Color.white);var e=t.gameObject.AddComponent<LayoutElement>();e.preferredHeight=54;return t;}
    private static Text Label(Transform p,string value,int size,FontStyle style,TextAnchor anchor,Color color){var n=new GameObject("Text");n.transform.SetParent(p,false);var t=n.AddComponent<Text>();t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.text=value;t.fontSize=size;t.fontStyle=style;t.alignment=anchor;t.color=color;t.resizeTextForBestFit=false;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Truncate;return t;}
    private static GameObject Node(Transform p,string name,Vector2 min,Vector2 max){var n=new GameObject(name);n.transform.SetParent(p,false);var r=n.AddComponent<RectTransform>();Stretch(r,min,max);return n;}
    private static void Stretch(RectTransform r,Vector2 min,Vector2 max){r.anchorMin=min;r.anchorMax=max;r.offsetMin=Vector2.zero;r.offsetMax=Vector2.zero;}
    private string L(string english,string russian)=>localization.Locale=="ru"?russian:english;
    private static string PrettyCardName(string cardId){if(string.IsNullOrWhiteSpace(cardId))return "Unknown Card";var parts=cardId.Split('-').Where(x=>!string.IsNullOrWhiteSpace(x)&&x!="journey").TakeLast(4).ToArray();return string.Join(" ",parts.Select(x=>int.TryParse(x,out var number)?"#"+number.ToString("00"):char.ToUpperInvariant(x[0])+x.Substring(1)));}
    private static void EnsureEventSystem()
    {
      if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
      var e = new GameObject("EventSystem"); e.AddComponent<EventSystem>();
      var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
      if (inputModuleType != null) e.AddComponent(inputModuleType); else e.AddComponent<StandaloneInputModule>();
      UnityEngine.Object.DontDestroyOnLoad(e);
    }
  }
}
