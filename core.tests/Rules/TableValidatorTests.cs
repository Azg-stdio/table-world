using System.Collections.Generic;
using System.Linq;
using Core.Rules;

namespace Core.Tests.Rules
{
    public class TableValidatorTests
    {
        private static IReadOnlyList<ValidationError> Errors(string objects, string zones = "", string seats = "") =>
            TableJson.Parse(SampleTables.WithObjects(objects, zones, seats)).Errors;

        [Fact]
        public void DuplicateIds_AcrossObjectsCardsStatesAndZones_AreRejected()
        {
            var errors = Errors(@"
                { ""id"": ""a"", ""type"": ""Die"" },
                { ""id"": ""a"", ""type"": ""Die"" },
                { ""id"": ""deck"", ""type"": ""Deck"", ""faceAsset"": ""sheet"", ""backAsset"": ""back"", ""cards"": [ { ""id"": ""deck"", ""faceIndex"": 0 } ] },
                { ""id"": ""m"", ""type"": ""Die"", ""states"": { ""2"": { ""id"": ""m"", ""type"": ""Die"" } } }",
                zones: @"{ ""id"": ""a"", ""type"": ""ScriptingZone"" }");

            var paths = errors.Where(e => e.Code == ErrorCodes.DuplicateId).Select(e => e.Path).ToList();
            Assert.Equal(new[] { "objects[1].id", "objects[2].cards[0].id", "objects[3].states.2.id", "zones[0].id" }, paths);
        }

        [Theory]
        [InlineData("Deck_1", "deck_1")]
        [InlineData("my deck", "my_deck")]
        public void InvalidIdCharacters_SuggestAFixedId(string id, string suggested)
        {
            var error = Assert.Single(Errors($@"{{ ""id"": ""{id}"", ""type"": ""Die"" }}"));

            Assert.Equal(ErrorCodes.InvalidId, error.Code);
            Assert.Equal($"Use '{suggested}'.", error.Suggestion);
        }

        [Fact]
        public void MissingId_IsRejected()
        {
            var error = Assert.Single(Errors(@"{ ""type"": ""Die"" }"));

            Assert.Equal(ErrorCodes.MissingField, error.Code);
            Assert.Equal("objects[0].id", error.Path);
        }

