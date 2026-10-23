using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>T4.2: one test per effect, per condition, and per engine error code.</summary>
    public class RulesEngineTests
    {
        // ---- effects ----

        [Fact]
        public void Move_TopToDeckBottom_AndAllButTop_AndObjectToZone()
        {
            var game = MiniGame.Run(@"{ ""move"": { ""from"": ""d"", ""to"": ""pile"" } }, { ""move"": { ""from"": ""d"", ""take"": ""bottom"", ""to"": ""pile"", ""at"": ""bottom"" } }");
            Assert.Equal(new[] { "c0", "c4" }, game.Deck("pile").Ids());

            game = MiniGame.Run(@"{ ""move"": { ""from"": ""d"", ""take"": ""allButTop"", ""to"": ""pile"" } }");
            Assert.Equal(new[] { "c0" }, game.Deck("d").Ids());
            Assert.Equal(4, game.Deck("pile").Cards.Count);

            game = MiniGame.Run(@"{ ""move"": { ""object"": ""c2"", ""to"": ""z"", ""faceUp"": true } }");
            Assert.Equal(new[] { "c2" }, game.Table.ObjectsIn("z").Select(o => o.Id));
            Assert.True(game.Table.Find("c2")!.IsFaceUp);
        }

        [Fact]
        public void Deal_ToHandsAndToTaggedDecks()
        {
            var game = MiniGame.Run(@"{ ""deal"": { ""from"": ""d"", ""count"": 2, ""toZone"": ""$it.hand"" } }");
            Assert.Equal(new[] { "c0", "c2" }, game.Table.ObjectsIn("h1").Select(o => o.Id));
            Assert.Equal(new[] { "c1", "c3" }, game.Table.ObjectsIn("h2").Select(o => o.Id));

            game = MiniGame.Run(@"{ ""deal"": { ""from"": ""d"", ""count"": 1, ""toTag"": ""mine"", ""seats"": [""s2""] } }");
            Assert.Equal(new[] { "c0" }, game.Deck("mine2").Ids());
        }

        [Fact]
        public void Shuffle_Flip_Lock_Roll()
        {
            var game = MiniGame.Run(@"{ ""shuffle"": { ""object"": ""d"" } }, { ""flip"": { ""object"": ""pile"" } }, { ""roll"": { ""object"": ""die"", ""store"": ""v"" } }, { ""lock"": { ""object"": ""die"" } }");
            Assert.NotEqual(new[] { "c0", "c1", "c2", "c3", "c4" }, game.Deck("d").Ids());
            Assert.False(game.Deck("pile").IsFaceUp);
            Assert.True(game.Table.Find("die")!.Locked);
            Assert.InRange((int)game.Session!.Variables["v"], 1, 6);
        }

        [Fact]
        public void SetVariable_ValueAndDelta_AndSetResource_Clamped()
        {
            var game = MiniGame.Run(@"{ ""setVariable"": { ""name"": ""v"", ""value"": 5 } }, { ""setVariable"": { ""name"": ""v"", ""delta"": -2 } },
                                      { ""setVariable"": { ""name"": ""note"", ""value"": ""$obj.d.top.meta.suit"" } },
                                      { ""setResource"": { ""player"": ""$actor"", ""resource"": ""score"", ""delta"": -5 } }");
            Assert.Equal(3.0, (double)game.Session!.Variables["v"]);
            Assert.Equal("red", (string)game.Session.Variables["note"]!);
            Assert.Equal(0, game.Session.Resources["s1"]["score"]);
            Assert.Equal(1, game.Session.Resources["s2"]["score"]);
        }

        [Fact]
        public void If_ThenAndElse_AndForEach_PlayersAndObjects()
        {
            var game = MiniGame.Run(@"{ ""if"": { ""condition"": { ""zoneCount"": { ""zone"": ""d"", ""op"": ""=="", ""value"": 5 } },
                                                  ""then"": [ { ""setVariable"": { ""name"": ""v"", ""value"": 1 } } ], ""else"": [ { ""setVariable"": { ""name"": ""v"", ""value"": 2 } } ] } },
                                      { ""forEach"": { ""players"": true, ""do"": [ { ""setResource"": { ""player"": ""$it"", ""resource"": ""score"", ""delta"": 10 } } ] } },
                                      { ""forEach"": { ""objectsIn"": ""d"", ""do"": [ { ""setVariable"": { ""name"": ""note"", ""value"": ""$it.meta.suit"" } } ] } }");
            Assert.Equal(1, (int)game.Session!.Variables["v"]);
            Assert.Equal(11, game.Session.Resources["s2"]["score"]);
            Assert.Equal("red", (string)game.Session.Variables["note"]!);   // last card c4
        }

        [Fact]
        public void Message_FillsTemplates()
        {
            var game = MiniGame.Run(@"{ ""message"": { ""text"": ""{$actor.id} sees {$obj.d.top.name}{$obj.d.top.id} ({$obj.d.count})"" } }");
            Assert.Equal("s1 sees c0 (5)", game.Session!.Log.Last());
        }

        [Fact]
        public void EndTurn_PassesToNextSeat_AndEndPhase_EndsTheTurnAfterTheLastPhase()
        {
            var game = MiniGame.Run(@"{ ""endTurn"": {} }");
            Assert.Equal(("s2", 2), (game.Session!.CurrentSeat, game.Session.Turn));

            game = MiniGame.Run(@"{ ""endPhase"": {} }");
            Assert.Equal("s2", game.Session!.CurrentSeat);
        }

        [Fact]
        public void GotoPhase_ReentersAPhaseAndRunsOnEnter()
        {
            var json = MiniGame.Json(MiniGame.Action(@"{ ""gotoPhase"": { ""phase"": ""second"" } }"),
                phases: @", { ""id"": ""second"", ""actions"": [""b""], ""onEnter"": [ { ""setVariable"": { ""name"": ""v"", ""delta"": 1 } } ] }",
                extraPhaseIds: @", ""second""");
            var game = MiniGame.Start(json);
            game.Execute(new PerformActionCommand { Actor = "s1", ActionId = "a" });
            Assert.Equal(("second", 1.0), (game.Session!.Phase, (double)game.Session.Variables["v"]));
        }

        [Fact]
        public void Win_EndsTheGame_AndWinConditionsAreChecked()
        {
            var game = MiniGame.Run(@"{ ""win"": { ""player"": ""$other"" } }");
            Assert.Equal((true, "s2"), (game.Session!.Over, game.Session.Winner));

            var json = MiniGame.Json(MiniGame.Action(@"{ ""move"": { ""from"": ""d"", ""take"": ""all"", ""to"": ""$actor.mine"" } }"),
                win: @"{ ""when"": { ""zoneCount"": { ""zone"": ""$player.mine"", ""op"": ""=="", ""value"": 5 } }, ""winner"": ""$player"" }");
            game = MiniGame.Start(json);
            Assert.True(game.Execute(new PerformActionCommand { Actor = "s1", ActionId = "a" }).Ok);
            Assert.Equal("s1", game.Session!.Winner);
        }

        // ---- conditions ----

        [Theory]
        [InlineData(@"{ ""zoneCount"": { ""zone"": ""d"", ""op"": "">="", ""value"": 5 } }", true)]
        [InlineData(@"{ ""zoneCount"": { ""zone"": ""z"", ""op"": "">"", ""value"": 0 } }", false)]
        [InlineData(@"{ ""compare"": { ""left"": ""$obj.d.top.meta.rank"", ""op"": ""<"", ""right"": ""$obj.d.bottom.meta.rank"" } }", true)]
        [InlineData(@"{ ""compare"": { ""left"": ""$obj.d.top.meta.suit"", ""op"": ""=="", ""right"": ""black"" } }", false)]
        [InlineData(@"{ ""compare"": { ""left"": ""$obj.pile.top.meta.suit"", ""op"": ""=="", ""right"": ""red"" } }", false)]
        [InlineData(@"{ ""variable"": { ""name"": ""v"", ""op"": ""=="", ""value"": 0 } }", true)]
        [InlineData(@"{ ""resource"": { ""player"": ""$actor"", ""resource"": ""score"", ""op"": "">"", ""value"": 1 } }", false)]
        [InlineData(@"{ ""isCurrentPlayer"": { ""player"": ""$actor"" } }", true)]
        [InlineData(@"{ ""isCurrentPlayer"": { ""player"": ""s2"" } }", false)]
        [InlineData(@"{ ""objectIn"": { ""object"": ""$obj.d.top"", ""zone"": ""d"" } }", true)]
        [InlineData(@"{ ""hasTag"": { ""object"": ""$obj.d.top"", ""tag"": ""first"" } }", true)]
        [InlineData(@"{ ""and"": [ { ""variable"": { ""name"": ""v"", ""op"": ""=="", ""value"": 0 } }, { ""isCurrentPlayer"": { ""player"": ""s2"" } } ] }", false)]
        [InlineData(@"{ ""or"": [ { ""variable"": { ""name"": ""v"", ""op"": ""=="", ""value"": 9 } }, { ""isCurrentPlayer"": { ""player"": ""s1"" } } ] }", true)]
        [InlineData(@"{ ""not"": { ""isCurrentPlayer"": { ""player"": ""s1"" } } }", false)]
        public void Condition_Evaluates(string condition, bool expected)
        {
            Assert.Equal(expected, MiniGame.Holds(condition));
        }

        // ---- parameters and legal actions ----

        [Fact]
        public void ObjectParams_AreFilteredAndListedAsLegalChoices()
        {
            var actions = @"{ ""id"": ""a"", ""actor"": ""current"", ""params"": [ { ""name"": ""card"", ""type"": ""object"", ""filter"": { ""inZone"": ""$actor.hand"", ""type"": ""Card"" } } ],
                              ""preconditions"": [ { ""compare"": { ""left"": ""$param.card.meta.suit"", ""op"": ""=="", ""right"": ""red"" } } ],
                              ""effects"": [ { ""move"": { ""object"": ""$param.card"", ""to"": ""pile"" } } ] },
                            { ""id"": ""b"", ""actor"": ""current"", ""effects"": [ { ""endTurn"": {} } ] }";
            var game = MiniGame.Start(MiniGame.Json(actions, events: @"""onGameStart"": [ { ""deal"": { ""from"": ""d"", ""count"": 2, ""toZone"": ""$it.hand"" } } ]"));

            var legal = game.LegalActions("s1").Single(a => a.ActionId == "a");
            Assert.Equal(new[] { "c0", "c2" }, legal.Choices.Select(c => c["card"]));
            Assert.Empty(game.LegalActions("s2"));   // not s2's turn

            Fails(game, "s1", "a", ErrorCodes.MissingParam);
            Fails(game, "s1", "a", ErrorCodes.InvalidParam, ("card", "c1"));   // c1 is in s2's hand
            Assert.True(game.Execute(new PerformActionCommand { Actor = "s1", ActionId = "a", Params = { ["card"] = "c2" } }).Ok);
            Assert.Equal(new[] { "c2" }, game.Deck("pile").Ids());
        }

        // ---- engine error codes ----

        [Fact]
        public void ErrorCodes_ForEveryRefusal()
        {
            var parsed = TableJson.Parse(Core.Tests.Rules.SampleTables.AllTypes).State!;
            Assert.Equal(ErrorCodes.NoRules, new GameEngine(parsed, 1).Execute(new StartGameCommand()).Error!.Code);

            var notStarted = new GameEngine(TableJson.Parse(MiniGame.Json(MiniGame.Action(""))).State!, 1);
            Fails(notStarted, "s1", "a", ErrorCodes.GameNotStarted);

            var game = MiniGame.Start(MiniGame.Json(MiniGame.Action(@"{ ""setVariable"": { ""name"": ""v"", ""delta"": 1 } }",
                @"{ ""variable"": { ""name"": ""v"", ""op"": ""<"", ""value"": 1 } }", @", ""limitPerPhase"": 1")));
            Assert.Equal(ErrorCodes.GameAlreadyStarted, game.Execute(new StartGameCommand()).Error!.Code);
            Fails(game, "s1", "nope", ErrorCodes.UnknownAction);
            Fails(game, "s2", "a", ErrorCodes.NotYourTurn);
            Assert.True(game.Execute(new PerformActionCommand { Actor = "s1", ActionId = "a" }).Ok);
            Fails(game, "s1", "a", ErrorCodes.LimitReached);

            game = MiniGame.Start(MiniGame.Json(MiniGame.Action("", @"{ ""variable"": { ""name"": ""v"", ""op"": ""=="", ""value"": 7 } }")));
            Fails(game, "s1", "a", ErrorCodes.PreconditionFailed);

            var withSecond = MiniGame.Json(MiniGame.Action(@"{ ""gotoPhase"": { ""phase"": ""second"" } }"),
                phases: @", { ""id"": ""second"", ""actions"": [""b""] }", extraPhaseIds: @", ""second""");
            game = MiniGame.Start(withSecond);
            game.Execute(new PerformActionCommand { Actor = "s1", ActionId = "a" });
            Fails(game, "s1", "a", ErrorCodes.ActionNotAvailable);

            game = MiniGame.Run(@"{ ""win"": { ""player"": ""s1"" } }");
            Fails(game, "s1", "b", ErrorCodes.GameOver);
        }

        [Fact]
        public void RulesLoop_IsDetected_AndFailedEffects_RollBackEverything()
        {
            var looping = MiniGame.Json(MiniGame.Action(@"{ ""gotoPhase"": { ""phase"": ""spin"" } }"),
                phases: @", { ""id"": ""spin"", ""advanceWhen"": { ""immediate"": true }, ""onEnter"": [ { ""gotoPhase"": { ""phase"": ""spin"" } } ] }",
                extraPhaseIds: @", ""spin""");
            var game = MiniGame.Start(looping);
            Fails(game, "s1", "a", ErrorCodes.RulesLoop);

            // The first move succeeds, the second fails (z is empty): the deck must be whole again.
            game = MiniGame.Start(MiniGame.Json(MiniGame.Action(@"{ ""move"": { ""from"": ""d"", ""to"": ""pile"" } }, { ""move"": { ""from"": ""z"", ""take"": ""all"", ""to"": ""pile"" } }, { ""flip"": { ""object"": ""$obj.z.top"" } }")));
            var hash = TableJson.Hash(game.State);
            var result = game.Execute(new PerformActionCommand { Actor = "s1", ActionId = "a" });
            Assert.False(result.Ok);
            Assert.True(result.StateRestored);
            Assert.Equal(hash, TableJson.Hash(game.State));
            Assert.Equal(5, game.Deck("d").Cards.Count);
        }

        private static void Fails(GameEngine game, string seat, string action, string code, params (string, string)[] args)
        {
            var command = new PerformActionCommand { Actor = seat, ActionId = action };
            foreach (var (k, v) in args)
                command.Params[k] = v;
            var result = game.Execute(command);
            Assert.False(result.Ok, $"expected {code}");
            Assert.Equal(code, result.Error!.Code);
        }
    }
}
