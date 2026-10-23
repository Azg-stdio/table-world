using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace TableWorld.Editor
{
    /// <summary>
    /// Creates the single PanelSettings asset (CLAUDE.md §4) used by every UIDocument in the game, and the UI font
    /// assets (Inter, SIL OFL, in Art/ThirdParty/Inter) that the theme points at.
    /// </summary>
    public static class HudBuilder
    {
        public const string PanelSettingsPath = "Assets/_Project/UI/PanelSettings.asset";
        public const string ThemePath = "Assets/_Project/UI/TableWorld.tss";
        public const string HudPath = "Assets/_Project/UI/Hud/Hud.uxml";
        public const string FontFolder = "Assets/_Project/Art/ThirdParty/Inter/";
        public const string RegularFontAssetPath = FontFolder + "InterRegularSDF.asset";
        public const string SemiBoldFontAssetPath = FontFolder + "InterSemiBoldSDF.asset";

        /// <summary>
        /// The HUD is laid out for this resolution and scaled to the screen. 1600×900 makes a 15 px label 18 px on a
        /// 1080p screen; the previous 1920×1080 shrank everything to two thirds in a 720p window.
        /// </summary>
        public static readonly Vector2Int ReferenceResolution = new Vector2Int(1600, 900);

        /// <summary>SDF glyphs sampled at 90 pt with 9 px padding stay sharp from small labels to large titles.</summary>
        public const int SamplingPointSize = 90;
        public const int AtlasPadding = 9;

        [MenuItem("Table World/Build UI Fonts and Panel")]
        public static void BuildAll()
        {
            GetOrCreatePanelSettings();
            Debug.Log("[HudBuilder] Panel settings and UI fonts are up to date.");
        }

        public static PanelSettings GetOrCreatePanelSettings()
        {
            GetOrCreateFonts();
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            }
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = ReferenceResolution;
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }

        /// <summary>Regular and SemiBold SDF font assets; SemiBold is the bold face of Regular (<c>-unity-font-style: bold</c>).</summary>
        public static FontAsset GetOrCreateFonts()
        {
            var regular = GetOrCreateFontAsset("Inter-Regular.ttf", RegularFontAssetPath);
            var semiBold = GetOrCreateFontAsset("Inter-SemiBold.ttf", SemiBoldFontAssetPath);
            var weights = regular.fontWeightTable;
            if (weights != null && weights.Length > 7 && weights[7].regularTypeface != semiBold)
            {
                weights[7].regularTypeface = semiBold;
                EditorUtility.SetDirty(regular);
                AssetDatabase.SaveAssets();
            }
            return regular;
        }

        private static FontAsset GetOrCreateFontAsset(string ttf, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<FontAsset>(path);
            if (existing != null)
                return existing;
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontFolder + ttf);
            if (font == null)
                throw new FileNotFoundException($"UI font missing: {FontFolder}{ttf}");
            var asset = FontAsset.CreateFontAsset(font, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            // The atlas and its material live inside the font asset, as the Font Asset Creator does it.
            asset.atlasTextures[0].name = asset.name + " Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
            asset.material.name = asset.name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            Populate(asset);
            return asset;
        }

        /// <summary>
        /// Every printable ASCII and Latin-1 character, plus the symbols the UI writes (menus, counters, status lines).
        /// A text that needs a character not listed here shows a blank: add it and run Rebuild UI Font Atlases.
        /// </summary>
        public static string Characters()
        {
            var characters = new System.Text.StringBuilder();
            for (var c = 32; c < 127; c++)
                characters.Append((char)c);
            for (var c = 160; c < 256; c++)
                characters.Append((char)c);
            characters.Append("…–—‘’“”·×÷−±≈≠≤≥→←↑↓↔•●○✓✗°′″€‹›");
            return characters.ToString();
        }

        /// <summary>
        /// Renders <see cref="Characters"/> into a fresh atlas. Text that needed a glyph missing from it made the dynamic
        /// atlas grow during play, so the asset changed on disk; reverting that file under a running Editor scrambled
        /// the glyphs (letters cut or swapped). With every character the UI writes already in, nothing is added at runtime.
        /// (Static atlases are deprecated in Unity 6.6.)
        /// </summary>
        private static void Populate(FontAsset asset)
        {
            asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            asset.ClearFontAssetData(setAtlasSizeToZero: false);
            if (!asset.TryAddCharacters(Characters(), out var missing) && !string.IsNullOrEmpty(missing))
                Debug.LogWarning($"[HudBuilder] {asset.name} has no glyph for: {missing}");
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Table World/Rebuild UI Font Atlases")]
        public static string RebuildFontAtlases()
        {
            var report = "";
            foreach (var path in new[] { RegularFontAssetPath, SemiBoldFontAssetPath })
            {
                var asset = AssetDatabase.LoadAssetAtPath<FontAsset>(path);
                Populate(asset);
                report += $"{asset.name}: {asset.atlasTextures.Length} atlas(es), {asset.atlasPopulationMode}; ";
            }
            GetOrCreateFonts();
            return report;
        }

        public static VisualTreeAsset Hud => AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudPath);
    }
}
