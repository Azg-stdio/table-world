using System;
using System.Collections.Generic;
using System.Linq;
using TableWorld.View.Settings;
using TableWorld.View.Voice;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace TableWorld.View.Chalkers
{
    /// <summary>
    /// The chalker lab (scene ChalkerLab, built by <c>ChalkerLabBuilder</c>): one chalker across a small table, talking
    /// with this machine's microphone and looking at the mouse. The panel switches personality, plays emotes, repaints
    /// it, and sets the voice up: open mic or push to talk, the microphone, the open-mic level, and whether the voice
    /// goes through the network codec (and plays back) or drives the mouth straight from the microphone.
    /// </summary>
    public sealed class ChalkerLab : MonoBehaviour
    {
        public static readonly string[] Colors = { "red", "blue", "green", "yellow", "orange", "purple", "pink", "teal", "white", "brown" };
        public static readonly Vector3 HeadTarget = new Vector3(0f, 0.3f, 0f);
        public const float HomeYaw = 180f;
        public const float HomePitch = 12f;
        public const float HomeDistance = 1.6f;

        /// <summary>Emote hot keys, in the order of <see cref="ChalkerEmote"/> after None.</summary>
        private static readonly Key[] EmoteKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0, Key.Minus };

        [SerializeField] private Camera _camera;
        [SerializeField] private UIDocument _document;
        [SerializeField] private VoiceChat _voice;

        private readonly Dictionary<ChalkerPersonality, Button> _personalityButtons = new Dictionary<ChalkerPersonality, Button>();
        private readonly Dictionary<VoiceMode, Button> _modeButtons = new Dictionary<VoiceMode, Button>();
        private readonly List<VisualElement> _swatches = new List<VisualElement>();
        private Chalker _chalker;
        private string _color = "red";
        private bool _codec;
        private float _yaw = HomeYaw;
        private float _pitch = HomePitch;
        private float _distance = HomeDistance;
        private VisualElement _panel;
        private VisualElement _meterFill;
        private VisualElement _meterThreshold;
        private Label _status;
        private Button _micButton;
        private Button _codecButton;
        private Button _holdButton;

        public Chalker Chalker => _chalker;

        /// <summary>The mouth follows the voice through the codec (and you hear it) instead of straight from the microphone.</summary>
        public bool ThroughCodec
        {
            get => _codec;
            set
            {
                _codec = value;
                VoiceChat.HearMyself = value;
                Refresh();
            }
        }

        public void Configure(Camera camera, UIDocument document, VoiceChat voice)
        {
            _camera = camera;
            _document = document;
            _voice = voice;
        }

        private void Start()
        {
            BuildSet();
            _chalker = new Chalker("lab", transform, ZoneViewFactory.SeatColor(_color));
            VoiceChat.Mode = VoiceMode.VoiceActivation;
            VoiceChat.HearMyself = false;
            VoiceChat.ThresholdDb = -45f;
            VoiceChat.Volume = 1f;
            if (_voice != null)
                _voice.Listen = true;
            BuildPanel();
            PlaceCamera();
        }

        /// <summary>A floor and a little table in front of the chalker, so it sits somewhere.</summary>
        private void BuildSet()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.localPosition = new Vector3(0f, EnvironmentView.FloorY, 0f);
            floor.GetComponent<Renderer>().sharedMaterial = SetMaterial(EnvironmentView.DefaultFloor);
            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "Table";
            table.transform.SetParent(transform, false);
            table.transform.localPosition = new Vector3(0f, -0.02f, 0.42f);
            table.transform.localScale = new Vector3(0.9f, 0.04f, 0.4f);
            table.GetComponent<Renderer>().sharedMaterial = SetMaterial(new Color(0.09f, 0.27f, 0.19f));
        }

        private static Material SetMaterial(Color color)
        {
            var material = new Material(RequiredShaders.Lit) { name = "LabSet" };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.2f);
            return material;
        }

        private void BuildPanel()
        {
            var root = _document != null ? _document.rootVisualElement : null;
            if (root == null)
                return;
            _panel = root.Q("labPanel");
            var personalities = root.Q("personalities");
            foreach (ChalkerPersonality personality in Enum.GetValues(typeof(ChalkerPersonality)))
            {
                var chosen = personality;
                var button = Choice(personality.ToString(), () => SetPersonality(chosen));
                personalities.Add(button);
                _personalityButtons[personality] = button;
            }
            var emotes = root.Q("emotes");
            var emoteList = Emotes().ToList();
            for (var i = 0; i < emoteList.Count; i++)
            {
                var emote = emoteList[i];
                var key = i < 9 ? (i + 1).ToString() : i == 9 ? "0" : "-";
                emotes.Add(Choice($"{key} {emote}", () => _chalker.Play(emote)));
            }
            _holdButton = Choice("", () => SetHold(!_chalker.Hold));
            emotes.Add(_holdButton);
            var colors = root.Q("colors");
            foreach (var name in Colors)
            {
                var swatch = new VisualElement { name = name, tooltip = name };
                swatch.AddToClassList("lab-swatch");
                // The swatch shows the seat colour itself: a value from data, not a style.
                swatch.style.backgroundColor = ZoneViewFactory.SeatColor(name);
                var chosen = name;
                swatch.RegisterCallback<ClickEvent>(_ => SetColor(chosen));
                colors.Add(swatch);
                _swatches.Add(swatch);
            }
            var modes = root.Q("voiceModes");
            foreach (var (mode, label) in new[] { (VoiceMode.VoiceActivation, "Open mic"), (VoiceMode.PushToTalk, "Push to talk (T)"), (VoiceMode.Off, "Off") })
            {
                var chosen = mode;
                var button = Choice(label, () => { VoiceChat.Mode = chosen; Refresh(); });
                modes.Add(button);
                _modeButtons[mode] = button;
            }
            _micButton = root.Q<Button>("micButton");
            _micButton.clicked += NextMicrophone;
            _codecButton = root.Q<Button>("codecButton");
            _codecButton.clicked += () => ThroughCodec = !ThroughCodec;
            var slider = root.Q<Slider>("thresholdSlider");
            slider.value = VoiceChat.ThresholdDb;
            slider.RegisterValueChangedCallback(e => VoiceChat.ThresholdDb = e.newValue);
            _meterFill = root.Q("meterFill");
            _meterThreshold = root.Q("meterThreshold");
            _status = root.Q<Label>("status");
            Refresh();
        }

        public static IEnumerable<ChalkerEmote> Emotes() => ((ChalkerEmote[])Enum.GetValues(typeof(ChalkerEmote))).Where(e => e != ChalkerEmote.None);

        private static Button Choice(string text, Action clicked)
        {
            var button = new Button(clicked) { text = text };
            button.AddToClassList("hud-button");
            button.AddToClassList("lab-choice");
            return button;
        }

        public void SetPersonality(ChalkerPersonality personality)
        {
            _chalker.Personality = personality;
            Refresh();
        }

        public void SetColor(string color)
        {
            _color = color;
            _chalker.SetColor(ZoneViewFactory.SeatColor(color));
            Refresh();
        }

        /// <summary>Freezes emotes mid-way, to look at one moment of them.</summary>
        public void SetHold(bool hold)
        {
            _chalker.Hold = hold;
            Refresh();
        }

        private void NextMicrophone()
        {
            var devices = new List<string> { "" };
            devices.AddRange(Microphone.devices);
            var index = devices.IndexOf(VoiceChat.Device);
            VoiceChat.Device = devices[(index + 1) % devices.Count];
            Refresh();
        }

        private void Refresh()
        {
            if (_micButton == null || _chalker == null)
                return;
            foreach (var pair in _personalityButtons)
                pair.Value.EnableInClassList("lab-choice--on", pair.Key == _chalker.Personality);
            foreach (var pair in _modeButtons)
                pair.Value.EnableInClassList("lab-choice--on", pair.Key == VoiceChat.Mode);
            foreach (var swatch in _swatches)
                swatch.EnableInClassList("lab-swatch--on", swatch.name == _color);
            _holdButton.text = _chalker.Hold ? "H Hold: on" : "H Hold: off";
            _holdButton.EnableInClassList("lab-choice--on", _chalker.Hold);
            _micButton.text = "Microphone: " + (VoiceChat.Device == "" ? "system default" : VoiceChat.Device);
            _codecButton.text = _codec ? "Through the network codec: on (use headphones)" : "Through the network codec: off";
            _codecButton.EnableInClassList("lab-choice--on", _codec);
        }

        private void Update()
        {
            if (_chalker == null)
                return;
            HandleInput();
            PlaceCamera();

            var gaze = Gaze();
            var level = _codec ? _voice.LevelOf(VoiceChat.SelfSeat) : _voice != null && _voice.Talking ? _voice.MicLevel : 0f;
            _chalker.Pose(Vector3.zero, Vector3.forward, gaze, gaze, level, Time.deltaTime);
            var speaker = _voice != null ? _voice.SpeakerOf(VoiceChat.SelfSeat) : null;
            if (speaker != null)
                speaker.transform.position = _chalker.Mouth;
            UpdateMeter();
        }

        private void HandleInput()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null)
            {
                var emotes = Emotes().ToList();
                for (var i = 0; i < EmoteKeys.Length && i < emotes.Count; i++)
                    if (keyboard[EmoteKeys[i]].wasPressedThisFrame)
                        _chalker.Play(emotes[i]);
                if (keyboard.tabKey.wasPressedThisFrame)
                {
                    var all = (ChalkerPersonality[])Enum.GetValues(typeof(ChalkerPersonality));
                    SetPersonality(all[(Array.IndexOf(all, _chalker.Personality) + 1) % all.Length]);
                }
                if (keyboard.hKey.wasPressedThisFrame)
                    SetHold(!_chalker.Hold);
                if (keyboard.spaceKey.wasPressedThisFrame)
                {
                    _yaw = HomeYaw;
                    _pitch = HomePitch;
                    _distance = HomeDistance;
                }
            }
            if (mouse == null || OverPanel(mouse.position.ReadValue()))
                return;
            if (mouse.rightButton.isPressed)
            {
                var delta = mouse.delta.ReadValue();
                _yaw += delta.x * 0.25f;
                _pitch = Mathf.Clamp(_pitch - delta.y * 0.25f, -30f, 70f);
            }
            var wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f)
                _distance = Mathf.Clamp(_distance * (wheel > 0f ? 0.9f : 1.1f), 0.45f, 3f);
        }

        private bool OverPanel(Vector2 screen)
        {
            if (_panel == null || _panel.panel == null)
                return false;
            var point = RuntimePanelUtils.ScreenToPanel(_panel.panel, new Vector2(screen.x, Screen.height - screen.y));
            return _panel.worldBound.Contains(point);
        }

        private void PlaceCamera()
        {
            if (_camera == null)
                return;
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            _camera.transform.SetPositionAndRotation(HeadTarget + rotation * new Vector3(0f, 0f, -_distance), rotation);
        }

        /// <summary>Where the mouse points, on a plane between the chalker and the camera; the camera itself when the mouse is on the panel.</summary>
        private Vector3 Gaze()
        {
            var mouse = Mouse.current;
            if (_camera == null)
                return HeadTarget + Vector3.forward;
            if (mouse == null || OverPanel(mouse.position.ReadValue()))
                return _camera.transform.position;
            var ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
            var toCamera = _camera.transform.position - HeadTarget;
            var plane = new Plane(-_camera.transform.forward, HeadTarget + toCamera * 0.5f);
            return plane.Raycast(ray, out var distance) ? ray.GetPoint(distance) : _camera.transform.position;
        }

        private void UpdateMeter()
        {
            if (_meterFill == null || _voice == null)
                return;
            static float Percent(float db) => Mathf.Clamp01((db + 70f) / 70f) * 100f;
            var db = Core.Net.VoiceGate.Decibels(_voice.MicLevel);
            // Live values from the microphone, not styles.
            _meterFill.style.width = Length.Percent(Percent(db));
            _meterThreshold.style.left = Length.Percent(Percent(VoiceChat.ThresholdDb));
            _meterFill.EnableInClassList("lab-meter-fill--talking", _voice.Talking);
            _status.text = _voice.MicProblem != null ? _voice.MicProblem
                : VoiceChat.Mode == VoiceMode.Off ? "Microphone off"
                : !_voice.MicRunning && _voice.FakeMicrophone == null ? "Starting the microphone…"
                : $"{(_voice.Talking ? "Talking" : "Listening")}  ·  {Mathf.RoundToInt(db)} dB  ·  mouth {Mathf.RoundToInt(_chalker.MouthOpen * 100f)}%"
                  + (_chalker.Emote != ChalkerEmote.None ? $"  ·  {_chalker.Emote}" : "");
        }

        private void OnDestroy()
        {
            _chalker?.Destroy();
            VoiceChat.HearMyself = false;
        }
    }
}
