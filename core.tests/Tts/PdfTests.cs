using System.Collections.Generic;
using System.Linq;
using Core.Engine;
using Core.Rules;
using Core.Scripting;
using Core.Tts;

namespace Core.Tests.Tts
{
    /// <summary>M7 T8.8: TTS <c>Custom_PDF</c> imports as a PDF on the table; pages turn for everyone, from the menu or a script.</summary>
    public class PdfTests
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
    }
}
