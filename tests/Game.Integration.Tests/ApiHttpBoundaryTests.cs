using System.Net;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Game.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Game.Integration.Tests;

public sealed class ApiHttpBoundaryTests
{
  [Fact]
  public async Task MalformedJsonRequestReturnsBadRequest()
  {
    await using var factory = new WebApplicationFactory<Program>();
    using var client = factory.CreateClient();
    using var content = new StringContent("{ malformed-json", Encoding.UTF8, "application/json");

    using var response = await client.PostAsync("/sessions", content, TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task OversizedJsonRequestReturnsPayloadTooLarge()
  {
    using var metrics = new MetricCapture("game.api.requests.rejected");
    await using var factory = new WebApplicationFactory<Program>();
    using var client = factory.CreateClient();
    using var content = new StringContent(
      "{\"playerId\":\"" + new string('x', 1_100_000) + "\"}",
      Encoding.UTF8,
      "application/json");

    using var response = await client.PostAsync("/sessions", content, TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    Assert.Contains("REQUEST_BODY_TOO_LARGE", body, StringComparison.Ordinal);
    Assert.True(metrics.Contains("game.api.requests.rejected", "reason", "request_body_too_large"));
  }

  [Fact]
  public async Task HealthEndpointRemainsAvailableWithoutRequestBody()
  {
    await using var factory = new WebApplicationFactory<Program>();
    using var client = factory.CreateClient();

    using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task EngineCapabilitiesExposeUnityCompatibilityFields()
  {
    await using var factory = new WebApplicationFactory<Program>();
    using var client = factory.CreateClient();

    using var response = await client.GetAsync("/engine/capabilities", TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    var root = document.RootElement;
    Assert.Equal("unity-contract-2026-07-07", root.GetProperty("apiContractVersion").GetString());
    Assert.Equal("match-state-v1", root.GetProperty("stateSchemaVersion").GetString());
    Assert.Equal("match-events-v1", root.GetProperty("eventSchemaVersion").GetString());
    Assert.Equal("unity-contract-2026-07-07", root.GetProperty("minimumUnityClientContractVersion").GetString());
    Assert.Contains(root.GetProperty("supportedCommandTypes").EnumerateArray(), value => value.GetString() == "END_TURN");
    Assert.Contains(root.GetProperty("supportedCommandTypes").EnumerateArray(), value => value.GetString() == "RESOLVE_CHOICE");
    Assert.Contains(root.GetProperty("supportedEventTypes").EnumerateArray(), value => value.GetString() == "GAME_STARTED");
    Assert.Contains(root.GetProperty("supportedEventTypes").EnumerateArray(), value => value.GetString() == "CHOICE_RESOLVED");
    Assert.Contains(root.GetProperty("supportedActionTypes").EnumerateArray(), value => value.GetString() == "END_TURN");
  }

  [Fact]
  public async Task CorsIsDisabledByDefault()
  {
    await using var factory = new WebApplicationFactory<Program>();
    using var client = factory.CreateClient();
    using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
    request.Headers.Add("Origin", "http://localhost:8080");

    using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
  }

  [Fact]
  public async Task ConfiguredCorsAllowsUnityWebglOriginAndSessionTokenPreflight()
  {
    await using var factory = CreateCorsFactory();
    using var client = factory.CreateClient();
    using var request = new HttpRequestMessage(HttpMethod.Options, "/matches/demo/state");
    request.Headers.Add("Origin", "http://localhost:8080");
    request.Headers.Add("Access-Control-Request-Method", "GET");
    request.Headers.Add("Access-Control-Request-Headers", "X-Session-Token");

    using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
    Assert.Equal("http://localhost:8080", Assert.Single(origins));
    Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Headers", out var headers));
    Assert.Contains("X-Session-Token", string.Join(",", headers), StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task ConfiguredCorsDoesNotAllowUnknownOrigin()
  {
    await using var factory = CreateCorsFactory();
    using var client = factory.CreateClient();
    using var request = new HttpRequestMessage(HttpMethod.Options, "/matches/demo/state");
    request.Headers.Add("Origin", "https://malicious.example");
    request.Headers.Add("Access-Control-Request-Method", "GET");
    request.Headers.Add("Access-Control-Request-Headers", "X-Session-Token");

    using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

    Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
  }

  [Fact]
  public async Task WriteEndpointsRejectBurstRequestsWithTooManyRequests()
  {
    using var metrics = new MetricCapture("game.api.requests.rejected");
    await using var factory = new WebApplicationFactory<Program>();
    using var client = factory.CreateClient();

    for (var i = 0; i < 60; i++)
    {
      using var acceptedContent = BuildSessionContent(i);
      using var acceptedResponse = await client.PostAsync(
        "/sessions",
        acceptedContent,
        TestContext.Current.CancellationToken);

      Assert.Equal(HttpStatusCode.OK, acceptedResponse.StatusCode);
    }

    using var rejectedContent = BuildSessionContent(60);
    using var rejectedResponse = await client.PostAsync(
      "/sessions",
      rejectedContent,
      TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.TooManyRequests, rejectedResponse.StatusCode);
    var body = await rejectedResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    Assert.Contains("RATE_LIMIT_EXCEEDED", body, StringComparison.Ordinal);
    Assert.True(metrics.Contains("game.api.requests.rejected", "reason", "rate_limit_exceeded"));

    using var healthResponse = await client.GetAsync("/health", TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
  }

  [Fact]
  public void ApiTelemetryPublishesAuthorizationAndCommandCounters()
  {
    using var metrics = new MetricCapture(
      "game.api.authorization.denied",
      "game.api.commands.admitted");
    using var telemetry = new ApiTelemetry(NullLogger<ApiTelemetry>.Instance);

    telemetry.RecordAuthorizationDenied("match_state", "match-observed");
    telemetry.RecordCommandAdmission(
      succeeded: true,
      "match-observed",
      "player-1",
      "END_TURN",
      "accepted");
    telemetry.RecordCommandAdmission(
      succeeded: false,
      "match-observed",
      "player-1",
      "END_TURN",
      "COMMAND_PRE_STATE_HASH_MISMATCH");

    Assert.True(metrics.Contains("game.api.authorization.denied", "endpoint", "match_state"));
    Assert.True(metrics.Contains("game.api.commands.admitted", "result", "accepted"));
    Assert.True(metrics.Contains("game.api.commands.admitted", "reason", "COMMAND_PRE_STATE_HASH_MISMATCH"));
  }

  private static StringContent BuildSessionContent(int index) =>
    new(
      $"{{\"playerId\":\"rate-player-{index}\",\"sessionToken\":\"rate-token-{index}\"}}",
      Encoding.UTF8,
      "application/json");

  private static WebApplicationFactory<Program> CreateCorsFactory() =>
    new WebApplicationFactory<Program>()
      .WithWebHostBuilder(builder =>
      {
        builder.UseSetting("Api:Cors:Enabled", "true");
        builder.UseSetting("Api:Cors:AllowedOrigins", "http://localhost:8080,http://127.0.0.1:8080");
        builder.UseSetting("Api:Cors:AllowedMethods", "GET,POST,OPTIONS");
        builder.UseSetting("Api:Cors:AllowedHeaders", "Content-Type,X-Session-Token");
      });

  private sealed class MetricCapture : IDisposable
  {
    private readonly ConcurrentBag<MetricMeasurement> _measurements = [];
    private readonly HashSet<string> _instrumentNames;
    private readonly MeterListener _listener = new();

    public MetricCapture(params string[] instrumentNames)
    {
      _instrumentNames = new HashSet<string>(instrumentNames, StringComparer.Ordinal);
      _listener.InstrumentPublished = (instrument, listener) =>
      {
        if (instrument.Meter.Name == ApiTelemetry.MeterName
          && _instrumentNames.Contains(instrument.Name))
        {
          listener.EnableMeasurementEvents(instrument);
        }
      };
      _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
      {
        _measurements.Add(new MetricMeasurement(
          instrument.Name,
          measurement,
          tags.ToArray()));
      });
      _listener.Start();
    }

    public bool Contains(string instrumentName, string tagName, string tagValue) =>
      _measurements.Any(measurement =>
        measurement.InstrumentName == instrumentName
        && measurement.Value > 0
        && measurement.Tags.Any(tag =>
          tag.Key == tagName
          && string.Equals(tag.Value?.ToString(), tagValue, StringComparison.Ordinal)));

    public void Dispose() => _listener.Dispose();
  }

  private sealed record MetricMeasurement(
    string InstrumentName,
    long Value,
    KeyValuePair<string, object?>[] Tags);
}
