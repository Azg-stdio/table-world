using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TableWorld.View.Settings
{
    public enum DisplayMode { Windowed, Borderless, Fullscreen }
    public enum Antialiasing { Off, Fxaa, Smaa }
    public enum VoiceMode { PushToTalk, VoiceActivation, Off }

    /// <summary>
    /// This machine's preferences (G13, G31): sound, voice, camera and mouse, display. Saved as JSON in the user's data folder
    /// (<see cref="DefaultPath"/>), never with a table. Values are clamped on load, so a hand-edited file cannot break
    /// the game. <see cref="Apply"/> pushes them into the engine and the scene.
    /// </summary>
    public sealed class GameSettings
    {
        public const string FileName = "settings.json";
        public static readonly int[] FrameLimits = { 0, 30, 60, 120, 144, 240 };
        public const float MinScale = 0.25f;
        public const float MaxScale = 3f;
        public const float MinUiScale = 0.75f;
        public const float MaxUiScale = 1.5f;

        /// <summary>The name other players see (G23); empty means "Player N".</summary>
        public string PlayerName { get; set; } = "";

        /// <summary>Your chalker's personality, the face other players see (a <see cref="Chalkers.ChalkerPersonality"/> name).</summary>
        public string Personality { get; set; } = nameof(Chalkers.ChalkerPersonality.Cheerful);

        public static readonly int[] AutosaveChoices = { 0, 5, 10, 15 };

        /// <summary>Minutes between autosaves while the table changes (G30); 0 is off.</summary>
        [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Include)]
        public int AutosaveMinutes { get; set; } = 10;

        /// <summary>Everything the game plays, 0–1.</summary>
        public float MasterVolume { get; set; } = 1f;
        /// <summary>Table sounds (cards, dice, chips), 0–1, on top of the master volume.</summary>
        public float TableVolume { get; set; } = 0.8f;
        /// <summary>The table's music (TTS Music Player), 0–1, on top of the master volume.</summary>
        public float MusicVolume { get; set; } = 0.5f;

        /// <summary>How this player's microphone opens (T7.3): while T is held, when they speak, or never.</summary>
        public VoiceMode VoiceMode { get; set; } = VoiceMode.PushToTalk;
        /// <summary>The microphone's name; empty means the system default.</summary>
        public string Microphone { get; set; } = "";
        /// <summary>Other players' voices, 0–1, on top of the master volume.</summary>
        public float VoiceVolume { get; set; } = 1f;
        /// <summary>Voice activation opens above this loudness, in dBFS.</summary>
        public float VoiceThresholdDb { get; set; } = -45f;
        public const float MinVoiceThresholdDb = -70f;
        public const float MaxVoiceThresholdDb = -20f;
        /// <summary>Plays your own voice back through the network codec, with a chalker across the table that talks with it.</summary>
        public bool HearMyself { get; set; }

        /// <summary>Multipliers on the camera's speeds: orbit with the mouse, wheel zoom, and keyboard pan.</summary>
        public float OrbitSensitivity { get; set; } = 1f;
        public float ZoomSpeed { get; set; } = 1f;
        public float PanSpeed { get; set; } = 1f;
        public bool InvertOrbitY { get; set; }

        public DisplayMode DisplayMode { get; set; } = DisplayMode.Borderless;
        /// <summary>Screen size in full screen; 0 means the monitor's own.</summary>
        public int Width { get; set; }
        public int Height { get; set; }
        public bool VSync { get; set; } = true;
        /// <summary>Frames per second when VSync is off; 0 means no limit.</summary>
        public int FrameLimit { get; set; } = 0;
        public Antialiasing Antialiasing { get; set; } = Antialiasing.Smaa;
        /// <summary>Shadows and render scale (<see cref="GraphicsPresets"/>); Auto picks from the machine.</summary>
        public GraphicsQuality Graphics { get; set; } = GraphicsQuality.Auto;
        /// <summary>Size of the HUD, 1 = designed size.</summary>
        public float UiScale { get; set; } = 1f;

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>Puts every value in range.</summary>
        public GameSettings Clamped()
        {
            PlayerName = Core.Net.PlayerInfo.CleanName(PlayerName) ?? "";
            if (!Enum.TryParse<Chalkers.ChalkerPersonality>(Personality, out var personality) || !Enum.IsDefined(typeof(Chalkers.ChalkerPersonality), personality) || Personality != personality.ToString())
                Personality = nameof(Chalkers.ChalkerPersonality.Cheerful);
            MasterVolume = Clamp(MasterVolume, 0f, 1f, 1f);
            TableVolume = Clamp(TableVolume, 0f, 1f, 0.8f);
            MusicVolume = Clamp(MusicVolume, 0f, 1f, 0.5f);
            VoiceVolume = Clamp(VoiceVolume, 0f, 1f, 1f);
            VoiceThresholdDb = Clamp(VoiceThresholdDb, MinVoiceThresholdDb, MaxVoiceThresholdDb, -45f);
            Microphone ??= "";
            if (!Enum.IsDefined(typeof(VoiceMode), VoiceMode))
                VoiceMode = VoiceMode.PushToTalk;
            OrbitSensitivity = Clamp(OrbitSensitivity, MinScale, MaxScale, 1f);
            ZoomSpeed = Clamp(ZoomSpeed, MinScale, MaxScale, 1f);
            PanSpeed = Clamp(PanSpeed, MinScale, MaxScale, 1f);
            UiScale = Clamp(UiScale, MinUiScale, MaxUiScale, 1f);
            if (!Enum.IsDefined(typeof(DisplayMode), DisplayMode))
                DisplayMode = DisplayMode.Borderless;
            if (!Enum.IsDefined(typeof(Antialiasing), Antialiasing))
                Antialiasing = Antialiasing.Smaa;
            if (!Enum.IsDefined(typeof(GraphicsQuality), Graphics))
                Graphics = GraphicsQuality.Auto;
            if (Width < 640 || Height < 360 || Width > 16384 || Height > 16384)
                Width = Height = 0;
            if (Array.IndexOf(FrameLimits, FrameLimit) < 0)
                FrameLimit = 0;
            if (Array.IndexOf(AutosaveChoices, AutosaveMinutes) < 0)
                AutosaveMinutes = 10;
            return this;
        }

        private static float Clamp(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        public GameSettings Copy() => (GameSettings)MemberwiseClone();

        /// <summary>The settings in <paramref name="path"/>, or defaults when it is missing or unreadable.</summary>
        public static GameSettings Load(string path)
        {
            try
            {
                if (File.Exists(path))
                    return (JsonConvert.DeserializeObject<GameSettings>(File.ReadAllText(path)) ?? new GameSettings()).Clamped();
            }
            catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[Settings] {path} could not be read ({e.Message}); using defaults.");
            }
            return new GameSettings();
        }

        public void Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented, new Newtonsoft.Json.Converters.StringEnumConverter()));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[Settings] Could not save {path}: {e.Message}");
            }
        }

        /// <summary>Applies sound, camera, display and antialiasing. The HUD applies <see cref="UiScale"/> itself.</summary>
        public void Apply(TableCamera tableCamera)
        {
            AudioListener.volume = MasterVolume;
            TableSounds.Volume = TableVolume;
            MusicPlayerView.Volume = MusicVolume;
            Voice.VoiceChat.Mode = VoiceMode;
            Voice.VoiceChat.Device = Microphone;
            Voice.VoiceChat.Volume = VoiceVolume;
            Voice.VoiceChat.ThresholdDb = VoiceThresholdDb;
            Voice.VoiceChat.HearMyself = HearMyself;
            if (tableCamera != null)
            {
                tableCamera.OrbitScale = OrbitSensitivity;
                tableCamera.ZoomScale = ZoomSpeed;
                tableCamera.PanScale = PanSpeed;
                tableCamera.InvertOrbitY = InvertOrbitY;
                if (tableCamera.TryGetComponent<UniversalAdditionalCameraData>(out var urp))
                    urp.antialiasing = Antialiasing switch
                    {
                        Antialiasing.Fxaa => AntialiasingMode.FastApproximateAntialiasing,
                        Antialiasing.Smaa => AntialiasingMode.SubpixelMorphologicalAntiAliasing,
                        _ => AntialiasingMode.None,
                    };
            }
            GraphicsPresets.Apply(Graphics);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = VSync || FrameLimit <= 0 ? -1 : FrameLimit;
            // The Editor's Game view ignores window modes and sizes; a player applies them.
            if (!Application.isEditor)
            {
                var mode = DisplayMode switch
                {
                    DisplayMode.Windowed => FullScreenMode.Windowed,
                    DisplayMode.Fullscreen => FullScreenMode.ExclusiveFullScreen,
                    _ => FullScreenMode.FullScreenWindow,
                };
                var native = Screen.currentResolution;
                var width = Width > 0 ? Width : native.width;
                var height = Height > 0 ? Height : native.height;
                if (Screen.fullScreenMode != mode || ((mode != FullScreenMode.Windowed || Width > 0) && (Screen.width != width || Screen.height != height)))
                    Screen.SetResolution(width, height, mode);
            }
        }
    }
}
