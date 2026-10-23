using System.Linq;
using TableWorld.View;
using TableWorld.View.Hud;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

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

        /// <summary>Playing surface in metres (<see cref="TableWorld.View.TableGeometry"/>). The top of the table is Y = 0, as in core.</summary>
        public static readonly Vector2 TableSize = TableWorld.View.TableGeometry.Size;
        public const float TableThickness = TableWorld.View.TableGeometry.Thickness;
        public const float RimHeight = TableWorld.View.TableGeometry.RimHeight;
        public const float RimWidth = TableWorld.View.TableGeometry.RimWidth;

        [MenuItem("Table World/Build Table Scene")]
        public static void Build()
        {
            ProjectSettingsSetup.Apply();

            var felt = GetOrCreateMaterial("TableFelt", new Color(0.09f, 0.27f, 0.19f), 0.15f);
            var wood = GetOrCreateMaterial("TableWood", new Color(0.36f, 0.22f, 0.12f), 0.35f);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = BuildLighting();
            var tableRoot = BuildTable(felt, wood);
            var camera = BuildCamera();
            var objects = new GameObject("Objects");
            var tableGo = new GameObject("TableView");
            var table = tableGo.AddComponent<TableView>();
            table.Configure("Tables/sample", objects.transform, seed: 0);
            tableGo.AddComponent<TableWorld.View.Bridge.BridgeClient>().Configure(table);
            var session = tableGo.AddComponent<TableWorld.View.Net.NetworkTableSession>();
            session.Configure(table);
            tableGo.AddComponent<TableWorld.View.Net.NetworkAutoplay>().Configure(table, session);
            // Other players' pointers and the objects they carry, live.
            var presence = tableGo.AddComponent<TableWorld.View.Net.PresenceView>();
            presence.Configure(table, session);
            // The room around the table, rebuilt from each loaded table's environment.
            tableGo.AddComponent<EnvironmentView>().Configure(table, tableRoot.transform, sun, camera);
            var interaction = tableGo.AddComponent<TableInteraction>();
            interaction.Configure(table, camera, camera.GetComponent<TableCamera>());
            tableGo.AddComponent<TableSounds>().Configure(table, interaction, presence, SoundClips());

            var hud = new GameObject("Hud");
            var document = hud.AddComponent<UIDocument>();
            document.panelSettings = HudBuilder.GetOrCreatePanelSettings();
            document.visualTreeAsset = HudBuilder.Hud;
            hud.AddComponent<HudController>().Configure(table, interaction, session);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettingsFirst(ScenePath);
        }

        /// <summary>A directional light for tables without a room (environment room "none"); the game night room switches it off.</summary>
        private static Light BuildLighting()
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
            return sun;
        }

        private static GameObject BuildTable(Material felt, Material wood)
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
            return table;
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

        public const string CasinoAudio = "Assets/_Project/Art/ThirdParty/KenneyCasinoAudio";
        public const string ImpactAudio = "Assets/_Project/Art/ThirdParty/KenneyImpactSounds";

        /// <summary>The table's sound banks from the CC0 packs (referenced here so builds include them).</summary>
        private static TableSounds.Clips SoundClips() => new TableSounds.Clips
        {
            CardPlace = Clips(CasinoAudio, "card-place"),
            CardSlide = Clips(CasinoAudio, "card-slide"),
            CardShuffle = Clips(CasinoAudio, "card-shuffle"),
            CardFan = Clips(CasinoAudio, "card-fan"),
            ChipLay = Clips(CasinoAudio, "chip-lay"),
            ChipsHandle = Clips(CasinoAudio, "chips-handle"),
            DiceGrab = Clips(CasinoAudio, "dice-grab"),
            DiceThrow = Clips(CasinoAudio, "dice-throw"),
            DieImpact = Clips(CasinoAudio, "die-throw"),
            WoodImpact = Clips(ImpactAudio, "impactWood_light"),
            SoftImpact = Clips(ImpactAudio, "impactSoft_medium"),
            Ping = Clips(ImpactAudio, "impactGlass_light"),
        };

        private static AudioClip[] Clips(string folder, string prefix)
        {
            var clips = new System.Collections.Generic.List<AudioClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileName(path).StartsWith(prefix))
                    clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(path));
            }
            if (clips.Count == 0)
                Debug.LogError($"[TableSceneBuilder] No sounds '{prefix}*' in {folder}.");
            return clips.ToArray();
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
