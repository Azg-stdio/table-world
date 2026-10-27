using System.Collections.Generic;
using TableWorld.View.Karaoke;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace TableWorld.View.Hud
{
    /// <summary>
    /// The karaoke room's song search (T12.15): Search on the screen bar opens our own list of songs. A hidden browser
    /// reads YouTube's results for the words (plus "karaoke"), and every result is checked before it is listed: first
    /// with oEmbed, then by playing it, muted, in YouTube's embedded player in hidden testers (some videos pass oEmbed and
    /// still refuse every embedded player). Only songs that play here appear, in YouTube's order; clicking one queues it
    /// and closes the panel. YouTube's page is never shown. More results are asked for when few pass. Escape or Close
    /// closes it, and its browsers with it.
    /// </summary>
    public sealed class SongSearch
    {
        /// <summary>Added to what the player types, so the results are versions to sing over.</summary>
        public const string Suffix = " karaoke";

        /// <summary>Hidden players trying results at once.</summary>
        public const int Testers = 2;

        /// <summary>Fewer listed than this once every result is checked: more results are asked for.</summary>
        public const int Enough = 20;

        /// <summary>How many results one search reads at most (more costs time for little).</summary>
        public const int MostRead = 80;

        private readonly VisualElement _panel;
        private readonly TextField _query;
        private readonly Label _status;
        private readonly ScrollView _list;
        private readonly ScreenBar _bar;
        private SearchBrowser _reader;
        private readonly List<SearchBrowser> _testers = new();
        private readonly Queue<SearchResult> _toTry = new();
        /// <summary>Results that play here, in YouTube's order, as listed.</summary>
        private readonly List<SearchResult> _listed = new();
        private readonly List<Texture2D> _thumbnails = new();
        private int _read;
        private int _hidden;
        private int _asking;
        private int _trying;
        private float _askedMoreAt;
        private bool _queueing;
        private string _statusShown;

        public SongSearch(VisualElement root, ScreenBar bar)
        {
            _bar = bar;
            _panel = root.Q("songSearch");
            _query = root.Q<TextField>("songSearchQuery");
            _query.textEdition.placeholder = Ui.T("Song or artist");
            _query.textEdition.hidePlaceholderOnFocus = true;
            _status = root.Q<Label>("songSearchStatus");
            _list = root.Q<ScrollView>("songSearchResults");
            root.Q<Button>("screenSearch").clicked += () => Open(_query.value);
            root.Q<Button>("songSearchGo").clicked += () => Search(_query.value);
            root.Q<Button>("songSearchClose").clicked += Close;
            root.Q<Button>("songSearchMore").clicked += More;
            _query.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    Search(_query.value);
            });
        }

        /// <summary>True while the panel is open: the HUD's keys and walking wait.</summary>
        public bool Visible => !_panel.ClassListContains("hidden");

        /// <summary>The songs listed now, in order.</summary>
        public IReadOnlyList<SearchResult> Listed => _listed;

        /// <summary>Results still being checked.</summary>
        public int Checking => _asking + _trying + _toTry.Count;

        /// <summary>Opens the panel on the songs for <paramref name="query"/> (popular karaoke songs when empty).</summary>
        public void Open(string query)
        {
            _panel.RemoveFromClassList("hidden");
            Search(query);
            _query.Focus();
        }

        /// <summary>Searches again for <paramref name="query"/>: the list starts empty and fills as songs pass.</summary>
        public void Search(string query)
        {
            ClearList();
            if (_reader == null)
            {
                _reader = SearchBrowser.CreateReader(null, Words(query));
                _reader.Results += OnResults;
            }
            else
                _reader.Search(Words(query));
            while (_testers.Count < Testers)
                _testers.Add(SearchBrowser.CreateTester(null));
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
            ClearList();
            if (_reader != null)
                Object.Destroy(_reader.gameObject);
            _reader = null;
            foreach (var tester in _testers)
                if (tester != null)
                    Object.Destroy(tester.gameObject);
            _testers.Clear();
        }

        /// <summary>Asks YouTube for more results.</summary>
        public void More()
        {
            _askedMoreAt = Time.unscaledTime;
            _reader?.More();
        }

        /// <summary>Feeds the testers, keeps the status line, asks for more when few pass; Escape or leaving the room closes.</summary>
        public void Update(bool karaoke)
        {
            if (!Visible)
                return;
            if (!karaoke)
            {
                Close();
                return;
            }
            TryNext();
            if (Checking == 0 && _listed.Count < Enough && _read > 0 && _read < MostRead && Time.unscaledTime - _askedMoreAt > 3f)
                More();
            ShowStatus();
            if (Keyboard.current is { } keyboard && keyboard.escapeKey.wasPressedThisFrame)
                Close();
        }

        private void ClearList()
        {
            _list.Clear();
            _listed.Clear();
            _toTry.Clear();
            foreach (var texture in _thumbnails)
                if (texture != null)
                    Object.Destroy(texture);
            _thumbnails.Clear();
            _read = 0;
            _hidden = 0;
            _asking = 0;
            _queueing = false;
            _statusShown = null;
            // Answers still on their way belong to the search they were asked for.
            _generation++;
        }

        private int _generation;

        /// <summary>Results the reader found: each asked about (oEmbed), then tried unless it already played here.</summary>
        private void OnResults(SearchResult[] results)
        {
            var generation = _generation;
            foreach (var result in results)
            {
                _read++;
                _asking++;
                _bar.Videos.Ask(result.VideoId, (answer, _) =>
                {
                    if (generation != _generation)
                        return;
                    _asking--;
                    if (answer == VideoCheck.Answer.Blocked || answer == VideoCheck.Answer.Gone)
                        _hidden++;
                    else if (_bar.Videos.Played(result.VideoId))
                        List(result);
                    else
                        _toTry.Enqueue(result);
                });
            }
        }

        /// <summary>Hands the waiting results to free testers, top of YouTube's list first.</summary>
        private void TryNext()
        {
            foreach (var tester in _testers)
            {
                if (_toTry.Count == 0)
                    return;
                if (tester == null || !tester.Ready || tester.Probing)
                    continue;
                var result = _toTry.Dequeue();
                var generation = _generation;
                _trying++;
                tester.Probe(result.VideoId, error =>
                {
                    _trying--;
                    if (error is { } code)
                        _bar.Videos.Mark(result.VideoId, code == 100 ? VideoCheck.Answer.Gone : VideoCheck.Answer.Blocked);
                    else
                        _bar.Videos.MarkPlayed(result.VideoId);
                    if (generation != _generation)
                        return;
                    if (error != null)
                        _hidden++;
                    else
                        List(result);
                });
            }
        }

        /// <summary>Puts a song that plays here in the list, at its place in YouTube's order.</summary>
        private void List(SearchResult result)
        {
            var at = _listed.FindIndex(r => r.Rank > result.Rank);
            if (at < 0)
                at = _listed.Count;
            _listed.Insert(at, result);
            var row = new Button(() => Choose(result)) { text = "", tooltip = Ui.T("Add it to the queue") };
            row.AddToClassList("song-search__result");
            var thumbnail = new Image { scaleMode = ScaleMode.ScaleAndCrop };
            thumbnail.AddToClassList("song-search__thumb");
            row.Add(thumbnail);
            var text = new VisualElement();
            text.AddToClassList("song-search__text");
            var title = new Label(result.Title) { enableRichText = false };
            title.AddToClassList("song-search__result-title");
            text.Add(title);
            var meta = string.Join(" · ", new[] { result.Channel, result.Length }).Trim(' ', '·');
            var details = new Label(meta) { enableRichText = false };
            details.AddToClassList("song-search__result-meta");
            text.Add(details);
            row.Add(text);
            _list.Insert(at, row);
            LoadThumbnail(result.VideoId, thumbnail);
        }

        /// <summary>YouTube's public thumbnail for the video (no key needed).</summary>
        private void LoadThumbnail(string videoId, Image into)
        {
            var generation = _generation;
            var request = UnityWebRequestTexture.GetTexture($"https://i.ytimg.com/vi/{videoId}/mqdefault.jpg");
            request.timeout = 10;
            request.SendWebRequest().completed += _ =>
            {
                if (request.result == UnityWebRequest.Result.Success && DownloadHandlerTexture.GetContent(request) is { } texture)
                {
                    if (generation == _generation)
                    {
                        _thumbnails.Add(texture);
                        into.image = texture;
                    }
                    else
                        Object.Destroy(texture);
                }
                request.Dispose();
            };
        }

        /// <summary>A song was chosen: it plays here (it was tried), so it joins the queue and the panel closes.</summary>
        private void Choose(SearchResult result)
        {
            if (_queueing)
                return;
            _queueing = true;
            Debug.Log($"[SongSearch] Chose {result.VideoId}.");
            _bar.QueueLink("https://www.youtube.com/watch?v=" + result.VideoId, refused =>
            {
                _queueing = false;
                if (refused != null)
                {
                    _status.text = _statusShown = refused;
                    return;
                }
                _query.SetValueWithoutNotify("");
                Close();
            });
        }

        /// <summary>How the search is going: songs ready, results still being checked, and how many could not play here.</summary>
        private void ShowStatus()
        {
            if (_queueing)
                return;
            string text;
            if (_read == 0)
                text = Ui.T("Looking for songs that play here…");
            else if (Checking > 0)
                text = Ui.T("Ready: {ready} · checking: {checking}", ("ready", _listed.Count), ("checking", Checking));
            else if (_listed.Count == 0)
                text = Ui.T("No song for these words plays here. Try other words.");
            else
                text = Ui.T("Songs that play here: {count}", ("count", _listed.Count));
            if (_hidden > 0)
                text += " · " + Ui.T("Left out because they cannot play here: {count}", ("count", _hidden));
            if (text == _statusShown)
                return;
            _statusShown = text;
            _status.text = text;
        }
    }
}
