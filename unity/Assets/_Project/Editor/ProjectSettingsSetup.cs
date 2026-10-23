using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TableWorld.Editor
{
    /// <summary>
    /// Physics, time and shadow settings for a table of small objects (cards are 0.3 mm thick, dice 16 mm).
    /// Unity's defaults assume metre-sized objects. Idempotent; run from the menu or by the scene builder.
    /// </summary>
    public static class ProjectSettingsSetup
    {
        public const float ContactOffset = 0.001f;
        public const float FixedTimestep = 0.01f;
        public const int SolverIterations = 12;
        public const int SolverVelocityIterations = 4;
        public const float SleepThreshold = 0.0005f;
        public const float ShadowDistance = 6f;

        /// <summary>
        /// Shaders the game builds materials from at runtime must be always included, or builds strip them
        /// (<see cref="TableWorld.View.RequiredShaders"/>).
        /// </summary>
        public static void IncludeRequiredShaders()
        {
            var graphics = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0];
            var serialized = new SerializedObject(graphics);
            var list = serialized.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in TableWorld.View.RequiredShaders.Names)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogError($"[ProjectSettingsSetup] Shader '{name}' not found in the Editor.");
                    continue;
                }
                var present = false;
                for (var i = 0; i < list.arraySize; i++)
                    present |= list.GetArrayElementAtIndex(i).objectReferenceValue == shader;
                if (present)
                    continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            }
            serialized.ApplyModifiedProperties();
        }

        [MenuItem("Table World/Apply Project Settings")]
        public static void Apply()
        {
            Physics.defaultContactOffset = ContactOffset;
            Physics.defaultSolverIterations = SolverIterations;
            Physics.defaultSolverVelocityIterations = SolverVelocityIterations;
            Physics.sleepThreshold = SleepThreshold;
            // Setting Time.fixedDeltaTime alone does not reach the disk; dirtying the TimeManager makes SaveAssets write it.
            Time.fixedDeltaTime = FixedTimestep;
            EditorUtility.SetDirty(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0]);
            // A networked table must keep exchanging messages while its window is in the background.
            PlayerSettings.runInBackground = true;
            // No project-wide input actions: the game reads Mouse/Keyboard directly and UI Toolkit falls back to the
            // package's DefaultInputActions. The template asset Unity assigned left a stale action state on
            // Mouse/position in the first Play session after each domain reload (hundreds of NullReferenceExceptions).
            UnityEngine.InputSystem.InputSystem.actions = null;
            IncludeRequiredShaders();

            // Every URP asset (one per quality level) gets the short shadow distance so small objects cast crisp shadows.
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets/_Project/Settings" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null || Mathf.Approximately(asset.shadowDistance, ShadowDistance))
                    continue;
                asset.shadowDistance = ShadowDistance;
                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
        }
    }
}
