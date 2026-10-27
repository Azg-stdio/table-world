using System;
using System.Collections.Generic;
using UnityEngine.Networking;

namespace TableWorld.View.Karaoke
{
    /// <summary>
    /// Asks YouTube's public oEmbed address (no key) about a video: its title, and whether its owner lets it play outside
    /// YouTube (401 or 403: no) or it is gone (404 or 400). Answers are remembered for the session; a few questions run at
    /// once, the rest wait. Used by Add to queue and by the song search, which hides results that cannot play.
    /// </summary>
    public sealed class VideoCheck
    {
        public enum Answer { Playable, Blocked, Gone, Unknown }

        /// <summary>Questions in flight at once.</summary>
        public const int MaxRunning = 4;

        private readonly Dictionary<string, (Answer Answer, string Title)> _known = new();
        private readonly Dictionary<string, List<Action<Answer, string>>> _waiting = new();
        private readonly Queue<string> _queue = new();
        private int _running;

        /// <summary>What is already known about <paramref name="videoId"/>, without asking.</summary>
        public bool TryGet(string videoId, out Answer answer, out string title)
        {
            if (_known.TryGetValue(videoId, out var known))
            {
                (answer, title) = known;
                return true;
            }
            answer = Answer.Unknown;
            title = null;
            return false;
        }

        private readonly HashSet<string> _played = new();

        /// <summary>True once <paramref name="videoId"/> started in YouTube's embedded player here (so it will on the screen).</summary>
        public bool Played(string videoId) => _played.Contains(videoId);

        /// <summary>Records that <paramref name="videoId"/> started in the embedded player.</summary>
        public void MarkPlayed(string videoId) => _played.Add(videoId);

        /// <summary>
        /// Records what playing <paramref name="videoId"/> showed (YouTube's player refused it, though oEmbed allowed it):
        /// later questions get this answer.
        /// </summary>
        public void Mark(string videoId, Answer answer)
        {
            _known.TryGetValue(videoId, out var known);
            _known[videoId] = (answer, known.Title);
        }

        /// <summary>Asks about <paramref name="videoId"/>; <paramref name="done"/> hears the answer and the title (null when unknown).</summary>
        public void Ask(string videoId, Action<Answer, string> done)
        {
            if (TryGet(videoId, out var answer, out var title))
            {
                done?.Invoke(answer, title);
                return;
            }
            if (_waiting.TryGetValue(videoId, out var listeners))
            {
                listeners.Add(done);
                return;
            }
            _waiting[videoId] = new List<Action<Answer, string>> { done };
            _queue.Enqueue(videoId);
            Next();
        }

        private void Next()
        {
            while (_running < MaxRunning && _queue.Count > 0)
            {
                var id = _queue.Dequeue();
                _running++;
                var address = "https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString("https://www.youtube.com/watch?v=" + id);
                var request = UnityWebRequest.Get(address);
                request.timeout = 10;
                request.SendWebRequest().completed += _ =>
                {
                    var code = request.responseCode;
                    string title = null;
                    if (request.result == UnityWebRequest.Result.Success)
                        try
                        {
                            title = (string)Newtonsoft.Json.Linq.JObject.Parse(request.downloadHandler.text)["title"];
                        }
                        catch (Newtonsoft.Json.JsonException)
                        {
                        }
                    var answer = code == 401 || code == 403 ? Answer.Blocked
                        : code == 404 || code == 400 ? Answer.Gone
                        : request.result == UnityWebRequest.Result.Success ? Answer.Playable
                        : Answer.Unknown;
                    request.Dispose();
                    // No answer (offline, a timeout) is not remembered: the next question asks again.
                    if (answer != Answer.Unknown)
                        _known[id] = (answer, title);
                    _running--;
                    if (_waiting.Remove(id, out var listeners))
                        foreach (var listener in listeners)
                            listener?.Invoke(answer, title);
                    Next();
                };
            }
        }
    }
}
