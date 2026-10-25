using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TableWorld.View.Settings
{
    public enum GraphicsQuality { Auto, Low, Medium, High }

    /// <summary>
    /// How much the machine draws. The table's two spot lights each render every piece again into the shadow atlas (up
    /// to 14 000 shadow casters on an imported game): Medium keeps only the table light's shadows, hard-edged; Low drops
    /// real-time shadows and renders at <see cref="LowRenderScale"/>, upscaled with AMD FSR 1. Auto picks from the video
    /// memory and cores.
    /// </summary>
    public static class GraphicsPresets
    {
        public const float LowRenderScale = 0.75f;

        /// <summary>The preset in force (never Auto).</summary>
        public static GraphicsQuality Current { get; private set; } = GraphicsQuality.High;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = GraphicsQuality.High;

        /// <summary>What Auto chooses on this machine.</summary>
        public static GraphicsQuality Detect()
        {
            var video = SystemInfo.graphicsMemorySize;
            if (video < 2500 || SystemInfo.processorCount < 4)
                return GraphicsQuality.Low;
            if (video < 5000 || SystemInfo.processorCount < 6)
                return GraphicsQuality.Medium;
            return GraphicsQuality.High;
        }

        public static void Apply(GraphicsQuality quality)
        {
            Current = quality == GraphicsQuality.Auto ? Detect() : quality;
            // A player's copy of the pipeline asset is its own; the Editor's is the project file, so it is left alone there.
            if (!Application.isEditor && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                var low = Current == GraphicsQuality.Low;
                urp.renderScale = low ? LowRenderScale : 1f;
                urp.upscalingFilter = low ? UpscalingFilterSelection.FSR : UpscalingFilterSelection.Auto;
            }
            if (Object.FindAnyObjectByType<EnvironmentView>() is { } environment)
                ApplyLights(environment.Lamp, environment.TableLight);
        }

        /// <summary>Shadows of the room lamp and the table light for the preset in force.</summary>
        public static void ApplyLights(Light lamp, Light tableLight)
        {
            if (lamp != null)
                lamp.shadows = Current == GraphicsQuality.High ? LightShadows.Soft : LightShadows.None;
            if (tableLight != null)
                tableLight.shadows = Current switch
                {
                    GraphicsQuality.Low => LightShadows.None,
                    GraphicsQuality.Medium => LightShadows.Hard,
                    _ => LightShadows.Soft,
                };
        }
    }
}
