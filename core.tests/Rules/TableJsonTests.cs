using System.Linq;
using Core.Rules;

namespace Core.Tests.Rules
{
    public class TableJsonTests
    {
        [Fact]
        public void Parse_AllTypesSample_IsValid()
        {
            var result = TableJson.Parse(SampleTables.AllTypes);

            Assert.True(result.Ok, string.Join("\n", result.Errors));
            var state = result.State!;
            Assert.Equal(13, state.Objects.Count);
            Assert.Equal(5, state.Zones.Count);
            Assert.Equal(2, state.Players.Seats.Count);
        }

        [Fact]
        public void Parse_ReadsConcreteTypesAndFields()
        {
            var state = TableJson.Parse(SampleTables.AllTypes).State!;

            var deck = Assert.IsType<Deck>(state.Objects.Single(o => o.Id == "deck_main"));
            Assert.Equal("Ace of hearts", deck.Cards[0].Name);
            Assert.Equal(14, (int)deck.Cards[0].Meta!["rank"]!);
            Assert.Equal("promo", deck.Cards[1].FaceAsset);
            Assert.False(deck.IsFaceUp);

            var model = Assert.IsType<Model>(state.Objects.Single(o => o.Id == "dragon_1"));
            Assert.Equal(ModelKind.Figurine, model.Kind);
            Assert.IsType<Figure>(model.States["2"]);

            Assert.IsType<InfiniteBag>(state.Objects.Single(o => o.Id == "coins"));
            Assert.Equal(new Vec3(1.5f, 1.5f, 1.5f), state.Objects.Single(o => o.Id == "bag_1").Transform.Scale);
            Assert.Equal(Vec3.One, state.Objects.Single(o => o.Id == "d20").Transform.Scale);
            Assert.True(state.Objects.Single(o => o.Id == "coin_template").TtsExtra!.Value<bool>("Sticky"));
            Assert.Equal("seat_2", Assert.IsType<HandZone>(state.Zones[1]).Owner);
        }

        [Fact]
        public void RoundTrip_SerializeParseSerialize_IsIdentical()
        {
            var first = TableJson.Parse(SampleTables.AllTypes).State!;
            var json = TableJson.Serialize(first);

            var second = TableJson.Parse(json);

            Assert.True(second.Ok, string.Join("\n", second.Errors));
            Assert.Equal(json, TableJson.Serialize(second.State!));
            Assert.Equal(TableJson.Hash(first), TableJson.Hash(second.State!));
        }

        [Fact]
        public void Serialize_WritesIdAndTypeFirst_AndOmitsDefaults()
        {
            var state = new TableState();
            state.Objects.Add(new Die { Id = "d6" });

            var json = TableJson.Serialize(state, indented: false);

            Assert.Contains(@"{""id"":""d6"",""type"":""Die"",""faces"":6}", json);
            Assert.DoesNotContain("tags", json);
            Assert.DoesNotContain("isFaceUp", json);
        }

        [Fact]
        public void Hash_ChangesWhenStateChanges()
        {
            var state = TableJson.Parse(SampleTables.AllTypes).State!;
            var before = TableJson.Hash(state);

            ((Die)state.Objects.Single(o => o.Id == "d20")).Value = 3;

            Assert.NotEqual(before, TableJson.Hash(state));
            Assert.StartsWith("sha256:", before);
        }

