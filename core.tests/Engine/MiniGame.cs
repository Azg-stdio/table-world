using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>
    /// A small rules table for unit tests: deck <c>d</c> (5 cards c0..c4 top first, ranks 1..5, suits alternating
    /// red/black), empty face-up deck <c>pile</c>, scripting zone <c>z</c>, die <c>die</c>, seats s1/s2 with hands h1/h2,
    /// decks tagged <c>mine:s1</c>/<c>mine:s2</c>, variable <c>v</c> = 0, resource <c>score</c>, one phase <c>main</c>.
    /// </summary>
    internal static class MiniGame
    {
        public static string Json(string actions, string events = "", string phases = "", string win = "", string extraPhaseIds = "") => @"{
  ""schemaVersion"": ""0.1"",
  ""game"": { ""id"": ""mini"", ""name"": ""Mini"", ""minPlayers"": 2, ""maxPlayers"": 2 },
  ""assets"": [ { ""id"": ""sheet"", ""type"": ""image"", ""path"": ""builtin:standard_deck"", ""columns"": 10, ""rows"": 7 },
                { ""id"": ""back"", ""type"": ""image"", ""path"": ""builtin:standard_back"" } ],
  ""objects"": [
    { ""id"": ""d"", ""type"": ""Deck"", ""faceAsset"": ""sheet"", ""backAsset"": ""back"", ""transform"": { ""rotation"": [0, 0, 180] }, ""cards"": [
      { ""id"": ""c0"", ""faceIndex"": 0, ""tags"": [""first""], ""meta"": { ""rank"": 1, ""suit"": ""red"" } },
      { ""id"": ""c1"", ""faceIndex"": 1, ""meta"": { ""rank"": 2, ""suit"": ""black"" } },
      { ""id"": ""c2"", ""faceIndex"": 2, ""meta"": { ""rank"": 3, ""suit"": ""red"" } },
      { ""id"": ""c3"", ""faceIndex"": 3, ""meta"": { ""rank"": 4, ""suit"": ""black"" } },
      { ""id"": ""c4"", ""faceIndex"": 4, ""meta"": { ""rank"": 5, ""suit"": ""red"" } } ] },
    { ""id"": ""pile"", ""type"": ""Deck"", ""faceAsset"": ""sheet"", ""backAsset"": ""back"", ""transform"": { ""position"": [0.3, 0, 0] } },
    { ""id"": ""mine1"", ""type"": ""Deck"", ""tags"": [""mine:s1""], ""faceAsset"": ""sheet"", ""backAsset"": ""back"", ""transform"": { ""position"": [-0.3, 0, -0.2] } },
    { ""id"": ""mine2"", ""type"": ""Deck"", ""tags"": [""mine:s2""], ""faceAsset"": ""sheet"", ""backAsset"": ""back"", ""transform"": { ""position"": [-0.3, 0, 0.2] } },
    { ""id"": ""die"", ""type"": ""Die"", ""transform"": { ""position"": [0.5, 0, 0] } }
  ],
  ""zones"": [
    { ""id"": ""h1"", ""type"": ""HandZone"", ""owner"": ""s1"", ""transform"": { ""position"": [0, 0.05, -0.45] }, ""size"": [0.5, 0.1, 0.1] },
    { ""id"": ""h2"", ""type"": ""HandZone"", ""owner"": ""s2"", ""transform"": { ""position"": [0, 0.05, 0.45] }, ""size"": [0.5, 0.1, 0.1] },
    { ""id"": ""z"", ""type"": ""ScriptingZone"", ""transform"": { ""position"": [0, 0.05, 0.1] }, ""size"": [0.2, 0.1, 0.1] }
  ],
  ""players"": { ""seats"": [ { ""id"": ""s1"", ""color"": ""red"", ""handZone"": ""h1"" }, { ""id"": ""s2"", ""color"": ""blue"", ""handZone"": ""h2"" } ] },
  ""variables"": { ""v"": 0, ""note"": """" },
  ""resources"": [ { ""id"": ""score"", ""initial"": 1, ""min"": 0 } ],
  ""turn"": { ""phases"": [""main""" + extraPhaseIds + @"] },
  ""phases"": [ { ""id"": ""main"", ""actions"": [""a"", ""b""] }" + phases + @" ],
  ""actions"": [" + actions + @"],
  ""events"": {" + events + @"},
  ""winConditions"": [" + win + @"]
}";

        /// <summary>Action <c>a</c> with the given effects (and optional preconditions/params); action <c>b</c> ends the turn.</summary>
        public static string Action(string effects, string preconditions = "", string extra = "") =>
            @"{ ""id"": ""a"", ""actor"": ""current"", ""effects"": [" + effects + @"], ""preconditions"": [" + preconditions + @"]" + extra + @" },
              { ""id"": ""b"", ""actor"": ""current"", ""effects"": [ { ""endTurn"": {} } ] }";

        public static GameEngine Start(string json, ulong seed = 3)
        {
            var parsed = TableJson.Parse(json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            var game = new GameEngine(parsed.State!, seed);
            var start = game.Execute(new StartGameCommand());
            Assert.True(start.Ok, start.Error?.ToString());
            return game;
        }

        /// <summary>Starts a game whose action <c>a</c> runs <paramref name="effects"/>, then performs it as s1.</summary>
        public static GameEngine Run(string effects, string preconditions = "")
        {
            var game = Start(Json(Action(effects, preconditions)));
            var result = game.Execute(new PerformActionCommand { Actor = "s1", ActionId = "a" });
            Assert.True(result.Ok, result.Error?.ToString());
            return game;
        }

        public static bool Holds(string condition)
        {
            var game = Start(Json(Action(@"{ ""setVariable"": { ""name"": ""v"", ""value"": 1 } }", condition)));
            var result = game.Execute(new PerformActionCommand { Actor = "s1", ActionId = "a" });
            if (!result.Ok)
                Assert.Equal(ErrorCodes.PreconditionFailed, result.Error!.Code);
            return result.Ok;
        }

        public static Deck Deck(this GameEngine game, string id) => (Deck)game.Table.Find(id)!;
        public static string[] Ids(this Deck deck) => System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(deck.Cards, c => c.Id ?? "?"));
    }
}
