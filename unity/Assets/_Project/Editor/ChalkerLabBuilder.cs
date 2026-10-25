using TableWorld.View.Chalkers;
using TableWorld.View.Voice;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.Editor
{
    /// <summary>
    /// Builds Assets/_Project/Scenes/ChalkerLab.unity, the chalker test bench (<see cref="ChalkerLab"/>): one chalker,
    /// your microphone, the expression and emote panel. A development scene, not in the build. Run from the menu or with
    /// <c>unity command script eval --code "TableWorld.Editor.ChalkerLabBuilder.Build(); return \"ok\";"</c>.
    /// </summary>
    public static class ChalkerLabBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/ChalkerLab.unity";
        public const string UxmlPath = "Assets/_Project/UI/ChalkerLab/ChalkerLab.uxml";

        [MenuItem("Table World/Build Chalker Lab Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 30f;
            camera.fieldOfView = 40f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.11f, 0.13f);
            cameraGo.AddComponent<AudioListener>();

            var key = new GameObject("Key Light").AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.95f, 0.86f);
            key.intensity = 1.6f;
            key.shadows = LightShadows.Soft;
            key.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
            var fill = new GameObject("Fill Light").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.7f, 0.78f, 1f);
            fill.intensity = 0.5f;
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(20f, -140f, 0f);

            var lab = new GameObject("Chalker Lab");
            var document = lab.AddComponent<UIDocument>();
            document.panelSettings = HudBuilder.GetOrCreatePanelSettings();
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            var voice = lab.AddComponent<VoiceChat>();
            lab.AddComponent<ChalkerLab>().Configure(camera, document, voice);

            EditorSceneManager.SaveScene(scene, ScenePath);
        }
    }
}
