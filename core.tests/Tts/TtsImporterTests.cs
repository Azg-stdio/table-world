using System;
using System.IO;
using System.Linq;
using Core.Rules;
using Core.Tts;
using Xunit.Abstractions;

namespace Core.Tests.Tts
{
    /// <summary>M7 T8.1: a TTS save becomes our table, valid, with GUIDs as ids and the assets it needs listed.</summary>
    public class TtsImporterTests
    {
        private readonly ITestOutputHelper _output;

        public TtsImporterTests(ITestOutputHelper output) => _output = output;

        private const string Save = @"{
  ""SaveName"": ""Sample"",
  ""LuaScript"": ""function onLoad() end"",
  ""XmlUI"": ""<Button id='go' onClick='go'>Go</Button>"",
  ""CustomUIAssets"": [ { ""Name"": ""Add Character"", ""URL"": ""http://cloud-3.steamusercontent.com/ugc/1/ABC/"" } ],
  ""Hands"": { ""HandTransforms"": [ { ""Color"": ""Red"", ""Transform"": { ""posX"": 0, ""posY"": 4, ""posZ"": -20, ""rotY"": 0, ""scaleX"": 12, ""scaleY"": 5, ""scaleZ"": 4 } } ] },
  ""SnapPoints"": [ { ""Position"": { ""x"": 2, ""y"": 0.96, ""z"": 0 } } ],
  ""TabStates"": { ""0"": { ""title"": ""Rules"", ""body"": ""Play nice"", ""color"": ""Grey"" } },
  ""ObjectStates"": [
    { ""GUID"": ""a1b2c3"", ""Name"": ""Card"", ""Nickname"": ""Ace"", ""CardID"": 1203, ""Transform"": { ""posX"": 10, ""posY"": 1.06, ""posZ"": 0, ""rotZ"": 180, ""scaleX"": 1, ""scaleY"": 1, ""scaleZ"": 1 },
      ""CustomDeck"": { ""12"": { ""FaceURL"": ""http://cloud-3.steamusercontent.com/ugc/2/FACE/"", ""BackURL"": ""http://cloud-3.steamusercontent.com/ugc/2/BACK/"", ""NumWidth"": 10, ""NumHeight"": 7 } },
      ""LuaScript"": ""function onDrop() end"", ""XmlUI"": ""<Text id='t'>x</Text>"", ""AttachedSnapPoints"": [ { ""Position"": { ""x"": 0, ""y"": 0.1, ""z"": 0 } } ] },
    { ""GUID"": ""dd0001"", ""Name"": ""Deck"", ""Transform"": { ""posX"": 0, ""posY"": 1, ""posZ"": 0 }, ""DeckIDs"": [ 1201, 1202 ],
      ""ContainedObjects"": [
        { ""GUID"": ""c00001"", ""Name"": ""Card"", ""CardID"": 1201, ""Nickname"": ""Top"", ""CustomDeck"": { ""12"": { ""FaceURL"": ""http://cloud-3.steamusercontent.com/ugc/2/FACE/"", ""BackURL"": ""http://cloud-3.steamusercontent.com/ugc/2/BACK/"", ""NumWidth"": 10, ""NumHeight"": 7 } } },
        { ""GUID"": ""c00002"", ""Name"": ""Card"", ""CardID"": 1202, ""CustomDeck"": { ""12"": { ""FaceURL"": ""http://cloud-3.steamusercontent.com/ugc/2/FACE/"", ""BackURL"": ""http://cloud-3.steamusercontent.com/ugc/2/BACK/"", ""NumWidth"": 10, ""NumHeight"": 7 } } } ] },
    { ""GUID"": ""b00001"", ""Name"": ""Custom_Model_Bag"", ""CustomMesh"": { ""MeshURL"": ""http://x/bag.obj"" }, ""ContainedObjects"": [
        { ""GUID"": ""a1b2c3"", ""Name"": ""Custom_Assetbundle"", ""Nickname"": ""Staraptor"", ""CustomAssetbundle"": { ""AssetbundleURL"": ""http://cloud-3.steamusercontent.com/ugc/3/BUNDLE/"" } } ] },
    { ""GUID"": ""z00001"", ""Name"": ""ScriptingTrigger"", ""Transform"": { ""posX"": 0, ""posY"": 2, ""posZ"": 0, ""scaleX"": 4, ""scaleY"": 2, ""scaleZ"": 4 } },
    { ""GUID"": ""t00001"", ""Name"": ""Custom_Tile"", ""CustomImage"": { ""ImageURL"": ""http://x/tile.jpg"", ""CustomTile"": { ""Type"": 1, ""Thickness"": 0.2, ""Stackable"": true } }, ""Number"": 3 },
    { ""GUID"": ""s00001"", ""Name"": ""Die_6"", ""States"": { ""2"": { ""GUID"": ""s00002"", ""Name"": ""Die_20"" } } },
    { ""GUID"": ""p00001"", ""Name"": ""Tablet"", ""Nickname"": ""Rulebook"", ""LuaScript"": ""x = 1"" } ] }";

        [Fact]
        public void MusicPlayer_BecomesTheTablesMusic_NotPlaying()
        {
            const string save = @"{ ""MusicPlayer"": { ""RepeatSong"": true, ""PlaylistEntry"": 1, ""CurrentAudioTitle"": ""Death"", ""CurrentAudioURL"": ""http://cloud-3.steamusercontent.com/ugc/2/B/"",
                ""AudioLibrary"": [ { ""Item1"": ""http://cloud-3.steamusercontent.com/ugc/1/A/"", ""Item2"": ""Awakening"" }, { ""Item1"": ""http://cloud-3.steamusercontent.com/ugc/2/B/"", ""Item2"": ""Death"" } ] },
                ""ObjectStates"": [] }";
            var music = TtsImporter.Import(save).State.Music!;
            Assert.Equal(new[] { "Awakening", "Death" }, music.Playlist.Select(t => t.Title));
            Assert.Equal("Death", music.Current!.Title);
            Assert.Equal(1, music.Index);
            Assert.True(music.Repeat);
            Assert.False(music.Playing);
        }

        [Fact]
        public void StretchedTiles_KeepTheirDepth_AndTakeTheImagesProportionsInWidth()
        {
            // TTS: depth 2 units × scaleZ, width × the image's aspect, for tall images too (Pokémon Sinnoh's portrait
            // panels sit edge to edge with their landscape neighbours only that way).
            const string save = @"{ ""ObjectStates"": [
                { ""GUID"": ""wide01"", ""Name"": ""Custom_Tile"", ""CustomImage"": { ""ImageURL"": ""http://x/wide.png"", ""CustomTile"": { ""Stretch"": true } } },
                { ""GUID"": ""tall01"", ""Name"": ""Custom_Tile"", ""CustomImage"": { ""ImageURL"": ""http://x/tall.png"", ""CustomTile"": { ""Stretch"": true } } } ] }";
            var state = TtsImporter.Import(save, url => url.Contains("wide") ? (2000, 1000) : (1000, 2000)).State;
            var wide = ((Tile)state.Objects.Single(o => o.Id == "wide01")).Size!.Value;
            var tall = ((Tile)state.Objects.Single(o => o.Id == "tall01")).Size!.Value;
            Assert.Equal(0.12f, wide.X, 4);
            Assert.Equal(0.06f, wide.Y, 4);
            Assert.Equal(0.03f, tall.X, 4);
            Assert.Equal(0.06f, tall.Y, 4);
        }

        [Fact]
        public void Save_BecomesAValidTable_WithGuidsScriptsUiAndAssets()
        {
            var result = TtsImporter.Import(Save);
            var parsed = TableJson.Parse(TableJson.Serialize(result.State));
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            var state = parsed.State!;

            var card = (Card)state.Objects.Single(o => o.Id == "a1b2c3");
            Assert.Equal("Ace", card.Name);
            Assert.Equal(3, card.FaceIndex);
            Assert.Equal(0.3f, card.Transform.Position.X, 4);
            Assert.Equal(0.003f, card.Transform.Position.Y, 4);
            Assert.Equal(180f, card.Transform.Rotation.Z);
            Assert.Equal(10, state.Assets.Single(a => a.Id == card.FaceAsset).Columns);
            Assert.NotNull(card.Script);
            Assert.Equal("t", card.ScriptUi.Single().Id);
            Assert.Contains(state.Zones.OfType<SnapPoint>(), z => z.Parent == "a1b2c3");

            var deck = (Deck)state.Objects.Single(o => o.Id == "dd0001");
            Assert.Equal(new[] { "c00001", "c00002" }, deck.Cards.Select(c => c.Id));
            Assert.Equal("Top", deck.Cards[0].Name);
            Assert.Equal(1, deck.Cards[0].FaceIndex);

            var bundle = (Model)state.Objects.Single(o => o.Name == "Staraptor");
            Assert.Equal("a1b2c3_2", bundle.Id);
            Assert.Equal("b00001", bundle.Container);
            Assert.Equal(AssetType.AssetBundle, state.Assets.Single(a => a.Id == bundle.Asset).Type);

            var tile = (Tile)state.Objects.Single(o => o.Id == "t00001");
            Assert.Equal(TileShape.Hex, tile.Shape);
            Assert.Equal(3, tile.Count);
            Assert.Equal(20, ((Die)state.Objects.Single(o => o.Id == "s00001").States["2"]).Faces);

            var placeholder = (Model)state.Objects.Single(o => o.Id == "p00001");
            Assert.Equal("Rulebook", placeholder.Name);
            Assert.Equal("x = 1", placeholder.Script);
            Assert.Equal(1, result.Placeholders);

            var zone = state.Zones.OfType<ScriptingZone>().Single();
            Assert.Equal(0.12f, zone.Size.X, 4);
            Assert.Equal("red", state.Players.Seats.Single().Id);
            Assert.Equal(state.Zones.OfType<HandZone>().Single().Id, state.Players.Seats.Single().HandZone);
            Assert.Equal("Rules", state.Notebook.Single().Title);
            Assert.Equal("go", state.ScriptUi.Single().Id);
            Assert.Equal("ui_add_character", TtsImporter.UiAssetId("Add Character"));
            Assert.Contains(state.Assets, a => a.Id == "ui_add_character");
            var face = result.Assets.Single(a => a.Id == card.FaceAsset);
            Assert.Equal("httpcloud3steamusercontentcomugc2FACE", face.CacheKey);
            Assert.Equal("images/httpcloud3steamusercontentcomugc2FACE.png", face.Path);
        }

        [Fact]
        public void Package_ListsSaves_CopiesCachedAssets_AndListsTheRest()
        {
            var tts = Path.Combine(Path.GetTempPath(), "tw-tts-" + Guid.NewGuid().ToString("N"));
            var package = Path.Combine(Path.GetTempPath(), "tw-pkg-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(tts, "Mods", "Workshop"));
                Directory.CreateDirectory(Path.Combine(tts, "Mods", "Images"));
                File.WriteAllText(Path.Combine(tts, "Mods", "Workshop", "123.json"), Save);
                File.WriteAllText(Path.Combine(tts, "Mods", "Workshop", "WorkshopFileInfos.json"), "[]");
                // TTS cached the card faces as a .jpg; the package keeps them under the name the table uses.
                File.WriteAllBytes(Path.Combine(tts, "Mods", "Images", "httpcloud3steamusercontentcomugc2FACE.jpg"), new byte[] { 1, 2, 3 });

                var saves = TtsPackage.Find(tts);
                Assert.Equal("Sample", saves.Single().Name);
                var report = TtsPackage.Write(saves[0].File, tts, package);
                Assert.Equal(1, report.Copied);
                Assert.True(report.Missing.Count > 3);
                Assert.True(File.Exists(Path.Combine(package, "images", "httpcloud3steamusercontentcomugc2FACE.png")));
                var parsed = TableJson.Parse(File.ReadAllText(report.TableFile));
                Assert.True(parsed.Ok, string.Join("; ", parsed.Errors));
                Assert.Contains("Not in the cache yet: http://x/tile.jpg", File.ReadAllText(Path.Combine(package, "import-notes.txt")));
                Assert.Equal("Sample (TTS)", TtsPackage.FolderName("Sample"));
                Assert.Equal("ab (TTS)", TtsPackage.FolderName("a:b?"));
            }
            finally
            {
                if (Directory.Exists(tts)) Directory.Delete(tts, true);
                if (Directory.Exists(package)) Directory.Delete(package, true);
            }
        }

        /// <summary>The Tabletop Simulator saves on this machine (TW_TTS_DIR) import into valid tables. Never in the repository.</summary>
        [Fact]
        public void LocalMods_ImportIntoValidTables()
        {
            var root = Environment.GetEnvironmentVariable("TW_TTS_DIR");
            if (string.IsNullOrEmpty(root))
                return;
            foreach (var file in Directory.GetFiles(Path.Combine(root, "Mods", "Workshop"), "*.json"))
            {
                var text = File.ReadAllText(file);
                if (!text.TrimStart().StartsWith("{", StringComparison.Ordinal) || !text.Contains("\"ObjectStates\""))
                    continue;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var result = TtsImporter.Import(text);
                var parsed = TableJson.Parse(TableJson.Serialize(result.State));
                _output.WriteLine($"{result.State.Game?.Name}: {result.State.Objects.Count} objects, {result.State.Zones.Count} zones, {result.Assets.Count} assets, " +
                                  $"{result.Placeholders} placeholders, {parsed.Errors.Count} validation errors, {watch.ElapsedMilliseconds} ms");
                foreach (var kind in parsed.Errors.GroupBy(e => e.Code + " " + System.Text.RegularExpressions.Regex.Replace(e.Path, @"\[\d+\]", "[]")).OrderByDescending(g => g.Count()).Take(8))
                    _output.WriteLine($"    {kind.Count()}x error {kind.Key}: {kind.First().Message}");
                foreach (var group in result.Notes.GroupBy(n => n.Split(' ')[0]).OrderByDescending(g => g.Count()).Take(6))
                    _output.WriteLine($"    {group.Count()}x {group.First()}");
                Assert.True(parsed.Ok, $"{file}: " + string.Join("\n", parsed.Errors.Take(10)));
            }
        }
    }
}
