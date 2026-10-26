using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.Errors
{
  public static class UnityErrorMapper
  {
    public static UnityError FromTransport(string code, string message, string diagnostic = "")
    {
      return new UnityError
      {
        Code = string.IsNullOrWhiteSpace(code) ? "TRANSPORT_ERROR" : code,
        Message = message,
        SuggestedUnityBehavior = diagnostic,
        Retryable = code != "COMMAND_PRE_STATE_HASH_MISMATCH"
      };
    }

    public static string ToDebugMessage(UnityError error)
    {
      if (error == null || string.IsNullOrWhiteSpace(error.Code))
      {
        return "No API error.";
      }

      var suggestion = error.Code switch
      {
        "COMMAND_PRE_STATE_HASH_MISMATCH" => "State is stale. Refresh or reconnect, then fetch fresh legal actions.",
        "MATCH_SESSION_FORBIDDEN" => "The current session token cannot view or act in this match.",
        "RATE_LIMITED" => "The backend is rate limiting requests. Wait before retrying.",
        "SERVER_FAULT" => "The backend reported an internal failure. Stop command submission and inspect backend logs.",
        "REPLAY_MISMATCH" => "Replay verification failed. Treat the match evidence as invalid.",
        "UNSUPPORTED_CLIENT_VERSION" => "The Unity client and backend contract versions are incompatible.",
        "TRANSPORT_ERROR" => "The backend could not be reached. Verify the URL and local server.",
        _ => error.Retryable ? "Retry may be safe for reads only. Do not blindly retry commands." : "Manual recovery is required."
      };

      return $"{error.Code}: {error.Message} {suggestion}".Trim();
    }
  }
}
