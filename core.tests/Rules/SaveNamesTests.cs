using Core.Rules;

namespace Core.Tests.Rules
{
    /// <summary>G30: typed save names become safe file names; autosaves rotate.</summary>
    public class SaveNamesTests
    {
        [Theory]
        [InlineData("Game night", "Game night")]
        [InlineData("  ../../etc/passwd ", ".._.._etc_passwd")]
        [InlineData("a:b*c?d", "a_b_c_d")]
        [InlineData("Turn 3.", "Turn 3")]
        [InlineData("CON", "_CON")]
        [InlineData("nul.txt", "_nul.txt")]
        [InlineData("Ünïcödé ☺", "Ünïcödé ☺")]
        public void ToFileName_KeepsWhatIsSafe(string typed, string expected) => Assert.Equal(expected, SaveNames.ToFileName(typed));

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("///")]
        [InlineData("..")]
        [InlineData(null)]
        public void ToFileName_RefusesNothing(string? typed) => Assert.Null(SaveNames.ToFileName(typed));

        [Fact]
        public void LongNames_AreCut_AndAutosavesRotate()
        {
            Assert.Equal(SaveNames.MaxLength, SaveNames.ToFileName(new string('x', 500))!.Length);
            Assert.Equal("autosave-1", SaveNames.Autosave(1));
            Assert.Equal("autosave-3", SaveNames.Autosave(3));
            Assert.Equal("autosave-1", SaveNames.Autosave(4));
            Assert.True(SaveNames.IsAutosave("autosave-2"));
            Assert.False(SaveNames.IsAutosave("my autosave"));
        }
    }
}
