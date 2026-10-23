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
    /// 0.1 s, once synced), <c>-tw-report path</c> (after autoplay and 5 s of quiet, writes the state hash, seat and order
    /// as JSON), <c>-tw-quit</c> (quit after reporting). <see cref="Run"/> starts the same from code (Editor tests).
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
            // Let the table load first.
            yield return new WaitForSeconds(0.5f);
            var autoplay = int.TryParse(Arg("-tw-autoplay"), out var n) ? n : 0;
            yield return Run(host, join, autoplay, Arg("-tw-report"), Array.IndexOf(args, "-tw-quit") >= 0, seed: host ? 1 : 2);
        }

        /// <summary>Hosts (or joins), plays <paramref name="commands"/> random commands, then reports.</summary>
        public IEnumerator Run(bool host, string join, int commands, string report, bool quit, int seed)
        {
            if (host)
                _session.StartHost(ParsePort(Arg2(join)) ?? NetworkTableSession.DefaultPort);
            else
            {
                var parts = join.Split(':');
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
                var command = RandomCommand(random);
                if (command != null && _table.Execute(command).Ok)
                    Performed++;
                yield return new WaitForSeconds(StepSeconds);
            }
            yield return new WaitForSeconds(QuietSeconds);
            if (report != null)
            {
                var state = _table.Engine.State;
                File.WriteAllText(report, new JObject
                {
                    ["hash"] = TableJson.Hash(state),
                    ["seat"] = _table.LocalSeat,
                    ["order"] = _session.Host?.Order ?? -1,
                    ["performed"] = Performed,
                    ["objects"] = state.Objects.Count,
                }.ToString());
            }
            Done = true;
            if (quit)
                Application.Quit();
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

        private static string Arg2(string value) => value;

        private static ushort? ParsePort(string value) => ushort.TryParse(value, out var port) ? port : (ushort?)null;
    }
}
