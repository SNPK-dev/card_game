using Game.Application.Matches;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Game.Api;

public static class MatchEndpointHandlers
{
  public static IResult CreateSession(CreateApiSessionRequest request, MatchRuntimeService service)
  {
    var result = service.CreateSession(request);
    return result.Succeeded ? Results.Ok(result) : Results.BadRequest(result);
  }

  public static IResult StartMatch(ExternalMatchStartRequest request, MatchRuntimeService service)
  {
    var result = service.Start(request);
    return result.Succeeded ? Results.Ok(result) : Results.BadRequest(result);
  }

  public static IResult GetState(
    string matchId,
    HttpRequest request,
    MatchRuntimeService service,
    ApiTelemetry telemetry)
  {
    var state = service.GetState(matchId, SessionToken(request));
    if (state is not null)
    {
      return Results.Ok(state);
    }

    telemetry.RecordAuthorizationDenied("match_state", matchId);
    return Results.StatusCode(StatusCodes.Status403Forbidden);
  }

  public static IResult GetLegalActions(
    string matchId,
    string playerId,
    HttpRequest request,
    MatchRuntimeService service,
    ApiTelemetry telemetry)
  {
    var result = service.GetLegalActions(matchId, SessionToken(request), playerId);
    if (result.Succeeded)
    {
      return Results.Ok(result);
    }

    telemetry.RecordAuthorizationDenied("match_legal_actions", matchId);
    return Results.StatusCode(StatusCodes.Status403Forbidden);
  }

  public static IResult SubmitCommand(
    string matchId,
    ExternalCommandSubmissionRequest submission,
    MatchRuntimeService service,
    ApiTelemetry telemetry)
  {
    var result = service.SubmitCommand(matchId, submission);
    var reason = result.Succeeded
      ? "accepted"
      : result.Diagnostics.FirstOrDefault()?.Code ?? "COMMAND_REJECTED";
    telemetry.RecordCommandAdmission(
      result.Succeeded,
      matchId,
      submission.Command.PlayerId,
      submission.Command.CommandType,
      reason);

    if (result.Succeeded)
    {
      return Results.Ok(result);
    }

    return result.Diagnostics.Any(diagnostic => diagnostic.Code.Contains("FORBIDDEN", StringComparison.Ordinal))
      ? Results.StatusCode(StatusCodes.Status403Forbidden)
      : Results.BadRequest(result);
  }

  public static IResult PollEvents(
    string matchId,
    long afterEventSeq,
    HttpRequest request,
    MatchRuntimeService service,
    ApiTelemetry telemetry)
  {
    var result = service.PollEvents(matchId, SessionToken(request), afterEventSeq);
    if (result.Succeeded)
    {
      return Results.Ok(result);
    }

    telemetry.RecordAuthorizationDenied("match_events", matchId);
    return Results.StatusCode(StatusCodes.Status403Forbidden);
  }

  public static IResult Reconnect(
    string matchId,
    long afterEventSeq,
    HttpRequest request,
    MatchRuntimeService service,
    ApiTelemetry telemetry)
  {
    var result = service.Reconnect(matchId, SessionToken(request), afterEventSeq);
    if (result.Succeeded)
    {
      return Results.Ok(result);
    }

    telemetry.RecordAuthorizationDenied("match_reconnect", matchId);
    return Results.StatusCode(StatusCodes.Status403Forbidden);
  }

  public static IResult VerifyReplay(
    string matchId,
    HttpRequest request,
    MatchRuntimeService service,
    ApiTelemetry telemetry)
  {
    var result = service.VerifyReplay(matchId, SessionToken(request));
    if (result.Succeeded)
    {
      return Results.Ok(result);
    }

    telemetry.RecordAuthorizationDenied("match_replay_verify", matchId);
    return Results.StatusCode(StatusCodes.Status403Forbidden);
  }

  private static string SessionToken(HttpRequest request) =>
    request.Headers.TryGetValue("X-Session-Token", out var values)
      ? values.ToString()
      : string.Empty;
}
