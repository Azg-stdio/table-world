using System.Linq;
using Core.Engine;
using Core.Rules;

namespace Core.Tests.Engine
{
    /// <summary>G25: notebook pages are written, replaced, deleted, saved, validated, and private pages show to their seat.</summary>
    public class NotebookTests
    {
        [Fact]
        public void Pages_AreWrittenReplacedAndDeleted_AndSaved()
        {
            var engine = EngineFixture.Create();
            var made = engine.Ok(new SetNoteTabCommand { Tab = new NoteTab { Title = "Rules", Body = "Draw two." } });
            Assert.Equal("page-1", made.CreatedId);
            engine.Ok(new SetNoteTabCommand { Tab = new NoteTab { Id = "page-1", Title = "Rules", Body = "Draw three." } });
            engine.Ok(new SetNoteTabCommand { Tab = new NoteTab { Title = "Mine", Body = "bluff", Color = "red" } });
            Assert.Equal(2, engine.State.Notebook.Count);
            Assert.Equal("Draw three.", engine.State.Notebook[0].Body);

            var back = TableJson.Parse(TableJson.Serialize(engine.State)).State!;
            Assert.Equal("red", back.Notebook[1].Color);
            Assert.DoesNotContain("notebook", TableJson.Serialize(EngineFixture.Create().State));

            engine.Ok(new DeleteNoteTabCommand { TabId = "page-1" });
            engine.Fails(new DeleteNoteTabCommand { TabId = "page-1" }, ErrorCodes.UnknownObject);
            Assert.Single(engine.State.Notebook);
        }

        [Fact]
        public void PrivatePages_ShowToTheirSeatAndTheGameMaster()
        {
            var shared = new NoteTab { Title = "Rules" };
            var mine = new NoteTab { Title = "Mine", Color = "Red" };
            Assert.True(shared.VisibleTo("blue", false));
            Assert.True(mine.VisibleTo("red", false), "colours match whatever their case");
            Assert.False(mine.VisibleTo("blue", false));
            Assert.False(mine.VisibleTo(null, false), "a spectator does not read private pages");
            Assert.True(mine.VisibleTo(null, true), "the Game Master reads everything");
        }

        [Fact]
        public void Limits_AreKept()
        {
            var engine = EngineFixture.Create();
            engine.Fails(new SetNoteTabCommand { Tab = new NoteTab { Title = new string('t', NoteTab.MaxTitle + 1) } }, ErrorCodes.InvalidValue);
            engine.Fails(new SetNoteTabCommand { Tab = new NoteTab { Body = new string('b', NoteTab.MaxBody + 1) } }, ErrorCodes.InvalidValue);
            for (var i = 0; i < NoteTab.MaxTabs; i++)
                engine.Ok(new SetNoteTabCommand { Tab = new NoteTab { Title = $"p{i}" } });
            engine.Fails(new SetNoteTabCommand { Tab = new NoteTab { Title = "one more" } }, ErrorCodes.LimitReached);
            Assert.Equal("seat_1 wrote in the notebook (p0)",
                ActionLog.Describe(engine, new SetNoteTabCommand { Tab = engine.State.Notebook[0] }, CommandResult.Success(new System.Collections.Generic.List<StateEvent>()), "seat_1", null).Single().Text);
        }
    }
}
