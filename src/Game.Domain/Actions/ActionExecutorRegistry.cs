namespace Game.Domain.Actions;

public sealed class ActionExecutorRegistry
{
  private readonly IReadOnlyDictionary<string, IActionExecutor> _executors;

  public ActionExecutorRegistry(IEnumerable<IActionExecutor> executors)
  {
    ArgumentNullException.ThrowIfNull(executors);

    var byType = new Dictionary<string, IActionExecutor>(StringComparer.Ordinal);
    foreach (var executor in executors)
    {
      if (string.IsNullOrWhiteSpace(executor.ActionType))
      {
        throw new InvalidOperationException("Action executor type must be set.");
      }

      if (!byType.TryAdd(executor.ActionType, executor))
      {
        throw new InvalidOperationException(
          $"Duplicate action executor for action type '{executor.ActionType}'.");
      }
    }

    _executors = byType;
  }

  public IActionExecutor GetRequiredExecutor(string actionType)
  {
    if (!_executors.TryGetValue(actionType, out var executor))
    {
      throw new InvalidOperationException(
        $"No action executor registered for action type '{actionType}'.");
    }

    return executor;
  }
}
