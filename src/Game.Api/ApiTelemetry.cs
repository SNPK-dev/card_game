using System.Diagnostics.Metrics;

namespace Game.Api;

public sealed class ApiTelemetry : IDisposable
{
  public const string MeterName = "Game.Api";
  private readonly ILogger<ApiTelemetry> _logger;
  private readonly Meter _meter;
  private readonly Counter<long> _requestRejections;
  private readonly Counter<long> _authorizationDenials;
  private readonly Counter<long> _commandAdmissions;

  public ApiTelemetry(ILogger<ApiTelemetry> logger)
  {
    _logger = logger;
    _meter = new Meter(MeterName, "1.0.0");
    _requestRejections = _meter.CreateCounter<long>("game.api.requests.rejected");
    _authorizationDenials = _meter.CreateCounter<long>("game.api.authorization.denied");
    _commandAdmissions = _meter.CreateCounter<long>("game.api.commands.admitted");
  }

  public void RecordRequestRejected(string reason, PathString path, int statusCode)
  {
    var pathValue = path.HasValue ? path.Value! : "/";
    _requestRejections.Add(
      1,
      new KeyValuePair<string, object?>("reason", reason),
      new KeyValuePair<string, object?>("path", pathValue),
      new KeyValuePair<string, object?>("status_code", statusCode));

    _logger.LogWarning(
      "API request rejected: {Reason} {Path} {StatusCode}",
      reason,
      pathValue,
      statusCode);
  }

  public void RecordAuthorizationDenied(string endpoint, string matchId)
  {
    _authorizationDenials.Add(
      1,
      new KeyValuePair<string, object?>("endpoint", endpoint),
      new KeyValuePair<string, object?>("match_id", matchId));

    _logger.LogWarning(
      "API authorization denied: {Endpoint} {MatchId}",
      endpoint,
      matchId);
  }

  public void RecordCommandAdmission(
    bool succeeded,
    string matchId,
    string playerId,
    string commandType,
    string reason)
  {
    _commandAdmissions.Add(
      1,
      new KeyValuePair<string, object?>("result", succeeded ? "accepted" : "rejected"),
      new KeyValuePair<string, object?>("match_id", matchId),
      new KeyValuePair<string, object?>("player_id", playerId),
      new KeyValuePair<string, object?>("command_type", commandType),
      new KeyValuePair<string, object?>("reason", reason));

    if (succeeded)
    {
      _logger.LogInformation(
        "API command admitted: {MatchId} {PlayerId} {CommandType}",
        matchId,
        playerId,
        commandType);
      return;
    }

    _logger.LogWarning(
      "API command rejected: {MatchId} {PlayerId} {CommandType} {Reason}",
      matchId,
      playerId,
      commandType,
      reason);
  }

  public void Dispose() => _meter.Dispose();
}
