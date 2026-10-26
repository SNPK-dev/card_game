#if UNITY_EDITOR
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;

namespace CardGame.Unity.Client.EditorTools
{
  [InitializeOnLoad]
  public static class UnityMcpConnector
  {
    static UnityMcpConnector()
    {
      EditorApplication.delayCall += ConnectWhenConfigured;
    }

    [MenuItem("CardGame/Tools/Connect Unity MCP")]
    public static async void Connect()
    {
      EditorPrefs.SetBool("MCPForUnity.UseHttpTransport", true);
      EditorPrefs.SetString("MCPForUnity.HttpTransportScope", "remote");
      var endpoint = System.Environment.GetEnvironmentVariable("CARDGAME_UNITY_MCP_ENDPOINT");
      if (string.IsNullOrWhiteSpace(endpoint))
        endpoint = EditorPrefs.GetString("CardGame.UnityMcpEndpoint", "http://127.0.0.1:18088");
      endpoint = endpoint.TrimEnd('/');
      EditorPrefs.SetString("MCPForUnity.HttpRemoteUrl", endpoint);
      if (System.Uri.TryCreate(endpoint, System.UriKind.Absolute, out var endpointUri) && endpointUri.IsLoopback)
        EditorPrefs.SetBool("MCPForUnity.Security.AllowInsecureRemoteHttp", true);
      EditorPrefs.SetBool("MCPForUnity.ProjectScopedTools.LocalHttp", true);
      EditorPrefs.SetBool("MCPForUnity.AutoRegisterEnabled", true);
      var connected = await MCPServiceLocator.Bridge.StartAsync();
      Debug.Log($"CardGame Unity MCP connection: {(connected ? "connected" : "failed")}.");
    }

    private static void ConnectWhenConfigured()
    {
      if (EditorPrefs.GetBool("CardGame.UnityMcpAutoConnect", true))
      {
        Connect();
      }
    }
  }
}
#endif
