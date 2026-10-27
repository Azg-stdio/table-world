using System;
using System.Globalization;
using UnityEngine;
using VoltstroStudios.UnityWebBrowser.Communication;
using VoltstroStudios.UnityWebBrowser.Core;
using VoltstroStudios.UnityWebBrowser.Core.Engines;
using VoltstroStudios.UnityWebBrowser.Shared.Popups;

namespace TableWorld.View.Karaoke
{
    /// <summary>
    /// The karaoke screen (ADR 0013 §6, T12.9): an embedded Chromium (UnityWebBrowser's CEF engine, for now) drawn onto a
    /// renderer, showing our own <see cref="PlayerPage"/> with YouTube's official player in it. It shows nothing else:
    /// any other page is sent back, pop-ups are ignored, and it keeps no cookies or logins (incognito). The rest of the
    /// game talks to this screen (load, play, pause, seek, its state), never to the plugin, so the engine under it can
    /// change.
    /// </summary>
    public sealed class BrowserScreen : BaseUwbClientManager
    {
        private Renderer _target;
        private Material _material;
        private PlayerPage _page;
        /// <summary>A video was asked for and has not started yet: once the player reports, the screen clicks and plays.</summary>
        private bool _starting = true;

        /// <summary>The video the screen was last asked to show.</summary>
        public string VideoId { get; private set; }

        /// <summary>True once the engine process answered and the screen can load pages.</summary>
        public bool Ready => browserClient != null && browserClient.ReadySignalReceived && browserClient.IsConnected;

        /// <summary>Frames per second the engine sends.</summary>
        public int Fps => browserClient?.FPS ?? 0;

        /// <summary>Pages refused since the screen opened (another site, a link out of the player).</summary>
        public int Refused { get; private set; }

        /// <summary>
        /// The page's title as it last changed: the page reports the player's state through it (see <see cref="PlayerPage"/>),
        /// the one channel from page to game that needs nothing registered.
        /// </summary>
        public string Title { get; private set; }

        /// <summary>YouTube's player state: -1 unstarted, 0 ended, 1 playing, 2 paused, 3 buffering, 5 cued; null before it reports.</summary>
        public int? State { get; private set; }

        /// <summary>The video's time in seconds, as the player last reported it.</summary>
        public double Time { get; private set; }

        public double Duration { get; private set; }

        private float _reportedAt;

        /// <summary>
        /// The video's time now: the last report, plus the time since it arrived while playing (reports come every tenth
        /// of a second, so a bare <see cref="Time"/> is up to that much behind).
        /// </summary>
        public double TimeNow => State == 1 ? Time + (UnityEngine.Time.realtimeSinceStartup - _reportedAt) : Time;

        /// <summary>YouTube's error for the video (2 bad id, 5 HTML5 error, 100 not found, 101 and 150 embedding not allowed), or null.</summary>
        public int? Error { get; private set; }

        /// <summary>The page the screen may show: ours, with YouTube's player in it.</summary>
        public bool Allowed(string url) =>
            _page != null && url.StartsWith(_page.Url, StringComparison.Ordinal);

