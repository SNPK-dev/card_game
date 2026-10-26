namespace Game.Application.Catalog;

public interface ICatalogCompatibilityValidator
{
  CatalogCompatibilityResult Validate(CatalogCompatibilityRequest request);
}
