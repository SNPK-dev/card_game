namespace Game.Application.Decks;

public sealed record DeckValidationResult(
  bool IsValid,
  IReadOnlyList<DeckValidationDiagnostic> Diagnostics,
  string RulesetId = "",
  string RulesetVersion = "",
  string CatalogId = "",
  int ExpectedDeckSize = 0,
  int ActualDeckSize = 0,
  bool CopyLimitPassed = true,
  bool CardLegalityPassed = true)
{
  public static DeckValidationResult Valid(
    string rulesetId = "",
    string rulesetVersion = "",
    string catalogId = "",
    int expectedDeckSize = 0,
    int actualDeckSize = 0) =>
    new(true, [], rulesetId, rulesetVersion, catalogId, expectedDeckSize, actualDeckSize);

  public static DeckValidationResult Rejected(
    IReadOnlyList<DeckValidationDiagnostic> diagnostics,
    string rulesetId = "",
    string rulesetVersion = "",
    string catalogId = "",
    int expectedDeckSize = 0,
    int actualDeckSize = 0,
    bool copyLimitPassed = true,
    bool cardLegalityPassed = true) =>
    new(false, diagnostics, rulesetId, rulesetVersion, catalogId, expectedDeckSize, actualDeckSize, copyLimitPassed, cardLegalityPassed);
}
