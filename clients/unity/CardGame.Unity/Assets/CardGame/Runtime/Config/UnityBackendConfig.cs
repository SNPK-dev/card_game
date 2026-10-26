using UnityEngine;

namespace CardGame.Unity.Client.Config
{
  [CreateAssetMenu(menuName = "Card Game/Unity Backend Config", fileName = "UnityBackendConfig")]
  public sealed class UnityBackendConfig : ScriptableObject
  {
    public const string DefaultLocalBackendUrl = "http://localhost:5000";

    [SerializeField] private string backendBaseUrl = DefaultLocalBackendUrl;
    [SerializeField] private float pollingIntervalSeconds = 1.0f;
    [SerializeField] private float requestTimeoutSeconds = 10.0f;
    [SerializeField] private bool developerDiagnostics = true;
    [SerializeField] private bool seedDemoDataFromUnity = true;
    [SerializeField] private bool autoBootDebugMatch = false;

    public string BackendBaseUrl => string.IsNullOrWhiteSpace(backendBaseUrl) ? DefaultLocalBackendUrl : backendBaseUrl.Trim();
    public float PollingIntervalSeconds => Mathf.Max(0.25f, pollingIntervalSeconds);
    public float RequestTimeoutSeconds => Mathf.Max(1.0f, requestTimeoutSeconds);
    public bool DeveloperDiagnostics => developerDiagnostics;
    public bool SeedDemoDataFromUnity => seedDemoDataFromUnity;
    public bool AutoBootDebugMatch => autoBootDebugMatch;

    public static UnityBackendConfig CreateRuntimeDefault()
    {
      var config = CreateInstance<UnityBackendConfig>();
      config.backendBaseUrl = DefaultLocalBackendUrl;
      config.pollingIntervalSeconds = 1.0f;
      config.requestTimeoutSeconds = 10.0f;
      config.developerDiagnostics = true;
      config.seedDemoDataFromUnity = true;
      config.autoBootDebugMatch = false;
      return config;
    }
  }
}
