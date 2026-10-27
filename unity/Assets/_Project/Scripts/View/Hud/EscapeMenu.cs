using System;
using System.Collections.Generic;
using System.Linq;
using TableWorld.View.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// The Escape menu (G13): resume, settings, controls, leave the session, quit (asked twice). Settings (G31) apply as
    /// they change and are saved when the settings page closes. Escape steps back: settings → menu → table. Styling lives
    /// in Hud.uss (escape-*, settings-*).
    /// </summary>
    public sealed class EscapeMenu
    {
        private const string Hidden = "hidden";

        private readonly VisualElement _menu;
        private readonly VisualElement _settingsPanel;
        private readonly ScrollView _settingsList;
        private readonly VisualElement _quitConfirm;
        private readonly Label _quitQuestion;
        private readonly Button _leave;
        private readonly TableWorld.View.Net.NetworkTableSession _session;
        private readonly Action<GameSettings> _apply;
        private bool _dirty;

        /// <summary>The settings in force on this machine.</summary>
        public GameSettings Settings { get; private set; }

        /// <summary>Where settings are saved; tests point it at a scratch file.</summary>
        public string SettingsPath { get; set; } = GameSettings.DefaultPath;

        public bool MenuOpen => !_menu.ClassListContains(Hidden);
        public bool SettingsOpen => !_settingsPanel.ClassListContains(Hidden);
        public bool QuitAsked => !_quitConfirm.ClassListContains(Hidden);

        /// <summary>Opens the save and load page (the HUD owns it).</summary>
        public Action OpenSaves { get; set; }

        /// <summary>Opens the Connect an AI agent page (the HUD's <see cref="AgentPanel"/>).</summary>
        public Action OpenAgent { get; set; }

        /// <summary>Raised instead of quitting when set (tests).</summary>
        public Action QuitOverride { get; set; }

        public EscapeMenu(VisualElement root, GameSettings settings, Action<GameSettings> apply, TableWorld.View.Net.NetworkTableSession session, Action showControls)
        {
            Settings = settings;
            _apply = apply;
            _session = session;
            _menu = root.Q("escapePanel");
            _settingsPanel = root.Q("settingsPanel");
            _settingsList = root.Q<ScrollView>("settingsList");
            _quitConfirm = root.Q("escQuitConfirm");
            _quitQuestion = root.Q<Label>("escQuitQuestion");
            _leave = root.Q<Button>("escLeave");
            root.Q<Button>("escResume").clicked += Close;
            root.Q<Button>("escSettings").clicked += OpenSettings;
            root.Q<Button>("escSaves").clicked += () =>
            {
                _menu.AddToClassList(Hidden);
                OpenSaves?.Invoke();
            };
            root.Q<Button>("escAgent").clicked += () =>
            {
                _menu.AddToClassList(Hidden);
                OpenAgent?.Invoke();
            };
            root.Q<Button>("escControls").clicked += () =>
            {
                Close();
                showControls();
            };
            _leave.clicked += Leave;
            root.Q<Button>("escQuit").clicked += AskQuit;
            root.Q<Button>("escQuitYes").clicked += Quit;
            root.Q<Button>("escQuitNo").clicked += () => _quitConfirm.AddToClassList(Hidden);
            root.Q<Button>("settingsBack").clicked += CloseSettings;
            root.Q<Button>("settingsReset").clicked += () =>
            {
                Change(_ => Settings = new GameSettings());
                BuildSettings();
            };
        }

        /// <summary>Escape: closes the settings page, then the menu; opens the menu when nothing is open.</summary>
        public void Back()
        {
            if (SettingsOpen)
                CloseSettings();
            else if (QuitAsked)
                _quitConfirm.AddToClassList(Hidden);
            else if (MenuOpen)
                Close();
            else
                Open();
        }

        public void Open()
        {
            var online = _session != null && (_session.IsHost || _session.IsClient);
            _leave.EnableInClassList(Hidden, !online);
            _leave.text = _session != null && _session.IsHost ? Ui.T("Stop hosting") : Ui.T("Leave session");
            _quitConfirm.AddToClassList(Hidden);
            _menu.RemoveFromClassList(Hidden);
        }

        public void Close()
        {
            CloseSettings();
            _quitConfirm.AddToClassList(Hidden);
            _menu.AddToClassList(Hidden);
        }

        public void OpenSettings()
        {
            _menu.AddToClassList(Hidden);
            BuildSettings();
            _settingsPanel.RemoveFromClassList(Hidden);
        }

        /// <summary>Back to the menu, saving what changed.</summary>
        public void CloseSettings()
        {
            if (!SettingsOpen)
                return;
            _settingsPanel.AddToClassList(Hidden);
            _menu.RemoveFromClassList(Hidden);
            if (_dirty)
                Settings.Save(SettingsPath);
            _dirty = false;
        }

        private void Leave()
        {
            _session?.Stop();
            Close();
        }

        private void AskQuit()
        {
            _quitQuestion.text = _session != null && _session.IsHost && _session.JoinedCount > 0
                ? Ui.T("Quit Table World? The {joinedCount} player(s) at your table will be disconnected.", ("joinedCount", _session.JoinedCount))
                : Ui.T("Quit Table World?");
            _quitConfirm.RemoveFromClassList(Hidden);
        }

        private void Quit()
        {
            if (QuitOverride != null)
            {
                QuitOverride();
                return;
            }
            _session?.Stop();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Change(Action<GameSettings> change)
        {
            change(Settings);
            Settings.Clamped();
            _dirty = true;
            _apply(Settings);
        }

        // ---- the settings page --------------------------------------------------------------------------------------

        /// <summary>Changes one setting as its control would (tests): applies it and marks it for saving.</summary>
        public void Set(Action<GameSettings> change) => Change(change);

        private void BuildSettings()
        {
            _settingsList.Clear();
            var s = Settings;
            Group(Ui.T("You"));
            Choice(Ui.T("Language"), new[] { "" }.Concat(Ui.Languages).ToArray(), s.Language, v => s.Language = v,
                v => v == "" ? Ui.T("System ({language})", ("language", Ui.NameOf(Ui.SystemLanguage()))) : Ui.NameOf(v));
            Text(Ui.T("Your name"), s.PlayerName, Ui.T("Player"), v => s.PlayerName = v);
            Choice(Ui.T("Chalker face"), Enum.GetNames(typeof(TableWorld.View.Chalkers.ChalkerPersonality)), s.Personality, v => s.Personality = v, v => Ui.T(v));

            Group(Ui.T("Saving"));
            Choice(Ui.T("Autosave"), GameSettings.AutosaveChoices, s.AutosaveMinutes, v => s.AutosaveMinutes = v, v => v == 0 ? Ui.T("Off") : Ui.T("Every {v} minutes", ("v", v)));

            Group(Ui.T("Sound"));
            Slider(Ui.T("Master volume"), 0f, 1f, s.MasterVolume, v => s.MasterVolume = v, Percent);
            Slider(Ui.T("Table sounds"), 0f, 1f, s.TableVolume, v => s.TableVolume = v, Percent);
            Slider(Ui.T("Music"), 0f, 1f, s.MusicVolume, v => s.MusicVolume = v, Percent);

            Group(Ui.T("Voice"));
            Choice(Ui.T("Microphone"), (VoiceMode[])Enum.GetValues(typeof(VoiceMode)), s.VoiceMode, v => s.VoiceMode = v,
                v => v == VoiceMode.PushToTalk ? Ui.T("Push to talk (hold {label})", ("label", KeyBindings.Label(GameAction.Talk))) : v == VoiceMode.VoiceActivation ? Ui.T("Open when I speak") : Ui.T("Off"));
            var devices = new List<string> { "" };
            devices.AddRange(UnityEngine.Microphone.devices);
            if (!devices.Contains(s.Microphone))
                devices.Add(s.Microphone);
            Choice(Ui.T("Device"), devices.ToArray(), s.Microphone, v => s.Microphone = v, v => v == "" ? Ui.T("System default") : v);
            Slider(Ui.T("Voice activation level"), GameSettings.MinVoiceThresholdDb, GameSettings.MaxVoiceThresholdDb, s.VoiceThresholdDb, v => s.VoiceThresholdDb = v, v => $"{Mathf.RoundToInt(v)} dB");
            Slider(Ui.T("Other players' voices"), 0f, 1f, s.VoiceVolume, v => s.VoiceVolume = v, Percent);
            Choice(Ui.T("Hear myself (test)"), new[] { false, true }, s.HearMyself, v => s.HearMyself = v, v => v ? Ui.T("On") : Ui.T("Off"));

            Group(Ui.T("Camera and mouse"));
            Slider(Ui.T("Orbit sensitivity"), GameSettings.MinScale, GameSettings.MaxScale, s.OrbitSensitivity, v => s.OrbitSensitivity = v, Times);
            Slider(Ui.T("Zoom speed"), GameSettings.MinScale, GameSettings.MaxScale, s.ZoomSpeed, v => s.ZoomSpeed = v, Times);
            Slider(Ui.T("Keyboard pan speed"), GameSettings.MinScale, GameSettings.MaxScale, s.PanSpeed, v => s.PanSpeed = v, Times);
            Choice(Ui.T("Invert vertical orbit"), new[] { false, true }, s.InvertOrbitY, v => s.InvertOrbitY = v, v => v ? Ui.T("On") : Ui.T("Off"));

            Group(Ui.T("Keys"));
            foreach (GameAction action in Enum.GetValues(typeof(GameAction)))
                KeyRow(action);
            var reset = new Button(() =>
            {
                Change(settings => settings.Keys = new Dictionary<string, string>());
                Rebuild();
            }) { text = Ui.T("Back to TTS keys") };
            reset.AddToClassList("hud-button");
            Row("").Add(reset);
            Note(Ui.T("Click an action, then press its new key (Escape cancels). A key another action had goes to that action's old key."));

            Group(Ui.T("Display"));
            Choice(Ui.T("Window"), (DisplayMode[])Enum.GetValues(typeof(DisplayMode)), s.DisplayMode, v => s.DisplayMode = v,
                v => v == DisplayMode.Borderless ? Ui.T("Borderless full screen") : v == DisplayMode.Fullscreen ? Ui.T("Exclusive full screen") : Ui.T("Windowed"));
            var sizes = new List<(int W, int H)> { (0, 0) };
            sizes.AddRange(Screen.resolutions.Select(r => (r.width, r.height)).Distinct().OrderBy(r => r.width * r.height));
            if (!sizes.Contains((s.Width, s.Height)))
                sizes.Add((s.Width, s.Height));
            Choice<(int W, int H)>(Ui.T("Resolution"), sizes.ToArray(), (s.Width, s.Height), v => { s.Width = v.W; s.Height = v.H; },
                v => v.W == 0 ? Ui.T("Monitor's own") : $"{v.W} × {v.H}");
            Choice(Ui.T("VSync"), new[] { true, false }, s.VSync, v => s.VSync = v, v => v ? Ui.T("On") : Ui.T("Off"));
            Choice(Ui.T("Frame limit (VSync off)"), GameSettings.FrameLimits, s.FrameLimit, v => s.FrameLimit = v, v => v == 0 ? Ui.T("None") : $"{v} fps");
            Choice(Ui.T("Antialiasing"), (Antialiasing[])Enum.GetValues(typeof(Antialiasing)), s.Antialiasing, v => s.Antialiasing = v,
                v => v == Antialiasing.Off ? Ui.T("Off") : v == Antialiasing.Fxaa ? Ui.T("FXAA (fast)") : Ui.T("SMAA (sharper)"));
            Choice(Ui.T("Graphics quality"), (GraphicsQuality[])Enum.GetValues(typeof(GraphicsQuality)), s.Graphics, v => s.Graphics = v,
                v => v == GraphicsQuality.Auto ? Ui.T("Auto ({detect} on this computer)", ("detect", GraphicsPresets.Detect())) : v == GraphicsQuality.Low
                    ? Ui.T("Low (no shadows, 75% resolution, smaller images)") : v == GraphicsQuality.Medium ? Ui.T("Medium (fewer shadows)") : Ui.T("High"));
            Slider(Ui.T("Interface size"), GameSettings.MinUiScale, GameSettings.MaxUiScale, s.UiScale, v => s.UiScale = v, Percent);
            if (Application.isEditor)
                Note(Ui.T("Window and resolution apply in the built game, not in the Editor."));
        }

        private static string Percent(float v) => $"{Mathf.RoundToInt(v * 100f)}%";

        private static string Times(float v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "×";

        private void Group(string title)
        {
            var label = new Label(title);
            label.AddToClassList("help-group");
            _settingsList.Add(label);
        }

        private void Note(string text)
        {
            var label = new Label(text);
            label.AddToClassList("settings-note");
            _settingsList.Add(label);
        }

        private VisualElement Row(string name)
        {
            var row = new VisualElement();
            row.AddToClassList("settings-row");
            var label = new Label(name);
            label.AddToClassList("settings-label");
            row.Add(label);
            _settingsList.Add(row);
            return row;
        }

        private void Slider(string name, float min, float max, float value, Action<float> set, Func<float, string> format)
        {
            var row = Row(name);
            var slider = new Slider(min, max) { value = value };
            slider.AddToClassList("settings-slider");
            var shown = new Label(format(value));
            shown.AddToClassList("settings-value");
            slider.RegisterValueChangedCallback(e =>
            {
                Change(_ => set(e.newValue));
                shown.text = format(e.newValue);
            });
            row.Add(slider);
            row.Add(shown);
        }

        /// <summary>A text setting, applied when the field loses focus or Enter is pressed.</summary>
        private void Text(string name, string value, string placeholder, Action<string> set)
        {
            var row = Row(name);
            var field = new TextField { value = value, maxLength = Core.Net.PlayerInfo.MaxNameLength };
            field.textEdition.placeholder = placeholder;
            field.AddToClassList("settings-text");
            void Commit()
            {
                if (field.value != value)
                {
                    value = field.value;
                    Change(_ => set(value));
                }
            }
            field.RegisterCallback<FocusOutEvent>(_ => Commit());
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    Commit();
            });
            row.Add(field);
        }

        // ---- keys (G31) ----------------------------------------------------------------------------------------------

        private GameAction? _capturing;
        private Button _captureButton;

        /// <summary>Waiting for the key an action moves to: every key is the menu's until one is pressed.</summary>
        public bool CapturingKey => _capturing != null;

        /// <summary>An action and its key; clicking the key waits for the next key pressed (<see cref="PollKeyCapture"/>).</summary>
        private void KeyRow(GameAction action)
        {
            var row = Row(KeyBindings.Name(action));
            Button key = null;
            key = new Button(() =>
            {
                if (_captureButton != null)
                    _captureButton.text = KeyBindings.Label(KeyBindings.Resolve(Settings.Keys)[_capturing!.Value]);
                _capturing = action;
                _captureButton = key;
                key.text = Ui.T("Press a key…");
            }) { text = KeyBindings.Label(KeyBindings.Resolve(Settings.Keys)[action]) };
            key.AddToClassList("hud-button");
            key.AddToClassList("settings-key");
            row.Add(key);
        }

        /// <summary>Called every frame by the HUD: takes the key pressed while an action waits for one (Escape cancels).</summary>
        public void PollKeyCapture()
        {
            if (_capturing == null)
                return;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.escapeKey.wasPressedThisFrame || !SettingsOpen)
            {
                _capturing = null;
                Rebuild();
                return;
            }
            foreach (var control in keyboard.allKeys)
            {
                if (control == null || !control.wasPressedThisFrame || KeyBindings.Reserved.Contains(control.keyCode))
                    continue;
                var action = _capturing.Value;
                _capturing = null;
                Change(settings => settings.Keys = KeyBindings.Rebind(settings.Keys, action, control.keyCode));
                Rebuild();
                return;
            }
        }

        /// <summary>The interface's language changed: the settings page, when built, is drawn again in it.</summary>
        public void Redraw()
        {
            if (_settingsList.childCount > 0)
                Rebuild();
        }

        /// <summary>Redraws the settings page where it was scrolled to (a key moved, or keys reset).</summary>
        private void Rebuild()
        {
            _captureButton = null;
            var scroll = _settingsList.scrollOffset;
            BuildSettings();
            _settingsList.schedule.Execute(() => _settingsList.scrollOffset = scroll);
        }

        /// <summary>A setting with a few values, stepped with ‹ and › (no drop-down to restyle).</summary>
        private void Choice<T>(string name, T[] values, T current, Action<T> set, Func<T, string> format)
        {
            var row = Row(name);
            var index = Math.Max(0, Array.IndexOf(values, current));
            var shown = new Label(format(values[index]));
            shown.AddToClassList("settings-choice");
            void Step(int by)
            {
                index = (index + by + values.Length) % values.Length;
                shown.text = format(values[index]);
                var chosen = values[index];
                Change(_ => set(chosen));
            }
            var previous = new Button(() => Step(-1)) { text = "‹" };
            var next = new Button(() => Step(1)) { text = "›" };
            previous.AddToClassList("settings-step");
            next.AddToClassList("settings-step");
            row.Add(previous);
            row.Add(shown);
            row.Add(next);
        }
    }
}
