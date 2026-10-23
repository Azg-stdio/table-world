using System.Linq;
using TableWorld.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TableWorld.Editor
{
    /// <summary>
    /// Builds Assets/_Project/Scenes/Table.unity from nothing. The scene is a build output: change this script,
    /// never the scene file. Run from the menu or with
    /// <c>unity command script eval --code "TableWorld.Editor.TableSceneBuilder.Build(); return \"ok\";"</c>.
    /// </summary>
    public static class TableSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Table.unity";
        public const string MaterialsFolder = "Assets/_Project/Art/Materials";

        /// <summary>Playing surface in metres. The top of the table is Y = 0, as in core.</summary>
        public static readonly Vector2 TableSize = new Vector2(1.6f, 1.0f);
        public const float TableThickness = 0.04f;
        public const float RimHeight = 0.02f;
        public const float RimWidth = 0.04f;

        [MenuItem("Table World/Build Table Scene")]
        public static void Build()
        {
            ProjectSettingsSetup.Apply();

            var felt = GetOrCreateMaterial("TableFelt", new Color(0.09f, 0.27f, 0.19f), 0.15f);
            var wood = GetOrCreateMaterial("TableWood", new Color(0.36f, 0.22f, 0.12f), 0.35f);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting();
            BuildTable(felt, wood);
            var camera = BuildCamera();
            var objects = new GameObject("Objects");
            var tableGo = new GameObject("TableView");
            var table = tableGo.AddComponent<TableView>();
            table.Configure("Tables/sample", objects.transform, seed: 0);
            tableGo.AddComponent<TableInteraction>().Configure(table, camera, camera.GetComponent<TableCamera>());

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettingsFirst(ScenePath);
        }

        private static void BuildLighting()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sun.shadowBias = 0.01f;
            sun.shadowNormalBias = 0.1f;
            sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.65f);
            RenderSettings.ambientEquatorColor = new Color(0.35f, 0.33f, 0.3f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.1f, 0.08f);
        }

        private static void BuildTable(Material felt, Material wood)
        {
            var table = new GameObject("Table");

            var top = GameObject.CreatePrimitive(PrimitiveType.Cube);
            top.name = "Surface";
            top.transform.SetParent(table.transform, false);
            top.transform.localPosition = new Vector3(0f, -TableThickness / 2f, 0f);
            top.transform.localScale = new Vector3(TableSize.x, TableThickness, TableSize.y);
            top.GetComponent<Renderer>().sharedMaterial = felt;
            top.isStatic = true;

            // Rim so objects pushed to the edge stay on the table.
            var halfX = TableSize.x / 2f + RimWidth / 2f;
            var halfZ = TableSize.y / 2f + RimWidth / 2f;
            var rimY = RimHeight / 2f - TableThickness / 2f;
            var rimHeight = RimHeight + TableThickness;
            CreateRim(table, "RimNorth", new Vector3(0f, rimY, halfZ), new Vector3(TableSize.x + 2 * RimWidth, rimHeight, RimWidth), wood);
            CreateRim(table, "RimSouth", new Vector3(0f, rimY, -halfZ), new Vector3(TableSize.x + 2 * RimWidth, rimHeight, RimWidth), wood);
            CreateRim(table, "RimEast", new Vector3(halfX, rimY, 0f), new Vector3(RimWidth, rimHeight, TableSize.y), wood);
            CreateRim(table, "RimWest", new Vector3(-halfX, rimY, 0f), new Vector3(RimWidth, rimHeight, TableSize.y), wood);
        }

        private static void CreateRim(GameObject parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var rim = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rim.name = name;
            rim.transform.SetParent(parent.transform, false);
            rim.transform.localPosition = position;
            rim.transform.localScale = scale;
            rim.GetComponent<Renderer>().sharedMaterial = material;
            rim.isStatic = true;
        }

        private static Camera BuildCamera()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = go.AddComponent<Camera>();
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 30f;
            camera.fieldOfView = 50f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.07f, 0.08f);
            go.AddComponent<AudioListener>();
            var tableCamera = go.AddComponent<TableCamera>();
            tableCamera.Configure(Vector3.zero, 0f, 55f, 1.4f, TableSize / 2f + new Vector2(0.2f, 0.2f));
            return camera;
        }

        private static Material GetOrCreateMaterial(string name, Color color, float smoothness)
        {
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Art", "Materials");

            var path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void AddToBuildSettingsFirst(string path)
        {
            var others = EditorBuildSettings.scenes.Where(s => s.path != path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) }.Concat(others).ToArray();
        }
    }
}
