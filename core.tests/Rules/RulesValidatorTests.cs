using System.Collections.Generic;
using System.Linq;
using Core.Rules;
using Core.Tests.Engine;

namespace Core.Tests.Rules
{
    /// <summary>T4.1: every rules error code has a test, with the path and suggestion an agent would act on.</summary>
    public class RulesValidatorTests
    {
        private static ValidationError Single(string json, string code)
        {
            var errors = TableJson.Parse(json).Errors;
            Assert.True(errors.Count == 1, string.Join("\n", errors));
            Assert.Equal(code, errors[0].Code);
            return errors[0];
        }

        private static string WithEffect(string effect) => MiniGame.Json(MiniGame.Action(effect));
        private static string WithCondition(string condition) => MiniGame.Json(MiniGame.Action("", condition));

        [Fact]
        public void UnknownEffect()
        {
            var e = Single(WithEffect(@"{ ""shufle"": { ""object"": ""d"" } }"), ErrorCodes.UnknownEffect);
            Assert.Equal("actions[0].effects[0].shufle", e.Path);
            Assert.Equal("Did you mean 'shuffle'?", e.Suggestion);
        }

        [Fact]
        public void UnknownCondition()
        {
            var e = Single(WithCondition(@"{ ""zoneCont"": { ""zone"": ""d"", ""op"": "">"", ""value"": 0 } }"), ErrorCodes.UnknownCondition);
            Assert.Equal("Did you mean 'zoneCount'?", e.Suggestion);
        }

        [Fact]
        public void UnknownPhase()
        {
            var e = Single(WithEffect(@"{ ""gotoPhase"": { ""phase"": ""mian"" } }"), ErrorCodes.UnknownPhase);
            Assert.Equal("actions[0].effects[0].gotoPhase.phase", e.Path);
            Assert.Equal("Did you mean 'main'?", e.Suggestion);
        }

        [Fact]
        public void UnknownAction()
        {
            var json = MiniGame.Json(@"{ ""id"": ""a"", ""effects"": [] }");   // phase lists a and b; b is missing
            var e = Single(json, ErrorCodes.UnknownAction);
            Assert.Equal("phases[0].actions[1]", e.Path);
        }

        [Fact]
        public void UnknownVariable()
        {
            var e = Single(WithEffect(@"{ ""setVariable"": { ""name"": ""vv"", ""value"": 1 } }"), ErrorCodes.UnknownVariable);
            Assert.Equal("Did you mean 'v'?", e.Suggestion);
            Single(WithCondition(@"{ ""compare"": { ""left"": ""$var.nope"", ""op"": ""=="", ""right"": 1 } }"), ErrorCodes.UnknownVariable);
        }

        [Fact]
        public void UnknownResource()
        {
            var e = Single(WithEffect(@"{ ""setResource"": { ""player"": ""$actor"", ""resource"": ""scor"", ""delta"": 1 } }"), ErrorCodes.UnknownResource);
            Assert.Equal("Did you mean 'score'?", e.Suggestion);
        }

