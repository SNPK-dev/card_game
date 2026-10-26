using System.Net;
using System.Text;
using Game.Generator.Providers;
using Game.Infrastructure.Generation;
using Xunit;

namespace Game.Integration.Tests;

public sealed class OpenAiPlayerGenerationProviderTests
{
  [Fact]
  public void NameAllocatorGuaranteesStableCaseInsensitiveUniqueNames()
  {
    var allocator = new GeneratedCardNameAllocator();

    var names = new[]
    {
      allocator.Allocate("Astral Keeper", 1),
      allocator.Allocate(" astral   keeper ", 2),
      allocator.Allocate("ASTRAL KEEPER", 3),
      allocator.Allocate(string.Empty, 4),
      allocator.Allocate(null, 5)
    };

    Assert.Equal(new[] { "Astral Keeper", "astral keeper 02", "ASTRAL KEEPER 03", "Generated Card", "Generated Card 05" }, names);
    Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
  }

  [Fact]
  public void StructuredProviderMapsOfficialResponsesPayload()
  {
    var json = """
      {"output":[{"content":[{"type":"output_text","text":"{\"name\":\"Astral Keeper\",\"manaCost\":3,\"attack\":3,\"health\":4,\"visualPrompt\":\"an astral guardian beneath a blue moon\"}"}]}]}
      """;
    using var client = new HttpClient(new StubHandler(HttpStatusCode.OK, json));
    var provider = new OpenAiStructuredCardGenerationProvider(client, new Uri("https://api.openai.com/v1/responses"), "test-model", "secret-test-key");

    var result = provider.Generate(new StructuredCardGenerationRequest("generation-1", "v1", "test-model", "astral guardians", new Dictionary<string, string>(), TimeSpan.FromSeconds(5)));

    Assert.True(result.Succeeded);
    Assert.Equal("Astral Keeper", result.Output!.Name);
    Assert.Equal(3, result.Output.ManaCost);
    Assert.Equal(3, result.Output.Attack);
    Assert.Equal(4, result.Output.Health);
  }

  [Fact]
  public void ImageProviderPersistsOfficialBase64Payload()
  {
    var bytes = Enumerable.Range(0, 512).Select(index => (byte)(index % 251)).ToArray();
    var json = "{\"data\":[{\"b64_json\":\"" + Convert.ToBase64String(bytes) + "\"}]}";
    var root = Path.Combine(Path.GetTempPath(), "chronicle-openai-test-" + Guid.NewGuid().ToString("N"));
    try
    {
      using var client = new HttpClient(new StubHandler(HttpStatusCode.OK, json));
      var provider = new OpenAiGeneratedAssetProvider(client, new Uri("https://api.openai.com/v1/images/generations"), "test-image-model", "secret-test-key", root);

      var result = provider.Generate(new GeneratedAssetProviderRequest("generation-1", "card-1", "card-1-v1", "STATIC_IMAGE", "astral guardian", "style", "safe", TimeSpan.FromSeconds(5)));

      Assert.True(result.Succeeded);
      Assert.True(File.Exists(result.AssetRef));
      Assert.Equal(bytes, File.ReadAllBytes(result.AssetRef));
    }
    finally
    {
      if (Directory.Exists(root)) Directory.Delete(root, true);
    }
  }

  [Fact]
  public void ProvidersFailClearlyWhenApiKeyIsMissing()
  {
    using var client = new HttpClient(new StubHandler(HttpStatusCode.OK, "{}"));
    var provider = new OpenAiStructuredCardGenerationProvider(client, new Uri("https://api.openai.com/v1/responses"), "test-model", string.Empty);

    var result = provider.Generate(new StructuredCardGenerationRequest("generation-1", "v1", "test-model", "prompt", new Dictionary<string, string>(), TimeSpan.FromSeconds(5)));

    Assert.False(result.Succeeded);
    Assert.Equal("OPENAI_API_KEY_MISSING", result.ErrorCode);
  }

  private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
  {
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
      new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
      Task.FromResult(Send(request, cancellationToken));
  }
}
