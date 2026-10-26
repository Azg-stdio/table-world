using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>G18: layout zones arrange their contents in a grid; randomize zones shuffle where things lie.</summary>
    public class ZoneLayoutTests
    {
        // A 0.3 m wide, 0.2 m deep layout zone at the origin: room for 4 cards (0.0635 + 0.01) per row.
        private const string Json = @"{
  ""schemaVersion"": ""0.1"",
  ""assets"": [ { ""id"": ""sheet"", ""type"": ""image"", ""path"": ""builtin:standard_deck"", ""columns"": 10, ""rows"": 7 } ],
  ""objects"": [
    { ""id"": ""a"", ""type"": ""Card"", ""faceAsset"": ""sheet"", ""backAsset"": ""sheet"", ""transform"": { ""position"": [0.9, 0, 0] } },
    { ""id"": ""b"", ""type"": ""Card"", ""faceAsset"": ""sheet"", ""backAsset"": ""sheet"", ""transform"": { ""position"": [0.9, 0, 0.1] } },
    { ""id"": ""c"", ""type"": ""Card"", ""faceAsset"": ""sheet"", ""backAsset"": ""sheet"", ""transform"": { ""position"": [0.9, 0, 0.2] } },
    { ""id"": ""d"", ""type"": ""Card"", ""faceAsset"": ""sheet"", ""backAsset"": ""sheet"", ""transform"": { ""position"": [0.9, 0, 0.3] } },
    { ""id"": ""e"", ""type"": ""Card"", ""faceAsset"": ""sheet"", ""backAsset"": ""sheet"", ""transform"": { ""position"": [0.9, 0, 0.4] } },
    { ""id"": ""t1"", ""type"": ""Token"", ""transform"": { ""position"": [-0.6, 0, 0] } },
    { ""id"": ""t2"", ""type"": ""Token"", ""transform"": { ""position"": [-0.5, 0, 0] } },
    { ""id"": ""t3"", ""type"": ""Token"", ""locked"": true, ""transform"": { ""position"": [-0.55, 0, 0.05] } }
  ],
  ""zones"": [
    { ""id"": ""row"", ""type"": ""LayoutZone"", ""name"": ""Market"", ""size"": [0.3, 0.1, 0.2] },
    { ""id"": ""manual"", ""type"": ""LayoutZone"", ""manualOnly"": true, ""facing"": ""faceUp"", ""transform"": { ""position"": [0.9, 0, 0.2] }, ""size"": [0.2, 0.1, 0.6] },
    { ""id"": ""mix"", ""type"": ""RandomizeZone"", ""transform"": { ""position"": [-0.55, 0, 0] }, ""size"": [0.2, 0.1, 0.2] }
  ]
}";

        private static TableEngine Create()
        {
            var parsed = TableJson.Parse(Json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            return new TableEngine(parsed.State!, 3);
        }

        private static Vec3 P(TableEngine e, string id) => e.Find(id)!.Transform.Position;

        [Fact]
        public void DroppingIntoAnAutomaticZone_ArrangesEverythingInReadingOrder()
        {
            var engine = Create();
            // Drop five cards at the right end of the first row: each goes after the others; the fifth starts a second row.
            for (var i = 0; i < 5; i++)
                engine.Ok(new MoveCommand { ObjectId = "abcde"[i].ToString(), Position = new Vec3(0.14f, 0f, 0.05f) });
            var cell = Card.DefaultSize.X + 0.01f;
            var first = -0.15f + cell / 2f;
            for (var i = 0; i < 4; i++)
                Assert.Equal(first + i * cell, P(engine, "abcde"[i].ToString()).X, 3);
            Assert.Equal(P(engine, "a").Z, P(engine, "d").Z, 3);
            Assert.Equal(first, P(engine, "e").X, 3);
            Assert.True(P(engine, "e").Z < P(engine, "a").Z, "the fifth card starts the next row, nearer the front");
            Assert.Equal(5, engine.ObjectsIn("row").Count());
            engine.AssertValid();
        }

        [Fact]
        public void MoreThanFit_CloseUp_AndStayInsideTheZone()
        {
            // Room for 4 x 2 cards; 13 arrive (a deck's worth of a suit laid out).
            var json = Json.Replace(@"""objects"": [", @"""objects"": [" + string.Concat(Enumerable.Range(0, 13).Select(i =>
                $@"{{ ""id"": ""m{i}"", ""type"": ""Card"", ""faceAsset"": ""sheet"", ""backAsset"": ""sheet"", ""transform"": {{ ""position"": [-0.6, 0, {0.3 + i * 0.01}] }} }},")));
            var parsed = TableJson.Parse(json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            var engine = new TableEngine(parsed.State!, 3);
            for (var i = 0; i < 13; i++)
                engine.Ok(new MoveCommand { ObjectId = "m" + i, Position = new Vec3(0f, 0f, 0f) });
            Assert.Equal(13, engine.ObjectsIn("row").Count());
            foreach (var id in Enumerable.Range(0, 13).Select(i => "m" + i))
            {
                var at = P(engine, id);
                Assert.InRange(at.X, -0.15f + Card.DefaultSize.X / 2f - 0.001f, 0.15f - Card.DefaultSize.X / 2f + 0.001f);
                Assert.InRange(at.Z, -0.1f + Card.DefaultSize.Y / 2f - 0.001f, 0.1f - Card.DefaultSize.Y / 2f + 0.001f);
            }
            // Still in reading order: the first card at the back left, the last at the front.
            Assert.True(P(engine, "m0").Z > P(engine, "m12").Z);
            engine.AssertValid();
        }

        [Fact]
        public void ManualZone_WaitsForLayOut_ThenFacesCardsUp_AndStaysPut()
        {
            var engine = Create();
            // Cards a..e already lie in the manual zone (x 0.9, z 0..0.4), face up by default; turn one face down.
            engine.Ok(new FlipCommand { ObjectId = "c" });
            var before = P(engine, "c");
            engine.Ok(new MoveCommand { ObjectId = "c", Position = before });
            Assert.Equal(before.X, P(engine, "c").X, 4);

            var laid = engine.Ok(new ArrangeZoneCommand { ZoneId = "manual" });
            Assert.True(engine.Find("c")!.IsFaceUp, "the zone turns cards face up");
            // Two cards per row in a 0.2 m wide zone; top row is a then b (reading order by depth, far first).
            Assert.Equal(P(engine, "e").Z, P(engine, "d").Z, 3);
            Assert.True(P(engine, "e").X < P(engine, "d").X, "e lay furthest back, so it comes first");
            Assert.Equal("laid out a zone", ActionLog.Describe(engine, new ArrangeZoneCommand { ZoneId = "manual" }, laid, null, null).Single().Text.ToLowerInvariant());
            Assert.Empty(engine.Ok(new ArrangeZoneCommand { ZoneId = "manual" }).Events.OfType<ObjectUpdated>());
        }

        [Fact]
        public void Randomize_ShufflesPositions_LeavesLockedObjects()
        {
            var engine = Create();
            var spots = new[] { "t1", "t2" }.Select(id => P(engine, id)).ToList();
            var locked = P(engine, "t3");
            var moved = false;
            for (var i = 0; i < 10 && !moved; i++)
            {
                engine.Ok(new RandomizeZoneCommand { ZoneId = "mix" });
                moved = P(engine, "t1").X != spots[0].X;
            }
            Assert.True(moved, "ten randomizes never moved anything");
            Assert.Contains(P(engine, "t1"), spots);
            Assert.Contains(P(engine, "t2"), spots);
            Assert.Equal(locked, P(engine, "t3"));
        }

        [Fact]
        public void WrongZones_AreRefused_AndBadOptionsDoNotValidate()
        {
            var engine = Create();
            engine.Fails(new ArrangeZoneCommand { ZoneId = "mix" }, ErrorCodes.WrongObjectType);
            engine.Fails(new RandomizeZoneCommand { ZoneId = "nope" }, ErrorCodes.UnknownZone);
            var bad = TableJson.Parse(Json.Replace(@"""manualOnly"": true", @"""manualOnly"": true, ""maxPerRow"": -1, ""spacing"": 4"));
            Assert.Contains(bad.Errors, e => e.Path.EndsWith(".maxPerRow"));
            Assert.Contains(bad.Errors, e => e.Path.EndsWith(".spacing"));
        }
    }
}
