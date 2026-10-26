using System.Security.Cryptography;

namespace Game.Application.Players;

public sealed record PlayerProfile(
  string DisplayName,
  string BoardTheme,
  float MusicVolume,
  float EffectsVolume,
  bool ReducedMotion,
  string StarterHeroId = StarterHeroProfiles.AstralWardenId);

public sealed record StarterHeroProfile(
  string HeroId,
  string DisplayName,
  string Description,
  string PortraitAssetRef,
  int StartingHealth,
  int CatalogOffset,
  string HeroPowerId = "",
  string HeroPowerDisplayName = "",
  string HeroPowerRulesText = "",
  int HeroPowerCost = 2);

public static class StarterHeroProfiles
{
  public const string AstralWardenId = "astral-warden";
  public const string CinderVanguardId = "cinder-vanguard";

  public static readonly IReadOnlyList<StarterHeroProfile> All =
  [
    new(
      AstralWardenId,
      "Astra, Astral Warden",
      "Measured control and resilient constellations.",
      "hero://astral-warden",
      30,
      0,
      "astral-restoration",
      "Astral Restoration",
      "Restore 2 Health to a friendly character.",
      2),
    new(
      CinderVanguardId,
      "Varg, Cinder Vanguard",
      "Relentless pressure forged in living flame.",
      "hero://cinder-vanguard",
      30,
      15,
      "cinder-spark",
      "Cinder Spark",
      "Deal 1 damage to an enemy character.",
      2)
  ];

  public static StarterHeroProfile? Find(string? heroId) =>
    All.FirstOrDefault(hero => StringComparer.Ordinal.Equals(hero.HeroId, heroId));

  public static StarterHeroProfile OpponentOf(string? heroId) =>
    All.First(hero => !StringComparer.Ordinal.Equals(hero.HeroId, heroId));
}

public sealed record PlayerCollectionEntry(string CardId, string CardVersionId, int Count, bool Favorite, DateTimeOffset AcquiredAtUtc);

public sealed record PlayerDeck(string DeckId, string Name, IReadOnlyList<PlayerDeckCard> Cards, DateTimeOffset UpdatedAtUtc, string? RuntimeDeckId = null, string? BotRuntimeDeckId = null);

public sealed record PlayerDeckCard(string CardId, string CardVersionId, int Count);

public sealed record PlayerGenerationJob(
  string JobId,
  string Prompt,
  string ProviderMode,
  string Status,
  int ProgressPercent,
  IReadOnlyList<PlayerCollectionEntry> Results,
  string? ErrorCode,
  DateTimeOffset UpdatedAtUtc,
  int RequestedCount = 3,
  int Attempt = 1,
  bool CancelRequested = false);

public sealed record PlayerAccount(
  string PlayerId,
  string Email,
  string PasswordSalt,
  string PasswordHash,
  int PasswordIterations,
  PlayerProfile Profile,
  IReadOnlyList<PlayerCollectionEntry> Collection,
  IReadOnlyList<PlayerDeck> Decks,
  string? ActiveDeckId,
  IReadOnlyList<PlayerGenerationJob> GenerationJobs,
  DateTimeOffset CreatedAtUtc);

public sealed record PlayerAuthSession(string AccessTokenHash, string RefreshTokenHash, string PlayerId, DateTimeOffset AccessExpiresAtUtc, DateTimeOffset RefreshExpiresAtUtc);

public interface IPlayerAccountStore
{
  PlayerAccount? FindById(string playerId);
  PlayerAccount? FindByEmail(string normalizedEmail);
  PlayerAuthSession? FindSessionByAccessTokenHash(string tokenHash);
  PlayerAuthSession? FindSessionByRefreshTokenHash(string tokenHash);
  void Upsert(PlayerAccount account);
  void UpsertSession(PlayerAuthSession session);
  void RemoveSession(string accessTokenHash);
}

public sealed record PlayerGenerationExecutionRequest(string JobId, string Prompt, string ProviderMode, int Count, int Attempt, Action<int>? ReportProgress = null);
public sealed record PlayerGenerationExecutionResult(bool Succeeded, string? ErrorCode, Game.Shared.Catalog.CatalogSnapshot? PublishedCatalog = null)
{
  public static PlayerGenerationExecutionResult Success(Game.Shared.Catalog.CatalogSnapshot? catalog = null) => new(true, null, catalog);
  public static PlayerGenerationExecutionResult Failure(string code) => new(false, code);
}

public interface IPlayerGenerationExecutor
{
  Task<PlayerGenerationExecutionResult> ExecuteAsync(PlayerGenerationExecutionRequest request, CancellationToken cancellationToken);
}

public interface IPlayerGenerationJobQueue
{
  void Enqueue(string playerId, string jobId);
}

public sealed class InMemoryPlayerAccountStore : IPlayerAccountStore
{
  private readonly object gate = new();
  private readonly Dictionary<string, PlayerAccount> accounts = new(StringComparer.Ordinal);
  private readonly Dictionary<string, PlayerAuthSession> sessions = new(StringComparer.Ordinal);

