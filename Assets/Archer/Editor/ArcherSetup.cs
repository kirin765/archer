using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Archer.EditorTools
{
    /// <summary>
    /// One-shot project setup for the archer vertical slice:
    /// creates a 3D Universal Renderer (the template ships a 2D one) and a
    /// playable scene containing just the <see cref="GameBootstrap"/> object.
    /// </summary>
    public static class ArcherSetup
    {
        const string RendererPath = "Assets/Settings/UniversalRenderer3D.asset";
        const string UrpPath = "Assets/Settings/UniversalRP3D.asset";
        const string ScenePath = "Assets/Scenes/Game.unity";
        const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("Archer/Setup Project (3D + Game Scene)")]
        public static void Setup()
        {
            ConfigurePortrait();
            SetupRenderPipeline();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            CreateGameScene();
            Debug.Log("[Archer] Setup complete. Open Assets/Scenes/Game.unity, set the Game view to a 9:16 size, and press Play.");
        }

        [MenuItem("Archer/Apply Portrait Settings")]
        public static void ConfigurePortrait()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.useAnimatedAutorotation = false;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            PlayerSettings.defaultScreenWidth = 1080;
            PlayerSettings.defaultScreenHeight = 1920;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;

            Debug.Log("[Archer] Portrait settings applied (1080x1920, portrait only).");
        }

        static void SetupRenderPipeline()
        {
            var urpType = typeof(UniversalRenderPipelineAsset);
            var rendererTypeType = typeof(RendererType);

            var createRendererAsset = urpType.GetMethod("CreateRendererAsset",
                BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(string), rendererTypeType, typeof(bool), typeof(string) }, null);
            if (createRendererAsset == null)
                throw new InvalidOperationException("Could not find UniversalRenderPipelineAsset.CreateRendererAsset.");

            var rendererData = (ScriptableRendererData)createRendererAsset.Invoke(null,
                new object[] { RendererPath, RendererType.UniversalRenderer, false, "Renderer" });

            var createPipeline = urpType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
            var urp = (UniversalRenderPipelineAsset)createPipeline.Invoke(null, new object[] { rendererData });

            TrySet(urpType, urp, "msaaSampleCount", 4);
            TrySet(urpType, urp, "supportsHDR", true);
            TrySet(urpType, urp, "shadowDistance", 60f);

            AssetDatabase.CreateAsset(urp, UrpPath);
            EditorUtility.SetDirty(urp);

            GraphicsSettings.defaultRenderPipeline = urp;

            int level = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(level, false);

            Debug.Log($"[Archer] Render pipeline set to {UrpPath} with {RendererPath}.");
        }

        static void TrySet(Type type, object target, string property, object value)
        {
            var prop = type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite) prop.SetValue(target, value);
        }

        static void CreateGameScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("GameBootstrap");
            go.AddComponent<GameBootstrap>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene(SampleScenePath, true)
            };

            Debug.Log($"[Archer] Created scene at {ScenePath}.");
        }
    }
}
