using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VoltstroStudios.UnityWebBrowser.Core;

namespace TableWorld.View.Karaoke
{
    /// <summary>One video YouTube's search listed: its id, title, channel and length as the page shows them, and its place in the list.</summary>
    public sealed class SearchResult
    {
        public string VideoId;
        public string Title;
        public string Channel;
        public string Length;
        public int Rank;
    }

    /// <summary>
    /// The song search's hidden browsers (ADR 0013 §6, T12.15), never shown to anyone, muted, keeping no cookies or logins.
    /// A reader opens YouTube's search results and reads what they list (<see cref="Results"/>): the game shows its own
    /// list, of the videos that play here only. A tester tries videos in YouTube's official embedded player on our own
    /// page (<see cref="Probe"/>), as the screen will play them: some videos pass oEmbed and still refuse every embedded
    /// player, and only the player tells. Both run only while the search panel is open.
    /// </summary>
    public sealed class SearchBrowser : BaseUwbClientManager
    {
        /// <summary>
        /// Run on the results page: every single video the list shows (not the ads YouTube places in it, nor playlists
        /// and mixes) is read once, as
        /// <c>[id, title, channel, length]</c>, and handed over through the page's title (the channel
        /// <see cref="BrowserScreen"/> uses too) a few at a time, <c>twres|n|[…]</c>; the game answers <c>twAck()</c>
        /// before the next batch (one not answered in 2 s is sent again). <c>twMore()</c> scrolls to the end, where
        /// YouTube loads more results.
        /// </summary>
        private const string ReaderJs =
            "if(!window.__tw){window.__tw=1;var n=0,seen={},out=[],last=null,sentAt=0;" +
            "var ad=function(e){return e.closest('ytd-ad-slot-renderer,ytd-in-feed-ad-layout-renderer,ytd-promoted-video-renderer," +
            "ytd-promoted-sparkles-web-renderer,ytd-search-pyv-renderer,ytd-display-ad-renderer');};" +
            "var txt=function(e){return e?(e.getAttribute('title')||e.textContent||'').replace(/\\s+/g,' ').trim():'';};" +
            "var scan=function(){document.querySelectorAll('ytd-video-renderer,yt-lockup-view-model').forEach(function(r){if(ad(r))return;" +
            "var a=r.querySelector('a#video-title,a[href*=\"/watch?v=\"]');if(!a)return;var id=null;" +
            // Only single songs are read. A video entry may carry a radio list (list=RD…&start_radio=1) and is still one
            // song; a card with stacked thumbnails, or a card linking into a list, is a playlist or a mix.
            "var single=r.tagName=='YTD-VIDEO-RENDERER';if(!single&&r.querySelector('yt-collection-thumbnail-view-model,yt-collections-stack'))return;" +
            "try{var u=new URL(a.href,location.href);if(!single&&u.searchParams.get('list'))return;id=u.searchParams.get('v');}catch(e){}" +
            "if(!id||seen[id])return;var t=txt(r.querySelector('#video-title'))||txt(r.querySelector('h3'))||txt(a);if(!t)return;seen[id]=1;" +
            "out.push([id,t.slice(0,120),txt(r.querySelector('ytd-channel-name a,#channel-name a')).slice(0,60)," +
            "txt(r.querySelector('ytd-thumbnail-overlay-time-status-renderer,badge-shape')).slice(0,24)]);});send();};" +
            "var send=function(){if(last&&Date.now()-sentAt<2000)return;if(last){out=last.concat(out);last=null;}if(!out.length)return;" +
            "var b=[],len=0;while(out.length&&len<1000){var x=out.shift();b.push(x);len+=JSON.stringify(x).length;}" +
            "last=b;sentAt=Date.now();document.title='twres|'+(++n)+'|'+JSON.stringify(b);};" +
            "window.twAck=function(){last=null;send();};" +
            "window.twMore=function(){window.scrollTo(0,document.documentElement.scrollHeight);};" +
            "setInterval(scan,400);}";

        private string _search;
        private bool _muted;
        private PlayerPage _page;
        /// <summary>A tester stays on our player page between tries.</summary>
        private bool _isTester;
        /// <summary>Our player page is loaded and answering: the next try switches the video in it, no reload.</summary>
        private bool _onPlayer;
        /// <summary>The video being tried in the official player, and who hears how it went.</summary>
        private string _probing;
        private Action<int?> _probed;
        private float _probeUntil;
        private int _rank;
        private readonly HashSet<string> _read = new();

