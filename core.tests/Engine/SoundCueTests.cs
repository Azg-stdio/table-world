using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Net;
using Core.Rules;
using Newtonsoft.Json.Linq;

namespace Core.Tests.Engine
{
    /// <summary>
    /// Sound cues (M12, T13.4): a rule marks a moment with a sound for everyone or for an audience; each machine is sent
    /// only the cues it plays, so a sleeper never hears the awake's.
    /// </summary>
    public class SoundCueTests
    {
        private const string Json = @"{
  ""assets"": [ { ""id"": ""horn"", ""type"": ""audio"", ""path"": ""horn.ogg"" } ],
  ""players"": { ""seats"": [ { ""id"": ""a"", ""color"": ""red"" }, { ""id"": ""b"", ""color"": ""blue"" }, { ""id"": ""c"", ""color"": ""green"" } ] },
  ""turn"": { ""phases"": [ ""day"", ""night"" ] },
  ""phases"": [
    { ""id"": ""day"", ""actions"": [ ""sleep"", ""horn"" ], ""onEnter"": [ { ""sound"": { ""play"": ""chime"", ""volume"": 0.5 } } ] },
    { ""id"": ""night"", ""awake"": ""a"", ""actions"": [ ""wake"" ], ""onEnter"": [ { ""sound"": { ""play"": ""gong"", ""to"": ""awake"" } } ] } ],
  ""actions"": [
    { ""id"": ""sleep"", ""actor"": ""any"", ""effects"": [ { ""sound"": { ""play"": ""drum"", ""to"": ""b"" } }, { ""endPhase"": {} } ] },
    { ""id"": ""horn"", ""actor"": ""any"", ""effects"": [ { ""sound"": { ""play"": ""horn"" } }, { ""sound"": { ""play"": ""library:alkakrab/fx-1"" } } ] },
    { ""id"": ""wake"", ""actor"": ""any"", ""effects"": [ { ""endPhase"": {} } ] } ]
}";

        private static GameEngine Start()
        {
            var parsed = TableJson.Parse(Json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            Assert.Empty(TableValidator.Validate(parsed.State!));
            return new GameEngine(parsed.State!, 1);
        }

        [Fact]
        public void APhase_PlaysItsCue_ForEveryone_AndAnActionForOneSeat()
        {
            var game = Start();
            var started = game.Execute(new StartGameCommand());
            var chime = started.Events.OfType<SoundCue>().Single();
            Assert.Equal("chime", chime.Sound);
            Assert.Equal(0.5f, chime.Volume, 3);
            Assert.Null(chime.Seats);
            Assert.True(chime.For(null), "a spectator hears what everyone hears");

            var slept = game.Execute(new PerformActionCommand { Actor = "a", ActionId = "sleep" });
            Assert.True(slept.Ok, slept.Error?.ToString());
            var cues = slept.Events.OfType<SoundCue>().ToList();
            Assert.Equal(new[] { "drum", "gong" }, cues.Select(c => c.Sound));
            Assert.Equal(new[] { "b" }, cues[0].Seats);
            Assert.Equal(new[] { "a" }, cues[1].Seats);
            Assert.False(cues[1].For("b"), "a sleeper does not hear the awake's gong");
            Assert.False(cues[1].For(null));
        }

        [Fact]
        public void AssetsAndLibraryTracks_AreSounds_AndUnknownOnesAreNot()
        {
            var game = Start();
            game.Execute(new StartGameCommand());
            var horn = game.Execute(new PerformActionCommand { Actor = "a", ActionId = "horn" });
            Assert.Equal(new[] { "horn", "library:alkakrab/fx-1" }, horn.Events.OfType<SoundCue>().Select(c => c.Sound));

            var bad = TableJson.Parse(Json.Replace(@"""play"": ""drum""", @"""play"": ""dong""")).State!;
            var error = Assert.Single(TableValidator.Validate(bad), e => e.Path.EndsWith(".play"));
            Assert.Contains("gong", error.Suggestion);
            Assert.False(game.Execute(new PlaySoundCommand { Sound = "kazoo" }).Ok);
            Assert.False(game.Execute(new PlaySoundCommand { Sound = "gong", Volume = 2f }).Ok);
        }

        [Fact]
        public void EachMachine_IsSentOnlyTheCuesItPlays()
        {
            var game = Start();
            Assert.True(game.Execute(new StartGameCommand()).Ok);
            var host = new SessionHost(game);
            var received = new Dictionary<string, List<string>>();
            foreach (var seat in new[] { "a", "b", "c" })
            {
                host.Seats[seat] = seat;
                host.Snapshot(seat);
                received[seat] = new List<string>();
            }
            // The host's own move: b's drum and a's gong go only where they play.
            foreach (var outgoing in host.ExecuteLocal(new PerformActionCommand { Actor = "c", ActionId = "sleep" }).Messages)
                foreach (var seat in outgoing.To == null ? received.Keys.ToList() : new List<string> { outgoing.To })
                    if (outgoing.Message["events"] is JArray events)
                        received[seat].AddRange(EventCodec.DecodeAll(events).OfType<SoundCue>().Select(c => c.Sound));
            Assert.Equal(new[] { "gong" }, received["a"]);
            Assert.Equal(new[] { "drum" }, received["b"]);
            Assert.Empty(received["c"]);
        }
    }
}

namespace Core.Tests.Engine
{
    public class CueSynthTests
    {
        [Fact]
        public void EveryBuiltInCue_IsMade_ShortAndJustUnderFullScale()
        {
            foreach (var name in SoundCues.Builtin)
            {
                var samples = Core.Music.CueSynth.Make(name);
                Assert.NotNull(samples);
                var seconds = samples!.Length / (double)Core.Music.CueSynth.Rate;
                Assert.InRange(seconds, 0.3, 5);
                var peak = samples.Max(System.Math.Abs);
                Assert.InRange(peak, 0.85f, 0.91f);
                Assert.Equal(samples, Core.Music.CueSynth.Make(name));
                Assert.Equal(0f, samples[^1], 3);
                if (System.Environment.GetEnvironmentVariable("TW_CUE_WAVS") is { Length: > 0 } folder)
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + ".wav"), Core.Music.CueSynth.Wav(samples));
            }
            Assert.Null(Core.Music.CueSynth.Make("kazoo"));
        }
    }
}
