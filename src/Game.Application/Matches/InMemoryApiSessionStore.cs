namespace Game.Application.Matches;

public sealed class InMemoryApiSessionStore : IApiSessionStore
{
  private readonly object syncRoot = new();
  private readonly SortedDictionary<string, ApiSession> sessions = new(StringComparer.Ordinal);

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
}
