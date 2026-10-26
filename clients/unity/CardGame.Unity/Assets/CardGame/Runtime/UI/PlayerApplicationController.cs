using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using CardGame.Unity.Client.Api;
using CardGame.Unity.Client.Config;
using Newtonsoft.Json;
using CardGame.Unity.Client.Match;
using CardGame.Unity.Client.DebugTools;
using UnityEngine;

namespace CardGame.Unity.Client.UI
{
  public enum PlayerRoute { Bootstrap, Login, Register, MainMenu, CardGeneration, Collection, DeckBuilder, DeckSelection, Play, Gameplay, Results, Replay, Settings, Profile }

  public sealed class PlayerApplicationController : MonoBehaviour
  {
    private const string TokenKey = "cardgame.player.access-token";
    private readonly List<CollectionCardDto> collection = new();
    private readonly List<PlayerDeckDto> decks = new();
    private readonly Dictionary<string, int> deckDraft = new(StringComparer.Ordinal);
    private GUIStyle titleStyle;
    private GUIStyle bodyStyle;
    private GUIStyle buttonStyle;
    private BackendApiClient api;
    private PlayerApplicationView productionView;
    private LocalizationService localization;
    private PlayerRoute route = PlayerRoute.Bootstrap;
    private string accessToken = "";
    private string playerId = "";
    private string email = "player@example.test";
    private string password = "ReleaseReady!123";
    private string displayName = "Astra";
    private string starterHeroId = "astral-warden";
    private string prompt = "astral clockwork guardians";
    private string status = "Connecting to the Chronicle service…";
    private string providerMode = "local";
    private string collectionSearch = "";
    private bool favoritesOnly;
    private string deckName = "Astral Vanguard";
    private string editingDeckId = "";
    private string activeDeckId = "";
    private string generationJobId = "";
    private int generationProgress;
    private string generationState = "idle";
    private float musicVolume = .65f;
    private float effectsVolume = .80f;
    private bool reducedMotion;
    private Vector2 scroll;
    private bool busy;
    private static readonly Vector2Int[] SupportedResolutions = { new(1280,720),new(1600,900),new(1920,1080),new(2560,1440),new(3840,2160) };
    private int resolutionIndex;
    private int displayModeIndex;
    private bool returnToActiveMatch;

    public PlayerRoute Route => route;
    public bool ReleaseDebugControlsVisible => Debug.isDebugBuild;
    public string Email { get => email; set => email = value ?? ""; }
    public string Password { get => password; set => password = value ?? ""; }
    public string DisplayName { get => displayName; set => displayName = value ?? ""; }
    public string StarterHeroId => starterHeroId;
    public string StarterHeroName => starterHeroId=="cinder-vanguard" ? MatchText("Varg, Cinder Vanguard","Варг, Пламенный авангард") : MatchText("Astra, Astral Warden","Астра, Звёздный страж");
    public void SelectNextStarterHero(){starterHeroId=starterHeroId=="astral-warden"?"cinder-vanguard":"astral-warden";productionView?.Invalidate();}
    public void SelectStarterHero(string heroId){starterHeroId=heroId=="cinder-vanguard"?"cinder-vanguard":"astral-warden";productionView?.Invalidate();}
    public string Prompt { get => prompt; set => prompt = value ?? ""; }
    public string ProviderMode { get => providerMode; set => providerMode = value == "openai" ? "openai" : "local"; }
    public string ThemeId => PlayerPrefs.GetString("cardgame.board-theme", "arcane");
    public string StatusText => status;
    public string LocalizedStatusText => LocalizeStatus(status);
    public bool StatusIsError => status.StartsWith("ERROR:",StringComparison.Ordinal);
    public string CollectionSearch { get => collectionSearch; set => collectionSearch = value ?? ""; }
    public bool FavoritesOnly => favoritesOnly;
    public string DeckName { get => deckName; set => deckName = value ?? ""; }
    public int DeckCardCount => deckDraft.Values.Sum();
    public string ActiveDeckId => activeDeckId;
    public string EditingDeckId => editingDeckId;
    public int GenerationProgress => generationProgress;
    public string GenerationState => generationState;
    public float MusicVolume => musicVolume;
    public float EffectsVolume => effectsVolume;
    public bool ReducedMotion => reducedMotion;
    public string ResolutionLabel => $"{SupportedResolutions[resolutionIndex].x} × {SupportedResolutions[resolutionIndex].y}";
    public string DisplayModeLabel => displayModeIndex switch { 0=>MatchText("FULLSCREEN","НА ВЕСЬ ЭКРАН"),1=>MatchText("BORDERLESS","БЕЗ РАМОК"),_=>MatchText("WINDOWED","В ОКНЕ") };
    public IReadOnlyList<CollectionUiCard> CollectionCards => collection.Select(x => new CollectionUiCard(x.CardId, x.CardVersionId, x.Count, x.Favorite, deckDraft.GetValueOrDefault(x.CardVersionId), x.DisplayName, x.CardType, x.Rarity, x.ManaCost, x.Attack, x.Health, x.Durability, x.ArmorGain, x.RulesText, x.VisualAssetRef, x.Art)).ToArray();
    public IReadOnlyList<DeckUiEntry> Decks => decks.Select(x => new DeckUiEntry(x.DeckId, x.Name, x.Cards?.Sum(c => c.Count) ?? 0, x.DeckId == activeDeckId)).ToArray();
    public string DeckStatus => $"{DeckCardCount} / 30 · {deckName}";
    public string ProfileSummary => $"{displayName}\n{email}";
    public string PlayerDisplayName => displayName;
    public string PlayerEmail => email;
    public int CollectionCount => collection.Count;
    public int DeckCount => decks.Count;
    public string ActiveDeckName => decks.FirstOrDefault(x => x.DeckId == activeDeckId)?.Name ?? string.Empty;
    public int ActiveDeckCardCount => decks.FirstOrDefault(x => x.DeckId == activeDeckId)?.Cards?.Sum(x => x.Count) ?? 0;
    public string LastCompletedMatchId => DebugMatchBootstrapper.LastCompletedMatchId;
    public string LastTerminalReason => DebugMatchBootstrapper.LastTerminalReason;
    public int LastCompletedCommandCount => DebugMatchBootstrapper.LastCompletedCommandCount;
    public bool LastReplayVerified => DebugMatchBootstrapper.LastReplayVerified;
    public bool ReturnToActiveMatch => returnToActiveMatch;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsurePlayerApplication()
    {
      if (FindFirstObjectByType<PlayerApplicationController>() != null) return;
      var root = new GameObject("PlayerApplicationController");
      DontDestroyOnLoad(root);
      root.AddComponent<PlayerApplicationController>();
    }

