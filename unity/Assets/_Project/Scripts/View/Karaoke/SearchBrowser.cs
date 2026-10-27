using System;
using System.Linq;
using UnityEngine;
using VoltstroStudios.UnityWebBrowser.Core;
using VoltstroStudios.UnityWebBrowser.Shared.Events;

namespace TableWorld.View.Karaoke
{
    /// <summary>
    /// The song search (ADR 0013 §6, T12.15): YouTube's own search page in a second embedded Chromium, drawn into a HUD
    /// panel. It is muted, keeps no cookies or logins, and shows only YouTube's search results (and its cookie consent
    /// page): choosing a video does not open it, it is handed to <see cref="Picked"/> and the page stays on the results;
    /// any other page (a channel, sign-in, another site) sends it back to the search. It is open only while the panel is.
    /// </summary>
    public sealed class SearchBrowser : BaseUwbClientManager
    {
        /// <summary>
        /// Run on every page the search loads, reporting through the page's title (the channel <see cref="BrowserScreen"/>
        /// uses too): a click on a link to a video is caught before YouTube's own handler (<c>twpick|n|link</c>); every
        /// video the results show is reported once (<c>twids|n|id,id,…</c>, a few at a time); <c>twBlock([ids])</c>
        /// hides the results of those videos, and keeps them hidden as YouTube redraws its list.
        /// </summary>
        private static readonly string PageJs =
            "if(!window.__tw){window.__tw=1;var n=0,seen={},blocked={};" +
            "var vid=function(h){try{var u=new URL(h,location.href);if(u.pathname=='/watch')return u.searchParams.get('v');" +
            "var m=u.pathname.match(/^\\/shorts\\/([\\w-]{11})/);return m?m[1]:null;}catch(e){return null;}};" +
            "var box=function(a){return a.closest('ytd-video-renderer,ytd-rich-item-renderer,ytd-reel-item-renderer,ytd-radio-renderer," +
            "ytd-playlist-renderer,yt-lockup-view-model,ytm-shorts-lockup-view-model-v2,ytm-shorts-lockup-view-model,ytd-compact-video-renderer');};" +
            "var scan=function(){var fresh=[],done=new Set();" +
            "document.querySelectorAll('a[href*=\"/watch?v=\"],a[href*=\"/shorts/\"]').forEach(function(a){var i=vid(a.href);if(!i)return;" +
            "var b=box(a);if(b&&!done.has(b)){done.add(b);if(blocked[i]){b.style.display='none';b.dataset.twHidden='1';}" +
            "else if(b.dataset.twHidden){b.style.display='';delete b.dataset.twHidden;}}" +
            "if(!seen[i]&&fresh.length<" + IdsPerReport + "){seen[i]=1;fresh.push(i);}});" +
            "if(fresh.length)document.title='twids|'+(++n)+'|'+fresh.join(',');};" +
            "window.twBlock=function(ids){ids.forEach(function(i){blocked[i]=1;});scan();};" +
            "document.addEventListener('click',function(e){" +
            "var a=e.target&&e.target.closest?e.target.closest('a[href]'):null;if(!a)return;var h=a.href||'';" +
            "if(h.indexOf('/watch')<0&&h.indexOf('/shorts/')<0)return;" +
            "e.preventDefault();e.stopPropagation();e.stopImmediatePropagation();document.title='twpick|'+(++n)+'|'+h;},true);" +
            "setInterval(scan,500);}";

        /// <summary>Videos reported in one go (the rest follow half a second later).</summary>
        public const int IdsPerReport = 30;

        private string _search;
        private bool _muted;

        /// <summary>A video was chosen: its id.</summary>
        public event Action<string> Picked;

        /// <summary>Videos the results show, each reported once a page.</summary>
        public event Action<string[]> Found;

        /// <summary>The page's size in pixels.</summary>
        public Vector2Int Size { get; private set; }

        public bool Ready => browserClient != null && browserClient.ReadySignalReceived && browserClient.IsConnected;

        /// <summary>The engine's latest frame (upside down: the engine sends it that way).</summary>
        public Texture2D Texture => browserClient?.BrowserTexture;

        /// <summary>Pages refused since it opened.</summary>
        public int Refused { get; private set; }

        /// <summary>YouTube's search results for <paramref name="query"/>.</summary>
        public static string SearchUrl(string query) =>
            "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query ?? "");

