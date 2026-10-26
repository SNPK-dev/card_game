using CardGame.Unity.Client.DebugTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CardGame.Unity.Client.Editor.Build
{
  public static class PolishedGameplaySceneBuilder
  {
    private const string LocalMatchScenePath = "Assets/Scenes/CardGameLocalMatch.unity";
    private const string RootPrefabPath = "Assets/CardGame/Prefabs/CardGameClientRoot.prefab";

    private static readonly string[] RootChildren =
    {
      "Bootstrap",
      "Canvas",
      "Status",
      "Interaction",
      "Presentation",
      "Diagnostics",
      "Audio"
    };

    private static readonly string[] CanvasChildren =
    {
      "Background",
      "Header",
      "OpponentPanel",
      "OpponentHand",
      "Board",
      "PlayerPanel",
      "PlayerHand",
      "InteractionPanel",
      "PresentationOverlay",
      "ChoiceModal",
      "TargetOverlay",
      "EventFeed",
      "DiagnosticsPanel",
      "AudioControls"
    };

    [MenuItem("Card Game/Rebuild Polished Local Match Root")]
    public static void RebuildMenu()
    {
      RebuildOrThrow();
      Debug.Log("Rebuilt polished CardGameClientRoot scene and prefab hierarchy.");
    }

    public static void RebuildBatch()
    {
      try
      {
        RebuildOrThrow();
      }
      catch (System.Exception ex)
      {
        Debug.LogError(ex);
        EditorApplication.Exit(1);
        return;
      }

      EditorApplication.Exit(0);
    }

    private static void RebuildOrThrow()
    {
      var scene = EditorSceneManager.OpenScene(LocalMatchScenePath, OpenSceneMode.Single);
      foreach (var rootObject in scene.GetRootGameObjects())
      {
        if (rootObject.name == "CardGameClientRoot")
        {
          Object.DestroyImmediate(rootObject);
        }
      }

      var root = CreateRoot();
      EditorSceneManager.MarkSceneDirty(scene);
      EditorSceneManager.SaveScene(scene);

      var prefabRoot = CreateRoot();
      PrefabUtility.SaveAsPrefabAsset(prefabRoot, RootPrefabPath);
      Object.DestroyImmediate(prefabRoot);
      AssetDatabase.SaveAssets();
    }

    private static GameObject CreateRoot()
    {
      var root = new GameObject("CardGameClientRoot");
      root.AddComponent<DebugMatchBootstrapper>();
      foreach (var childName in RootChildren)
      {
        var child = new GameObject(childName);
        child.transform.SetParent(root.transform, false);
      }

      var canvas = root.transform.Find("Canvas");
      foreach (var childName in CanvasChildren)
      {
        var child = new GameObject(childName);
        child.transform.SetParent(canvas, false);
      }

      return root;
    }
  }
}
