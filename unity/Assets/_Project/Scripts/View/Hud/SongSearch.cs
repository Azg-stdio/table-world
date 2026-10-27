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
    /// panel. A video whose owner does not allow playing it elsewhere is refused with the panel still open, to choose
    /// another. Escape or Close closes it.
    /// </summary>
    public sealed class SongSearch
    {
        /// <summary>Added to what the player types, so the results are versions to sing over.</summary>
        public const string Suffix = " karaoke";

        private readonly VisualElement _panel;
        private readonly TextField _query;
        private readonly Image _view;
        private readonly Label _status;
        private readonly ScreenBar _bar;
        private SearchBrowser _browser;
        private string _picked;

        public SongSearch(VisualElement root, ScreenBar bar)
        {
            _bar = bar;
            _panel = root.Q("songSearch");
            _query = root.Q<TextField>("songSearchQuery");
            _query.textEdition.placeholder = Ui.T("Song or artist");
            _query.textEdition.hidePlaceholderOnFocus = true;
            _view = root.Q<Image>("songSearchView");
            _status = root.Q<Label>("songSearchStatus");
            // The engine sends its frames upside down.
            _view.uv = new Rect(0f, 1f, 1f, -1f);
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
            var search = (query ?? "").Trim() + Suffix;
            if (_browser == null)
            {
                _browser = SearchBrowser.Create(null, search.Trim());
                _browser.Picked += OnPicked;
            }
            else
                _browser.Search(search.Trim());
            _query.Focus();
        }

        /// <summary>Searches again for <paramref name="query"/>.</summary>
        public void Search(string query)
        {
            if (_browser == null)
                Open(query);
            else
                _browser.Search(((query ?? "").Trim() + Suffix).Trim());
        }

        public void Close()
        {
            _panel.AddToClassList("hidden");
            _view.image = null;
            if (_browser != null)
                Object.Destroy(_browser.gameObject);
            _browser = null;
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
                _view.image = _browser.Texture;
            if (Keyboard.current is { } keyboard && keyboard.escapeKey.wasPressedThisFrame)
                Close();
        }

        private void OnPicked(string videoId)
        {
            // A click can report the video twice (the click, then the page it would have opened).
            if (videoId == _picked)
                return;
            _picked = videoId;
            Debug.Log($"[SongSearch] Picked {videoId}.");
            _status.text = Ui.T("Checking the video…");
            _bar.QueueLink("https://www.youtube.com/watch?v=" + videoId, refused =>
            {
                if (refused != null)
                {
                    _picked = null;
                    _status.text = refused;
                    return;
                }
                _query.SetValueWithoutNotify("");
                Close();
            });
        }

        /// <summary>A point on the picture as 0–1 across and down the page.</summary>
        private Vector2 At(Vector2 local)
        {
            var size = _view.contentRect.size;
            return size.x > 0 && size.y > 0 ? new Vector2(local.x / size.x, local.y / size.y) : Vector2.zero;
        }
    }
}
