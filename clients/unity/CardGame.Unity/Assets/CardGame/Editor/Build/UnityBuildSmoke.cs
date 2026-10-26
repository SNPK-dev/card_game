using System;
using System.Linq;
using UnityEditor.Build.Reporting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CardGame.Unity.Client.Editor.Build
{
  public static class UnityBuildSmoke
  {
    private const string LocalMatchScenePath = "Assets/Scenes/CardGameLocalMatch.unity";
    private static readonly string[] RequiredRootChildren =
    {
      "Bootstrap",
      "Canvas",
      "Status",
      "Interaction",
      "Presentation",
      "Diagnostics",
      "Audio"
    };

    private static readonly string[] RequiredCanvasChildren =
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

    private static readonly string[] RequiredPrefabAssets =
    {
      "Assets/CardGame/Prefabs/CardGameClientRoot.prefab",
      "Assets/CardGame/Prefabs/UI/CardFace.prefab",
      "Assets/CardGame/Prefabs/UI/CardBack.prefab",
      "Assets/CardGame/Prefabs/UI/CardInHand.prefab",
      "Assets/CardGame/Prefabs/UI/CardOnBoard.prefab",
      "Assets/CardGame/Prefabs/UI/BoardView.prefab",
      "Assets/CardGame/Prefabs/UI/EntityView.prefab",
      "Assets/CardGame/Prefabs/UI/HeroPanel.prefab",
      "Assets/CardGame/Prefabs/UI/ResourcePanel.prefab"
    };

    [MenuItem("Card Game/Validate Local Match Build Smoke")]
    public static void ValidateMenu()
    {
      ValidateOrThrow();
      Debug.Log("CardGame Unity build smoke validation passed.");
    }

    public static void ValidateBatch()
    {
      try
      {
        ValidateOrThrow();
      }
      catch (Exception ex)
      {
        Debug.LogError(ex.Message);
        EditorApplication.Exit(1);
        return;
      }

      EditorApplication.Exit(0);
    }

    public static void BuildStandaloneDevelopmentBatch()
    {
      ValidateAndBuild(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, ResolveOutputPath("artifacts/unity-dev-builds/standalone/CardGameUnity.exe"), BuildOptions.Development);
    }

    public static void BuildWebGlDevelopmentBatch()
    {
      ValidateAndBuild(BuildTarget.WebGL, BuildTargetGroup.WebGL, ResolveOutputPath("artifacts/unity-dev-builds/webgl"), BuildOptions.Development);
    }

    public static void BuildStandaloneReleaseCandidateBatch()
    {
      ValidateAndBuild(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, ResolveOutputPath("artifacts/releases/latest/client/windows/CardGame.exe"), BuildOptions.None);
    }

    public static void BuildWebGlReleaseCandidateBatch()
    {
      ValidateAndBuild(BuildTarget.WebGL, BuildTargetGroup.WebGL, ResolveOutputPath("artifacts/unity-builds/webgl"), BuildOptions.None);
    }

    private static void ValidateAndBuild(BuildTarget target, BuildTargetGroup group, string outputPath, BuildOptions options)
    {
      try
      {
        ValidateOrThrow();
        if (!BuildPipeline.IsBuildTargetSupported(group, target))
        {
          Debug.LogWarning("Unity build target module is not installed or not supported in this editor: " + target);
          EditorApplication.Exit(0);
          return;
        }

        var outputDirectory = target == BuildTarget.WebGL
          ? outputPath
          : System.IO.Path.GetDirectoryName(outputPath) ?? outputPath;
        System.IO.Directory.CreateDirectory(outputDirectory);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
          scenes = new[] { LocalMatchScenePath },
          locationPathName = outputPath,
          target = target,
          options = options
        });

        if (report.summary.result != BuildResult.Succeeded)
        {
          Debug.LogError("Unity build failed: " + report.summary.result);
          EditorApplication.Exit(1);
          return;
        }
      }
      catch (Exception ex)
      {
        Debug.LogError(ex.Message);
        EditorApplication.Exit(1);
        return;
      }

      EditorApplication.Exit(0);
    }

    private static string ResolveOutputPath(string fallback)
    {
      var configured = Environment.GetEnvironmentVariable("CARDGAME_UNITY_BUILD_OUTPUT_PATH");
      return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
    }

    private static void ValidateOrThrow()
    {
      var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(LocalMatchScenePath);
      if (scene == null)
      {
        throw new InvalidOperationException("Local match scene is missing: " + LocalMatchScenePath);
      }

      if (!EditorBuildSettings.scenes.Any(entry => entry.enabled && entry.path == LocalMatchScenePath))
      {
        throw new InvalidOperationException("Local match scene is not enabled in EditorBuildSettings.");
      }

      var opened = EditorSceneManager.OpenScene(LocalMatchScenePath, OpenSceneMode.Single);
      var root = opened.GetRootGameObjects().FirstOrDefault(obj => obj.name == "CardGameClientRoot");
      if (root == null)
      {
        throw new InvalidOperationException("CardGameClientRoot is missing from local match scene.");
      }

      if (root.GetComponent<DebugTools.DebugMatchBootstrapper>() == null)
      {
        throw new InvalidOperationException("CardGameClientRoot is missing DebugMatchBootstrapper.");
      }

      foreach (var childName in RequiredRootChildren)
      {
        if (root.transform.Find(childName) == null)
        {
          throw new InvalidOperationException("CardGameClientRoot is missing child hierarchy node: " + childName);
        }
      }

      var canvas = root.transform.Find("Canvas");
      foreach (var childName in RequiredCanvasChildren)
      {
        if (canvas == null || canvas.Find(childName) == null)
        {
          throw new InvalidOperationException("CardGameClientRoot/Canvas is missing gameplay UI region: " + childName);
        }
      }

      foreach (var prefabPath in RequiredPrefabAssets)
      {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
        {
          throw new InvalidOperationException("Required polished gameplay prefab is missing or invalid: " + prefabPath);
        }
      }

      RequireType("audio controller", typeof(Client.Audio.AudioPlaybackController));
      RequireType("presentation overlay", typeof(Client.UI.Presentation.PresentationPanelBinder));
      RequireType("card face binder", typeof(Client.UI.Cards.CardFaceView));
      RequireType("board binder", typeof(Client.UI.Board.BoardView));
      RequireType("production player application", typeof(Client.UI.PlayerApplicationController));

      var missingScriptCount = opened.GetRootGameObjects()
        .Sum(CountMissingScripts);
      if (missingScriptCount > 0)
      {
        throw new InvalidOperationException("Local match scene contains missing script references: " + missingScriptCount);
      }

      var gitignore = FindRepositoryGitignore();
      var ignoredOutputConfigured = System.IO.File.Exists(gitignore) &&
        System.IO.File.ReadAllText(gitignore).Contains("artifacts/");
      if (!ignoredOutputConfigured)
      {
        throw new InvalidOperationException("Build output ignore rule is missing for artifacts/.");
      }

      RequireScript("scripts/unity/run-unity-playmode-smoke.sh");
      RequireScript("scripts/unity/run-unity-dev-build-smoke.sh");
    }

    private static void RequireType(string label, Type type)
    {
      if (type == null)
      {
        throw new InvalidOperationException("Required polished gameplay type is missing: " + label);
      }
    }

    private static void RequireScript(string relativePath)
    {
      var gitignore = FindRepositoryGitignore();
      var root = string.IsNullOrWhiteSpace(gitignore) ? string.Empty : System.IO.Path.GetDirectoryName(gitignore);
      var candidate = string.IsNullOrWhiteSpace(root) ? relativePath : System.IO.Path.Combine(root, relativePath);
      if (!System.IO.File.Exists(candidate))
      {
        throw new InvalidOperationException("Required Unity validation script is missing: " + relativePath);
      }
    }

    private static int CountMissingScripts(GameObject gameObject)
    {
      var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
      foreach (Transform child in gameObject.transform)
      {
        count += CountMissingScripts(child.gameObject);
      }

      return count;
    }

    private static string FindRepositoryGitignore()
    {
      var directory = new System.IO.DirectoryInfo(Application.dataPath);
      while (directory != null)
      {
        var candidate = System.IO.Path.Combine(directory.FullName, ".gitignore");
        if (System.IO.File.Exists(candidate))
        {
          return candidate;
        }

        directory = directory.Parent;
      }

      return string.Empty;
    }
  }
}
