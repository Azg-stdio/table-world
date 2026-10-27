using Core.Rules;
using TableWorld.View.Karaoke;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// The karaoke room's big screen bar (M11): paste a YouTube link, Play puts it on the room's screen, Pause pauses it,
    /// and a line says what the screen is doing. Shown only in the karaoke room. For now each player's screen is their own;
    /// the host's clock and the song queue come next (T12.11, T12.12).
    /// </summary>
    public sealed class ScreenBar
    {
        private readonly VisualElement _bar;
        private readonly TextField _link;
        private readonly Label _status;
        private readonly TableView _table;
        private EnvironmentView _environment;
        private BrowserScreen _screen;
        private string _shown;

        public ScreenBar(VisualElement root, TableView table)
        {
            _table = table;
            _bar = root.Q("screenBar");
            _link = root.Q<TextField>("screenLink");
            _status = root.Q<Label>("screenStatus");
            root.Q<Button>("screenPlay").clicked += () => PlayLink(_link.value);
            root.Q<Button>("screenPause").clicked += () => _screen?.Pause();
            _link.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    PlayLink(_link.value);
            });
        }

        /// <summary>The screen the bar drives (null before the first video).</summary>
        public BrowserScreen Screen => _screen != null ? _screen : null;

        /// <summary>Puts the video a YouTube link names on the room's screen; false with a reason in the status line otherwise.</summary>
        public bool PlayLink(string link)
        {
            var id = YouTube.VideoId(link);
            if (id == null)
            {
                Say(Ui.T("That is not a YouTube link."));
                return false;
            }
            _environment ??= Object.FindAnyObjectByType<EnvironmentView>();
            if (_environment?.Screen == null)
            {
                Say(Ui.T("This room has no screen."));
                return false;
            }
            if (_screen == null)
                _screen = BrowserScreen.Create(_environment.Screen, id);
            else
                _screen.LoadVideo(id);
            Say(Ui.T("Loading the video…"));
            return true;
        }

        /// <summary>Shows the bar in the karaoke room and keeps the status line up to date.</summary>
        public void Update()
        {
            if (_bar == null)
                return;
            var karaoke = _table.Engine?.State.Environment is { Room: RoomPreset.Karaoke };
            _bar.EnableInClassList("hidden", !karaoke);
            // Another room (or the room rebuilt): the screen it was drawing on went with the old room.
            if (_screen != null && !karaoke)
                Object.Destroy(_screen.gameObject);
            if (_screen == null || !karaoke)
                _screen = null;
            if (_screen == null || !karaoke)
                return;
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
