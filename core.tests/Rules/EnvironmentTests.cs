using System.Linq;
using Core.Rules;

namespace Core.Tests.Rules
{
    /// <summary>The <c>environment</c> block: the room a game sets around its table.</summary>
    public class EnvironmentTests
    {
        private static string Table(string environment) => $@"{{
  ""schemaVersion"": ""0.1"",
  ""assets"": [
    {{ ""id"": ""felt"", ""type"": ""image"", ""path"": ""img/felt.png"" }},
    {{ ""id"": ""room"", ""type"": ""model"", ""path"": ""models/room.glb"" }}
  ],
  ""environment"": {environment}
}}";

        [Fact]
        public void FullEnvironment_ParsesAndRoundTrips()
        {
            var json = Table(@"{
    ""room"": ""none"",
    ""roomAsset"": ""room"",
    ""wallColor"": [0.1, 0.1, 0.12],
    ""floorColor"": [0.2, 0.15, 0.1],
    ""lighting"": { ""ambientIntensity"": 0.3, ""ambientColor"": [1, 0.9, 0.8], ""lampIntensity"": 2.5, ""lampColor"": [1, 0.85, 0.6], ""tableLightIntensity"": 2, ""tableLightColor"": [1, 1, 1] },
    ""table"": { ""feltColor"": [0.1, 0.3, 0.2], ""feltAsset"": ""felt"", ""woodColor"": [0.4, 0.25, 0.15] }
  }");
            var parsed = TableJson.Parse(json);
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            var env = parsed.State!.Environment!;
            Assert.Equal(RoomPreset.None, env.Room);
            Assert.Equal("room", env.RoomAsset);
            Assert.Equal(2.5f, env.Lighting.LampIntensity);
            Assert.Equal(2f, env.Lighting.TableLightIntensity);
            Assert.Equal("felt", env.Table.FeltAsset);

            var again = TableJson.Parse(TableJson.Serialize(parsed.State));
            Assert.True(again.Ok);
            Assert.Equal(TableJson.Hash(parsed.State), TableJson.Hash(again.State!));
        }

        [Fact]
        public void NoEnvironment_IsTheDefaultRoom()
        {
            var parsed = TableJson.Parse(@"{ ""schemaVersion"": ""0.1"" }");
            Assert.True(parsed.Ok);
            Assert.Null(parsed.State!.Environment);
            Assert.Equal(RoomPreset.GameNight, new EnvironmentDef().Room);
        }

        [Fact]
        public void BadValues_AreReportedWithPaths()
        {
            var errors = TableJson.Parse(Table(@"{
    ""wallColor"": [0.1, 0.1],
    ""floorColor"": [2, 0, 0],
    ""lighting"": { ""lampIntensity"": -1 },
    ""table"": { ""feltAsset"": ""room"", ""woodColor"": [0, 0, 0] },
    ""roomAsset"": ""nope""
  }")).Errors;
            Assert.Contains(errors, e => e.Path == "environment.wallColor" && e.Code == ErrorCodes.InvalidValue);
            Assert.Contains(errors, e => e.Path == "environment.floorColor" && e.Code == ErrorCodes.InvalidValue);
            Assert.Contains(errors, e => e.Path == "environment.lighting.lampIntensity" && e.Code == ErrorCodes.InvalidValue);
            Assert.Contains(errors, e => e.Path == "environment.table.feltAsset" && e.Code == ErrorCodes.WrongAssetType);
            Assert.Contains(errors, e => e.Path == "environment.roomAsset" && e.Code == ErrorCodes.UnknownAsset);
            Assert.Equal(5, errors.Count);
        }

        [Fact]
        public void UnknownKeys_AreRejected()
        {
            var errors = TableJson.Parse(Table(@"{ ""lighting"": { ""lampIntensty"": 1 } }")).Errors;
            Assert.Contains(errors, e => e.Code == ErrorCodes.UnknownKey);
        }
    }
}
