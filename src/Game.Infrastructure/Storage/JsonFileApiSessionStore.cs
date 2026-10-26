using Game.Application.Matches;

namespace Game.Infrastructure.Storage;

public sealed class JsonFileApiSessionStore : IApiSessionStore
{
  private readonly object syncRoot = new();
  private readonly string filePath;
  private readonly SortedDictionary<string, ApiSession> sessions = new(StringComparer.Ordinal);

  public JsonFileApiSessionStore(string rootDirectory)
  {
    if (string.IsNullOrWhiteSpace(rootDirectory))
    {
      throw new ArgumentException("Storage root directory is required.", nameof(rootDirectory));
    }

    filePath = Path.Combine(rootDirectory, "api-sessions.json");
    foreach (var session in JsonFileStoreIO.Load<List<ApiSession>>(filePath) ?? [])
    {
      sessions[session.SessionToken] = session;
    }
  }

  public ApiSession Store(CreateApiSessionRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    var now = request.Now ?? DateTimeOffset.UtcNow;
    var ttl = request.Ttl ?? TimeSpan.FromHours(8);
    var token = string.IsNullOrWhiteSpace(request.SessionToken)
      ? $"session:{Guid.NewGuid():N}"
      : request.SessionToken;
    var session = new ApiSession(token, request.PlayerId, now, now.Add(ttl));

    lock (syncRoot)
    {
      sessions[token] = session;
      Save();
    }

    return session;
  }

  public ApiSession? FindByToken(string sessionToken, DateTimeOffset now)
  {
    if (string.IsNullOrWhiteSpace(sessionToken))
    {
      return null;
    }

    lock (syncRoot)
    {
      return sessions.TryGetValue(sessionToken, out var session) && session.ExpiresAt > now
        ? session
        : null;
    }
  }

  private void Save() => JsonFileStoreIO.Save(filePath, sessions.Values.ToArray());
}
