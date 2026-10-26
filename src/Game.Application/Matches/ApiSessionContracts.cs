namespace Game.Application.Matches;

public sealed record ApiSession(
  string SessionToken,
  string PlayerId,
  DateTimeOffset CreatedAt,
  DateTimeOffset ExpiresAt);

public sealed record CreateApiSessionRequest(
  string PlayerId,
  string? SessionToken = null,
  DateTimeOffset? Now = null,
  TimeSpan? Ttl = null);

public sealed record CreateApiSessionResult(
  bool Succeeded,
  ApiSession? Session,
  IReadOnlyList<MatchRuntimeDiagnostic> Diagnostics)
{
  public static CreateApiSessionResult Success(ApiSession session) => new(true, session, []);

  public static CreateApiSessionResult Rejected(IReadOnlyList<MatchRuntimeDiagnostic> diagnostics) =>
    new(false, null, diagnostics);
}

public interface IApiSessionStore
{
  ApiSession Store(CreateApiSessionRequest request);

  ApiSession? FindByToken(string sessionToken, DateTimeOffset now);
}
