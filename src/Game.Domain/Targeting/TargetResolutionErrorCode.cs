namespace Game.Domain.Targeting;

public enum TargetResolutionErrorCode
{
  MissingRequiredTarget,
  TooFewTargets,
  TooManyTargets,
  InvalidChosenTarget,
  UnsupportedSelectionMode,
  UnsupportedZone,
  UnsupportedControllerScope,
  UnsupportedTagFilter,
  InvalidTargetSpec
}
