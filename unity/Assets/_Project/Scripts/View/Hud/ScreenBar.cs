using System.Linq;
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
        private readonly VisualElement _songs;
        private string _songsShown;
        /// <summary>The screen the host already moved on from (its video and start), so an ended song advances once.</summary>
        private string _advancedFrom;
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
            _link.textEdition.placeholder = Ui.T("Paste a YouTube link");
            _link.textEdition.hidePlaceholderOnFocus = true;
            _status = root.Q<Label>("screenStatus");
            root.Q<Button>("screenPlay").clicked += () => PlayLink(_link.value);
            root.Q<Button>("screenPause").clicked += () => Pause();
            root.Q<Button>("screenQueue").clicked += () => QueueLink(_link.value);
            root.Q<Button>("screenNext").clicked += () => Next();
            _songs = root.Q("screenSongs");
            _link.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    PlayLink(_link.value);
            });
        }

        /// <summary>What YouTube said about videos this session (titles, which cannot play here): Add to queue and the song search share it.</summary>
        public VideoCheck Videos { get; } = new VideoCheck();

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
                Tell(Ui.T("That is not a YouTube link."));
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

        /// <summary>
        /// Adds the video a YouTube link names to the end of the queue. Its title comes from YouTube's public oEmbed address
        /// (no key needed), which also says whether the owner allows playing it elsewhere: a video that cannot play is
        /// refused here instead of failing on everyone's screen later. Without an answer the song is queued untitled.
        /// <paramref name="done"/> hears how it ended: null once queued, else why not.
        /// </summary>
        public bool QueueLink(string link, System.Action<string> done = null)
        {
            var id = YouTube.VideoId(link);
            if (id == null)
            {
                Tell(Ui.T("That is not a YouTube link."));
                return false;
            }
            _link.SetValueWithoutNotify("");
            Tell(Ui.T("Checking the video…"));
            Videos.Ask(id, (answer, title) =>
            {
                string refused = null;
                if (answer == VideoCheck.Answer.Blocked)
                    refused = ErrorText(150);
                else if (answer == VideoCheck.Answer.Gone)
                    refused = ErrorText(100);
                else if (_table.Execute(new QueueSongCommand { VideoId = id, Title = title }) is { Ok: false } result)
                    refused = Ui.Refusal(result.Error);
                Tell(refused ?? Ui.T("Queued: {song}", ("song", title ?? id)));
                done?.Invoke(refused);
            });
            return true;
        }

        /// <summary>Skips to the next queued song for everyone (the screen goes off when none is waiting).</summary>
        public bool Next() => Send(new PlayNextSongCommand { At = HostNow });

        /// <summary>Takes a queued song out (yours, or any when hosting).</summary>
        public bool Remove(int index) => Send(new RemoveSongCommand { Index = index });

        private bool Send(Command command)
        {
            var result = _table.Execute(command);
            if (!result.Ok)
                Tell(Ui.Refusal(result.Error));
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
            if (karaoke)
            {
                // The host moves the queue on first, so the list never shows a song already playing.
                Advance(state);
                state = _table.Engine.State.Screen;
                ShowSongs();
            }
            // Off, another room, or the room rebuilt: the player goes (it was drawing on the old room's screen).
            if (_screen != null && state?.VideoId == null)
                Object.Destroy(_screen.gameObject);
            if (_screen == null || state?.VideoId == null)
                _screen = null;
            if (state?.VideoId is not { } videoId)
            {
                if (karaoke)
                    Status("");
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
            Status(_screen.Error is { } error ? ErrorText(error)
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

        /// <summary>
        /// The host moves the queue on: when nothing is on the screen, or the song on it has ended, the next queued song
        /// starts for everyone. Only the host, so it happens once.
        /// </summary>
        private void Advance(ScreenState state)
        {
            if (!_table.HostsTable || _table.Engine.State.SongQueue.Count == 0)
                return;
            var ended = state?.VideoId != null && _screen != null && _screen.VideoId == state.VideoId && _screen.State == 0;
            if (state?.VideoId != null && !ended)
                return;
            var key = state == null ? "off" : state.VideoId + "@" + state.At;
            if (key == _advancedFrom)
                return;
            _advancedFrom = key;
            Next();
        }

        /// <summary>The waiting songs under the bar: number, title (or the video id), who added it, and a ✕ for whoever may take it out.</summary>
        private void ShowSongs()
        {
            if (_songs == null)
                return;
            var queue = _table.Engine.State.SongQueue;
            var key = string.Join("|", queue.Select(s => s.VideoId + s.Seat + s.Title)) + "#" + _table.LocalSeat + _table.HostsTable + Ui.Language;
            if (key == _songsShown)
                return;
            _songsShown = key;
            _songs.Clear();
            for (var i = 0; i < queue.Count; i++)
            {
                var song = queue[i];
                var row = new VisualElement();
                row.AddToClassList("screen-bar__song");
                var who = song.Seat != null ? Names.Seat(_table.Engine.State, song.Seat) : Ui.T("the host");
                var name = new Label(Ui.T("{n}. {song} · {who}", ("n", i + 1), ("song", song.Title ?? song.VideoId), ("who", who))) { enableRichText = false };
                name.AddToClassList("screen-bar__song-name");
                row.Add(name);
                if (_table.HostsTable || song.Seat == null || song.Seat == _table.LocalSeat)
                {
                    var index = i;
                    var remove = new Button(() => Remove(index)) { text = "✕", tooltip = Ui.T("Take it out of the queue") };
                    remove.AddToClassList("hud-button");
                    remove.AddToClassList("screen-bar__song-remove");
                    row.Add(remove);
                }
                _songs.Add(row);
            }
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

        /// <summary>How long an answer to something this player did stays in the status line, in seconds.</summary>
        public const float NoticeSeconds = 6f;
        private string _notice;
        private float _noticeUntil;

        /// <summary>Answers something this player did (a refusal, a queued song): it stays a few seconds over the screen's state.</summary>
        private void Tell(string text)
        {
            _notice = text;
            _noticeUntil = Time.unscaledTime + NoticeSeconds;
            Say(text);
        }

        /// <summary>The screen's own state, unless an answer is still showing.</summary>
        private void Status(string text) => Say(Time.unscaledTime < _noticeUntil ? _notice : text);

        /// <summary>The status line's text now.</summary>
        public string Shown => _shown;

        private void Say(string text)
        {
            if (text == _shown)
                return;
            _shown = text;
            _status.text = text;
        }
    }
}