        /// <summary>What the search may show: <c>Show</c>, a video to <c>Pick</c>, or <c>Refuse</c> (sent back to the search).</summary>
        public enum Verdict { Show, Pick, Refuse }

        /// <summary>
        /// Judges a page the search was about to show: YouTube's results and its cookie consent pages are shown, a video
        /// page is a pick, anything else is refused.
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

        /// <summary>A search under <paramref name="parent"/> for <paramref name="query"/>, its page <paramref name="width"/> × <paramref name="height"/> pixels.</summary>
        public static SearchBrowser Create(Transform parent, string query, int width = 1280, int height = 720)
        {
            var go = new GameObject("Song Search");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var search = go.AddComponent<SearchBrowser>();
            search._search = SearchUrl(query);
            search.Size = new Vector2Int(width, height);
            BrowserScreen.Configure(search.browserClient, search._search, width, height);
            go.SetActive(true);
            return search;
        }

        /// <summary>Searches YouTube for <paramref name="query"/>.</summary>
        public void Search(string query)
        {
            _search = SearchUrl(query);
            if (Ready)
                browserClient.LoadUrl(_search);
        }

        /// <summary>The pointer moved to <paramref name="at"/> (0–1 across and down the page).</summary>
        public void PointerMove(Vector2 at)
        {
            if (Ready)
                browserClient.SendMouseMove(Pixel(at));
        }

        /// <summary>The left button went down or up at <paramref name="at"/> (0–1 across and down the page).</summary>
        public void PointerButton(Vector2 at, bool down)
        {
            if (Ready)
                browserClient.SendMouseClick(Pixel(at), 1, MouseClickType.Left, down ? MouseEventType.Down : MouseEventType.Up);
        }

        /// <summary>The wheel turned at <paramref name="at"/>: positive scrolls up, as Chromium counts (120 a notch).</summary>
        public void Scroll(Vector2 at, int amount)
        {
            if (Ready)
                browserClient.SendMouseScroll(Pixel(at), amount);
        }

        /// <summary>Hides the results of these videos (they cannot play here).</summary>
        public void Block(System.Collections.Generic.IEnumerable<string> videoIds)
        {
            var ids = string.Join(",", System.Linq.Enumerable.Select(videoIds, id => "'" + id + "'"));
            if (Ready && ids.Length > 0)
                browserClient.ExecuteJs("window.twBlock&&twBlock([" + ids + "])");
        }

        /// <summary>The shape of a YouTube video id (what the page reports goes back into its script).</summary>
        private static bool IsVideoId(string id) =>
            id.Length == 11 && id.All(c => (char.IsLetterOrDigit(c) && c < 128) || c == '-' || c == '_');

        private Vector2 Pixel(Vector2 at) =>
            new Vector2(Mathf.Clamp01(at.x) * Size.x, Mathf.Clamp01(at.y) * Size.y);

        protected override void OnStart()
        {
            browserClient.OnUrlChanged += OnUrlChanged;
            browserClient.OnLoadFinish += _ => browserClient.ExecuteJs(PageJs);
            browserClient.OnTitleChange += OnTitle;
        }

        /// <summary>Mutes it as soon as the engine answers: hovering a result plays a preview.</summary>
        protected override void OnFixedUpdate()
        {
            if (_muted || !Ready)
                return;
            _muted = true;
            browserClient.AudioMute(true);
            browserClient.ExecuteJs(PageJs);
        }

        private void OnTitle(string title)
        {
            var parts = title.Split(new[] { '|' }, 3);
            if (parts.Length != 3)
                return;
            if (parts[0] == "twpick" && Judge(parts[2], out var id) == Verdict.Pick)
                Picked?.Invoke(id);
            else if (parts[0] == "twids")
                Found?.Invoke(System.Array.FindAll(parts[2].Split(','), IsVideoId));
        }

        /// <summary>A video page is a pick; a page that is not the search goes back to it.</summary>
        private void OnUrlChanged(string url)
        {
            var verdict = Judge(url, out var id);
            if (verdict == Verdict.Show)
                return;
            if (verdict == Verdict.Pick)
                Picked?.Invoke(id);
            else
            {
                Refused++;
                Debug.LogWarning($"[SearchBrowser] Refused '{url}': the song search shows only YouTube's search results.");
            }
            browserClient.LoadUrl(_search);
        }
    }
}