    private async void Awake()
    {
      Application.runInBackground = true;
      InitializeDisplaySettings();
      api = new BackendApiClient(UnityBackendConfig.CreateRuntimeDefault());
      localization = new LocalizationService();
      productionView = new PlayerApplicationView(this, localization);
      productionView.Build();
      DebugMatchBootstrapper.SetGameplayVisible(false);
      DebugMatchBootstrapper.ProductionMatchCompleted += HandleProductionMatchCompleted;
      DebugMatchBootstrapper.GameplayMainMenuRequested += HandleGameplayMainMenuRequested;
      DebugMatchBootstrapper.GameplaySettingsRequested += HandleGameplaySettingsRequested;
      accessToken = PlayerPrefs.GetString(TokenKey, "");
      var health = await api.GetJsonAsync<object>("/health");
      if (!health.Succeeded) { status = "Server unavailable. Check that Chronicle Server is running."; route = PlayerRoute.Bootstrap; return; }
      await DebugMatchBootstrapper.PrepareProductionContentAsync();
      if (Environment.GetCommandLineArgs().Contains("--capture-ui", StringComparer.Ordinal))
      {
        await CaptureUiReference();
        return;
      }
      if (Environment.GetCommandLineArgs().Contains("--record-full-match-evidence", StringComparer.Ordinal))
      {
        await RunRecordedInteractiveMatchEvidence();
        return;
      }
      if (Environment.GetCommandLineArgs().Contains("--e2e-player-journey", StringComparer.Ordinal))
      {
        await RunPackagedJourneyAutomation();
        return;
      }
      var restoredPlayer = !string.IsNullOrWhiteSpace(accessToken) ? await api.GetJsonAsync<PlayerViewDto>("/me", accessToken) : null;
      if (restoredPlayer?.Succeeded == true)
      {
        ApplyPlayerView(restoredPlayer.Value);
        status = "Session restored."; route = PlayerRoute.MainMenu;
      }
      else { ClearSession(); status = "Welcome, Wayfinder."; route = PlayerRoute.Login; }
    }

    private void Update() => productionView?.Tick();

    private void OnDestroy() { DebugMatchBootstrapper.ProductionMatchCompleted -= HandleProductionMatchCompleted; DebugMatchBootstrapper.GameplayMainMenuRequested -= HandleGameplayMainMenuRequested; DebugMatchBootstrapper.GameplaySettingsRequested -= HandleGameplaySettingsRequested; }
    private void HandleGameplayMainMenuRequested()=>Navigate(PlayerRoute.MainMenu);
    private void HandleGameplaySettingsRequested(){returnToActiveMatch=true;Navigate(PlayerRoute.Settings);}
    public void ReturnToMatch(){returnToActiveMatch=false;Navigate(PlayerRoute.Gameplay);}

    private void LegacyOnGUI()
    {
      EnsureStyles();
      if (route == PlayerRoute.Gameplay)
      {
        if (GUI.Button(new Rect(Screen.width - 230, 18, 210, 46), "MATCH RESULTS", buttonStyle)) route = PlayerRoute.Results;
        return;
      }
      var width = Mathf.Min(1120, Screen.width - 64);
      var height = Mathf.Min(760, Screen.height - 54);
      var rect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
      GUI.color = new Color(0.035f, 0.045f, 0.075f, 0.97f);
      GUI.Box(rect, GUIContent.none);
      GUI.color = Color.white;
      GUILayout.BeginArea(new Rect(rect.x + 30, rect.y + 24, rect.width - 60, rect.height - 48));
      Header();
      switch (route)
      {
        case PlayerRoute.Bootstrap: Bootstrap(); break;
        case PlayerRoute.Login: Login(false); break;
        case PlayerRoute.Register: Login(true); break;
        case PlayerRoute.MainMenu: MainMenu(); break;
        case PlayerRoute.CardGeneration: CardGeneration(); break;
        case PlayerRoute.Collection: Collection(); break;
        case PlayerRoute.DeckBuilder: DeckBuilder(); break;
        case PlayerRoute.DeckSelection: DeckSelection(); break;
        case PlayerRoute.Play: Play(); break;
        case PlayerRoute.Gameplay: Gameplay(); break;
        case PlayerRoute.Results: Results(); break;
        case PlayerRoute.Replay: Replay(); break;
        case PlayerRoute.Settings: Settings(); break;
        case PlayerRoute.Profile: Profile(); break;
      }
      GUILayout.FlexibleSpace();
      GUILayout.Label(status, bodyStyle);
      GUILayout.EndArea();
    }

    private void Header()
    {
      GUILayout.BeginHorizontal();
      GUILayout.Label("CHRONICLE FORGE", titleStyle);
      GUILayout.FlexibleSpace();
      GUILayout.Label(route.ToString().ToUpperInvariant(), bodyStyle);
      if (route is not (PlayerRoute.Login or PlayerRoute.Register or PlayerRoute.Bootstrap) && GUILayout.Button("MAIN HALL", buttonStyle, GUILayout.Width(150))) route = PlayerRoute.MainMenu;
      GUILayout.EndHorizontal();
      GUILayout.Space(18);
    }

    private void Bootstrap()
    {
      GUILayout.Label("The archive is waking", titleStyle); GUILayout.Label("Checking server, catalog, ruleset and presentation assets.", bodyStyle);
      if (GUILayout.Button("RETRY CONNECTION", buttonStyle)) _ = RetryBoot();
    }

    private async Task RetryBoot()
    {
      busy = true; var health = await api.GetJsonAsync<object>("/health"); busy = false;
      route = health.Succeeded ? PlayerRoute.Login : PlayerRoute.Bootstrap; status = health.Succeeded ? "Service online." : "Connection failed.";
    }

    private void Login(bool registering)
    {
      GUILayout.Label(registering ? "Create your Wayfinder" : "Return to the tavern", titleStyle);
      GUILayout.Label("Email", bodyStyle); email = GUILayout.TextField(email, 128);
      GUILayout.Label("Password (12+ characters)", bodyStyle); password = GUILayout.PasswordField(password, '•', 128);
      if (registering) { GUILayout.Label("Display name", bodyStyle); displayName = GUILayout.TextField(displayName, 32); }
      GUI.enabled = !busy;
      if (GUILayout.Button(registering ? "CREATE ACCOUNT" : "ENTER", buttonStyle)) _ = Authenticate(registering);
      GUI.enabled = true;
      if (GUILayout.Button(registering ? "I ALREADY HAVE AN ACCOUNT" : "CREATE ACCOUNT", buttonStyle)) route = registering ? PlayerRoute.Login : PlayerRoute.Register;
    }

    private async Task Authenticate(bool register)
    {
      var validation=ValidateCredentials(register);
      if(!string.IsNullOrEmpty(validation)){status="ERROR:"+validation;productionView?.Invalidate();return;}
      busy = true; status = register ? "Creating account…" : "Signing in…";
      var result = register
        ? await api.PostJsonAsync<AuthDto>("/auth/register", new { email, password, displayName, starterHeroId })
        : await api.PostJsonAsync<AuthDto>("/auth/login", new { email, password });
      busy = false;
      if (!result.Succeeded) { status="ERROR:"+AuthErrorMessage(result.Error?.Code);productionView?.Invalidate();return; }
      accessToken = result.Value.AccessToken; playerId = result.Value.PlayerId; PlayerPrefs.SetString(TokenKey, accessToken); PlayerPrefs.Save();
      var player = await api.GetJsonAsync<PlayerViewDto>("/me", accessToken); if (player.Succeeded) ApplyPlayerView(player.Value);
      status = $"Welcome, {displayName}."; route = PlayerRoute.MainMenu;
    }