        [Fact]
        public void UnknownEvent()
        {
            var e = Single(MiniGame.Json(MiniGame.Action(""), events: @"""onGameStrat"": []"), ErrorCodes.UnknownEvent);
            Assert.Equal("Did you mean 'onGameStart'?", e.Suggestion);
        }

        [Fact]
        public void InvalidReference_AndUnknownTargets()
        {
            var e = Single(WithEffect(@"{ ""flip"": { ""object"": ""$acter.hand"" } }"), ErrorCodes.InvalidReference);
            Assert.Equal("Did you mean 'actor'?", e.Suggestion);
            Single(WithEffect(@"{ ""flip"": { ""object"": ""$param.card"" } }"), ErrorCodes.InvalidReference);   // action has no params
            Single(WithEffect(@"{ ""flip"": { ""object"": ""$seat.s9.hand"" } }"), ErrorCodes.UnknownSeat);
            Assert.Equal("Did you mean 'pile'?", Single(WithEffect(@"{ ""flip"": { ""object"": ""pil"" } }"), ErrorCodes.UnknownObject).Suggestion);
        }

        [Fact]
        public void InvalidOperator()
        {
            var e = Single(WithCondition(@"{ ""variable"": { ""name"": ""v"", ""op"": ""="", ""value"": 0 } }"), ErrorCodes.InvalidOperator);
            Assert.Equal("actions[0].preconditions[0].variable.op", e.Path);
        }

        [Fact]
        public void PlayerCount()
        {
            // Fewer seats than the fewest players the game takes; more seats than players is fine (they sit out).
            var json = MiniGame.Json(MiniGame.Action("")).Replace(@"""maxPlayers"": 2", @"""maxPlayers"": 4").Replace(@"""minPlayers"": 2", @"""minPlayers"": 3");
            Single(json, ErrorCodes.PlayerCount);
            var spare = MiniGame.Json(MiniGame.Action("")).Replace(@"""maxPlayers"": 2", @"""maxPlayers"": 1").Replace(@"""minPlayers"": 2", @"""minPlayers"": 1");
            Assert.DoesNotContain(TableValidator.Validate(TableJson.Parse(spare).State!), e => e.Code == ErrorCodes.PlayerCount);
        }

        [Fact]
        public void EffectShape_FieldsAndExclusiveOptions()
        {
            Single(WithEffect(@"{ ""move"": { ""to"": ""pile"" } }"), ErrorCodes.MissingField);
            Single(WithEffect(@"{ ""move"": { ""from"": ""d"", ""to"": ""pile"", ""take"": ""middle"" } }"), ErrorCodes.InvalidValue);
            Single(WithEffect(@"{ ""deal"": { ""from"": ""d"", ""toZone"": ""z"", ""toTag"": ""mine"" } }"), ErrorCodes.InvalidValue);
            Single(WithEffect(@"{ ""setVariable"": { ""name"": ""v"" } }"), ErrorCodes.InvalidValue);
            Single(WithEffect(@"{ ""endTurn"": 1 }"), ErrorCodes.InvalidValue);
            Single(WithEffect(@"{ ""move"": { ""from"": ""d"", ""to"": ""pile"" }, ""flip"": { ""object"": ""d"" } }"), ErrorCodes.InvalidValue);
            var unknownKey = Single(WithEffect(@"{ ""move"": { ""form"": ""d"", ""object"": ""c0"", ""to"": ""pile"" } }"), ErrorCodes.UnknownKey);
            Assert.Equal("actions[0].effects[0].move.form", unknownKey.Path);
        }

        [Fact]
        public void PhaseAdvance_MustSetExactlyOneRule()
        {
            var json = MiniGame.Json(MiniGame.Action(""), phases: @", { ""id"": ""x"", ""advanceWhen"": { ""immediate"": true, ""allPlayersActed"": true } }");
            Assert.Equal("phases[1].advanceWhen", Single(json, ErrorCodes.InvalidValue).Path);
        }

        [Fact]
        public void Ui_ButtonsReferencesAndCounters_AreChecked()
        {
            static string WithUi(string elements) => MiniGame.Json(MiniGame.Action("")).Replace(@"""winConditions"": []",
                @"""winConditions"": [], ""ui"": { ""panels"": [ { ""id"": ""p"", ""anchor"": ""topRight"", ""elements"": [" + elements + "] } ] }");

            Assert.Equal("Did you mean 'a'?", Single(WithUi(@"{ ""type"": ""button"", ""action"": ""aa"" }"), ErrorCodes.UnknownAction).Suggestion);
            Single(WithUi(@"{ ""type"": ""label"", ""text"": ""Round {$var.round}"" }"), ErrorCodes.UnknownVariable);
            Single(WithUi(@"{ ""type"": ""counter"", ""text"": ""Score"" }"), ErrorCodes.MissingField);
            Single(WithUi(@"{ ""type"": ""image"", ""asset"": ""shet"" }"), ErrorCodes.UnknownAsset);
            Single(WithUi(@"{ ""type"": ""slider"" }"), ErrorCodes.InvalidValue);
            Assert.Empty(TableJson.Parse(WithUi(@"{ ""type"": ""counter"", ""text"": ""Mine"", ""value"": ""$viewer.mine.count"" }, { ""type"": ""button"", ""action"": ""b"", ""seat"": ""s2"" }")).Errors);
        }

        [Fact]
        public void Ui_VisibleTo_TakesSeatsOrTheHost()
        {
            static string WithUi(string elements) => MiniGame.Json(MiniGame.Action("")).Replace(@"""winConditions"": []",
                @"""winConditions"": [], ""ui"": { ""panels"": [ { ""id"": ""p"", ""visibleTo"": [""host""], ""elements"": [" + elements + "] } ] }");

            var state = TableJson.Parse(WithUi(@"{ ""type"": ""button"", ""action"": ""b"", ""seat"": ""$current"", ""visibleTo"": [""host"", ""s2""] }"));
            Assert.Empty(state.Errors);
            Assert.Equal("Did you mean 'host'?", Single(WithUi(@"{ ""type"": ""label"", ""text"": ""x"", ""visibleTo"": [""hots""] }"), ErrorCodes.UnknownSeat).Suggestion);

            var element = state.State!.Ui!.Panels[0].Elements[0];
            Assert.True(PanelDef.Shows(element.VisibleTo, null, host: true), "the host sees it, seated or not");
            Assert.True(PanelDef.Shows(element.VisibleTo, "s2", host: false));
            Assert.False(PanelDef.Shows(element.VisibleTo, "s1", host: false), "another player does not");
            Assert.True(PanelDef.Shows(new List<string>(), "s1", host: false), "an empty list lets everyone see");
        }

        [Theory]
        [InlineData("war")]
        [InlineData("uno-lite")]
        public void SampleGames_Validate(string name)
        {
            Assert.Empty(TableJson.Parse(GameFixtures.GameJson(name)).Errors.Select(e => e.ToString()));
        }
    }
}