  public PlayerAccount? FindById(string playerId) { lock (gate) return accounts.GetValueOrDefault(playerId); }
  public PlayerAccount? FindByEmail(string normalizedEmail) { lock (gate) return accounts.Values.FirstOrDefault(x => x.Email == normalizedEmail); }
  public PlayerAuthSession? FindSessionByAccessTokenHash(string tokenHash) { lock (gate) return sessions.GetValueOrDefault(tokenHash); }
  public PlayerAuthSession? FindSessionByRefreshTokenHash(string tokenHash) { lock (gate) return sessions.Values.FirstOrDefault(x => x.RefreshTokenHash == tokenHash); }
  public void Upsert(PlayerAccount account) { lock (gate) accounts[account.PlayerId] = account; }
  public void UpsertSession(PlayerAuthSession session) { lock (gate) sessions[session.AccessTokenHash] = session; }
  public void RemoveSession(string accessTokenHash) { lock (gate) sessions.Remove(accessTokenHash); }
}

public sealed class PlayerAccountService(IPlayerAccountStore store, TimeProvider clock)
{
  public const int PasswordIterations = 210_000;

  public PlayerAuthResult Register(
    string email,
    string password,
    string displayName,
    string starterHeroId = StarterHeroProfiles.AstralWardenId)
  {
    var normalized = NormalizeEmail(email);
    if (!IsEmailValid(normalized) || string.IsNullOrEmpty(password) || password.Length < 12 || string.IsNullOrWhiteSpace(displayName)
      || StarterHeroProfiles.Find(starterHeroId) is null)
      return PlayerAuthResult.Fail("AUTH_VALIDATION_FAILED");
    if (store.FindByEmail(normalized) is not null)
      return PlayerAuthResult.Fail("AUTH_EMAIL_EXISTS");

    var salt = RandomNumberGenerator.GetBytes(32);
    var account = new PlayerAccount(
      $"player-{Guid.NewGuid():N}", normalized, Convert.ToBase64String(salt), HashPassword(password, salt, PasswordIterations), PasswordIterations,
      new PlayerProfile(displayName.Trim(), "arcane", 0.7f, 0.85f, false, starterHeroId), [], [], null, [], clock.GetUtcNow());
    store.Upsert(account);
    return IssueSession(account);
  }

  public PlayerAuthResult Login(string email, string password)
  {
    var account = store.FindByEmail(NormalizeEmail(email));
    if (account is null) return PlayerAuthResult.Fail("AUTH_INVALID_CREDENTIALS");
    var salt = Convert.FromBase64String(account.PasswordSalt);
    var actual = Convert.FromBase64String(HashPassword(password ?? string.Empty, salt, account.PasswordIterations));
    var expected = Convert.FromBase64String(account.PasswordHash);
    return CryptographicOperations.FixedTimeEquals(actual, expected) ? IssueSession(account) : PlayerAuthResult.Fail("AUTH_INVALID_CREDENTIALS");
  }

  public PlayerAuthResult Refresh(string refreshToken)
  {
    var current = store.FindSessionByRefreshTokenHash(HashToken(refreshToken));
    if (current is null || current.RefreshExpiresAtUtc <= clock.GetUtcNow()) return PlayerAuthResult.Fail("AUTH_REFRESH_INVALID");
    var account = store.FindById(current.PlayerId);
    if (account is null) return PlayerAuthResult.Fail("AUTH_REFRESH_INVALID");
    store.RemoveSession(current.AccessTokenHash);
    return IssueSession(account);
  }

  public PlayerAccount? Authenticate(string accessToken)
  {
    if (string.IsNullOrWhiteSpace(accessToken)) return null;
    var session = store.FindSessionByAccessTokenHash(HashToken(accessToken));
    return session is not null && session.AccessExpiresAtUtc > clock.GetUtcNow() ? store.FindById(session.PlayerId) : null;
  }

  public void Logout(string accessToken) { if (!string.IsNullOrWhiteSpace(accessToken)) store.RemoveSession(HashToken(accessToken)); }
  public void Save(PlayerAccount account) => store.Upsert(account);
  public PlayerAccount? FindById(string playerId) => store.FindById(playerId);

  private PlayerAuthResult IssueSession(PlayerAccount account)
  {
    var access = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    var refresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(48)).ToLowerInvariant();
    var now = clock.GetUtcNow();
    store.UpsertSession(new PlayerAuthSession(HashToken(access), HashToken(refresh), account.PlayerId, now.AddHours(2), now.AddDays(30)));
    return new(true, null, access, refresh, account.PlayerId, now.AddHours(2));
  }

  public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token ?? string.Empty))).ToLowerInvariant();
  private static string HashPassword(string password, byte[] salt, int iterations) => Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32));
  private static string NormalizeEmail(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();
  private static bool IsEmailValid(string email) => email.Length is >= 5 and <= 254 && email.Contains('@') && !email.Contains(' ');
}

public sealed record PlayerAuthResult(bool Succeeded, string? ErrorCode, string? AccessToken, string? RefreshToken, string? PlayerId, DateTimeOffset? AccessExpiresAtUtc)
{
  public static PlayerAuthResult Fail(string code) => new(false, code, null, null, null, null);
}