        /// <summary>How long a try may take before the video is taken as playable (the player said nothing against it).</summary>
        public const float ProbeSeconds = 8f;

        /// <summary>Videos the results list, in the list's order, each once a search.</summary>
        public event Action<SearchResult[]> Results;

        public bool Ready => browserClient != null && browserClient.ReadySignalReceived && browserClient.IsConnected;

        /// <summary>Pages refused since it opened.</summary>
        public int Refused { get; private set; }

        /// <summary>YouTube's search results for <paramref name="query"/>.</summary>
        public static string SearchUrl(string query) =>
            "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query ?? "");

        /// <summary>What the reader may show: <c>Show</c>, a video page (<c>Pick</c>, sent back like any other), or <c>Refuse</c>.</summary>
        public enum Verdict { Show, Pick, Refuse }

        /// <summary>
        /// Judges a page the reader was about to show: YouTube's results and its cookie consent pages are shown; a video
        /// page or anything else sends it back to the results.
        /// </summary>
        public static Verdict Judge(string url, out string videoId)
        {
            videoId = null;
            if (url == null || url.StartsWith("about:", StringComparison.Ordinal))
                return Verdict.Show;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                return Verdict.Refuse;
            var host = uri.Host.ToLowerInvariant();
            if (host == "consent.youtube.com")
                return Verdict.Show;
            if (host != "www.youtube.com" && host != "youtube.com" && host != "m.youtube.com")
                return Verdict.Refuse;
            if (uri.AbsolutePath == "/results")
                return Verdict.Show;
            if (uri.AbsolutePath == "/watch" || uri.AbsolutePath.StartsWith("/shorts/", StringComparison.Ordinal))
            {
                videoId = Core.Rules.YouTube.VideoId(url);
                return videoId != null ? Verdict.Pick : Verdict.Refuse;
            }
            return Verdict.Refuse;
        }

        /// <summary>A reader under <paramref name="parent"/> for <paramref name="query"/>.</summary>
        public static SearchBrowser CreateReader(Transform parent, string query) =>
            Make(parent, "Song Search", SearchUrl(query), 1280, 720, tester: false);

        /// <summary>A browser that only tries videos (the song search checks every result with it before listing it).</summary>
        public static SearchBrowser CreateTester(Transform parent) =>
            Make(parent, "Song Tester", "about:blank", 640, 360, tester: true);

        private static SearchBrowser Make(Transform parent, string name, string url, int width, int height, bool tester)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var browser = go.AddComponent<SearchBrowser>();
            browser._search = url;
            browser._isTester = tester;
            BrowserScreen.Configure(browser.browserClient, url, width, height);
            go.SetActive(true);
            return browser;
        }

        /// <summary>Searches YouTube for <paramref name="query"/>: the results are read afresh.</summary>
        public void Search(string query)
        {
            _search = SearchUrl(query);
            _rank = 0;
            _read.Clear();
            if (Ready)
                browserClient.LoadUrl(_search);
        }

        /// <summary>Asks YouTube for more results (they are read as they come).</summary>
        public void More()
        {
            if (Ready)
                browserClient.ExecuteJs("window.twMore&&twMore()");
        }

        /// <summary>True while a video is being tried.</summary>
        public bool Probing => _probing != null;

        /// <summary>
        /// Tries <paramref name="videoId"/> in YouTube's official player, muted, on our own page: <paramref name="done"/>
        /// hears null once it plays (or nothing went wrong in time), else the player's error (100 gone, 101 or 150 not
        /// allowed outside YouTube, 2 or 5 broken).
        /// </summary>
        public void Probe(string videoId, Action<int?> done)
        {
            if (!Ready)
            {
                done?.Invoke(null);
                return;
            }
            _page ??= new PlayerPage();
            _probing = videoId;
            _probed = done;
            _probeUntil = Time.realtimeSinceStartup + ProbeSeconds;
            if (_onPlayer)
                browserClient.ExecuteJs($"tw.load('{videoId}',0)");
            else
                browserClient.LoadUrl(_page.PageFor(videoId, muted: true));
        }

        private void EndProbe(int? error)
        {
            var done = _probed;
            _probing = null;
            _probed = null;
            if (Ready)
                browserClient.ExecuteJs("tw.pause()");
            done?.Invoke(error);
        }

