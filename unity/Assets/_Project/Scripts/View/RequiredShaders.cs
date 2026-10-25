using UnityEngine;

namespace TableWorld.View
{
    /// <summary>
    /// Shaders the game creates materials from at runtime. A build only keeps shaders that some included material uses,
    /// so these are added to Graphics Settings → Always Included Shaders by <c>ProjectSettingsSetup</c>; without that,
    /// <see cref="Shader.Find"/> returns null in the exe (it worked in the Editor, so pings, selection outlines and
    /// presence markers silently broke in builds). A missing one is reported once at startup.
    /// </summary>
    public static class RequiredShaders
    {
        public const string LitName = "Universal Render Pipeline/Lit";
        public const string UnlitName = "Universal Render Pipeline/Unlit";
        /// <summary>For TTS asset-bundle particles (M4).</summary>
        public const string ParticlesName = "Universal Render Pipeline/Particles/Unlit";
        /// <summary>Unity's built-in panorama sky (TTS custom skies): not part of URP, so it must be kept in builds too.</summary>
        public const string SkyName = "Skybox/Panoramic";
        public static readonly string[] Names = { LitName, UnlitName, ParticlesName, SkyName, Outline.ShaderName };

        public static Shader Lit => Find(LitName);

        /// <summary>Unlit, or Lit if a broken build lost it (the marker still shows, only shaded).</summary>
        public static Shader Unlit => Find(UnlitName) ?? Find(LitName);

        public static Shader Particles => Find(ParticlesName) ?? Unlit;

        private static Shader Find(string name) => Shader.Find(name);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CheckAtStartup()
        {
            foreach (var name in Names)
                if (Shader.Find(name) == null)
                    Debug.LogError($"[RequiredShaders] '{name}' is not in this build: add it to Always Included Shaders (Table World → Apply Project Settings).");
        }
    }
}