    private string ValidateCredentials(bool register)
    {
      if(string.IsNullOrWhiteSpace(email)||!email.Contains("@")||email.Contains(" "))return MatchText("Enter a valid email address.","Введите корректный адрес электронной почты.");
      if(string.IsNullOrEmpty(password)||password.Length<12)return MatchText("Password must contain at least 12 characters.","Пароль должен содержать не менее 12 символов.");
      if(register&&string.IsNullOrWhiteSpace(displayName))return MatchText("Display name is required.","Укажите отображаемое имя.");
      if(register&&displayName.Trim().Length>32)return MatchText("Display name must not exceed 32 characters.","Имя не должно превышать 32 символа.");
      return string.Empty;
    }
    private string AuthErrorMessage(string code)=>code switch
    {
      "AUTH_INVALID_CREDENTIALS"=>MatchText("Email or password is incorrect.","Неверный адрес электронной почты или пароль."),
      "AUTH_EMAIL_EXISTS"=>MatchText("An account with this email already exists.","Аккаунт с таким адресом уже существует."),
      "AUTH_VALIDATION_FAILED"=>MatchText("Registration data does not satisfy the account rules.","Данные регистрации не соответствуют требованиям."),
      "RATE_LIMITED"=>MatchText("Too many attempts. Please wait and try again.","Слишком много попыток. Подождите и повторите."),
      "TRANSPORT_TIMEOUT"=>MatchText("The server did not respond in time.","Сервер не ответил вовремя."),
      _=>MatchText("Authentication failed. Check the entered data.","Не удалось выполнить вход. Проверьте введённые данные.")
    };

    private void MainMenu()
    {
      GUILayout.Label("Choose your path", titleStyle);
      Nav("FORGE CARDS", PlayerRoute.CardGeneration, "Create and acquire validated cards");
      Nav("COLLECTION", PlayerRoute.Collection, "Browse, search and favorite owned cards");
      Nav("DECK WORKSHOP", PlayerRoute.DeckBuilder, "Build an authoritative 30-card deck");
      Nav("PLAY VS BOT", PlayerRoute.DeckSelection, "Select a deck and enter the board");
      Nav("REPLAYS", PlayerRoute.Replay, "Review verified command-log replays");
      Nav("SETTINGS", PlayerRoute.Settings, "Audio, motion and board theme");
      Nav("PROFILE", PlayerRoute.Profile, "Identity and account settings");
      if (GUILayout.Button("LOG OUT", buttonStyle)) { _ = api.PostJsonAsync<object>("/auth/logout", new { }, accessToken); ClearSession(); route = PlayerRoute.Login; status = "Signed out."; }
    }

    private void Nav(string label, PlayerRoute target, string subtitle)
    {
      GUILayout.BeginHorizontal(); if (GUILayout.Button(label, buttonStyle, GUILayout.Width(250))) { route = target; if (target == PlayerRoute.Collection) _ = LoadCollection(); } GUILayout.Label(subtitle, bodyStyle); GUILayout.EndHorizontal();
    }

    private void CardGeneration()
    {
      GUILayout.Label("Card Forge", titleStyle); GUILayout.Label("Prompt", bodyStyle); prompt = GUILayout.TextArea(prompt, GUILayout.Height(80));
      GUILayout.BeginHorizontal(); if (GUILayout.Button("LOCAL", buttonStyle)) providerMode = "local"; if (GUILayout.Button("OPENAI CONNECTOR", buttonStyle)) providerMode = "openai"; GUILayout.EndHorizontal();
      GUILayout.Label($"Provider: {providerMode}. Generation occurs before matches and never affects match state.", bodyStyle);
      GUI.enabled = !busy; if (GUILayout.Button("GENERATE & ACQUIRE 15 CARDS", buttonStyle)) _ = Generate(); GUI.enabled = true;
    }

    private async Task Generate(int count = 15)
    {
      busy = true; status = "Generating, validating and publishing…";
      if (!await DebugMatchBootstrapper.PrepareProductionContentAsync()) { busy = false; generationState = "failed"; status = "Production catalog is unavailable."; productionView?.Invalidate(); return; }
      generationState = "running"; generationProgress = 5; productionView?.Invalidate();
      var result = await api.PostJsonAsync<GenerationJobDto>("/player/generation/jobs", new { prompt, providerMode, count }, accessToken);
      if (!result.Succeeded) { busy = false; generationState = "failed"; generationProgress = 0; status = result.Error?.Message ?? "Generation failed."; productionView?.Invalidate(); return; }
      generationJobId = result.Value.JobId; generationState = result.Value.Status ?? "running"; generationProgress = result.Value.ProgressPercent;
      await PollGenerationJobAsync();
    }

    private async Task PollGenerationJobAsync()
    {
      for (var attempt = 0; attempt < 2400 && generationState is not ("completed" or "failed" or "cancelled"); attempt++)
      {
        await Task.Delay(500);
        var result = await api.GetJsonAsync<GenerationJobDto>($"/player/generation/jobs/{Uri.EscapeDataString(generationJobId)}", accessToken);
        if (!result.Succeeded) { generationState = "failed"; status = result.Error?.Message ?? "Generation status unavailable."; break; }
        generationState = result.Value.Status; generationProgress = result.Value.ProgressPercent;
        if (generationState == "failed" && !string.IsNullOrWhiteSpace(result.Value.ErrorCode)) status = MatchText("Generation failed: " + result.Value.ErrorCode, "Ошибка генерации: " + result.Value.ErrorCode);
        productionView?.Invalidate();
      }
      busy = false;
      if (generationState == "completed") { status = "Approved cards added to collection."; await LoadCollection(); Navigate(PlayerRoute.Collection); }
      else if (generationState != "failed") { status = MatchText("Generation did not complete. Retry is available.", "Генерация не завершилась. Доступен повторный запуск."); productionView?.Invalidate(); }
    }

    private async Task RetryGenerationAsync()
    {
      if (string.IsNullOrWhiteSpace(generationJobId)) { await Generate(); return; }
      busy = true;
      var result = await api.PostJsonAsync<GenerationJobDto>($"/player/generation/jobs/{Uri.EscapeDataString(generationJobId)}/retry", new { }, accessToken);
      if (!result.Succeeded) { busy = false; status = result.Error?.Message ?? "Generation retry failed."; productionView?.Invalidate(); return; }
      generationState = result.Value.Status; generationProgress = result.Value.ProgressPercent; status = "Generation retry queued."; productionView?.Invalidate();
      await PollGenerationJobAsync();
    }

    private async Task CancelGenerationAsync()
    {
      if (string.IsNullOrWhiteSpace(generationJobId)) return;
      var result = await api.PostJsonAsync<GenerationJobDto>($"/player/generation/jobs/{Uri.EscapeDataString(generationJobId)}/cancel", new { }, accessToken);
      if (result.Succeeded) { generationState = "cancelled"; generationProgress = 0; busy = false; status = "Generation cancelled."; }
      else status = result.Error?.Message ?? "Generation cancellation failed.";
      productionView?.Invalidate();
    }

    private void Collection()
    {
      GUILayout.Label($"Collection · {collection.Count} card versions", titleStyle);
      scroll = GUILayout.BeginScrollView(scroll);
      foreach (var card in collection) GUILayout.Label($"◆ {card.CardId}   copies {card.Count}   {(card.Favorite ? "★" : "☆")}", bodyStyle);
      GUILayout.EndScrollView();
      if (collection.Count == 0) GUILayout.Label("Your collection is empty. Visit the Card Forge.", bodyStyle);
      if (GUILayout.Button("REFRESH", buttonStyle)) _ = LoadCollection();
    }

