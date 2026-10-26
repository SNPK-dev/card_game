using Game.Application.Players;

namespace Game.Infrastructure.Storage;

public sealed class JsonFilePlayerAccountStore : IPlayerAccountStore
{
  private readonly object gate = new();
  private readonly string accountsPath;
  private readonly string sessionsPath;
  private readonly Dictionary<string, PlayerAccount> accounts;
  private readonly Dictionary<string, PlayerAuthSession> sessions;

  public JsonFilePlayerAccountStore(string rootDirectory)
  {
    accountsPath = Path.Combine(rootDirectory, "players", "accounts.json");
    sessionsPath = Path.Combine(rootDirectory, "players", "sessions.json");
    accounts = (JsonFileStoreIO.Load<List<PlayerAccount>>(accountsPath) ?? []).ToDictionary(x => x.PlayerId, StringComparer.Ordinal);
    sessions = (JsonFileStoreIO.Load<List<PlayerAuthSession>>(sessionsPath) ?? []).ToDictionary(x => x.AccessTokenHash, StringComparer.Ordinal);
  }

  public PlayerAccount? FindById(string playerId) { lock (gate) return accounts.GetValueOrDefault(playerId); }
  public PlayerAccount? FindByEmail(string normalizedEmail) { lock (gate) return accounts.Values.FirstOrDefault(x => x.Email == normalizedEmail); }
  public PlayerAuthSession? FindSessionByAccessTokenHash(string tokenHash) { lock (gate) return sessions.GetValueOrDefault(tokenHash); }
  public PlayerAuthSession? FindSessionByRefreshTokenHash(string tokenHash) { lock (gate) return sessions.Values.FirstOrDefault(x => x.RefreshTokenHash == tokenHash); }
  public void Upsert(PlayerAccount account) { lock (gate) { accounts[account.PlayerId] = account; JsonFileStoreIO.Save(accountsPath, accounts.Values.OrderBy(x => x.PlayerId).ToArray()); } }
  public void UpsertSession(PlayerAuthSession session) { lock (gate) { sessions[session.AccessTokenHash] = session; JsonFileStoreIO.Save(sessionsPath, sessions.Values.OrderBy(x => x.PlayerId).ToArray()); } }
  public void RemoveSession(string accessTokenHash) { lock (gate) { sessions.Remove(accessTokenHash); JsonFileStoreIO.Save(sessionsPath, sessions.Values.OrderBy(x => x.PlayerId).ToArray()); } }
}
