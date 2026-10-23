using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    internal static class EngineFixture
    {
        /// <summary>
        /// Deck of 10 face-down cards (c0 top … c9 bottom; c3 has no id), a loose card, a bag with three tokens, an infinite
        /// bag, a d6, a locked die, and two seats with hand zones on opposite sides of the table.
        /// </summary>
        public const string Json = @"{
  ""schemaVersion"": ""0.1"",
  ""assets"": [
    { ""id"": ""sheet"", ""type"": ""image"", ""path"": ""builtin:standard_deck"", ""columns"": 10, ""rows"": 7 },
    { ""id"": ""back"", ""type"": ""image"", ""path"": ""builtin:standard_back"" },
    { ""id"": ""alt_back"", ""type"": ""image"", ""path"": ""alt.png"" }
  ],
  ""objects"": [
    { ""id"": ""deck"", ""type"": ""Deck"", ""faceAsset"": ""sheet"", ""backAsset"": ""back"", ""transform"": { ""rotation"": [0, 0, 180] },
      ""cards"": [
        { ""id"": ""c0"", ""faceIndex"": 0, ""meta"": { ""rank"": 1 } }, { ""id"": ""c1"", ""faceIndex"": 1 }, { ""id"": ""c2"", ""faceIndex"": 2 },
        { ""faceIndex"": 3, ""name"": ""anonymous"" }, { ""id"": ""c4"", ""faceIndex"": 4 }, { ""id"": ""c5"", ""faceIndex"": 5 },
        { ""id"": ""c6"", ""faceIndex"": 6 }, { ""id"": ""c7"", ""faceIndex"": 7 }, { ""id"": ""c8"", ""faceIndex"": 8 },
        { ""id"": ""c9"", ""faceIndex"": 9, ""backAsset"": ""alt_back"" } ] },
    { ""id"": ""loose"", ""type"": ""Card"", ""faceAsset"": ""sheet"", ""faceIndex"": 20, ""backAsset"": ""back"", ""transform"": { ""position"": [0.3, 0, 0] } },
    { ""id"": ""bag"", ""type"": ""Bag"", ""transform"": { ""position"": [-0.3, 0, 0] } },
    { ""id"": ""t1"", ""type"": ""Token"", ""container"": ""bag"" },
    { ""id"": ""t2"", ""type"": ""Token"", ""container"": ""bag"" },
    { ""id"": ""t3"", ""type"": ""Token"", ""container"": ""bag"" },
    { ""id"": ""coins"", ""type"": ""InfiniteBag"", ""transform"": { ""position"": [-0.5, 0, 0] } },
    { ""id"": ""coin"", ""type"": ""Token"", ""container"": ""coins"", ""shape"": ""round"", ""states"": { ""2"": { ""id"": ""coin_back"", ""type"": ""Token"" } } },
    { ""id"": ""d6"", ""type"": ""Die"" },
    { ""id"": ""locked_die"", ""type"": ""Die"", ""locked"": true }
  ],
  ""zones"": [
    { ""id"": ""hand_1"", ""type"": ""HandZone"", ""owner"": ""seat_1"", ""transform"": { ""position"": [0, 0.05, -0.5] }, ""size"": [0.5, 0.1, 0.15] },
    { ""id"": ""hand_2"", ""type"": ""HandZone"", ""owner"": ""seat_2"", ""transform"": { ""position"": [0, 0.05, 0.5], ""rotation"": [0, 180, 0] }, ""size"": [0.5, 0.1, 0.15] },
    { ""id"": ""snap"", ""type"": ""SnapPoint"", ""parent"": ""loose"" }
  ],
  ""players"": { ""seats"": [
    { ""id"": ""seat_1"", ""color"": ""red"", ""handZone"": ""hand_1"" },
    { ""id"": ""seat_2"", ""color"": ""blue"", ""handZone"": ""hand_2"" },
    { ""id"": ""seat_3"", ""color"": ""green"" }
  ] }
}";

        public static TableEngine Create(ulong seed = 42)
        {
            var parsed = TableJson.Parse(Json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            return new TableEngine(parsed.State!, seed);
        }

        public static Deck Deck(this TableEngine engine, string id = "deck") => (Deck)engine.Find(id)!;

        public static string[] CardIds(this Deck deck) => deck.Cards.Select(c => c.Id ?? "?").ToArray();

        public static CommandResult Ok(this TableEngine engine, Command command)
        {
            var result = engine.Execute(command);
            Assert.True(result.Ok, result.Error?.ToString());
            return result;
        }

        public static ValidationError Fails(this TableEngine engine, Command command, string code)
        {
            var hashBefore = TableJson.Hash(engine.State);
            var result = engine.Execute(command);
            Assert.False(result.Ok, "Expected the command to fail.");
            Assert.Equal(code, result.Error!.Code);
            Assert.Empty(result.Events);
            Assert.Equal(hashBefore, TableJson.Hash(engine.State));
            return result.Error;
        }

        public static void AssertValid(this TableEngine engine)
        {
            var errors = TableValidator.Validate(engine.State);
            Assert.True(errors.Count == 0, string.Join("\n", errors));
            var reparsed = TableJson.Parse(TableJson.Serialize(engine.State));
            Assert.True(reparsed.Ok, string.Join("\n", reparsed.Errors));
        }
    }
}
