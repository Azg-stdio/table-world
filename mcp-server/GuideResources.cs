using System.ComponentModel;
using System.IO;
using ModelContextProtocol.Server;

namespace TableWorld.Mcp
{
    /// <summary>
    /// M8 T9.11: what an agent needs to build a game, served by the server itself so any agent has it without the
    /// repository: the rules reference, the game brief template and the agent guide.
    /// </summary>
    [McpServerResourceType]
    public static class GuideResources
    {
        public const string RulesReferenceUri = "tableworld://docs/rules-reference";
        public const string BriefTemplateUri = "tableworld://docs/game-brief-template";
        public const string AgentGuideUri = "tableworld://docs/agent-guide";

        [McpServerResource(UriTemplate = RulesReferenceUri, Name = "rules_reference", MimeType = "text/markdown"),
         Description("The rules format: objects, zones, phases, actions, effects, conditions, references, events, win conditions and error codes. The same text as the get_rules_reference tool.")]
        public static string RulesReference() => Embedded("rules-schema.md");

        [McpServerResource(UriTemplate = BriefTemplateUri, Name = "game_brief_template", MimeType = "text/markdown"),
         Description("The game brief template: everything about a game an agent needs to build it with few questions. Give it to the designer to fill in, or use its headings to check a rulebook.")]
        public static string BriefTemplate() => Embedded("game-brief-template.md");

        [McpServerResource(UriTemplate = AgentGuideUri, Name = "agent_guide", MimeType = "text/markdown"),
         Description("How to work with Table World: the build loop, what trips agents up, troubleshooting.")]
        public static string AgentGuide() => Embedded("agent-guide.md");

        internal static string Embedded(string name)
        {
            using var stream = typeof(GuideResources).Assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    /// <summary>M8 T9.11: ready-made instructions an agent's user can start a session with.</summary>
    [McpServerPromptType]
    public static class GuidePrompts
    {
        [McpServerPrompt(Name = "build_game_from_brief"),
         Description("Build a playable Table World game from a filled game brief (or a rulebook): table, rules, validation, questions to the host, then a test game.")]
        public static string BuildGameFromBrief(
            [Description("The filled game brief, or the rulebook text.")] string brief) =>
            "Build this game in Table World with the table-world tools.\n\n" +
            "1. Read the rules format first (resource " + GuideResources.RulesReferenceUri + ", or the get_rules_reference tool).\n" +
            "2. Map the brief to the format: components become decks, tokens, tiles and zones (new_game, create_deck, place_object, " +
            "define_zone); turns, phases, actions, events and the end of the game become rules (set_turn, add_phase, add_action, " +
            "add_event, set_win_condition).\n" +
            "3. Where the brief is silent or ambiguous, do not guess: call ask_host with the question and the options you see, and " +
            "continue with the answer. Section 11 (edge cases) and section 16 (open questions) of the brief are where to look first.\n" +
            "4. Run validate_rules after each part and fix every error before moving on.\n" +
            "5. Start a game (start_game) and play a few turns with perform_action for each seat; check get_table_state shows what " +
            "the brief says should happen. Report what you built, what you asked, and anything that still differs from the brief.\n\n" +
            "The brief:\n\n" + brief;

        [McpServerPrompt(Name = "review_rulebook"),
         Description("Read a rulebook before building anything: list what is ambiguous or missing, using the game brief template's sections, and the questions to ask the designer.")]
        public static string ReviewRulebook(
            [Description("The rulebook text.")] string rulebook) =>
            "Review this rulebook before building it in Table World. Do not call any building tool yet.\n\n" +
            "Read the game brief template (resource " + GuideResources.BriefTemplateUri + ") and go through its sections. For each " +
            "section marked ★, say what the rulebook answers and what it leaves out. Then list:\n" +
            "- components that are mentioned but not described (how many, what is printed on them);\n" +
            "- rules that two careful players could read differently, quoting the passage;\n" +
            "- situations the rulebook does not cover (ties, empty decks, a player who cannot act);\n" +
            "- how much Table World should enforce (section 2), if the rulebook does not make it clear.\n" +
            "End with the questions to ask the designer, most important first.\n\n" +
            "The rulebook:\n\n" + rulebook;
    }
}
