using System.Reflection;
using System.Xml.Linq;
using Game.Domain.Commands;
using Xunit;

namespace Game.Architecture.Tests;

public sealed class DomainBoundaryTests
{
  private static readonly string[] ForbiddenAssemblyPrefixes =
  [
    "Game.Infrastructure",
    "Game.Api",
    "OpenAI",
    "Azure.AI.OpenAI",
    "Microsoft.SemanticKernel",
    "System.Net.Http",
    "Microsoft.EntityFrameworkCore",
    "Npgsql",
    "MongoDB",
    "Dapper",
    "SkiaSharp",
    "SixLabors",
    "NAudio"
  ];

  private static readonly string[] ForbiddenPackageNameParts =
  [
    "OpenAI",
    "SemanticKernel",
    "Http",
    "EntityFramework",
    "Npgsql",
    "MongoDB",
    "Dapper",
    "SqlClient",
    "SkiaSharp",
    "SixLabors",
    "NAudio",
    "Serilog",
    "ApplicationInsights",
    "AWS",
    "Google.Cloud"
  ];

  [Fact]
  public void DomainDoesNotReferenceForbiddenAssemblies()
  {
    var referencedAssemblyNames = typeof(IGameCommand)
      .Assembly
      .GetReferencedAssemblies()
      .Select(name => name.Name ?? string.Empty)
      .ToArray();

    foreach (var forbiddenPrefix in ForbiddenAssemblyPrefixes)
    {
      Assert.DoesNotContain(
        referencedAssemblyNames,
        assemblyName => assemblyName.StartsWith(forbiddenPrefix, StringComparison.OrdinalIgnoreCase));
    }
  }

  [Fact]
  public void DomainProjectReferencesOnlySharedProject()
  {
    var project = XDocument.Load(GetRepositoryPath("src", "Game.Domain", "Game.Domain.csproj"));
    var references = project
      .Descendants("ProjectReference")
      .Select(element => element.Attribute("Include")?.Value.Replace('\\', '/'))
      .OfType<string>()
      .ToArray();

    Assert.Equal(new[] { "../Game.Shared/Game.Shared.csproj" }, references);
  }

  [Fact]
  public void DomainProjectDoesNotContainForbiddenPackageReferences()
  {
    var project = XDocument.Load(GetRepositoryPath("src", "Game.Domain", "Game.Domain.csproj"));
    var packages = project
      .Descendants("PackageReference")
      .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
      .ToArray();

    foreach (var forbiddenPart in ForbiddenPackageNameParts)
    {
      Assert.DoesNotContain(
        packages,
        package => package.Contains(forbiddenPart, StringComparison.OrdinalIgnoreCase));
    }
  }

  [Fact]
  public void GeneratorProjectReferencesOnlySharedProject()
  {
    var project = XDocument.Load(GetRepositoryPath("src", "Game.Generator", "Game.Generator.csproj"));
    var references = project
      .Descendants("ProjectReference")
      .Select(element => element.Attribute("Include")?.Value.Replace('\\', '/'))
      .OfType<string>()
      .ToArray();

    Assert.Equal(new[] { "../Game.Shared/Game.Shared.csproj" }, references);
  }

  [Fact]
  public void ProductionSourceDoesNotUseBootstrapOnlyNames()
  {
    var sourceRoot = GetRepositoryPath("src");
    var forbiddenNames = new[] { "Bootstrap", "Stub", "FirstPlayable" };
    var violations = Directory
      .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
      .SelectMany(file =>
        File.ReadLines(file)
          .Select((line, index) => new { file, line, lineNumber = index + 1 }))
      .Where(item => forbiddenNames.Any(name => item.line.Contains(name, StringComparison.Ordinal)))
      .Select(item => $"{Path.GetRelativePath(GetRepositoryPath(), item.file)}:{item.lineNumber}: {item.line.Trim()}")
      .Order(StringComparer.Ordinal)
      .ToArray();

    Assert.Empty(violations);
  }

  private static string GetRepositoryPath(params string[] parts)
  {
    var current = Directory.GetCurrentDirectory();
    while (current is not null)
    {
      var candidate = Path.Combine(current, "Game.sln");
      if (Path.Exists(candidate))
      {
        return Path.Combine([current, .. parts]);
      }

      current = Directory.GetParent(current)?.FullName;
    }

    throw new InvalidOperationException("Could not locate repository root.");
  }
}
