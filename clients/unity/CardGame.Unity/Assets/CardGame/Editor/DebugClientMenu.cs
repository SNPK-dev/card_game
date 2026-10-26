using CardGame.Unity.Client.DebugTools;
using UnityEditor;
using UnityEngine;

namespace CardGame.Unity.Client.Editor
{
  public static class DebugClientMenu
  {
    [MenuItem("Card Game/Create Debug Client Root")]
    public static void CreateDebugClientRoot()
    {
      var existing = Object.FindObjectOfType<DebugMatchBootstrapper>();
      if (existing != null)
      {
        Selection.activeObject = existing.gameObject;
        return;
      }

      var root = new GameObject("CardGameClientRoot");
      root.AddComponent<DebugMatchBootstrapper>();
      Selection.activeObject = root;
      Undo.RegisterCreatedObjectUndo(root, "Create CardGame Debug Client");
    }
  }
}
