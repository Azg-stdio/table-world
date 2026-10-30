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

                // The package records the import: source, kind, version and what is still missing.
                var info = TtsPackage.ReadInfo(package)!;
                Assert.Equal(TtsImporter.Version, info.Importer);
                Assert.Equal(TtsPackage.SaveKind.Workshop, info.Kind);
                Assert.Equal(Path.GetFullPath(saves[0].File), info.Source);
                Assert.Equal(report.Missing.Count, info.MissingNow(package).Count);
                var first = info.Missing[0];
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(package, first.Path))!);
                File.WriteAllBytes(Path.Combine(package, first.Path), new byte[] { 1 });
                Assert.Equal(report.Missing.Count - 1, info.MissingNow(package).Count); // a downloaded file is no longer missing
                Assert.False(TtsPackage.Outdated(package));
                Assert.Equal(info.Source, TtsPackage.SourceOf(package, tts));

                // An import from before import.json is outdated, and its source is found from its notes.
                File.Delete(Path.Combine(package, TtsPackage.InfoFile));
                Assert.True(TtsPackage.Outdated(package));
                Assert.Equal(Path.GetFullPath(saves[0].File), Path.GetFullPath(TtsPackage.SourceOf(package, tts)!));
            }
            finally
            {
                if (Directory.Exists(tts)) Directory.Delete(tts, true);
                if (Directory.Exists(package)) Directory.Delete(package, true);
            }
        }

        [Fact]
        public void OldDecks_WithOnlyDeckIds_GetTheirCardsFromTheDecksSheet()
        {
            // Saves from 2015 and before (Love Letter, Avalon) list no contained cards: DeckIDs and the deck's CustomDeck only.
            const string save = @"{ ""ObjectStates"": [
                { ""GUID"": ""dd0002"", ""Name"": ""Deck"", ""SidewaysCard"": false, ""DeckIDs"": [ 114, 103, 100 ],
                  ""CustomDeck"": { ""1"": { ""FaceURL"": ""http://i.imgur.com/FACE.jpg"", ""BackURL"": ""http://i.imgur.com/BACK.jpg"", ""NumWidth"": 10, ""NumHeight"": 7 } } } ] }";
            var result = TtsImporter.Import(save);
            var parsed = TableJson.Parse(TableJson.Serialize(result.State));
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            var deck = (Deck)parsed.State!.Objects.Single(o => o.Id == "dd0002");
            Assert.Equal(new[] { 14, 3, 0 }, deck.Cards.Select(c => c.FaceIndex));
            Assert.Equal(3, deck.Cards.Select(c => c.Id).Distinct().Count());
            Assert.Equal("http://i.imgur.com/FACE.jpg", parsed.State.Assets.Single(a => a.Id == deck.FaceAsset).Url);
            Assert.Equal("http://i.imgur.com/BACK.jpg", parsed.State.Assets.Single(a => a.Id == deck.BackAsset).Url);
            Assert.Contains(result.Assets, a => a.Url == "http://i.imgur.com/FACE.jpg");
        }

        [Fact]
        public void SavesWithoutHands_SeatTtsColours_RoundATableThatFitsThem_AndNamesSayTts()
        {
            // Old saves keep no Hands; TTS still seats players at its colours. Without seats the table would not validate.
            const string save = @"{ ""SaveName"": ""Love Letter"", ""ObjectStates"": [ { ""GUID"": ""bag001"", ""Name"": ""Bag"" } ] }";
            var parsed = TableJson.Parse(TableJson.Serialize(TtsImporter.Import(save).State));
            Assert.True(parsed.Ok, string.Join("\n", parsed.Errors));
            var state = parsed.State!;
            Assert.Equal("Love Letter (TTS)", state.Game!.Name);
            Assert.Equal("Love Letter (TTS)", TtsImporter.ImportedName("Love Letter (TTS)"));
            Assert.Equal(new[] { "white", "brown", "red", "orange", "yellow", "green", "teal", "blue", "purple", "pink" }, state.Players.Seats.Select(s => s.Id));
            Assert.All(state.Players.Seats, s => Assert.Contains(state.Zones.OfType<HandZone>(), z => z.Id == s.HandZone && z.Owner == s.Id));
            Assert.Equal(10, state.Game.MaxPlayers);
            var table = state.Environment!.Table.Size!.Value;
            Assert.All(state.Zones.OfType<HandZone>(), z =>
                Assert.True(Math.Abs(z.Transform.Position.X) < table.X / 2 && Math.Abs(z.Transform.Position.Z) < table.Y / 2, $"{z.Id} off the table"));

            // A save with hands keeps exactly its own seats.
            Assert.Equal("red", TtsImporter.Import(Save).State.Players.Seats.Single().Id);
        }

        [Fact]
        public void GoStones_AndBackgammonPieces_AreBuiltInPieces_InTheirColour()
        {
            const string save = @"{ ""ObjectStates"": [
                { ""GUID"": ""go0001"", ""Name"": ""go_game_piece_black"", ""ColorDiffuse"": { ""r"": 0, ""g"": 0, ""b"": 0 } },
                { ""GUID"": ""go0002"", ""Name"": ""go_game_piece_white"", ""ColorDiffuse"": { ""r"": 1, ""g"": 1, ""b"": 1 } },
                { ""GUID"": ""bg0001"", ""Name"": ""backgammon_piece_brown"", ""ColorDiffuse"": { ""r"": 0.4, ""g"": 0.2, ""b"": 0.1 } } ] }";
            var result = TtsImporter.Import(save);
            var state = result.State;
            string PathOf(string id) => state.Assets.Single(a => a.Id == ((Model)state.Objects.Single(o => o.Id == id)).Asset).Path;
            Assert.Equal("builtin:go_stone", PathOf("go0001"));
            Assert.Equal("builtin:go_stone", PathOf("go0002"));
            Assert.Equal("builtin:checker", PathOf("bg0001"));
            Assert.Equal(new[] { 0f, 0f, 0f }, ((Model)state.Objects.Single(o => o.Id == "go0001")).Tint);
            Assert.Equal(0, result.Placeholders);
        }

        [Fact]
        public void GoneFiles_AreNotOfferedAgain_AndStayGoneAcrossReimports()
        {
            var tts = Path.Combine(Path.GetTempPath(), "tw-tts-" + Guid.NewGuid().ToString("N"));
            var package = Path.Combine(Path.GetTempPath(), "tw-pkg-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(tts, "Mods", "Workshop"));
                var file = Path.Combine(tts, "Mods", "Workshop", "123.json");
                File.WriteAllText(file, Save);
                TtsPackage.Write(file, tts, package);
                var missing = TtsPackage.ReadInfo(package)!.MissingNow(package);
                var dead = missing[0].Url;

                TtsPackage.MarkGone(package, new[] { (dead, "the server answered 403"), (dead, "the server answered 403") });
                var info = TtsPackage.ReadInfo(package)!;
                Assert.Equal(dead, info.Gone.Single().Url);
                Assert.Equal(missing.Count - 1, info.MissingNow(package).Count);
                Assert.DoesNotContain(info.MissingNow(package), m => m.Url == dead);
                var notes = File.ReadAllText(Path.Combine(package, "import-notes.txt"));
                Assert.Contains($"No longer online (the server answered 403): {dead}", notes);

                // Re-importing keeps what the download learned.
                TtsPackage.Write(file, tts, package);
                Assert.Equal(dead, TtsPackage.ReadInfo(package)!.Gone.Single().Url);
                notes = File.ReadAllText(Path.Combine(package, "import-notes.txt"));
                Assert.Contains($"No longer online (the server answered 403): {dead}", notes);
                Assert.DoesNotContain($"Not in the cache yet: {dead}", notes);
            }
            finally
            {
                if (Directory.Exists(tts)) Directory.Delete(tts, true);
                if (Directory.Exists(package)) Directory.Delete(package, true);
            }
        }

        [Fact]
        public void Saves_SayWhereTheyComeFrom_AndImportIntoTheirOwnFolders()
        {
            var tts = Path.Combine(Path.GetTempPath(), "tw-tts-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(tts, "Mods", "Workshop"));
                Directory.CreateDirectory(Path.Combine(tts, "Saves"));
                File.WriteAllText(Path.Combine(tts, "Mods", "Workshop", "123.json"), Save);
                File.WriteAllText(Path.Combine(tts, "Saves", "TS_AutoSave_2.json"), Save);
                File.WriteAllText(Path.Combine(tts, "Saves", "TS_Save_7.json"), Save);
                File.SetLastWriteTime(Path.Combine(tts, "Saves", "TS_AutoSave_2.json"), new DateTime(2026, 3, 28, 20, 26, 0));

                var saves = TtsPackage.Find(tts);
                Assert.Equal(new[] { TtsPackage.SaveKind.Workshop, TtsPackage.SaveKind.Save, TtsPackage.SaveKind.Autosave }, saves.Select(s => s.Kind));
                Assert.Equal("Sample · Workshop", saves[0].Label);
                Assert.StartsWith("Sample · TTS autosave, 28 ", saves[2].Label);
                Assert.EndsWith(" 20:26", saves[2].Label);
                Assert.Equal(3, saves.Select(s => s.FolderName).Distinct().Count());
                Assert.Equal("Sample (TTS)", saves[0].FolderName);
            }
            finally
            {
                if (Directory.Exists(tts)) Directory.Delete(tts, true);
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
