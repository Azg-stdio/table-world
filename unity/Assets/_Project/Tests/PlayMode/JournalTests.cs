using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Core.Engine;
using Core.Logging;
using NUnit.Framework;
using TableWorld.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>Every PlayMode test writes its journals to a temporary folder: a test run never prunes a player's.</summary>
    [SetUpFixture]
    public class JournalFolderForTests
    {
        [OneTimeSetUp]
        public void UseATemporaryFolder() => SessionJournal.FolderOverride = Path.Combine(Application.temporaryCachePath, "test-journals");

        [OneTimeTearDown]
        public void Restore() => SessionJournal.FolderOverride = null;
    }

    /// <summary>The session journal in the game (docs/dev/logging.md): a table's story on disk, and a problem report holding it.</summary>
    public class JournalTests
    {
        private TableView _table;

        [UnitySetUp]
        public IEnumerator LoadTable()
        {
            yield return SceneManager.LoadSceneAsync("Table");
            yield return null;
            _table = Object.FindAnyObjectByType<TableView>();
            Object.FindAnyObjectByType<TableInteraction>().IgnoreDevices = true;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Commands_Refusals_AndWarnings_GoIntoThisSessionsJournal()
        {
            Assert.IsTrue(SessionJournal.Folder.Contains("test-journals"), "tests never write into a player's journals");
            var journal = SessionJournal.Current;
            Assert.IsNotNull(journal, "a loaded table has a journal");
            Assert.IsTrue(_table.Execute(new FlipCommand { ObjectId = "ace_spades", Actor = "seat_south" }).Ok);
            Assert.IsFalse(_table.Execute(new FlipCommand { ObjectId = "nothing_here", Actor = "seat_south" }).Ok);
            Debug.LogWarning("journal test warning");
            yield return null;

            var lines = Journal.ReadFile(journal.Path, max: 500);
            Assert.IsTrue(lines.Any(l => (string)l["kind"] == "table"), "the table it started with");
            Assert.IsTrue(lines.Any(l => (string)l["kind"] == "command" && (string)l["command"] == "Flip" && (string)l["seat"] == "seat_south"));
            var refused = lines.Last(l => (string)l["kind"] == "refused");
            Assert.AreEqual("Flip", (string)refused["command"]);
            Assert.IsFalse(string.IsNullOrEmpty((string)refused["reason"]));
            Assert.IsTrue(lines.Any(l => (string)l["kind"] == "warning" && (string)l["text"] == "journal test warning"));

            var zip = SessionJournal.SaveReport();
            using (var archive = ZipFile.OpenRead(zip))
                Assert.IsTrue(archive.Entries.Any(e => e.Name == Path.GetFileName(journal.Path)), "the report holds this session's journal");
            File.Delete(zip);
        }
    }
}