        [Fact]
        public void Container_MustExistAndBeABag()
        {
            var errors = Errors(@"
                { ""id"": ""bag"", ""type"": ""Bag"" },
                { ""id"": ""die"", ""type"": ""Die"", ""container"": ""bga"" },
                { ""id"": ""card"", ""type"": ""Card"", ""faceAsset"": ""sheet"", ""backAsset"": ""back"", ""container"": ""die"" }");

            var unknown = errors.Single(e => e.Path == "objects[1].container");
            Assert.Equal(ErrorCodes.UnknownContainer, unknown.Code);
            Assert.Equal("Did you mean 'bag'?", unknown.Suggestion);
            Assert.Equal(ErrorCodes.InvalidContainer, errors.Single(e => e.Path == "objects[2].container").Code);
        }

        [Fact]
        public void Container_Cycles_AreRejected()
        {
            var errors = Errors(@"
                { ""id"": ""a"", ""type"": ""Bag"", ""container"": ""b"" },
                { ""id"": ""b"", ""type"": ""Bag"", ""container"": ""c"" },
                { ""id"": ""c"", ""type"": ""Bag"", ""container"": ""a"" },
                { ""id"": ""self"", ""type"": ""Bag"", ""container"": ""self"" },
                { ""id"": ""ok"", ""type"": ""Die"", ""container"": ""a"" }");

            var cycles = errors.Where(e => e.Code == ErrorCodes.ContainerCycle).Select(e => e.Path).ToList();
            Assert.Equal(new[] { "objects[3].container", "objects[0].container", "objects[1].container", "objects[2].container" }, cycles);
            Assert.DoesNotContain(errors, e => e.Path == "objects[4].container");
        }

        [Fact]
        public void NestedBags_WithoutCycle_AreValid()
        {
            Assert.Empty(Errors(@"
                { ""id"": ""outer"", ""type"": ""Bag"" },
                { ""id"": ""inner"", ""type"": ""Bag"", ""container"": ""outer"" },
                { ""id"": ""deck"", ""type"": ""Deck"", ""container"": ""inner"", ""faceAsset"": ""sheet"", ""backAsset"": ""back"" }"));
        }

        [Fact]
        public void States_CannotHaveContainersOrNestedStates()
        {
            var errors = Errors(@"
                { ""id"": ""bag"", ""type"": ""Bag"" },
                { ""id"": ""m"", ""type"": ""Die"", ""states"": {
                    ""2"": { ""id"": ""m2"", ""type"": ""Die"", ""container"": ""bag"" },
                    ""x"": { ""id"": ""m3"", ""type"": ""Die"", ""states"": { ""4"": { ""id"": ""m4"", ""type"": ""Die"" } } } } }");

            Assert.Equal(ErrorCodes.InvalidContainer, errors.Single(e => e.Path == "objects[1].states.2.container").Code);
            Assert.Contains(errors, e => e.Path == "objects[1].states.x" && e.Code == ErrorCodes.InvalidValue);
            Assert.Contains(errors, e => e.Path == "objects[1].states.x.states" && e.Code == ErrorCodes.InvalidValue);
        }

        [Fact]
        public void CardAssets_MustExistBeImagesAndIndexInsideTheSheet()
        {
            var errors = Errors(@"
                { ""id"": ""c1"", ""type"": ""Card"", ""faceAsset"": ""shete"", ""backAsset"": ""back"" },
                { ""id"": ""c2"", ""type"": ""Card"", ""faceAsset"": ""sheet"", ""faceIndex"": 70, ""backAsset"": ""back"" },
                { ""id"": ""c3"", ""type"": ""Card"", ""faceAsset"": ""mesh"", ""backAsset"": ""back"" }");

            var unknown = errors.Single(e => e.Path == "objects[0].faceAsset");
            Assert.Equal(ErrorCodes.UnknownAsset, unknown.Code);
            Assert.Equal("Did you mean 'sheet'?", unknown.Suggestion);
            var range = errors.Single(e => e.Path == "objects[1].faceIndex");
            Assert.Equal(ErrorCodes.IndexOutOfRange, range.Code);
            Assert.Equal("Use 0..69 (row-major, 0 is top-left).", range.Suggestion);
            Assert.Equal(ErrorCodes.WrongAssetType, errors.Single(e => e.Path == "objects[2].faceAsset").Code);
        }

        [Fact]
        public void DeckCards_InheritDeckAssets_AndBadDeckAssetIsReportedOnce()
        {
            var errors = Errors(@"
                { ""id"": ""d1"", ""type"": ""Deck"", ""faceAsset"": ""sheet"", ""backAsset"": ""back"",
                  ""cards"": [ { ""faceIndex"": 0 }, { ""faceIndex"": 99 } ] },
                { ""id"": ""d2"", ""type"": ""Deck"", ""faceAsset"": ""nope"", ""backAsset"": ""back"",
                  ""cards"": [ { ""faceIndex"": 0 }, { ""faceIndex"": 1 } ] },
                { ""id"": ""d3"", ""type"": ""Deck"", ""cards"": [ { ""faceIndex"": 0 } ] }");

            Assert.Equal(ErrorCodes.IndexOutOfRange, errors.Single(e => e.Path == "objects[0].cards[1].faceIndex").Code);
            Assert.Single(errors, e => e.Path.StartsWith("objects[1]"));
            Assert.Equal(ErrorCodes.MissingField, errors.Single(e => e.Path == "objects[2].cards[0].faceAsset").Code);
            Assert.Equal(ErrorCodes.MissingField, errors.Single(e => e.Path == "objects[2].cards[0].backAsset").Code);
        }

        [Fact]
        public void Card_WithoutAssets_ReportsMissingFields()
        {
            var errors = Errors(@"{ ""id"": ""c"", ""type"": ""Card"" }");

            Assert.Equal(new[] { "objects[0].faceAsset", "objects[0].backAsset" }, errors.Select(e => e.Path));
            Assert.All(errors, e => Assert.Equal(ErrorCodes.MissingField, e.Code));
        }

        [Fact]
        public void Dice_FacesLabelsAndValue_AreChecked()
        {
            var errors = Errors(@"
                { ""id"": ""d7"", ""type"": ""Die"", ""faces"": 7 },
                { ""id"": ""d6"", ""type"": ""Die"", ""labels"": [""a""], ""value"": 9 }");

            Assert.Equal(ErrorCodes.InvalidDieFaces, errors.Single(e => e.Path == "objects[0].faces").Code);
            Assert.Equal(ErrorCodes.InvalidValue, errors.Single(e => e.Path == "objects[1].labels").Code);
            Assert.Equal(ErrorCodes.InvalidValue, errors.Single(e => e.Path == "objects[1].value").Code);
        }

        [Fact]
        public void Transform_ScaleAndTint_AreChecked()
        {
            var errors = Errors(@"
                { ""id"": ""a"", ""type"": ""Die"", ""transform"": { ""scale"": [1, 0, 1] } },
                { ""id"": ""b"", ""type"": ""Die"", ""tint"": [1, 2, 0] },
                { ""id"": ""c"", ""type"": ""Die"", ""tint"": [1, 1] }");

            Assert.Equal(new[] { "objects[0].transform.scale", "objects[1].tint", "objects[2].tint" }, errors.Select(e => e.Path));
        }

        [Fact]
        public void SeatsAndZones_ReferencesAreChecked()
        {
            var errors = Errors(@"{ ""id"": ""board"", ""type"": ""Board"" }",
                zones: @"
                    { ""id"": ""hand_1"", ""type"": ""HandZone"", ""owner"": ""seat_1"" },
                    { ""id"": ""hand_x"", ""type"": ""HandZone"", ""owner"": ""seat_9"" },
                    { ""id"": ""gm"", ""type"": ""HiddenZone"", ""visibleTo"": [""host"", ""seat_3""] },
                    { ""id"": ""snap"", ""type"": ""SnapPoint"", ""parent"": ""bord"" },
                    { ""id"": ""trigger"", ""type"": ""ScriptingZone"" }",
                seats: @"
                    { ""id"": ""seat_1"", ""color"": ""red"", ""handZone"": ""hand_1"" },
                    { ""id"": ""seat_2"", ""color"": ""blue"", ""handZone"": ""hand_1"" },
                    { ""id"": ""seat_3"", ""color"": """", ""handZone"": ""trigger"" },
                    { ""id"": ""seat_1"", ""color"": ""green"" }");

            Assert.Equal(ErrorCodes.UnknownSeat, errors.Single(e => e.Path == "zones[1].owner").Code);
            Assert.DoesNotContain(errors, e => e.Path == "zones[2].visibleTo[0]");
            Assert.DoesNotContain(errors, e => e.Path == "zones[2].visibleTo[1]");
            Assert.Equal("Did you mean 'board'?", errors.Single(e => e.Path == "zones[3].parent").Suggestion);
            Assert.Equal(ErrorCodes.InvalidValue, errors.Single(e => e.Path == "players.seats[1].handZone").Code);
            Assert.Equal(ErrorCodes.MissingField, errors.Single(e => e.Path == "players.seats[2].color").Code);
            Assert.Equal(ErrorCodes.UnknownZone, errors.Single(e => e.Path == "players.seats[2].handZone").Code);
            Assert.Equal(ErrorCodes.DuplicateId, errors.Single(e => e.Path == "players.seats[3].id").Code);
        }

        [Fact]
        public void SchemaVersion_MustBeCurrent()
        {
            var result = TableJson.Parse(@"{ ""schemaVersion"": ""9.9"" }");

            var error = Assert.Single(result.Errors);
            Assert.Equal(ErrorCodes.UnsupportedSchemaVersion, error.Code);
        }

        [Fact]
        public void ManyProblems_AreAllReportedInOneCall()
        {
            var errors = Errors(@"
                { ""id"": ""A"", ""type"": ""Die"", ""faces"": 3 },
                { ""id"": ""b"", ""type"": ""Die"", ""container"": ""zz"" }");

            Assert.Equal(3, errors.Count);
        }
    }
}
