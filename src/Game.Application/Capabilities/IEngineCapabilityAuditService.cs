namespace Game.Application.Capabilities;

public interface IEngineCapabilityAuditService
{
  EngineCapabilityAuditReport Audit(EngineCapabilityAuditRequest request);
}
