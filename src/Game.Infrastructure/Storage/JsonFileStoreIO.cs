using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;

namespace Game.Infrastructure.Storage;

internal static class JsonFileStoreIO
{
  private static readonly ConcurrentDictionary<string, object> PathLocks = new(StringComparer.OrdinalIgnoreCase);
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
  {
    WriteIndented = true,
    AllowOutOfOrderMetadataProperties = true,
    Converters = { new JsonStringEnumConverter() }
  };

  public static T? Load<T>(string filePath)
  {
    var fullPath = Path.GetFullPath(filePath);
    lock (PathLocks.GetOrAdd(fullPath, _ => new object()))
    {
      if (!File.Exists(fullPath)) return default;
      using var stream = File.OpenRead(fullPath);
      return JsonSerializer.Deserialize<T>(stream, JsonOptions);
    }
  }

  public static void Save<T>(string filePath, T value)
  {
    var fullPath = Path.GetFullPath(filePath);
    lock (PathLocks.GetOrAdd(fullPath, _ => new object()))
    {
      var directory = Path.GetDirectoryName(fullPath);
      if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
      var tempPath = $"{fullPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
      try
      {
        using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
          JsonSerializer.Serialize(stream, value, JsonOptions);

        MoveWithRetry(tempPath, fullPath);
      }
      finally
      {
        if (File.Exists(tempPath)) File.Delete(tempPath);
      }
    }
  }

  private static void MoveWithRetry(string source, string destination)
  {
    for (var attempt = 1; ; attempt++)
    {
      try { File.Move(source, destination, overwrite: true); return; }
      catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException) && attempt < 6)
      {
        Thread.Sleep(attempt * 15);
      }
    }
  }
}