        /// <summary>A screen on <paramref name="target"/>, its page <paramref name="width"/> × <paramref name="height"/> pixels, showing <paramref name="videoId"/>.</summary>
        public static BrowserScreen Create(Renderer target, string videoId, int width = 1280, int height = 720)
        {
            var go = new GameObject("Browser Screen");
            go.SetActive(false);
            go.transform.SetParent(target.transform, false);
            var screen = go.AddComponent<BrowserScreen>();
            screen._target = target;
            screen._page = new PlayerPage();
            screen.VideoId = videoId;
            var client = screen.browserClient;
            client.engine = Resources.Load<EngineConfiguration>("Cef Engine Configuration");
            client.communicationLayer = Resources.Load<CommunicationLayer>("TCP Communication Layer");
            client.initialUrl = screen._page.PageFor(videoId);
            // The Resolution setter resizes a running browser (and throws before it runs): the size it starts at is the field.
            typeof(WebBrowserClient).GetField("resolution", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(client, new VoltstroStudios.UnityWebBrowser.Shared.Resolution((uint)width, (uint)height));
            client.backgroundColor = new Color32(0, 0, 0, 255);
            client.incognitoMode = true;
            client.localStorage = false;
            client.popupAction = PopupAction.Ignore;
            client.webRtc = false;
            client.remoteDebugging = false;
            // Starting Chromium takes a moment the first time.
            client.engineStartupTimeout = 15000;
            go.SetActive(true);
            return screen;
        }

        /// <summary>Plays another video from <paramref name="at"/> seconds.</summary>
        public void LoadVideo(string videoId, double at = 0)
        {
            VideoId = videoId;
            Error = null;
            _starting = true;
            Run($"tw.load('{Escape(videoId)}',{at.ToString(CultureInfo.InvariantCulture)})");
        }

        public void Play() => Run("tw.play()");

        public void Pause() => Run("tw.pause()");

        public void Seek(double seconds) => Run($"tw.seek({seconds.ToString(CultureInfo.InvariantCulture)})");

        /// <summary>
        /// Clicks the middle of the page, as a player would click YouTube's play button. Chromium lets a page play sound
        /// only after someone interacted with it, and the plugin cannot pass the switch that lifts the rule, so the screen
        /// does it once a page has loaded; later play and seek calls then work.
        /// </summary>
        public void ClickToStart()
        {
            if (!Ready)
                return;
            var size = browserClient.Resolution;
            var middle = new Vector2(size.Width / 2f, size.Height / 2f);
            browserClient.SendMouseMove(middle);
            browserClient.SendMouseClick(middle, 1, VoltstroStudios.UnityWebBrowser.Shared.Events.MouseClickType.Left,
                VoltstroStudios.UnityWebBrowser.Shared.Events.MouseEventType.Down);
            browserClient.SendMouseClick(middle, 1, VoltstroStudios.UnityWebBrowser.Shared.Events.MouseClickType.Left,
                VoltstroStudios.UnityWebBrowser.Shared.Events.MouseEventType.Up);
        }

        /// <summary>Starts a video the player has loaded: the click Chromium wants for sound, then play.</summary>
        protected override void OnFixedUpdate()
        {
            if (!_starting || !Ready || State == null || Error != null)
                return;
            _starting = false;
            ClickToStart();
            Play();
        }

        private void Run(string js)
        {
            if (Ready)
                browserClient.ExecuteJs(js);
        }

        private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("'", "\\'");

        protected override void OnStart()
        {
            _material = new Material(RequiredShaders.Unlit) { name = "Browser screen" };
            _material.SetTexture("_BaseMap", browserClient.BrowserTexture);
            // The engine's frames arrive upside down.
            _material.SetTextureScale("_BaseMap", new Vector2(1f, -1f));
            _material.SetTextureOffset("_BaseMap", new Vector2(0f, 1f));
            _material.SetColor("_BaseColor", Color.white);
            if (_target != null)
                _target.sharedMaterial = _material;
            browserClient.OnUrlChanged += OnUrlChanged;
            browserClient.OnTitleChange += OnTitle;
        }

        /// <summary>Reads the page's report: <c>state|time|duration|video</c>, or <c>error|code</c>.</summary>
        private void OnTitle(string title)
        {
            Title = title;
            var parts = title.Split('|');
            if (parts.Length == 2 && parts[0] == "error" && int.TryParse(parts[1], out var code))
            {
                Error = code;
                return;
            }
            if (parts.Length >= 3 && int.TryParse(parts[0], out var state))
            {
                State = state;
                Time = double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : Time;
                _reportedAt = UnityEngine.Time.realtimeSinceStartup;
                Duration = double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : Duration;
            }
        }

        /// <summary>A page that is not ours (a link clicked in the player, a redirect) is sent back to the player.</summary>
        private void OnUrlChanged(string url)
        {
            if (Allowed(url) || url.StartsWith("about:", StringComparison.Ordinal))
                return;
            Refused++;
            Debug.LogWarning($"[BrowserScreen] Refused '{url}': the screen shows only YouTube's player.");
            if (_page != null && VideoId != null)
                browserClient.LoadUrl(_page.PageFor(VideoId));
        }

        protected override void OnDestroyed()
        {
            _page?.Dispose();
            if (_material != null)
                Destroy(_material);
        }
    }
}
