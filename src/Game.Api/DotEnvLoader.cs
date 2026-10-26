namespace Game.Api;

public static class DotEnvLoader
{
  public static string? LoadNearest(string? startDirectory = null)
  {
    var directory = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
    while (directory is not null)
    {
      var path = Path.Combine(directory.FullName, ".env");
      if (File.Exists(path))
      {
        foreach (var rawLine in File.ReadLines(path))
        {
          var line = rawLine.Trim();
          if (line.Length == 0 || line.StartsWith('#')) continue;
          var separator = line.IndexOf('=');
          if (separator <= 0) continue;
          var name = line[..separator].Trim();
          if (name.Length == 0 || Environment.GetEnvironmentVariable(name) is not null) continue;
          var value = line[(separator + 1)..].Trim();
          if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))) value = value[1..^1];
          Environment.SetEnvironmentVariable(name, value);
        }
        return path;
      }
      directory = directory.Parent;
    }
    return null;
  }
}
