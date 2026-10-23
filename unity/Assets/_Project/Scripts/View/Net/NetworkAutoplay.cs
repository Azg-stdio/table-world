using System;
using System.Collections;
using System.IO;
using System.Linq;
using Core.Engine;
using Core.Rules;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TableWorld.View.Net
{
    /// <summary>
    /// Command-line driven network sessions, for builds and automated multiplayer checks (T3.2, T3.5):
    /// <c>-tw-host [port]</c>, <c>-tw-join address[:port]</c>, <c>-tw-autoplay N</c> (N random table commands, one every
    /// 0.1 s, once synced), <c>-tw-report path</c> (after autoplay, once the table has not changed for 5 s, writes the state hash, seat and order
    /// as JSON), <c>-tw-quit</c> (quit after reporting), <c>-tw-game file</c> (the host loads a game package first),
    /// <c>-tw-cache folder</c> (asset cache, e.g. an empty one to check T3.4). Online (T3.3): <c>-tw-host online
    /// -tw-code-file f</c>, <c>-tw-join CODE</c> or <c>-tw-join @f</c>, <c>-tw-profile name</c> (one per process on a machine). <see cref="Run"/> starts the same from code.
    /// </summary>
    public sealed class NetworkAutoplay : MonoBehaviour
    {
        public const float StepSeconds = 0.1f;
        public const float QuietSeconds = 5f;

        [SerializeField] private TableView _table;
        [SerializeField] private NetworkTableSession _session;

        public bool Done { get; private set; }
        public int Performed { get; private set; }

        public void Configure(TableView table, NetworkTableSession session)
        {
            _table = table;
            _session = session;
        }

        private IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name) => Array.IndexOf(args, name) is var i && i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : null;
            var host = Array.IndexOf(args, "-tw-host") >= 0;
            var join = Arg("-tw-join");
            if (!host && join == null)
                yield break;
            if (Arg("-tw-cache") is { } cache)
                NetworkTableSession.CacheFolder = cache;
            if (Arg("-tw-profile") is { } profile)
                RelayLobby.Profile = profile;
            // Let the table load first.
            yield return new WaitForSeconds(0.5f);
            if (Arg("-tw-game") is { } game && !_table.Load(File.ReadAllText(game), Path.GetDirectoryName(Path.GetFullPath(game))))
                Debug.LogError($"[Autoplay] Could not load {game}.");
            var autoplay = int.TryParse(Arg("-tw-autoplay"), out var n) ? n : 0;
            yield return Run(host, host ? Arg("-tw-host") : join, autoplay, Arg("-tw-report"), Array.IndexOf(args, "-tw-quit") >= 0,
                seed: host ? 1 : 2, codeFile: Arg("-tw-code-file"));
        }

        /// <summary>
        /// Hosts or joins, plays <paramref name="commands"/> random commands, then reports. <paramref name="target"/>: for a
        /// host, a port or <c>online</c> (Relay; the join code goes to <paramref name="codeFile"/>); for a client, an
        /// address[:port], a join code, or <c>@file</c> (wait for a host to write its code there).
        /// </summary>
        public IEnumerator Run(bool host, string target, int commands, string report, bool quit, int seed, string codeFile = null)
        {
            if (host && target == "online")
            {
                var hosting = _session.HostWithCodeAsync();
                yield return new WaitUntil(() => hosting.IsCompleted);
                if (!hosting.Result)
                {
                    Debug.LogError("[Autoplay] " + _session.Status);
                    yield break;
                }
                Debug.Log("[Autoplay] Join code " + _session.JoinCode);
                if (codeFile != null)
                    File.WriteAllText(codeFile, _session.JoinCode);
            }
            else if (host)
                _session.StartHost(ParsePort(target) ?? NetworkTableSession.DefaultPort);
            else if (target.StartsWith("@") || RelayLobby.LooksLikeCode(target))
            {
                var file = target.StartsWith("@") ? target.Substring(1) : null;
                for (var wait = 0f; file != null && !File.Exists(file) && wait < 60f; wait += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                var joining = _session.JoinWithCodeAsync(file != null ? File.ReadAllText(file) : target);
                yield return new WaitUntil(() => joining.IsCompleted);
                if (!joining.Result)
                {
                    Debug.LogError("[Autoplay] " + _session.Status);
                    yield break;
                }
            }
            else
            {
                var parts = target.Split(':');
                _session.StartClient(parts[0], parts.Length > 1 && ushort.TryParse(parts[1], out var p) ? p : NetworkTableSession.DefaultPort);
            }
            for (var wait = 0f; !_session.Synced && wait < 30f; wait += 0.2f)
                yield return new WaitForSeconds(0.2f);
            if (!_session.Synced)
            {
                Debug.LogError("[Autoplay] Never synced.");
                yield break;
            }

            var random = new DeterministicRandom((ulong)seed);
            for (var i = 0; i < commands; i++)
            {
                var command = _table.Engine.State.HasRules ? RulesCommand(random, host) : RandomCommand(random);
                if (command != null && _table.Execute(command).Ok)
                    Performed++;
                yield return new WaitForSeconds(StepSeconds);
            }
            // Report once the table has not changed (and a host has sent no asset chunks) for QuietSeconds: the other
            // peers may still be playing or downloading.
            string Activity() => TableJson.Hash(_table.Engine.State) + "/" + (_session.Host?.Assets?.ChunksServed ?? 0);
            var last = Activity();
            for (float quiet = 0f, wait = 0f; quiet < QuietSeconds && wait < 300f; wait += 0.5f)
            {
                yield return new WaitForSeconds(0.5f);
                var hash = Activity();
                quiet = hash == last ? quiet + 0.5f : 0f;
                last = hash;
            }
            for (var wait = 0f; _session.Assets != null && !_session.Assets.Complete && wait < 120f; wait += 0.5f)
                yield return new WaitForSeconds(0.5f);
            if (report != null)
            {
                var state = _table.Engine.State;
                var images = state.Assets.Where(a => a.Type == AssetType.Image && !a.IsBuiltin).ToList();
                File.WriteAllText(report, new JObject
                {
                    // A client compares the state the host confirmed: once the host quits, the client's table plays on
                    // offline and its own physics may move things.
                    ["hash"] = TableJson.Hash(_session.Client?.Confirmed?.State ?? state),
                    ["liveHash"] = TableJson.Hash(state),
                    ["seat"] = _table.LocalSeat,
                    ["order"] = _session.Host?.Order ?? -1,
                    ["performed"] = Performed,
                    ["objects"] = state.Objects.Count,
                    ["game"] = state.Session == null ? null : $"turn {state.Session.Turn}, phase {state.Session.Phase}, over {state.Session.Over}, winner {state.Session.Winner ?? "-"}",
                    ["images"] = images.Count,
                    ["imagesShown"] = images.Count(a => _table.Assets.Texture(a.Id) != null),
                    ["assetsFetched"] = _session.Assets?.Fetched ?? 0,
                    ["assetsFromCache"] = _session.Assets?.FromCache ?? 0,
                    ["assetBytes"] = _session.Assets?.BytesReceived ?? 0,
                    ["assetsPending"] = _session.Assets?.Pending ?? 0,
                    ["assetSeconds"] = Math.Round(_session.AssetSeconds, 2),
                    ["roundTripsDownloading"] = Summary(_session.RoundTrips.Where(t => t.Downloading).Select(t => t.Seconds)),
                    ["roundTripsAfter"] = Summary(_session.RoundTrips.Where(t => !t.Downloading).Select(t => t.Seconds)),
                }.ToString());
            }
            Done = true;
            if (quit)
                Application.Quit();
        }

        /// <summary>
        /// A rules game played the way people play it online: the host starts it, then each peer performs a random legal
        /// action for its own seat when it has one (null while it waits for the others).
        /// </summary>
        private Command RulesCommand(DeterministicRandom r, bool host)
        {
            var game = _table.Game;
            if (game.Session == null)
                return host ? new StartGameCommand() : null;
            if (game.Session.Over)
                return null;
            var seat = _table.LocalSeat;
            var options = game.LegalActions(seat).SelectMany(a => a.Choices.Select(c => (a.ActionId, Params: c))).ToList();
            if (options.Count == 0)
                return null;
            var (action, parameters) = options[r.Next(options.Count)];
            return new PerformActionCommand { Actor = seat, ActionId = action, Params = new System.Collections.Generic.Dictionary<string, string>(parameters) };
        }

        /// <summary>Table commands that do not depend on physics: flips, shuffles, rolls, deals, takes, moves, cuts.</summary>
        private Command RandomCommand(DeterministicRandom r)
        {
            var ids = _table.Engine.State.Objects.Where(o => o.Container == null && !o.Locked).Select(o => o.Id).ToList();
            if (ids.Count == 0)
                return null;
            string Pick() => ids[r.Next(ids.Count)];
            var seat = _table.LocalSeat;
            return r.Next(8) switch
            {
                0 => new FlipCommand { ObjectId = Pick() },
                1 => new ShuffleCommand { ObjectId = Pick() },
                2 => new RollCommand { ObjectId = Pick() },
                3 => new DealCommand { DeckId = Pick(), Count = 1, Seats = { seat } },
                4 => new TakeCommand { ContainerId = Pick(), Position = new Vec3(r.Next(100) / 100f - 0.5f, 0.02f, r.Next(60) / 100f - 0.3f) },
                5 => new SetDieValueCommand { ObjectId = Pick(), Value = 1 + r.Next(4) },
                6 => new CutCommand { DeckId = Pick() },
                _ => new SetPropertiesCommand { ObjectId = Pick(), Name = $"n{r.Next(100)}" },
            };
        }

        /// <summary>"n=…, median …, max …" in milliseconds.</summary>
        private static string Summary(System.Collections.Generic.IEnumerable<float> seconds)
        {
            var sorted = seconds.OrderBy(s => s).ToList();
            return sorted.Count == 0 ? "n=0" : $"n={sorted.Count}, median {sorted[sorted.Count / 2] * 1000:F0} ms, max {sorted[sorted.Count - 1] * 1000:F0} ms";
        }

        private static ushort? ParsePort(string value) => ushort.TryParse(value, out var port) ? port : (ushort?)null;
    }
}
