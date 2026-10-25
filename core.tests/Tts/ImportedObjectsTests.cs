using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;
using Core.Scripting;
using Core.Tts;

namespace Core.Tests.Tts
{
    /// <summary>
    /// M7: imported objects as TTS has them. PDFs (T8.8) turn pages for everyone, from the menu or a script; model bags keep
    /// their image and textured models their tint; each state keeps its own snap points.
    /// </summary>
    public class ImportedObjectsTests
    {
        private const string Save = @"{ ""ObjectStates"": [
            { ""Name"": ""Custom_PDF"", ""GUID"": ""a1b2c3"", ""Nickname"": ""Rulebook"",
              ""Transform"": { ""posX"": 0, ""posY"": 1, ""posZ"": 0, ""rotX"": 0, ""rotY"": 180, ""rotZ"": 0, ""scaleX"": 7.0, ""scaleY"": 1, ""scaleZ"": 9.0 },
              ""CustomPDF"": { ""PDFUrl"": ""http://cloud-3.steamusercontent.com/ugc/1/ABC/"", ""PDFPassword"": """", ""PDFPage"": 2, ""PDFPageOffset"": -1 } } ] }";

        [Fact]
        public void CustomPdf_ImportsAsAPdf_WithItsPageAndFile()
        {
            var result = TtsImporter.Import(Save);
            var pdf = result.State.Objects.OfType<Pdf>().Single();
            Assert.Equal("Rulebook", pdf.Name);
            Assert.Equal(2, pdf.Page);
            Assert.Equal(-1, pdf.PageOffset);
            var asset = result.State.Assets.Single(a => a.Id == pdf.Asset);
            Assert.Equal(AssetType.Pdf, asset.Type);
            Assert.EndsWith(".pdf", asset.Path);
            Assert.StartsWith("pdfs/", asset.Path);
            Assert.Equal(AssetType.Pdf, result.Assets.Single().Type);
            // One TTS unit square, stretched by the save's scale: 21 x 27 cm.
            var footprint = Footprint.Of(pdf);
            Assert.Equal(0.21f, footprint.X, 3);
            Assert.Equal(0.27f, footprint.Y, 3);
            Assert.Equal(0, result.Placeholders);
            var json = TableJson.Serialize(result.State);
            Assert.True(TableJson.Parse(json).Ok, json);
        }

        [Fact]
        public void Pages_TurnForEveryone_AndScriptsUseTheBook()
        {
            var state = TtsImporter.Import(Save).State;
            state.Scripts[ScriptHost.GlobalName] = @"
                local book = getObjectFromGUID('a1b2c3').Book
                broadcastToAll(book.getPage() .. ' ' .. book.getPage(true))
                book.setPage(5, true)
                broadcastToAll(tostring(book:getPage()))";
            var engine = new TableEngine(state, 1);
            var said = new List<string>();
            var host = new ScriptHost(() => engine, c => engine.Execute(c));
            host.Broadcast += said.Add;
            host.Load(0);
            Assert.Equal(new[] { "2 1", "6" }, said);

            var turn = new SetPageCommand { ObjectId = "a1b2c3", Page = 0 };
            var result = engine.Execute(turn);
            Assert.True(result.Ok);
            Assert.Equal(0, ((Pdf)engine.Find("a1b2c3")!).Page);
            Assert.False(engine.Execute(new SetPageCommand { ObjectId = "a1b2c3", Page = -1 }).Ok);
            // Page numbers shown to people count from the PDF's offset (TTS PDFPageOffset): page 0 with offset -1 is "page 0".
            Assert.Contains(ActionLog.Describe(engine, turn, result, "seat_1", null), e => e.Text.Contains("turned Rulebook to page 0"));
        }

        [Fact]
        public void BadPages_AreRefusedWhenLoading()
        {
            var parsed = TableJson.Parse(@"{ ""schemaVersion"": ""0.1"", ""assets"": [ { ""id"": ""book"", ""type"": ""image"", ""path"": ""a.png"" } ],
                ""objects"": [ { ""id"": ""p"", ""type"": ""Pdf"", ""asset"": ""book"", ""page"": -2 } ] }");
            Assert.False(parsed.Ok);
            Assert.Contains(parsed.Errors, e => e.Path.EndsWith(".page"));
            Assert.Contains(parsed.Errors, e => e.Path.EndsWith(".asset"));
        }

        [Fact]
        public void ModelBags_KeepTheirImage()
        {
            var save = @"{ ""ObjectStates"": [ { ""Name"": ""Custom_Model_Bag"", ""GUID"": ""b00001"", ""ColorDiffuse"": { ""r"": 0.5, ""g"": 0.4, ""b"": 0.3 },
                ""CustomMesh"": { ""MeshURL"": ""http://x/bag.obj"", ""DiffuseURL"": ""http://x/bag.png"" } } ] }";
            var result = TtsImporter.Import(save);
            var bag = result.State.Objects.OfType<Bag>().Single();
            Assert.Equal(AssetType.Image, result.State.Assets.Single(a => a.Id == bag.DiffuseAsset).Type);
            Assert.Equal(new[] { 0.5f, 0.4f, 0.3f }, bag.Tint);
            Assert.True(TableJson.Parse(TableJson.Serialize(result.State)).Ok);

            // A textured custom model keeps its tint too (TTS multiplies the image by it); white stays no tint.
            var models = TtsImporter.Import(@"{ ""ObjectStates"": [
                { ""Name"": ""Custom_Model"", ""GUID"": ""m00001"", ""ColorDiffuse"": { ""r"": 0.49, ""g"": 0.39, ""b"": 0.29 }, ""CustomMesh"": { ""MeshURL"": ""http://x/hex.obj"", ""DiffuseURL"": ""http://x/white.png"" } },
                { ""Name"": ""Custom_Model"", ""GUID"": ""m00002"", ""ColorDiffuse"": { ""r"": 1.0, ""g"": 1.0, ""b"": 1.0 }, ""CustomMesh"": { ""MeshURL"": ""http://x/hex.obj"", ""DiffuseURL"": ""http://x/white.png"" } } ] }").State;
            Assert.Equal(new[] { 0.49f, 0.39f, 0.29f }, models.Objects.Single(o => o.Id == "m00001").Tint);
            Assert.Null(models.Objects.Single(o => o.Id == "m00002").Tint);
        }

        [Fact]
        public void EachState_KeepsItsOwnSnapPoints()
        {
            var save = @"{ ""ObjectStates"": [ { ""Name"": ""Custom_Tile"", ""GUID"": ""a00001"", ""CustomImage"": { ""ImageURL"": ""http://x/a.png"" },
                ""AttachedSnapPoints"": [ { ""Position"": { ""x"": 0.1, ""y"": 0, ""z"": 0 } } ],
                ""States"": { ""2"": { ""Name"": ""Custom_Tile"", ""GUID"": ""b00001"", ""CustomImage"": { ""ImageURL"": ""http://x/b.png"" },
                    ""AttachedSnapPoints"": [ { ""Position"": { ""x"": -0.1, ""y"": 0, ""z"": 0 } }, { ""Position"": { ""x"": 0, ""y"": 0, ""z"": 0.2 } } ] } } } ] }";
            var result = TtsImporter.Import(save);
            var snaps = result.State.Zones.OfType<SnapPoint>().ToList();
            Assert.Equal(1, snaps.Count(z => z.Parent == "a00001"));
            Assert.Equal(2, snaps.Count(z => z.Parent == "b00001"));
            Assert.DoesNotContain(result.Notes, n => n.Contains("snap points"));
            var parsed = TableJson.Parse(TableJson.Serialize(result.State));
            Assert.True(parsed.Ok, string.Join("; ", parsed.Errors));

            // Switching shows the other state with its own points; the first state's wait for it.
            var engine = new TableEngine(parsed.State!, 1);
            Assert.True(engine.Execute(new SetStateCommand { ObjectId = "a00001", State = "2" }).Ok);
            Assert.Equal(1, engine.State.Zones.Count(z => z.Parent == "a00001"));
            Assert.Equal(2, engine.State.Zones.Count(z => z.Parent == "b00001"));
            Assert.NotNull(engine.Find("b00001"));
            Assert.True(TableJson.Parse(TableJson.Serialize(engine.State)).Ok);
        }
    }
}