        [Fact]
        public void Parse_UnknownKeys_AreRejectedWithPathAndSuggestion()
        {
            var json = SampleTables.WithObjects(@"
                { ""id"": ""c1"", ""type"": ""Card"", ""nmae"": ""x"", ""faceAsset"": ""sheet"", ""backAsset"": ""back"",
                  ""transform"": { ""positon"": [0, 0, 0] } }");

            var result = TableJson.Parse(json);

            Assert.False(result.Ok);
            var name = result.Errors.Single(e => e.Path == "objects[0].nmae");
            Assert.Equal(ErrorCodes.UnknownKey, name.Code);
            Assert.Equal("Did you mean 'name'?", name.Suggestion);
            var position = result.Errors.Single(e => e.Path == "objects[0].transform.positon");
            Assert.Equal(ErrorCodes.UnknownKey, position.Code);
        }

        [Fact]
        public void Parse_KeysAreCaseSensitive()
        {
            var result = TableJson.Parse(@"{ ""SchemaVersion"": ""0.1"" }");

            var error = Assert.Single(result.Errors);
            Assert.Equal(ErrorCodes.UnknownKey, error.Code);
            Assert.Equal("Did you mean 'schemaVersion'?", error.Suggestion);
        }

        [Fact]
        public void Parse_KeyValidForOneTypeButNotAnother_IsRejected()
        {
            var json = SampleTables.WithObjects(@"{ ""id"": ""t1"", ""type"": ""Token"", ""faces"": 6 }");

            var error = Assert.Single(TableJson.Parse(json).Errors);

            Assert.Equal("objects[0].faces", error.Path);
            Assert.Equal(ErrorCodes.UnknownKey, error.Code);
        }

        [Fact]
        public void Parse_UnknownAndMissingTypes_AreReported()
        {
            var json = SampleTables.WithObjects(@"{ ""id"": ""a"", ""type"": ""Dice"" }, { ""id"": ""b"" }",
                zonesJson: @"{ ""id"": ""z"", ""type"": ""Hand"" }");

            var result = TableJson.Parse(json);

            var unknown = result.Errors.Single(e => e.Path == "objects[0].type");
            Assert.Equal(ErrorCodes.UnknownType, unknown.Code);
            Assert.Equal("Did you mean 'Die'?", unknown.Suggestion);
            Assert.Equal(ErrorCodes.MissingField, result.Errors.Single(e => e.Path == "objects[1]").Code);
            var zone = result.Errors.Single(e => e.Path == "zones[0].type");
            Assert.Equal(ErrorCodes.UnknownType, zone.Code);
            Assert.Equal("Did you mean 'HandZone'?", zone.Suggestion);
        }

        [Fact]
        public void Parse_WrongValueTypes_AreReportedWithPaths()
        {
            var json = SampleTables.WithObjects(@"
                { ""id"": ""d"", ""type"": ""Die"", ""locked"": ""yes"", ""transform"": { ""position"": [0, 0] } },
                { ""id"": ""t"", ""type"": ""Token"", ""shape"": ""triangle"" }");

            var result = TableJson.Parse(json);

            Assert.All(result.Errors, e => Assert.Equal(ErrorCodes.InvalidValue, e.Code));
            Assert.Contains(result.Errors, e => e.Path == "objects[0].locked");
            Assert.Contains(result.Errors, e => e.Path.StartsWith("objects[0].transform.position"));
            Assert.Contains(result.Errors, e => e.Path == "objects[1].shape");
        }

        [Fact]
        public void Parse_MalformedJson_ReportsInvalidJson()
        {
            var result = TableJson.Parse(@"{ ""objects"": [ }");

            Assert.Null(result.State);
            Assert.Equal(ErrorCodes.InvalidJson, Assert.Single(result.Errors).Code);
        }

        [Fact]
        public void Parse_AcceptsCommentsAndKeepsDateLikeStringsAsStrings()
        {
            var json = SampleTables.WithObjects(@"
                // jsonc comments are allowed, the docs use them
                { ""id"": ""c"", ""type"": ""Card"", ""faceAsset"": ""sheet"", ""backAsset"": ""back"", ""meta"": { ""printed"": ""2026-10-22"" } }");

            var result = TableJson.Parse(json);

            Assert.True(result.Ok, string.Join("\n", result.Errors));
            var card = (Card)result.State!.Objects[0];
            Assert.Equal("2026-10-22", (string)card.Meta!["printed"]!);
        }
    }
}
