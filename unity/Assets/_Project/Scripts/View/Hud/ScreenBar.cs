using Core.Engine;
using Core.Rules;
using TableWorld.View.Karaoke;
using TableWorld.View.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// The karaoke room's big screen bar (M11): paste a YouTube link and Play puts it on the room's screen for everyone,
    /// Pause pauses it for everyone, Play again resumes it. The screen is shared table state (<see cref="ScreenState"/>,
    /// changed by <see cref="SetScreenCommand"/>); each machine keeps its own player where that state says the video is
    /// now on the host's clock, a listener a little later by the singer's voice delay (T12.11). Shown only in the
    /// karaoke room.
    /// </summary>
    public sealed class ScreenBar
    {
        /// <summary>How far a player may drift from where the video should be before it is moved, in seconds.</summary>
        public const double Tolerance = 0.35;
        /// <summary>After a seek the player needs a moment to report where it landed.</summary>
        public const float SeekCooldown = 2f;

        private readonly VisualElement _bar;
        private readonly TextField _link;
        private readonly Label _status;
        private readonly TableView _table;
        private EnvironmentView _environment;
        private NetworkTableSession _session;
        private Chalkers.ChalkersView _chalkers;
        private BrowserScreen _screen;
        private string _shown;
        private float _nextSeek;

        public ScreenBar(VisualElement root, TableView table)
        {
            _table = table;
            _bar = root.Q("screenBar");
            _link = root.Q<TextField>("screenLink");
            _status = root.Q<Label>("screenStatus");
            root.Q<Button>("screenPlay").clicked += () => PlayLink(_link.value);
            root.Q<Button>("screenPause").clicked += () => Pause();
            _link.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    PlayLink(_link.value);
            });
        }

        /// <summary>This machine's player (null while the screen is off).</summary>
        public BrowserScreen Screen => _screen != null ? _screen : null;

        /// <summary>Now on the host's clock, in ms (this machine's clock when playing alone).</summary>
        public double HostNow
        {
            get
            {
                _session ??= Object.FindAnyObjectByType<NetworkTableSession>();
                return _session != null ? _session.HostNow : Time.realtimeSinceStartupAsDouble * 1000.0;
            }
        }

        /// <summary>
        /// How much later than the host's clock this machine plays the video, in ms: the singer's voice delay, so their
        /// voice and the song arrive together (ADR 0013 §1). Nobody singing, or singing yourself: none.
        /// </summary>
        public double Delay
        {
            get
            {
                _chalkers ??= Object.FindAnyObjectByType<Chalkers.ChalkersView>();
                var singer = _chalkers != null ? _chalkers.Singer : null;
                if (singer == null || singer == _table.LocalSeat || _session == null)
                    return 0;
                return _session.VoiceDelayOf(singer) ?? 0;
            }
        }

        /// <summary>
        /// Puts the video a YouTube link names on the screen for everyone, from the start. An empty link resumes what is
        /// paused. False with a reason in the status line otherwise.
        /// </summary>
        public bool PlayLink(string link)
        {
            var state = _table.Engine?.State.Screen;
            if (string.IsNullOrWhiteSpace(link) && state is { VideoId: { } paused, Playing: false })
                return Send(new SetScreenCommand { VideoId = paused, Playing = true, Position = state.Position, At = HostNow });
            var id = YouTube.VideoId(link);
            if (id == null)
            {
                Say(Ui.T("That is not a YouTube link."));
                return false;
            }
            _link.SetValueWithoutNotify("");
            Say(Ui.T("Loading the video…"));
            return Send(new SetScreenCommand { VideoId = id, Playing = true, Position = 0, At = HostNow });
        }

        /// <summary>Pauses the screen for everyone where it is now.</summary>
        public bool Pause()
        {
            var state = _table.Engine?.State.Screen;
            if (state is not { VideoId: { } id, Playing: true })
                return false;
            var now = HostNow;
            return Send(new SetScreenCommand { VideoId = id, Playing = false, Position = state.PositionAt(now), At = now });
        }

        private bool Send(SetScreenCommand command)
        {
            var result = _table.Execute(command);
            if (!result.Ok)
                Say(Ui.Refusal(result.Error));
            return result.Ok;
        }

        /// <summary>Shows the bar in the karaoke room, keeps this machine's player on the shared screen, and the status line up to date.</summary>
        public void Update()
        {
            if (_bar == null)
                return;
            var karaoke = _table.Engine?.State.Environment is { Room: RoomPreset.Karaoke };
            _bar.EnableInClassList("hidden", !karaoke);
            var state = karaoke ? _table.Engine.State.Screen : null;
            // Off, another room, or the room rebuilt: the player goes (it was drawing on the old room's screen).
            if (_screen != null && state?.VideoId == null)
                Object.Destroy(_screen.gameObject);
            if (_screen == null || state?.VideoId == null)
                _screen = null;
            if (state?.VideoId is not { } videoId)
            {
                if (karaoke)
                    Say("");
                return;
            }

            _environment ??= Object.FindAnyObjectByType<EnvironmentView>();
            if (_screen == null)
            {
                if (_environment == null || _environment.Screen == null)
                    return;
                _screen = BrowserScreen.Create(_environment.Screen, videoId);
            }
            else if (_screen.VideoId != videoId)
                _screen.LoadVideo(videoId, state.PositionAt(HostNow - Delay));
            Follow(state);
            Say(_screen.Error is { } error ? ErrorText(error)
                : !_screen.Ready ? Ui.T("Starting the screen…")
                : _screen.State switch
                {
                    1 => Ui.T("Playing {time}", ("time", Clock(_screen.Time))),
                    2 => Ui.T("Paused at {time}", ("time", Clock(_screen.Time))),
                    3 => Ui.T("Buffering…"),
                    0 => Ui.T("Finished"),
                    _ => Ui.T("Loading the video…"),
                });
        }

        /// <summary>Plays or pauses this machine's player as the shared screen says, and moves it when it drifts.</summary>
        private void Follow(ScreenState state)
        {
            if (!_screen.Ready || _screen.State is not { } playerState || _screen.Error != null)
                return;
            var target = state.PositionAt(HostNow - Delay);
            if (state.Playing && playerState is 2 or 5 or -1)
                _screen.Play();
            else if (!state.Playing && playerState is 1 or 3)
                _screen.Pause();
            if (playerState != 0 && System.Math.Abs(_screen.TimeNow - target) > Tolerance && Time.unscaledTime >= _nextSeek
                && (_screen.Duration <= 0 || target < _screen.Duration))
            {
                _screen.Seek(target);
                _nextSeek = Time.unscaledTime + SeekCooldown;
            }
        }

        /// <summary>YouTube's error codes in words a player can act on.</summary>
        private static string ErrorText(int code) => code switch
        {
            101 or 150 or 153 => Ui.T("This video's owner does not allow playing it outside YouTube. Try another upload of the song."),
            100 => Ui.T("This video was not found (removed or private)."),
            2 => Ui.T("That link does not name a video."),
            _ => Ui.T("YouTube could not play this video (error {code}).", ("code", code)),
        };

        private static string Clock(double seconds)
        {
            var total = (int)seconds;
            return $"{total / 60}:{total % 60:00}";
        }

        private void Say(string text)
        {
            if (text == _shown)
                return;
            _shown = text;
            _status.text = text;
        }
    }
}
