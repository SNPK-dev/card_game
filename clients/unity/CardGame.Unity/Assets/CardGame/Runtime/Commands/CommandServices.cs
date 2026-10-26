using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CardGame.Unity.Client.Api;
using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.Commands
{
  public sealed class CommandBuilder
  {
    public ExternalCommandSubmissionRequest Build(
      MatchCommandDto legalActionTemplate,
      string sessionToken,
      string expectedPreStateHash)
    {
      return BuildWithRefs(legalActionTemplate, sessionToken, expectedPreStateHash, null);
    }

    public ExternalCommandSubmissionRequest BuildWithSelectedRef(
      MatchCommandDto legalActionTemplate,
      string sessionToken,
      string expectedPreStateHash,
      string refName,
      string refValue)
    {
      if (string.IsNullOrWhiteSpace(refName) || string.IsNullOrWhiteSpace(refValue))
      {
        throw new InvalidOperationException("Selected target refs must come from the server-provided legal action or choice option.");
      }

      return BuildWithRefs(
        legalActionTemplate,
        sessionToken,
        expectedPreStateHash,
        new Dictionary<string, string> { [refName] = refValue });
    }

    public ExternalCommandSubmissionRequest BuildResolveChoice(
      MatchCommandDto legalActionTemplate,
      string sessionToken,
      string expectedPreStateHash,
      string choiceId,
      string selectedOptionId)
    {
      if (string.IsNullOrWhiteSpace(choiceId) || string.IsNullOrWhiteSpace(selectedOptionId))
      {
        throw new InvalidOperationException("Choice resolution requires server-provided choice and option ids.");
      }

      return BuildWithRefs(
        legalActionTemplate,
        sessionToken,
        expectedPreStateHash,
        new Dictionary<string, string>
        {
          ["choiceId"] = choiceId,
          ["selectedOptionId"] = selectedOptionId
        });
    }

    private ExternalCommandSubmissionRequest BuildWithRefs(
      MatchCommandDto legalActionTemplate,
      string sessionToken,
      string expectedPreStateHash,
      IDictionary<string, string> selectedRefs)
    {
      if (legalActionTemplate == null)
      {
        throw new InvalidOperationException("A server-provided legal action template is required.");
      }

      if (string.IsNullOrWhiteSpace(legalActionTemplate.CommandType))
      {
        throw new InvalidOperationException("The legal action template is missing a command type.");
      }

      if (string.IsNullOrWhiteSpace(expectedPreStateHash))
      {
        throw new InvalidOperationException("Expected pre-state hash is required.");
      }

      var refs = legalActionTemplate.Refs == null
        ? new Dictionary<string, string>()
        : new Dictionary<string, string>(legalActionTemplate.Refs);
      if (selectedRefs != null)
      {
        foreach (var selectedRef in selectedRefs)
        {
          refs[selectedRef.Key] = selectedRef.Value;
        }
      }

      return new ExternalCommandSubmissionRequest
      {
        SessionToken = sessionToken,
        Command = new MatchCommandDto
        {
          CommandType = legalActionTemplate.CommandType,
          PlayerId = legalActionTemplate.PlayerId,
          Payload = legalActionTemplate.Payload,
          Refs = refs.Count == 0 ? null : refs
        },
        ExpectedPreStateHash = expectedPreStateHash,
        CommandId = Guid.NewGuid().ToString("N"),
        CorrelationId = Guid.NewGuid().ToString("N")
      };
    }
  }

  public sealed class CommandQueue
  {
    private readonly BackendApiClient api;
    private readonly CommandBuilder builder;
    private bool commandInFlight;

    public CommandQueue(BackendApiClient api, CommandBuilder builder)
    {
      this.api = api;
      this.builder = builder;
    }

    public bool CommandInFlight => commandInFlight;

    public async Task<UnityApiResult<ExternalCommandSubmissionResult>> SubmitAsync(
      string matchId,
      MatchCommandDto legalActionTemplate,
      string sessionToken,
      string expectedPreStateHash,
      CancellationToken cancellationToken)
    {
      if (commandInFlight)
      {
        return UnityApiResult<ExternalCommandSubmissionResult>.Failure(
          new UnityError
          {
            Code = "COMMAND_IN_FLIGHT",
            Message = "A command is already in flight.",
            Retryable = false
          },
          0,
          string.Empty);
      }

      commandInFlight = true;
      try
      {
        var submission = builder.Build(legalActionTemplate, sessionToken, expectedPreStateHash);
        return await api.PostJsonAsync<ExternalCommandSubmissionResult>(
          $"/matches/{Uri.EscapeDataString(matchId)}/commands",
          submission,
          sessionToken,
          cancellationToken);
      }
      finally
      {
        commandInFlight = false;
      }
    }

    public static MatchCommandDto FirstOfType(ExternalLegalActionsResult legalActions, string commandType)
    {
      return legalActions?.LegalActions?.FirstOrDefault(action =>
        string.Equals(action.CommandType, commandType, StringComparison.OrdinalIgnoreCase));
    }
  }
}