    private async Task LoadCollection()
    {
      var query = $"?search={Uri.EscapeDataString(collectionSearch)}" + (favoritesOnly ? "&favorite=true" : "");
      var result = await api.GetJsonAsync<CollectionCardDto[]>("/player/collection" + query, accessToken);
      collection.Clear(); if (result.Succeeded && result.Value != null) collection.AddRange(result.Value);
      foreach (var card in collection.Where(x => !string.IsNullOrWhiteSpace(x.VisualAssetRef) && !x.VisualAssetRef.StartsWith("procedural://", StringComparison.Ordinal)))
      {
        var art = await api.GetTextureAsync($"/player/card-assets/{Uri.EscapeDataString(card.CardVersionId)}", accessToken);
        if (art.Succeeded) card.Art = art.Value;
      }
      status = result.Succeeded ? "Collection synchronized." : "Could not load collection.";
      productionView?.Invalidate();
    }

    private void DeckBuilder()
    {
      GUILayout.Label("Deck Workshop", titleStyle); GUILayout.Label("Select 15 owned card versions × 2 copies. The server validates exactly 30 cards and copy limits.", bodyStyle);
      GUILayout.Label(collection.Count >= 15 ? "LEGAL DRAFT READY · 30 / 30" : $"NEED {15 - collection.Count} MORE CARD VERSIONS", bodyStyle);
      GUI.enabled = collection.Count >= 15 && !busy; if (GUILayout.Button("SAVE STARTER DECK", buttonStyle)) _ = SaveStarterDeck(); GUI.enabled = true;
    }

    private async Task SaveStarterDeck()
    {
      if (deckDraft.Count == 0 && collection.Count >= 15) foreach (var card in collection.Take(15)) deckDraft[card.CardVersionId] = Math.Min(2, card.Count);
      if (DeckCardCount != 30) { status = "A legal deck requires exactly 30 cards."; productionView?.Invalidate(); return; }
      busy = true;
      var byVersion = collection.ToDictionary(x => x.CardVersionId, StringComparer.Ordinal);
      var cards = deckDraft.Where(x => x.Value > 0 && byVersion.ContainsKey(x.Key)).Select(x => new { byVersion[x.Key].CardId, CardVersionId = x.Key, count = x.Value }).ToArray();
      var result = await api.PostJsonAsync<PlayerDeckDto>("/player/decks", new { deckId = string.IsNullOrWhiteSpace(editingDeckId) ? null : editingDeckId, name = deckName, cards }, accessToken);
      busy = false; status = result.Succeeded ? "Deck saved and validated." : result.Error?.Message ?? "Deck rejected.";
      if (result.Succeeded) { editingDeckId = result.Value.DeckId; await LoadDecks(); Navigate(PlayerRoute.DeckSelection); } else productionView?.Invalidate();
    }

    private void DeckSelection() { GUILayout.Label("Choose a battle deck", titleStyle); GUILayout.Label("Astral Vanguard · production ruleset · 30 cards", bodyStyle); if (GUILayout.Button("SELECT & CONTINUE", buttonStyle)) route = PlayerRoute.Play; }
    private void Play() { GUILayout.Label("Play", titleStyle); GUILayout.Label("A deterministic server-authoritative opponent is ready.", bodyStyle); if (GUILayout.Button("ENTER PLAY VS BOT", buttonStyle)) { route = PlayerRoute.Gameplay; status = "Use the visible board controls to complete the match."; } }
    private void Gameplay() { GUILayout.Label("Gameplay", titleStyle); GUILayout.Label("The full themed gameplay board is active behind this journey shell.", bodyStyle); if (GUILayout.Button("OPEN RESULTS AFTER MATCH", buttonStyle)) route = PlayerRoute.Results; }
    private void Results() { GUILayout.Label("Match complete", titleStyle); GUILayout.Label("Command log sealed · replay verification available · rewards recorded", bodyStyle); if (GUILayout.Button("WATCH REPLAY", buttonStyle)) route = PlayerRoute.Replay; }
    private void Replay() { GUILayout.Label("Verified Replays", titleStyle); GUILayout.Label("Playback source: canonical command log. Event visuals are derived presentation output.", bodyStyle); GUILayout.BeginHorizontal(); GUILayout.Button("◀"); GUILayout.Button("▶"); GUILayout.Button("Ⅱ"); GUILayout.Button("1×"); GUILayout.EndHorizontal(); }
    private void Settings() { GUILayout.Label("Settings", titleStyle); GUILayout.Label("Board theme", bodyStyle); GUILayout.BeginHorizontal(); ThemeButton("arcane"); ThemeButton("cyberpunk"); ThemeButton("zen"); GUILayout.EndHorizontal(); GUILayout.Label("Music and effects volumes are stored with the player profile.", bodyStyle); }
    private void ThemeButton(string theme) { if (GUILayout.Button(theme.ToUpperInvariant(), buttonStyle)) { PlayerPrefs.SetString("cardgame.board-theme", theme); PlayerPrefs.Save(); status = $"Theme {theme} selected."; } }
    private void Profile() { GUILayout.Label("Wayfinder Profile", titleStyle); GUILayout.Label($"Display name: {displayName}\nAccount: {email}\nSession: encrypted-token boundary active", bodyStyle); }
    private void ClearSession() { accessToken = ""; playerId = ""; PlayerPrefs.DeleteKey(TokenKey); PlayerPrefs.Save(); }

    public void Navigate(PlayerRoute target)
    {
      route = target;
      DebugMatchBootstrapper.SetGameplayVisible(target == PlayerRoute.Gameplay);
      productionView?.Invalidate();
      if (target == PlayerRoute.Collection) _ = LoadCollection();
      if (target is PlayerRoute.DeckBuilder or PlayerRoute.DeckSelection) _ = LoadDeckWorkspaceAsync();
    }

    public void SetLocale(string locale) => localization.SetLocale(locale);
    public void ToggleLocale() => localization.SetLocale(localization.Locale == "ru" ? "en" : "ru");
    public void SelectProviderMode(string mode)
    {
      providerMode = mode == "openai" ? "openai" : "local";
      status = providerMode == "openai"
        ? MatchText("OpenAI connector selected. Internet access and OPENAI_API_KEY are required.", "Выбран коннектор OpenAI. Требуются интернет и OPENAI_API_KEY.")
        : MatchText("Local deterministic generation selected. No downloaded model is required.", "Выбрана локальная детерминированная генерация. Загруженная модель не требуется.");
      productionView?.Invalidate();
    }
    public void SetTheme(string theme)
    {
      PlayerPrefs.SetString("cardgame.board-theme", theme); PlayerPrefs.Save(); productionView?.Invalidate(); status = MatchText("Theme applied.", "Тема применена.");
      if (!string.IsNullOrWhiteSpace(accessToken)) _ = SaveProfileSettingsAsync();
    }
    public void CycleMusicVolume() { musicVolume = NextVolume(musicVolume); AudioListener.volume = Mathf.Max(musicVolume, effectsVolume); _ = SaveProfileSettingsAsync(); productionView?.Invalidate(); }
    public void CycleEffectsVolume() { effectsVolume = NextVolume(effectsVolume); AudioListener.volume = Mathf.Max(musicVolume, effectsVolume); _ = SaveProfileSettingsAsync(); productionView?.Invalidate(); }
    public void ToggleReducedMotion() { reducedMotion = !reducedMotion; PlayerPrefs.SetInt("cardgame.reduced-motion", reducedMotion ? 1 : 0); PlayerPrefs.Save(); _ = SaveProfileSettingsAsync(); productionView?.Invalidate(); }
    public void CycleResolution() { resolutionIndex=(resolutionIndex+1)%SupportedResolutions.Length; ApplyDisplaySettings(); }
    public void CycleDisplayMode() { displayModeIndex=(displayModeIndex+1)%3; ApplyDisplaySettings(); }
    public void RetryConnection() => _ = RetryBoot();

