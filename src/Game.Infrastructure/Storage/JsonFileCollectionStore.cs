namespace Game.Infrastructure.Storage;

internal sealed class JsonFileCollectionStore<T>
{
  private readonly string filePath;
  private readonly Func<T, string> getId;
  private readonly List<T> items;
  private readonly HashSet<string> ids;

  public JsonFileCollectionStore(string filePath, Func<T, string> getId)
  {
    if (string.IsNullOrWhiteSpace(filePath))
    {
      throw new ArgumentException("Storage file path is required.", nameof(filePath));
    }

    this.filePath = filePath;
    this.getId = getId ?? throw new ArgumentNullException(nameof(getId));
    items = Load(filePath);
    ids = new HashSet<string>(items.Select(getId), StringComparer.Ordinal);
    if (ids.Count != items.Count)
    {
      throw new InvalidOperationException($"Storage file '{filePath}' contains duplicate ids.");
    }
  }

  public T? Latest => items.Count == 0 ? default : items[^1];

  public IReadOnlyList<T> Items => items.ToArray();

  public bool TryStore(T item)
  {
    ArgumentNullException.ThrowIfNull(item);

    if (!ids.Add(getId(item)))
    {
      return false;
    }

    items.Add(item);
    Save();
    return true;
  }

  public T? FindById(string id) =>
    items.FirstOrDefault(item => string.Equals(getId(item), id, StringComparison.Ordinal));

  private static List<T> Load(string filePath)
  {
    if (!File.Exists(filePath))
    {
      return [];
    }

    return JsonFileStoreIO.Load<List<T>>(filePath) ?? [];
  }

  private void Save()
  {
    JsonFileStoreIO.Save(filePath, items);
  }
}
