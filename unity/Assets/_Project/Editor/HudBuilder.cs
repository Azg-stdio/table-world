using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.Editor
{
    /// <summary>Creates the single PanelSettings asset (CLAUDE.md §4) used by every UIDocument in the game.</summary>
    public static class HudBuilder
    {
        public const string PanelSettingsPath = "Assets/_Project/UI/PanelSettings.asset";
        public const string ThemePath = "Assets/_Project/UI/TableWorld.tss";
        public const string HudPath = "Assets/_Project/UI/Hud/Hud.uxml";

        public static PanelSettings GetOrCreatePanelSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            }
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }

        public static VisualTreeAsset Hud => AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudPath);
    }
}