    private void InitializeDisplaySettings()
    {
      resolutionIndex=Mathf.Clamp(PlayerPrefs.GetInt("cardgame.resolution-index",2),0,SupportedResolutions.Length-1);
      displayModeIndex=Mathf.Clamp(PlayerPrefs.GetInt("cardgame.display-mode",0),0,2);
      if(!Environment.GetCommandLineArgs().Any(arg=>arg.StartsWith("-screen-",StringComparison.OrdinalIgnoreCase)))ApplyDisplaySettings();
    }

    private void ApplyDisplaySettings()
    {
      var resolution=SupportedResolutions[resolutionIndex];
      var mode=displayModeIndex switch { 0=>FullScreenMode.ExclusiveFullScreen,1=>FullScreenMode.FullScreenWindow,_=>FullScreenMode.Windowed };
      Screen.SetResolution(resolution.x,resolution.y,mode);
      PlayerPrefs.SetInt("cardgame.resolution-index",resolutionIndex);PlayerPrefs.SetInt("cardgame.display-mode",displayModeIndex);PlayerPrefs.Save();
      productionView?.Invalidate();
    }
    public void AuthenticateFromUi(bool register) => _ = Authenticate(register);
    public void GenerateFromUi() => _ = Generate();
    public void RetryGenerationFromUi() => _ = RetryGenerationAsync();
    public void CancelGenerationFromUi() => _ = CancelGenerationAsync();
    public void RefreshCollection() => _ = LoadCollection();
    public void ApplyCollectionFilter() => _ = LoadCollection();
    public void ToggleFavoritesFilter() { favoritesOnly = !favoritesOnly; _ = LoadCollection(); }
    public void ToggleFavorite(string cardVersionId) => _ = ToggleFavoriteAsync(cardVersionId);
    public void AddDraftCard(string cardVersionId) { var owned = collection.FirstOrDefault(x => x.CardVersionId == cardVersionId); if (owned == null) return; var current = deckDraft.GetValueOrDefault(cardVersionId); if (current < Math.Min(2, owned.Count)) deckDraft[cardVersionId] = current + 1; productionView?.Invalidate(); }
    public void RemoveDraftCard(string cardVersionId) { var current = deckDraft.GetValueOrDefault(cardVersionId); if (current <= 1) deckDraft.Remove(cardVersionId); else deckDraft[cardVersionId] = current - 1; productionView?.Invalidate(); }
    public void NewDeck() { editingDeckId = ""; deckName = localization.Locale == "ru" ? "Новая колода" : "New Deck"; deckDraft.Clear(); productionView?.Invalidate(); }
    public void EditDeck(string deckId) { var deck = decks.FirstOrDefault(x => x.DeckId == deckId); if (deck == null) return; editingDeckId = deck.DeckId; deckName = deck.Name; deckDraft.Clear(); foreach (var card in deck.Cards ?? Array.Empty<DeckCardDto>()) deckDraft[card.CardVersionId] = card.Count; Navigate(PlayerRoute.DeckBuilder); }
    public void DeleteDeck(string deckId) => _ = DeleteDeckAsync(deckId);
    public void SelectDeck(string deckId) => _ = SelectDeckAsync(deckId);
    public void SaveDeckFromUi() => _ = SaveStarterDeck();
    public void StartMatchFromUi() => _ = StartMatchFromUiAsync();
    public void LogoutFromUi() { _ = api.PostJsonAsync<object>("/auth/logout", new { }, accessToken); ClearSession(); Navigate(PlayerRoute.Login); status = "Signed out."; }

    private async Task ToggleFavoriteAsync(string cardVersionId)
    {
      var card = collection.FirstOrDefault(x => x.CardVersionId == cardVersionId); if (card == null) return;
      var result = await api.PatchJsonAsync<CollectionCardDto>($"/player/collection/{Uri.EscapeDataString(cardVersionId)}/favorite", new { favorite = !card.Favorite }, accessToken);
      status = result.Succeeded ? "Favorite updated." : result.Error?.Message ?? "Favorite update failed."; await LoadCollection();
    }

    private async Task SaveProfileSettingsAsync()
    {
      if (string.IsNullOrWhiteSpace(accessToken)) return;
      var result = await api.PatchJsonAsync<PlayerViewDto>("/me", new { boardTheme = ThemeId, musicVolume, effectsVolume, reducedMotion }, accessToken);
      if (result.Succeeded) { ApplyPlayerView(result.Value); status = MatchText("Settings saved.", "Настройки сохранены."); }
      else status = result.Error?.Message ?? MatchText("Could not save settings.", "Не удалось сохранить настройки.");
      productionView?.Invalidate();
    }

    private void ApplyPlayerView(PlayerViewDto player)
    {
      if (player == null) return;
      playerId = player.PlayerId ?? playerId;
      if (player.Profile != null)
      {
        displayName = string.IsNullOrWhiteSpace(player.Profile.DisplayName) ? displayName : player.Profile.DisplayName;
        musicVolume = Mathf.Clamp01(player.Profile.MusicVolume);
        effectsVolume = Mathf.Clamp01(player.Profile.EffectsVolume);
        reducedMotion = player.Profile.ReducedMotion;
        if (!string.IsNullOrWhiteSpace(player.Profile.BoardTheme)) { PlayerPrefs.SetString("cardgame.board-theme", player.Profile.BoardTheme); PlayerPrefs.Save(); }
        PlayerPrefs.SetInt("cardgame.reduced-motion", reducedMotion ? 1 : 0); PlayerPrefs.Save();
      }
      AudioListener.volume = Mathf.Max(musicVolume, effectsVolume);
    }

    private static float NextVolume(float value) => value >= .99f ? 0f : Mathf.Clamp01(Mathf.Round((value + .25f) * 4f) / 4f);

    private async Task LoadDeckWorkspaceAsync()
    {
      collectionSearch = ""; favoritesOnly = false;
      await LoadCollection(); await LoadDecks();
      if (deckDraft.Count == 0 && decks.Count > 0)
      {
        var deck = decks.FirstOrDefault(x => x.DeckId == activeDeckId) ?? decks[0]; editingDeckId = deck.DeckId; deckName = deck.Name;
        foreach (var card in deck.Cards ?? Array.Empty<DeckCardDto>()) deckDraft[card.CardVersionId] = card.Count;
      }
      if (deckDraft.Count == 0 && collection.Count >= 15) foreach (var card in collection.Take(15)) deckDraft[card.CardVersionId] = Math.Min(2, card.Count);
      productionView?.Invalidate();
    }

