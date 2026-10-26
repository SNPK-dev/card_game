using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CardGame.Unity.Client.Config;
using CardGame.Unity.Client.Errors;
using Game.UnityClient.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine.Networking;
using UnityEngine;

namespace CardGame.Unity.Client.Api
{
  public sealed class BackendApiClient
  {
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
      ContractResolver = new CamelCasePropertyNamesContractResolver(),
      NullValueHandling = NullValueHandling.Ignore
    };

    private readonly UnityBackendConfig config;

    public BackendApiClient(UnityBackendConfig config)
    {
      this.config = config;
    }

    public string ComposeUrl(string relativePath)
    {
      var baseUrl = (config.BackendBaseUrl ?? UnityBackendConfig.DefaultLocalBackendUrl).TrimEnd('/') + "/";
      return new Uri(new Uri(baseUrl), relativePath.TrimStart('/')).ToString();
    }

    public Task<UnityApiResult<T>> GetJsonAsync<T>(string relativePath, string sessionToken = "", CancellationToken cancellationToken = default) =>
      SendAsync<T>("GET", relativePath, null, sessionToken, cancellationToken);

    public Task<UnityApiResult<T>> PostJsonAsync<T>(string relativePath, object body, string sessionToken = "", CancellationToken cancellationToken = default) =>
      SendAsync<T>("POST", relativePath, JsonConvert.SerializeObject(body, SerializerSettings), sessionToken, cancellationToken);

    public Task<UnityApiResult<T>> PostRawJsonAsync<T>(string relativePath, string rawJson, string sessionToken = "", CancellationToken cancellationToken = default) =>
      SendAsync<T>("POST", relativePath, rawJson, sessionToken, cancellationToken);

    public Task<UnityApiResult<T>> PatchJsonAsync<T>(string relativePath, object body, string sessionToken = "", CancellationToken cancellationToken = default) =>
      SendAsync<T>("PATCH", relativePath, JsonConvert.SerializeObject(body, SerializerSettings), sessionToken, cancellationToken);

    public Task<UnityApiResult<T>> DeleteAsync<T>(string relativePath, string sessionToken = "", CancellationToken cancellationToken = default) =>
      SendAsync<T>("DELETE", relativePath, null, sessionToken, cancellationToken);

    public async Task<UnityApiResult<Texture2D>> GetTextureAsync(string relativePath, string sessionToken = "", CancellationToken cancellationToken = default)
    {
      using var linkedTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      linkedTimeout.CancelAfter(TimeSpan.FromSeconds(config.RequestTimeoutSeconds));
      try
      {
        using var request = UnityWebRequestTexture.GetTexture(ComposeUrl(relativePath), true);
        if (!string.IsNullOrWhiteSpace(sessionToken)) request.SetRequestHeader("X-Session-Token", sessionToken);
        var operation = request.SendWebRequest();
        while (!operation.isDone) { linkedTimeout.Token.ThrowIfCancellationRequested(); await Task.Yield(); }
        if (request.result == UnityWebRequest.Result.Success && request.responseCode is >= 200 and < 300)
          return UnityApiResult<Texture2D>.Success(DownloadHandlerTexture.GetContent(request), (int)request.responseCode, string.Empty);
        return UnityApiResult<Texture2D>.Failure(ReadError(string.Empty, request.error, (int)request.responseCode), (int)request.responseCode, string.Empty);
      }
      catch (OperationCanceledException)
      {
        return UnityApiResult<Texture2D>.Failure(UnityErrorMapper.FromTransport("TRANSPORT_TIMEOUT", "Card art download timed out."), 0, string.Empty);
      }
      catch (Exception ex)
      {
        return UnityApiResult<Texture2D>.Failure(UnityErrorMapper.FromTransport("TRANSPORT_ERROR", ex.Message), 0, string.Empty);
      }
    }

    private async Task<UnityApiResult<T>> SendAsync<T>(
      string method,
      string relativePath,
      string body,
      string sessionToken,
      CancellationToken cancellationToken)
    {
      using var linkedTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      linkedTimeout.CancelAfter(TimeSpan.FromSeconds(config.RequestTimeoutSeconds));

      try
      {
        using var request = new UnityWebRequest(ComposeUrl(relativePath), method);
        request.downloadHandler = new DownloadHandlerBuffer();

        if (!string.IsNullOrEmpty(body))
        {
          var bytes = Encoding.UTF8.GetBytes(body);
          request.uploadHandler = new UploadHandlerRaw(bytes);
          request.SetRequestHeader("Content-Type", "application/json");
        }

        request.SetRequestHeader("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
          request.SetRequestHeader("X-Session-Token", sessionToken);
        }

        var operation = request.SendWebRequest();
        while (!operation.isDone)
        {
          linkedTimeout.Token.ThrowIfCancellationRequested();
          await Task.Yield();
        }

        var raw = request.downloadHandler?.text ?? string.Empty;
        var status = (int)request.responseCode;
        if (request.result == UnityWebRequest.Result.Success && status >= 200 && status < 300)
        {
          var value = string.IsNullOrWhiteSpace(raw) ? default : JsonConvert.DeserializeObject<T>(raw, SerializerSettings);
          return UnityApiResult<T>.Success(value, status, config.DeveloperDiagnostics ? raw : string.Empty);
        }

        return UnityApiResult<T>.Failure(ReadError(raw, request.error, status), status, config.DeveloperDiagnostics ? raw : string.Empty);
      }
      catch (OperationCanceledException)
      {
        return UnityApiResult<T>.Failure(
          UnityErrorMapper.FromTransport("TRANSPORT_TIMEOUT", $"Request timed out after {config.RequestTimeoutSeconds:0.0}s."),
          0,
          string.Empty);
      }
      catch (Exception ex)
      {
        return UnityApiResult<T>.Failure(UnityErrorMapper.FromTransport("TRANSPORT_ERROR", ex.Message), 0, string.Empty);
      }
    }

    private static UnityError ReadError(string raw, string transportError, int status)
    {
      if (!string.IsNullOrWhiteSpace(raw))
      {
        try
        {
          var direct = JsonConvert.DeserializeObject<UnityError>(raw, SerializerSettings);
          if (direct != null && !string.IsNullOrWhiteSpace(direct.Code))
          {
            return direct;
          }

          var diagnosticEnvelope = JsonConvert.DeserializeObject<DiagnosticEnvelope>(raw, SerializerSettings);
          var diagnostic = diagnosticEnvelope?.Diagnostics?.Length > 0 ? diagnosticEnvelope.Diagnostics[0] : null;
          if (diagnostic != null)
          {
            return new UnityError
            {
              Code = diagnostic.Code,
              Message = diagnostic.Message,
              Retryable = false,
              SuggestedUnityBehavior = raw
            };
          }
        }
        catch
        {
          // Keep the original response for diagnostics below.
        }
      }

      var code = status switch
      {
        403 => "MATCH_SESSION_FORBIDDEN",
        429 => "RATE_LIMITED",
        >= 500 => "SERVER_FAULT",
        _ => "HTTP_ERROR"
      };

      return UnityErrorMapper.FromTransport(code, string.IsNullOrWhiteSpace(transportError) ? $"HTTP {status}" : transportError, raw);
    }

    private sealed class DiagnosticEnvelope
    {
      public Diagnostic[] Diagnostics { get; set; }
    }
  }
}
