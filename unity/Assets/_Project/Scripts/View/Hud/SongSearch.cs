using TableWorld.View.Karaoke;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// The karaoke room's song search (T12.15): Search on the screen bar opens YouTube's own results in a panel
    /// (<see cref="SearchBrowser"/>, a second Chromium that runs only while the panel is open). The mouse and wheel go to
    /// the page; clicking a video adds it to the queue (as Add to queue does, title and embedding checked) and closes the
    /// panel. Every result is checked as it appears, first with oEmbed, then by trying it in YouTube's embedded player in
    /// a hidden, muted browser (some videos pass oEmbed and still refuse every embedded player); those that will not play
    /// are hidden. One clicked before its turn is tried there and then. Escape or Close closes it.
    /// </summary>
    public sealed class SongSearch
    {
        /// <summary>Added to what the player types, so the results are versions to sing over.</summary>
        public const string Suffix = " karaoke";

        /// <summary>The page's picture turned right way up (the engine sends its frames upside down).</summary>
        public static readonly Rect Flipped = new Rect(0f, 1f, 1f, -1f);

        private readonly VisualElement _panel;
        private readonly TextField _query;
        private readonly Image _view;
        private readonly Label _status;
        private readonly Label _hint;
        /// <summary>Results of this search hidden because they cannot play here.</summary>
        private readonly System.Collections.Generic.HashSet<string> _hidden = new();
        private readonly System.Collections.Generic.List<string> _toBlock = new();
        private string _hintShown;
        private readonly ScreenBar _bar;
        private SearchBrowser _browser;
        private string _picked;
        /// <summary>The hidden browser that tries every result in the embedded player, and the results waiting for it.</summary>
        private readonly System.Collections.Generic.List<SearchBrowser> _testers = new();
        private readonly System.Collections.Generic.List<string> _toConfirm = new();

        /// <summary>Hidden players trying results at once.</summary>
        public const int Testers = 2;
        private readonly System.Collections.Generic.Queue<string> _toTry = new();
        private readonly System.Collections.Generic.HashSet<string> _waitingToTry = new();

        public SongSearch(VisualElement root, ScreenBar bar)
        {
            _bar = bar;
            _panel = root.Q("songSearch");
            _query = root.Q<TextField>("songSearchQuery");
            _query.textEdition.placeholder = Ui.T("Song or artist");
            _query.textEdition.hidePlaceholderOnFocus = true;
            _view = root.Q<Image>("songSearchView");
            _status = root.Q<Label>("songSearchStatus");
            _hint = root.Q<Label>("songSearchHint");
            _view.uv = Flipped;
            _view.scaleMode = ScaleMode.StretchToFill;
            root.Q<Button>("screenSearch").clicked += () => Open(_query.value);
            root.Q<Button>("songSearchGo").clicked += () => Search(_query.value);
            root.Q<Button>("songSearchClose").clicked += Close;
            _query.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    Search(_query.value);
            });
            _view.RegisterCallback<PointerMoveEvent>(e => _browser?.PointerMove(At(e.localPosition)));
            _view.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0)
                    return;
                _view.CapturePointer(e.pointerId);
                _browser?.PointerButton(At(e.localPosition), true);
            });
            _view.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 0)
                    return;
                _view.ReleasePointer(e.pointerId);
                _browser?.PointerButton(At(e.localPosition), false);
            });
            // Unity counts about 3 a notch, down positive; Chromium 120 a notch, up positive.
            _view.RegisterCallback<WheelEvent>(e =>
            {
                _browser?.Scroll(At(e.localMousePosition), Mathf.RoundToInt(-e.delta.y * 40f));
                e.StopPropagation();
            });
        }

        /// <summary>True while the panel is open: the HUD's keys and walking wait.</summary>
        public bool Visible => !_panel.ClassListContains("hidden");

        /// <summary>The search browser (null while closed).</summary>
        public SearchBrowser Browser => _browser != null ? _browser : null;

        /// <summary>Opens the panel on YouTube's results for <paramref name="query"/> (popular karaoke songs when empty).</summary>
        public void Open(string query)
        {
            _panel.RemoveFromClassList("hidden");
            _picked = null;
            _status.text = "";
            _hidden.Clear();
            var search = Words(query);
            if (_browser == null)
            {
                _browser = SearchBrowser.Create(null, search);
                _browser.Picked += OnPicked;
                _browser.Found += OnFound;
            }
            else
                _browser.Search(search);
            _query.Focus();
        }

        /// <summary>Searches again for <paramref name="query"/>.</summary>
        public void Search(string query)
        {
            if (_browser == null)
                Open(query);
            else
            {
                _hidden.Clear();
                _browser.Search(Words(query));
            }
        }

        /// <summary>What is searched for: the player's words, with "karaoke" unless they said it.</summary>
        public static string Words(string query)
        {
            var words = (query ?? "").Trim();
            return words.IndexOf(Suffix.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0 ? words : (words + Suffix).Trim();
        }

        public void Close()
        {
            _panel.AddToClassList("hidden");
            _view.image = null;
            if (_browser != null)
                Object.Destroy(_browser.gameObject);
            _browser = null;
            foreach (var tester in _testers)
                if (tester != null)
                    Object.Destroy(tester.gameObject);
            _testers.Clear();
            _toConfirm.Clear();
            _toTry.Clear();
            _waitingToTry.Clear();
        }

        /// <summary>Keeps the picture current; Escape closes; leaving the karaoke room closes.</summary>
        public void Update(bool karaoke)
        {
            if (!Visible)
                return;
            if (!karaoke)
            {
                Close();
                return;
            }
            if (_browser != null && _view.image != _browser.Texture)
            {
                _view.image = _browser.Texture;
                // The engine sends its frames upside down; changing the picture resets the flip.
                _view.uv = Flipped;
            }
            TryNext();
            if (_browser != null && _toConfirm.Count > 0)
            {
                _browser.Confirm(_toConfirm);
                _toConfirm.Clear();
            }
            if (_browser != null && _toBlock.Count > 0)
            {
                _browser.Block(_toBlock);
                _toBlock.Clear();
            }
            ShowHint();
            if (Keyboard.current is { } keyboard && keyboard.escapeKey.wasPressedThisFrame)
                Close();
        }

        /// <summary>The results show these videos: each is checked (answers are remembered), and those that cannot play here are hidden.</summary>
        private void OnFound(string[] videoIds)
        {
            var browser = _browser;
            foreach (var id in videoIds)
                _bar.Videos.Ask(id, (answer, _) =>
                {
                    if (browser != _browser)
                        return;
                    if (answer == VideoCheck.Answer.Blocked || answer == VideoCheck.Answer.Gone)
                    {
                        if (_hidden.Add(id))
                            _toBlock.Add(id);
                    }
                    else if (_bar.Videos.Played(id))
                        _toConfirm.Add(id);
                    else if (_waitingToTry.Add(id))
                        _toTry.Enqueue(id);
                });
        }

        /// <summary>
        /// Tries the next waiting result in the hidden player, top of the list first: one that plays is remembered (a
        /// click on it queues at once), one that will not is hidden.
        /// </summary>
        private void TryNext()
        {
            if (_browser == null || _toTry.Count == 0)
                return;
            while (_testers.Count < Testers)
                _testers.Add(SearchBrowser.CreateTester(null));
            foreach (var tester in _testers)
            {
                if (_toTry.Count == 0)
                    return;
                if (tester == null || !tester.Ready || tester.Probing)
                    continue;
                var id = _toTry.Dequeue();
                _waitingToTry.Remove(id);
                if (_bar.Videos.Played(id))
                {
                    _toConfirm.Add(id);
                    continue;
                }
                if (_bar.Videos.TryGet(id, out var known, out _) && known != VideoCheck.Answer.Playable)
                    continue;
                tester.Probe(id, error =>
                {
                    if (!_testers.Contains(tester))
                        return;
                    if (error is { } code)
                    {
                        _bar.Videos.Mark(id, code == 100 ? VideoCheck.Answer.Gone : VideoCheck.Answer.Blocked);
                        if (_hidden.Add(id))
                            _toBlock.Add(id);
                    }
                    else
                    {
                        _bar.Videos.MarkPlayed(id);
                        _toConfirm.Add(id);
                    }
                });
            }
        }

        /// <summary>How to choose, and how many results were hidden.</summary>
        private void ShowHint()
        {
            var text = Ui.T("Click a video to add it to the queue. Scroll with the mouse wheel.");
            if (_hidden.Count == 1)
                text += " " + Ui.T("1 video that cannot play here is hidden.");
            else if (_hidden.Count > 1)
                text += " " + Ui.T("{count} videos that cannot play here are hidden.", ("count", _hidden.Count));
            if (text == _hintShown)
                return;
            _hintShown = text;
            _hint.text = text;
        }

        /// <summary>
        /// A video was chosen: asked about (oEmbed), then tried in the official player, muted, where the screen will play
        /// it; only a video that plays is queued and closes the panel. One that will not is hidden from the results and
        /// the panel says why.
        /// </summary>
        private void OnPicked(string videoId)
        {
            // A click can report the video twice (the click, then the page it would have opened); one try at a time.
            if (videoId == _picked || (_browser != null && _browser.Probing))
                return;
            _picked = videoId;
            Debug.Log($"[SongSearch] Picked {videoId}.");
            _status.text = Ui.T("Checking the video…");
            var browser = _browser;
            _bar.Videos.Ask(videoId, (answer, _) =>
            {
                if (browser == null || browser != _browser)
                    return;
                if (answer == VideoCheck.Answer.Blocked || answer == VideoCheck.Answer.Gone)
                {
                    Refuse(videoId, ScreenBar.ErrorText(answer == VideoCheck.Answer.Gone ? 100 : 150));
                    return;
                }
                if (_bar.Videos.Played(videoId))
                {
                    Queue(videoId);
                    return;
                }
                browser.Probe(videoId, error =>
                {
                    if (browser != _browser)
                        return;
                    if (error is { } code)
                    {
                        Debug.Log($"[SongSearch] {videoId} refused by the player (error {code}).");
                        _bar.Videos.Mark(videoId, code == 100 ? VideoCheck.Answer.Gone : VideoCheck.Answer.Blocked);
                        Refuse(videoId, ScreenBar.ErrorText(code));
                        return;
                    }
                    _bar.Videos.MarkPlayed(videoId);
                    Queue(videoId);
                });
            });
        }

        /// <summary>Queues a video known to play and closes the panel (or says why the queue refused it).</summary>
        private void Queue(string videoId)
        {
            _bar.QueueLink("https://www.youtube.com/watch?v=" + videoId, refused =>
            {
                if (refused != null)
                {
                    Refuse(videoId, refused);
                    return;
                }
                _query.SetValueWithoutNotify("");
                Close();
            });
        }

        /// <summary>The panel stays open saying why; the video is hidden from the results.</summary>
        private void Refuse(string videoId, string why)
        {
            _picked = null;
            _status.text = why;
            if (_hidden.Add(videoId))
                _toBlock.Add(videoId);
        }

        /// <summary>A point on the picture as 0–1 across and down the page.</summary>
        private Vector2 At(Vector2 local)
        {
            var size = _view.contentRect.size;
            return size.x > 0 && size.y > 0 ? new Vector2(local.x / size.x, local.y / size.y) : Vector2.zero;
        }
    }
}
