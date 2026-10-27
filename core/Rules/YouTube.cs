using System;
using System.Text.RegularExpressions;

namespace Core.Rules
{
    /// <summary>YouTube links as people paste them (karaoke, ADR 0013): the video id they name.</summary>
    public static class YouTube
    {
        private static readonly Regex Id = new Regex("^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);

        /// <summary>
        /// The 11-character video id in <paramref name="link"/>: a bare id, <c>youtube.com/watch?v=</c>, <c>youtu.be/</c>,
        /// <c>/embed/</c>, <c>/shorts/</c>, <c>/live/</c>, <c>music.youtube.com</c> and <c>m.youtube.com</c>; null for anything else.
        /// </summary>
        public static string? VideoId(string? link)
        {
            if (string.IsNullOrWhiteSpace(link))
                return null;
            var text = link!.Trim();
            if (Id.IsMatch(text))
                return text;
            if (!text.Contains("://"))
                text = "https://" + text;
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
                return null;
            var host = uri.Host.ToLowerInvariant();
            string? candidate = null;
            if (host == "youtu.be")
                candidate = uri.AbsolutePath.Trim('/');
            else if (host == "youtube.com" || host.EndsWith(".youtube.com") || host == "youtube-nocookie.com" || host.EndsWith(".youtube-nocookie.com"))
            {
                var path = uri.AbsolutePath;
                if (path == "/watch")
                    candidate = Query(uri.Query, "v");
                else
                    foreach (var prefix in new[] { "/embed/", "/shorts/", "/live/", "/v/" })
                        if (path.StartsWith(prefix, StringComparison.Ordinal))
                            candidate = path.Substring(prefix.Length).Split('/')[0];
            }
            return candidate != null && Id.IsMatch(candidate) ? candidate : null;
        }

        private static string? Query(string query, string name)
        {
            foreach (var pair in query.TrimStart('?').Split('&'))
            {
                var at = pair.IndexOf('=');
                if (at > 0 && pair.Substring(0, at) == name)
                    return Uri.UnescapeDataString(pair.Substring(at + 1));
            }
            return null;
        }
    }
}