    private async Task LoadDecks()
    {
      var result = await api.GetJsonAsync<DeckListDto>("/player/decks", accessToken); decks.Clear();
      if (result.Succeeded && result.Value != null) { activeDeckId = result.Value.ActiveDeckId ?? ""; if (result.Value.Decks != null) decks.AddRange(result.Value.Decks); }
      else status = result.Error?.Message ?? "Could not load decks.";
      productionView?.Invalidate();
    }

    private async Task DeleteDeckAsync(string deckId)
    {
      var result = await api.DeleteAsync<object>($"/player/decks/{Uri.EscapeDataString(deckId)}", accessToken);
      status = result.Succeeded ? "Deck deleted." : result.Error?.Message ?? "Deck deletion failed."; if (result.Succeeded && editingDeckId == deckId) NewDeck(); await LoadDecks();
    }

    private async Task SelectDeckAsync(string deckId)
    {
      var result = await api.PostJsonAsync<object>($"/player/decks/{Uri.EscapeDataString(deckId)}/select", new { }, accessToken);
      status = result.Succeeded ? "Battle deck selected." : result.Error?.Message ?? "Deck selection failed."; await LoadDecks();
    }

    private async Task StartMatchFromUiAsync()
    {
      if (busy) return;
      busy = true;
      status = MatchText("Preparing the arena and validating both decks...", "Подготовка арены и проверка обеих колод...");
      productionView?.Invalidate();
      var activeDeck = decks.FirstOrDefault(x => x.DeckId == activeDeckId);
      if (activeDeck == null) { await LoadDecks(); activeDeck = decks.FirstOrDefault(x => x.DeckId == activeDeckId); }
      DebugMatchBootstrapper.SetProductionCardCatalog(collection.Select(card => new GameplayCardPresentation(
        card.CardId,
        card.CardVersionId,
        PlayerApplicationView.PresentedCardName(card.CardId,card.DisplayName),
        card.ManaCost,
        card.CardType,
        card.RulesText,
        card.Art,
        card.Attack,
        card.Health,
        card.Durability,
        card.ArmorGain)));
      var started = activeDeck?.RuntimeDeckId is { Length: > 0 }
        ? await DebugMatchBootstrapper.StartProductionMatchAsync(playerId, activeDeck.RuntimeDeckId, activeDeck.BotRuntimeDeckId)
        : false;
      busy = false;
      var startupError=DebugMatchBootstrapper.LastStartupError;
      status = started
        ? MatchText("Match started.", "Матч начался.")
        : string.IsNullOrWhiteSpace(startupError)
          ? MatchText("The match could not be started. Please retry.", "Не удалось начать матч. Повторите попытку.")
          : MatchText("The match could not be started: ", "Не удалось начать матч: ")+startupError;
      if (started) Navigate(PlayerRoute.Gameplay); else productionView?.Invalidate();
    }

    private void HandleProductionMatchCompleted()
    {
      status = MatchText("Match complete. The command log is sealed and ready for replay.", "Матч завершён. Журнал команд сохранён и готов к повтору.");
      Navigate(PlayerRoute.Results);
    }

    private string MatchText(string english, string russian) => localization.Locale == "ru" ? russian : english;
    private string LocalizeStatus(string value)
    {
      if(value?.StartsWith("ERROR:",StringComparison.Ordinal)==true)return value.Substring(6);
      if (localization.Locale != "ru" || string.IsNullOrWhiteSpace(value)) return value;
      return value switch
      {
        "Collection synchronized." => "Коллекция синхронизирована.",
        "Could not load collection." => "Не удалось загрузить коллекцию.",
        "Session restored." => "Сессия восстановлена.",
        "Welcome, Wayfinder." => "Добро пожаловать, Странник.",
        "Favorite updated." => "Избранное обновлено.",
        "Deck saved and validated." => "Колода сохранена и проверена.",
        "Battle deck selected." => "Боевая колода выбрана.",
        "Deck deleted." => "Колода удалена.",
        "Theme applied." => "Тема применена.",
        "Signed out." => "Вы вышли из аккаунта.",
        _ => value
      };
    }
    public string CollectionSummary(string empty)
    {
      if (collection.Count == 0) return empty;
      return string.Join("\n", collection.Take(15).Select(card => $"◆ {card.CardId}  ×{card.Count}"));
    }

