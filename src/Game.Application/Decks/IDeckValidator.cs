namespace Game.Application.Decks;

public interface IDeckValidator
{
  DeckValidationResult Validate(DeckValidationRequest request);
}