        /// <summary>The shape of a YouTube video id (what the page reports may go back into a script).</summary>
        public static bool IsVideoId(string id) =>
            id != null && id.Length == 11 && id.All(c => (char.IsLetterOrDigit(c) && c < 128) || c == '-' || c == '_');

        /// <summary>
        /// Reads one batch the page handed over (<c>[[id, title, channel, length], …]</c>): results with a well-formed id
        /// and a title, not read before in this search, numbered in the list's order.
        /// </summary>
        public static List<SearchResult> ReadBatch(string json, HashSet<string> read, ref int rank)
        {
            var results = new List<SearchResult>();
            Newtonsoft.Json.Linq.JArray rows;
            try
            {
                rows = Newtonsoft.Json.Linq.JArray.Parse(json);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return results;
            }
            foreach (var row in rows.OfType<Newtonsoft.Json.Linq.JArray>())
            {
                if (row.Count < 2)
                    continue;
                var id = row[0].Type == Newtonsoft.Json.Linq.JTokenType.String ? (string)row[0] : null;
                var title = row[1].Type == Newtonsoft.Json.Linq.JTokenType.String ? ((string)row[1]).Trim() : "";
                if (!IsVideoId(id) || title.Length == 0 || !read.Add(id))
                    continue;
                string Text(int i, int max) =>
                    row.Count > i && row[i].Type == Newtonsoft.Json.Linq.JTokenType.String
                        ? new string(((string)row[i]).Where(c => !char.IsControl(c)).Take(max).ToArray()).Trim()
                        : "";
                results.Add(new SearchResult
                {
                    VideoId = id,
                    Title = Text(1, Core.Rules.QueuedSong.MaxTitle),
                    Channel = Text(2, 60),
                    // The page may show the length twice (the badge and its text): the first one.
                    Length = System.Text.RegularExpressions.Regex.Match(Text(3, 24), @"\d{1,2}(:\d{2}){1,2}").Value,
                    Rank = rank++,
                });
            }
            return results;
        }

        protected override void OnStart()
        {
            browserClient.OnUrlChanged += OnUrlChanged;
            browserClient.OnLoadFinish += _ =>
            {
                if (!_isTester)
                    browserClient.ExecuteJs(ReaderJs);
            };
            browserClient.OnTitleChange += OnTitle;
        }

        /// <summary>Mutes it as soon as the engine answers (a hovered result, a tried video).</summary>
        protected override void OnFixedUpdate()
        {
            if (_probing != null && Time.realtimeSinceStartup > _probeUntil)
                EndProbe(null);
            if (_muted || !Ready)
                return;
            _muted = true;
            browserClient.AudioMute(true);
            if (!_isTester)
                browserClient.ExecuteJs(ReaderJs);
        }

        protected override void OnDestroyed() => _page?.Dispose();

        private void OnTitle(string title)
        {
            if (_probing != null)
            {
                // Our page reports state|time|duration|video, or error|code|video.
                var report = title.Split('|');
                if (report.Length == 3 && report[0] == "error" && report[2] == _probing && int.TryParse(report[1], out var code))
                {
                    _onPlayer = true;
                    EndProbe(code);
                }
                else if (report.Length >= 4 && report[0] == "1" && report[3] == _probing)
                {
                    _onPlayer = true;
                    EndProbe(null);
                }
                return;
            }
            if (_isTester || !title.StartsWith("twres|", StringComparison.Ordinal))
                return;
            var parts = title.Split(new[] { '|' }, 3);
            if (parts.Length != 3)
                return;
            var batch = ReadBatch(parts[2], _read, ref _rank);
            browserClient.ExecuteJs("window.twAck&&twAck()");
            if (batch.Count > 0)
                Results?.Invoke(batch.ToArray());
        }

        /// <summary>Anything but the results (a video page, a channel, sign-in, another site) goes back to the results.</summary>
        private void OnUrlChanged(string url)
        {
            // Our own player page, while a video is tried.
            if (_page != null && url.StartsWith(_page.Url, StringComparison.Ordinal))
                return;
            _onPlayer = false;
            if (_isTester || Judge(url, out _) == Verdict.Show)
                return;
            Refused++;
            Debug.LogWarning($"[SearchBrowser] Refused '{url}': the song search reads only YouTube's search results.");
            browserClient.LoadUrl(_search);
        }
    }
}
