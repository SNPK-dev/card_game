namespace Game.Infrastructure.Generation;

internal sealed class GeneratedCardNameAllocator
{
  private const int MaxNameLength = 64;
  private readonly HashSet<string> allocated = new(StringComparer.OrdinalIgnoreCase);

  public string Allocate(string? proposedName, int ordinal)
  {
    var baseName = string.Join(' ', (proposedName ?? string.Empty)
      .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    if (string.IsNullOrWhiteSpace(baseName)) baseName = "Generated Card";
    baseName = baseName[..Math.Min(baseName.Length, MaxNameLength)];
    if (allocated.Add(baseName)) return baseName;

    for (var suffixOrdinal = Math.Max(2, ordinal); ; suffixOrdinal++)
    {
      var suffix = $" {suffixOrdinal:00}";
      var prefix = baseName[..Math.Min(baseName.Length, MaxNameLength - suffix.Length)].TrimEnd();
      var candidate = prefix + suffix;
      if (allocated.Add(candidate)) return candidate;
    }
  }
}