    private async Task RunPackagedJourneyAutomation()
    {
      var output = CommandLineValue("--e2e-output") ?? Path.Combine(Application.persistentDataPath, "player-journey-e2e.json");
      var runId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
      email = $"packaged-{runId}@example.test"; password = "ReleaseReady!123"; displayName = "Packaged Wayfinder";
      var stages = new List<string>();
      try
      {
        route = PlayerRoute.Register; await Authenticate(true); Require(route == PlayerRoute.MainMenu, "register/login"); stages.Add("auth");
        await LoadCollection();
        Require(collection.Count >= 15, "generation/starter-collection-loaded");
        var collectionBeforeGeneration = collection.Select(x => x.CardVersionId).ToHashSet(StringComparer.Ordinal);
        route = PlayerRoute.CardGeneration; prompt = "packaged astral vanguard"; providerMode = CommandLineValue("--e2e-generation-mode") == "openai" ? "openai" : "local"; await Generate(1);
        Require(generationState == "completed", "generation/completed");
        Require(collection.Select(x => x.CardVersionId).Count(id => !collectionBeforeGeneration.Contains(id)) == 1, "generation/new-card-acquired");
        Require(collection.Select(x => x.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 15, "generation/unique-card-names");
        if (providerMode == "openai") Require(collection.All(x => x.Art != null && !string.IsNullOrWhiteSpace(x.VisualAssetRef)), "generation/openai-card-art-delivery");
        stages.Add("generation-and-collection-" + providerMode);
        var favoriteVersion = collection[0].CardVersionId; var searchCardId = collection[0].CardId;
        await ToggleFavoriteAsync(favoriteVersion); Require(collection.Any(x => x.CardVersionId == favoriteVersion && x.Favorite), "favorite-mutation");
        collectionSearch = searchCardId; await LoadCollection(); Require(collection.Count > 0 && collection.All(x => x.CardId.Contains(searchCardId, StringComparison.OrdinalIgnoreCase)), "collection-search");
        collectionSearch = ""; favoritesOnly = true; await LoadCollection(); Require(collection.Count == 1 && collection[0].Favorite, "favorite-filter");
        favoritesOnly = false; await LoadCollection(); stages.Add("collection-search-and-favorites");
        route = PlayerRoute.DeckBuilder; await LoadDeckWorkspaceAsync();
        Require(decks.Count == 1 && editingDeckId == decks[0].DeckId && activeDeckId == decks[0].DeckId, "starter-deck-provisioned-and-selected");
        var starterDeckId = decks[0].DeckId;
        deckName = "Packaged Vanguard Updated"; await SaveStarterDeck(); Require(decks.Count == 1 && decks[0].Name == deckName, "deck-update");
        editingDeckId = ""; deckName = "Temporary Deck"; await SaveStarterDeck(); Require(decks.Count == 2, "deck-create");
        var temporaryDeck = decks.Single(x => x.Name == "Temporary Deck"); await DeleteDeckAsync(temporaryDeck.DeckId); Require(decks.Count == 1, "deck-delete");
        await SelectDeckAsync(starterDeckId); Require(activeDeckId == starterDeckId, "deck-select");
        stages.Add("deck-crud-and-selection");
        route = PlayerRoute.Settings; ThemeButtonAutomation("cyberpunk"); musicVolume = .5f; effectsVolume = .25f; reducedMotion = true; await SaveProfileSettingsAsync();
        var savedProfile = await api.GetJsonAsync<PlayerViewDto>("/me", accessToken);
        Require(savedProfile.Succeeded && savedProfile.Value?.Profile != null && savedProfile.Value.Profile.BoardTheme == "cyberpunk" && Mathf.Approximately(savedProfile.Value.Profile.MusicVolume,.5f) && Mathf.Approximately(savedProfile.Value.Profile.EffectsVolume,.25f) && savedProfile.Value.Profile.ReducedMotion, "settings-profile-persistence");
        stages.Add("settings-theme-audio-motion-persistence");
        var selectedDeck = decks.Single(x => x.DeckId == activeDeckId); Require(!string.IsNullOrWhiteSpace(selectedDeck.RuntimeDeckId) && !string.IsNullOrWhiteSpace(selectedDeck.BotRuntimeDeckId), "runtime-deck-registration");
        route = PlayerRoute.Gameplay;
        var match = await new LocalMatchFlowDriver(UnityBackendConfig.CreateRuntimeDefault()).RunAsync(new LocalMatchFlowOptions
        {
          UnityVersion = Application.unityVersion,
          SeedMode = SeedMode.Ensure,
          SmokeRunMode = "packaged-player-journey",
          RunToTerminal = true,
          RequireTerminal = true,
          MaxCommands = 512,
          InterCommandDelayMs = 650,
          PlayerOneId = playerId,
          PlayerOneDeckId = selectedDeck.RuntimeDeckId,
          PlayerTwoDeckId = selectedDeck.BotRuntimeDeckId,
          ReportRoot = Path.Combine(Path.GetDirectoryName(output) ?? Application.persistentDataPath, "match")
        }, default);
        Require(match.Succeeded && match.Report.ReplayVerified, "selected-deck-full-match/replay"); stages.Add("selected-deck-full-match-and-replay");
        Require(match.Report.CommandTypes.Any(x => string.Equals(x, "PLAY_CARD", StringComparison.OrdinalIgnoreCase)), "selected-deck-play-card-evidence");
        Require(match.Report.CommandTypes.Any(x => string.Equals(x, "ATTACK", StringComparison.OrdinalIgnoreCase)), "selected-deck-attack-evidence");
        Require(match.Report.MaxBoardEntityCount > 0, "selected-deck-visible-battlefield-evidence");
        Require(match.Report.CommandTypes.Any(x => !string.Equals(x, "END_TURN", StringComparison.OrdinalIgnoreCase)), "selected-deck-not-end-turn-only");
        stages.Add("played-cards-combat-and-battlefield");
        route = PlayerRoute.Results; stages.Add("results"); route = PlayerRoute.Replay; stages.Add("replay-screen");
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? Application.persistentDataPath);
        File.WriteAllText(output, JsonConvert.SerializeObject(new { verdict = "passed", runId, stages, generationMode = providerMode, generatedCardCount = collection.Count, matchId = match.Report.MatchId, replayVerified = match.Report.ReplayVerified, commandCount = match.Report.CommandCountAfter, theme = PlayerPrefs.GetString("cardgame.board-theme") }, Formatting.Indented));
        Application.Quit(0);
      }
      catch (Exception ex)
      {
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? Application.persistentDataPath);
        File.WriteAllText(output, JsonConvert.SerializeObject(new { verdict = "failed", runId, stages, error = ex.Message }, Formatting.Indented));
        Application.Quit(1);
      }
    }

