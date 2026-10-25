using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Core.Engine;
using Core.Rules;
using NUnit.Framework;
using TableWorld.View;
using TableWorld.View.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>
    /// M7 T8.8: a PDF on the table shows its page (rendered by PDFium), turns for everyone, and reads page by page in the
    /// reader without changing the table. The PDF is written here, two pages: page 1 white, page 2 filled red.
    /// </summary>
    public class PdfTests
    {
        private string _package;

        /// <summary>A minimal PDF: each page's content stream fills it with one colour.</summary>
        public static byte[] TwoPagePdf()
        {
            var contents = new[] { "1 1 1 rg 0 0 200 300 re f", "0.9 0.1 0.1 rg 0 0 200 300 re f" };
            var objects = new System.Collections.Generic.List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 300] /Contents 4 0 R >>",
                $"<< /Length {contents[0].Length} >>\nstream\n{contents[0]}\nendstream",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 300] /Contents 6 0 R >>",
                $"<< /Length {contents[1].Length} >>\nstream\n{contents[1]}\nendstream",
            };
            var pdf = new StringBuilder("%PDF-1.4\n");
            var offsets = new int[objects.Count];
            for (var i = 0; i < objects.Count; i++)
            {
                offsets[i] = pdf.Length;
                pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }
            var xref = pdf.Length;
            pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
            foreach (var offset in offsets)
                pdf.Append($"{offset:D10} 00000 n \n");
            pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            return Encoding.ASCII.GetBytes(pdf.ToString());
        }

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            _package = Path.Combine(Application.temporaryCachePath, "pdf-test");
            Directory.CreateDirectory(Path.Combine(_package, "pdfs"));
            File.WriteAllBytes(Path.Combine(_package, "pdfs", "book.pdf"), TwoPagePdf());
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            yield return new WaitForSeconds(0.2f);
        }

        [UnityTearDown]
        public IEnumerator Clean()
        {
            Object.FindAnyObjectByType<HudController>()?.Reader?.Hide();
            yield return null;
        }

        private static Color TopColor(ObjectView view)
        {
            var texture = (Texture2D)view.Renderer.sharedMaterials[0].GetTexture("_BaseMap");
            Assert.IsNotNull(texture, "the page is drawn on the PDF");
            // The rendered page is not readable; copy its middle pixel through a render texture.
            var target = RenderTexture.GetTemporary(8, 8);
            Graphics.Blit(texture, target);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var probe = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            probe.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
            probe.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            var color = probe.GetPixel(4, 4);
            Object.Destroy(probe);
            return color;
        }

        [UnityTest]
        public IEnumerator Pdf_ShowsItsPage_TurnsForEveryone_AndReads()
        {
            var table = Object.FindAnyObjectByType<TableView>();
            var json = @"{ ""schemaVersion"": ""0.1"",
                ""assets"": [ { ""id"": ""book"", ""type"": ""pdf"", ""path"": ""pdfs/book.pdf"" } ],
                ""objects"": [ { ""id"": ""rules"", ""type"": ""Pdf"", ""name"": ""Rules"", ""asset"": ""book"", ""size"": [0.2, 0.3] } ] }";
            Assert.IsTrue(table.Load(json, _package));
            yield return null;
            Assert.AreEqual(2, table.Assets.PdfDocument("book").PageCount, "PDFium opened the file");
            var view = table.ViewOf("rules");
            var white = TopColor(view);
            Assert.Greater(white.r, 0.9f);
            Assert.Greater(white.g, 0.9f);

            Assert.IsTrue(table.Execute(new SetPageCommand { ObjectId = "rules", Page = 1 }).Ok);
            yield return null;
            var red = TopColor(table.ViewOf("rules"));
            Assert.Greater(red.r, 0.8f, "page 2 is red");
            Assert.Less(red.g, 0.3f);

            // Past the last page shows the last page.
            Assert.IsTrue(table.Execute(new SetPageCommand { ObjectId = "rules", Page = 9 }).Ok);
            yield return null;
            Assert.Greater(TopColor(table.ViewOf("rules")).r, 0.8f);

            var hud = Object.FindAnyObjectByType<HudController>();
            hud.Reader.Show(table.ViewOf("rules"));
            Assert.IsTrue(hud.Reader.Visible);
            Assert.IsTrue(hud.TypingInUi(), "the table does not get keys while reading");
            hud.Reader.Turn(-1);
            Assert.AreEqual(0, hud.Reader.Page);
            Assert.AreEqual(9, ((Pdf)table.Engine.Find("rules")).Page, "reading does not turn the table's page");
            hud.PressEscape();
            Assert.IsFalse(hud.Reader.Visible);
        }
    }
}
