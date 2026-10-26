namespace Game.Domain.Mutation;

public sealed class StateMutationHandlerRegistry
{
  private static readonly string[] PresentationOnlyMutationTypes =
  [
    "PLAY_AUDIO",
    "STOP_AUDIO",
    "LOAD_AUDIO_ASSET",
    "SET_CLIENT_VOLUME",
    "SET_CARD_HOVER_STATE",
    "SET_ANIMATION_FRAME",
    "SET_GIF_PLAYBACK_STATE"
  ];

  private readonly IReadOnlyDictionary<string, IStateMutationHandler> _handlers;

  public StateMutationHandlerRegistry(IEnumerable<IStateMutationHandler> handlers)
  {
    ArgumentNullException.ThrowIfNull(handlers);

    var registeredHandlers = new Dictionary<string, IStateMutationHandler>(StringComparer.Ordinal);
    foreach (var handler in handlers)
    {
      ArgumentNullException.ThrowIfNull(handler);
      RejectPresentationOnlyMutationType(handler.MutationType);

      if (!registeredHandlers.TryAdd(handler.MutationType, handler))
      {
        throw new InvalidOperationException(
          $"Duplicate state mutation handler for mutation type '{handler.MutationType}'.");
      }
    }

    _handlers = registeredHandlers;
  }

  public IStateMutationHandler GetRequiredHandler(string mutationType)
  {
    RejectPresentationOnlyMutationType(mutationType);

    if (_handlers.TryGetValue(mutationType, out var handler))
    {
      return handler;
    }

    throw new InvalidOperationException(
      $"No state mutation handler registered for mutation type '{mutationType}'.");
  }

  private static void RejectPresentationOnlyMutationType(string mutationType)
  {
    if (PresentationOnlyMutationTypes.Contains(mutationType, StringComparer.Ordinal))
    {
      throw new InvalidOperationException(
        $"Mutation type '{mutationType}' is presentation-only and cannot change gameplay state.");
    }
  }
}