    private async Task RunRecordedInteractiveMatchEvidence()
    {
      var output = CommandLineValue("--record-evidence-output") ?? Path.Combine(Application.persistentDataPath, "recorded-interactive-match.json");
      var frameDirectory = CommandLineValue("--record-frame-dir");
      var captureFrames = true;
      var captureTask = string.IsNullOrWhiteSpace(frameDirectory) ? Task.CompletedTask : CaptureMatchFramesAsync(frameDirectory, () => captureFrames);
      var runId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
      email = $"video-evidence-{runId}@example.test";
      password = "ReleaseReady!123";
      displayName = "Recorded Wayfinder";
      prompt = "recorded battlefield minions";
      providerMode = "local";
      try
      {
        if(Environment.GetCommandLineArgs().Contains("--record-seeded-match-evidence",StringComparer.Ordinal))
        {
          DebugMatchBootstrapper.SetProductionCardCatalog(DebugDemoSeeder.LoadSeedPresentations());
          Require(await DebugMatchBootstrapper.StartProductionMatchAsync(),"recorded-seeded-match-start");
          Navigate(PlayerRoute.Gameplay);
        }
        else
        {
          route = PlayerRoute.Register;
          await Authenticate(true);
          Require(route == PlayerRoute.MainMenu, "recorded-auth");
          route = PlayerRoute.CardGeneration;
          await Generate();
          Require(collection.Count >= 15, "recorded-generation");
          deckName = "Recorded Vanguard";
          route = PlayerRoute.DeckBuilder;
          await SaveStarterDeck();
          Require(decks.Count > 0, "recorded-deck");
          await SelectDeckAsync(decks[0].DeckId);
          await StartMatchFromUiAsync();
        }
        if(route != PlayerRoute.Gameplay)throw new InvalidOperationException("Packaged journey failed at recorded-match-start: "+DebugMatchBootstrapper.LastStartupError);
        await Task.Delay(1500);
        var evidence = await DebugMatchBootstrapper.AutoPlayHumanToTerminalAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? Application.persistentDataPath);
        File.WriteAllText(output, JsonConvert.SerializeObject(evidence, Formatting.Indented));
        captureFrames = false;
        await captureTask;
        await Task.Delay(3500);
        Application.Quit(evidence.Succeeded ? 0 : 1);
      }
      catch (Exception ex)
      {
        captureFrames = false;
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? Application.persistentDataPath);
        File.WriteAllText(output, JsonConvert.SerializeObject(new { Succeeded = false, Error = ex.Message }, Formatting.Indented));
        Application.Quit(1);
      }
    }

    private static async Task CaptureMatchFramesAsync(string directory, Func<bool> keepRecording)
    {
      Directory.CreateDirectory(directory);
      var frame=0;
      while(keepRecording())
      {
        ScreenCapture.CaptureScreenshot(Path.Combine(directory,$"frame-{frame++:D6}.png"),1);
        await Task.Delay(200);
      }
      await Task.Delay(400);
    }

    private async Task CaptureUiReference()
    {
      SetLocale(CommandLineValue("--locale") ?? "en");
      SetTheme(CommandLineValue("--theme") ?? "arcane");
      var routeName = CommandLineValue("--route") ?? "MainMenu";
      var target = Enum.TryParse<PlayerRoute>(routeName, true, out var parsed) ? parsed : PlayerRoute.MainMenu;
      if (Environment.GetCommandLineArgs().Contains("--capture-populated", StringComparer.Ordinal))
      {
        var runId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        email = $"visual-{runId}@example.test"; password = "ReleaseReady!123"; displayName = localization.Locale == "ru" ? "Тестовый Странник" : "Visual Wayfinder";
        await Authenticate(true);
        await LoadCollection();await LoadDecks();
        if (decks.Count > 0) await SelectDeckAsync(decks[0].DeckId);
        // Authentication restores the persisted profile theme. Reapply the explicit capture theme
        // so theme evidence always proves the requested variant instead of the profile default.
        SetTheme(CommandLineValue("--theme") ?? "arcane");
      }
      if (target == PlayerRoute.Gameplay)
      {
        DebugMatchBootstrapper.SetProductionCardCatalog(collection.Select(card => new GameplayCardPresentation(
          card.CardId, card.CardVersionId, PlayerApplicationView.PresentedCardName(card.CardId,card.DisplayName), card.ManaCost, card.CardType, card.RulesText, card.Art, card.Attack, card.Health, card.Durability, card.ArmorGain)));
        var selected = decks.FirstOrDefault(x => x.DeckId == activeDeckId);
        var started = selected?.RuntimeDeckId is { Length: > 0 }
          ? await DebugMatchBootstrapper.StartProductionMatchAsync(playerId, selected.RuntimeDeckId, selected.BotRuntimeDeckId)
          : await DebugMatchBootstrapper.StartProductionMatchAsync();
        Navigate(started ? PlayerRoute.Gameplay : PlayerRoute.Play);
      }
      else Navigate(target);
      var output = CommandLineValue("--capture-output") ?? Path.Combine(Application.persistentDataPath, "ui-reference.png");
      Directory.CreateDirectory(Path.GetDirectoryName(output) ?? Application.persistentDataPath);
      await Task.Delay(2000);
      if (Environment.GetCommandLineArgs().Contains("--capture-card-preview", StringComparer.Ordinal))
      {
        var previewIndex=int.TryParse(CommandLineValue("--capture-card-preview-index"),out var parsedPreviewIndex)?parsedPreviewIndex:0;
        if (!productionView.ShowCollectionCardPreviewForCapture(previewIndex))
          throw new InvalidOperationException("Card preview capture requires a populated collection route.");
        await Task.Delay(400);
      }
      ScreenCapture.CaptureScreenshot(output, 1);
      await Task.Delay(1200);
      Application.Quit(0);
    }

    private void ThemeButtonAutomation(string theme) { PlayerPrefs.SetString("cardgame.board-theme", theme); PlayerPrefs.Save(); }
    private static void Require(bool condition, string stage) { if (!condition) throw new InvalidOperationException("Packaged journey failed at " + stage); }
    private static string CommandLineValue(string key)
    {
      var args = Environment.GetCommandLineArgs();
      for (var i = 0; i < args.Length - 1; i++) if (args[i] == key) return args[i + 1];
      return null;
    }

    private void EnsureStyles()
    {
      if (titleStyle != null) return;
      titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.92f, 0.78f, 0.38f) } };
      bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, normal = { textColor = new Color(0.82f, 0.88f, 0.96f) } };
      buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold, fixedHeight = 42, normal = { textColor = Color.white } };
    }

    [Serializable] private sealed class AuthDto { [JsonProperty("accessToken")] public string AccessToken; [JsonProperty("playerId")] public string PlayerId; }
    [Serializable] private sealed class PlayerViewDto { [JsonProperty("playerId")] public string PlayerId; [JsonProperty("profile")] public PlayerProfileDto Profile; }
    [Serializable] private sealed class PlayerProfileDto { [JsonProperty("displayName")] public string DisplayName; [JsonProperty("boardTheme")] public string BoardTheme; [JsonProperty("musicVolume")] public float MusicVolume; [JsonProperty("effectsVolume")] public float EffectsVolume; [JsonProperty("reducedMotion")] public bool ReducedMotion; }
    [Serializable] private sealed class CollectionCardDto { [JsonProperty("cardId")] public string CardId; [JsonProperty("cardVersionId")] public string CardVersionId; [JsonProperty("count")] public int Count; [JsonProperty("favorite")] public bool Favorite; [JsonProperty("displayName")] public string DisplayName; [JsonProperty("cardType")] public string CardType; [JsonProperty("rarity")] public string Rarity; [JsonProperty("manaCost")] public int ManaCost; [JsonProperty("attack")] public int Attack; [JsonProperty("health")] public int Health; [JsonProperty("durability")] public int Durability; [JsonProperty("armorGain")] public int ArmorGain; [JsonProperty("rulesText")] public string RulesText; [JsonProperty("visualAssetRef")] public string VisualAssetRef; [JsonIgnore] public UnityEngine.Texture2D Art; }
    [Serializable] private sealed class GenerationJobDto { [JsonProperty("jobId")] public string JobId; [JsonProperty("status")] public string Status; [JsonProperty("progressPercent")] public int ProgressPercent; [JsonProperty("errorCode")] public string ErrorCode; [JsonProperty("results")] public CollectionCardDto[] Results; }
    [Serializable] private sealed class DeckCardDto { [JsonProperty("cardId")] public string CardId; [JsonProperty("cardVersionId")] public string CardVersionId; [JsonProperty("count")] public int Count; }
    [Serializable] private sealed class PlayerDeckDto { [JsonProperty("deckId")] public string DeckId; [JsonProperty("name")] public string Name; [JsonProperty("cards")] public DeckCardDto[] Cards; [JsonProperty("runtimeDeckId")] public string RuntimeDeckId; [JsonProperty("botRuntimeDeckId")] public string BotRuntimeDeckId; }
    [Serializable] private sealed class DeckListDto { [JsonProperty("activeDeckId")] public string ActiveDeckId; [JsonProperty("decks")] public PlayerDeckDto[] Decks; }
    public sealed class CollectionUiCard { public CollectionUiCard(string cardId,string versionId,int owned,bool favorite,int selected,string displayName,string cardType,string rarity,int manaCost,int attack,int health,int durability,int armorGain,string rulesText,string visualAssetRef,UnityEngine.Texture2D art){CardId=cardId;VersionId=versionId;Owned=owned;Favorite=favorite;Selected=selected;DisplayName=PlayerApplicationView.PresentedCardName(cardId,displayName);CardType=cardType??string.Empty;Rarity=rarity??string.Empty;ManaCost=manaCost;Attack=attack;Health=health;Durability=durability;ArmorGain=armorGain;RulesText=rulesText??string.Empty;VisualAssetRef=visualAssetRef??string.Empty;Art=art;} public string CardId { get; } public string VersionId { get; } public int Owned { get; } public bool Favorite { get; } public int Selected { get; } public string DisplayName { get; } public string CardType { get; } public string Rarity { get; } public int ManaCost { get; } public int Attack { get; } public int Health { get; } public int Durability { get; } public int ArmorGain { get; } public string RulesText { get; } public string VisualAssetRef { get; } public UnityEngine.Texture2D Art { get; } }
    public sealed class DeckUiEntry { public DeckUiEntry(string id,string name,int count,bool active){Id=id;Name=name;Count=count;Active=active;} public string Id { get; } public string Name { get; } public int Count { get; } public bool Active { get; } }
  }
}
