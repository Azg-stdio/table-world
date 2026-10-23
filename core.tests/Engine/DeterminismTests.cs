using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    public class DeterminismTests
    {
        [Fact]
        public void SameSeed_SameShufflesAndRolls()
        {
            Assert.Equal(Play(seed: 7), Play(seed: 7));
        }

        [Fact]
        public void DifferentSeeds_DifferentResults()
        {
            Assert.NotEqual(Play(seed: 7), Play(seed: 8));
        }

        [Fact]
        public void Random_IsUniformEnough_AndStateRoundTrips()
        {
            var random = new DeterministicRandom(123);
            var counts = new int[6];
            for (var i = 0; i < 60000; i++)
                counts[random.Next(6)]++;
            Assert.All(counts, c => Assert.InRange(c, 9500, 10500));

            var saved = random.State;
            var expected = Enumerable.Range(0, 5).Select(_ => random.NextUInt()).ToArray();
            random.State = saved;
            Assert.Equal(expected, Enumerable.Range(0, 5).Select(_ => random.NextUInt()).ToArray());
        }

        [Fact]
        public void Pcg32_MatchesTheReferenceImplementation()
        {
            // pcg32-demo from pcg-c-basic: pcg32_srandom_r(&rng, 42u, 54u), first six pcg32_random_r outputs.
            var random = DeterministicRandom.ForStream(42, 54);

            var outputs = Enumerable.Range(0, 6).Select(_ => random.NextUInt()).ToArray();

            Assert.Equal(new uint[] { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e }, outputs);
        }

        /// <summary>
        /// 1 000 random commands (many invalid on purpose) never leave the table invalid, and failed commands never
        /// change it. The same seed replays to the same final hash.
        /// </summary>
        [Fact]
        public void RandomCommands_KeepTheTableValid_AndReplayIdentically()
        {
            var first = RunRandomSession(seed: 1, steps: 1000, out var okCount);
            var second = RunRandomSession(seed: 1, steps: 1000, out _);

            Assert.Equal(first, second);
            Assert.InRange(okCount, 200, 1000);
        }

        private static string RunRandomSession(ulong seed, int steps, out int okCount)
        {
            var engine = EngineFixture.Create(seed);
            var chooser = new DeterministicRandom(seed + 1000);
            okCount = 0;
            for (var step = 0; step < steps; step++)
            {
                var ids = engine.State.Objects.Select(o => o.Id).ToList();
                string Pick() => ids[chooser.Next(ids.Count)];
                Command command = chooser.Next(10) switch
                {
                    0 => new MoveCommand { ObjectId = Pick(), Position = new Vec3(chooser.Next(100) / 100f - 0.5f, 0, chooser.Next(100) / 100f - 0.5f) },
                    1 => new TakeCommand { ContainerId = Pick(), Index = chooser.Next(3), FromBottom = chooser.Next(2) == 0 },
                    2 => new InsertCommand { ObjectId = Pick(), IntoId = Pick(), AtBottom = chooser.Next(2) == 0 },
                    3 => new FlipCommand { ObjectId = Pick() },
                    4 => new ShuffleCommand { ObjectId = Pick() },
                    5 => new SearchCommand { ContainerId = Pick() },
                    6 => new DealCommand { DeckId = Pick(), Count = 1 + chooser.Next(3), Seats = { "seat_1", "seat_2" } },
                    7 => new LockCommand { ObjectId = Pick(), Locked = chooser.Next(4) == 0 },
                    8 => new GroupCommand { ObjectIds = { Pick(), Pick(), Pick() } },
                    _ => new RollCommand { ObjectId = Pick() },
                };

                var hashBefore = TableJson.Hash(engine.State);
                var result = engine.Execute(command);
                if (result.Ok)
                    okCount++;
                else
                    Assert.Equal(hashBefore, TableJson.Hash(engine.State));
            }
            engine.AssertValid();
            var hash = TableJson.Hash(engine.State);
            var reloaded = TableJson.Parse(TableJson.Serialize(engine.State)).State!;
            Assert.Equal(hash, TableJson.Hash(reloaded));
            return hash;
        }

        private static List<string> Play(ulong seed)
        {
            var engine = EngineFixture.Create(seed);
            var log = new List<string>();
            engine.Ok(new ShuffleCommand { ObjectId = "deck" });
            log.Add(string.Join(",", engine.Deck().CardIds()));
            for (var i = 0; i < 20; i++)
            {
                engine.Ok(new RollCommand { ObjectId = "d6" });
                log.Add(((Die)engine.Find("d6")!).Value!.ToString()!);
            }
            engine.Ok(new ShuffleCommand { ObjectId = "bag" });
            log.Add(string.Join(",", engine.ContentsOf("bag").Select(o => o.Id)));
            return log;
        }
    }
}
