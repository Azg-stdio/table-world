# Social games: hidden roles, votes, nights and winks

Table World plays games of talking, looking and voting: Avalon, Werewolf, Wink Murder, Secret Hitler, or your own.
Nothing in the game knows any of them by name. The same few pieces build all of them, and an agent can put them
together from your rulebook (ADR 0012).

Four examples ship in `games/`, each built through the MCP tools from a plain-text rulebook in `docs/rulebooks/`:
`avalon.json`, `werewolf.json`, `wink-murder.json` and `secret-dictator.json` (plays like Secret Hitler).

## Playing

- **Your role.** When the game starts, you read your role in gold above the game log, with what it lets you know
  ("You are Merlin (Loyal servants of Arthur). Red and Blue are Minions of Mordred."). Nobody else's screen shows it,
  and the other players' machines never receive it: not even a modified game could read it.
- **Before Start**, the host ticks the optional roles to deal (Percival, Morgana, the Doctor...) with the buttons
  beside Start.
- **Votes.** When you have a vote, its options are buttons ("Team vote · Approve"). While it runs you read "2 of 5
  voted (you: approve)"; nobody sees a choice before the count. Then the game shows what the vote's rules say: who
  voted what, only the numbers (face-down mission cards), or only the result.
- **Nights.** When your eyes are closed, your screen goes dark with the phase's words, and you neither see nor hear
  the players who are awake. The awake players see your chalker asleep, head down.
- **Out of the game.** A lynched or executed player is out: no more actions or votes, and the others no longer hear
  them.
- **Faces.** Press **O** to see the table from your chair, eye to eye with the others; O again goes back. Press **K** to
  wink at the face you are looking at. Whoever is looking at you sees it; in Wink Murder, a wink from the Murderer
  kills.
- **Hosting fairly.** The host's computer runs the rules, so it holds every secret, but the host's screen and the
  host's agent show only the host's seat. For a game among people who do not know each other, the host can sit out
  as the narrator and see everything.

## Building one

The pieces, each in [the rules format](../rules-schema.md):

| Piece | What it does |
|---|---|
| [Roles](../rules-schema.md#roles) | Teams, roles dealt at random, team sizes per player count, optional roles, who knows whom (also by player count), extra hidden traits (`set`) |
| `revealRole`, `setRole`, `swapRoles`, `setTeam` | Show a role later, convert a player, swap two, change a side; each can tell the player |
| `message` with `to` | A line only a seat, a team (`team:wolves`) or a role (`role:seer`) reads |
| [Votes](../rules-schema.md#votes) | Who votes, how it is shown and decided, what follows; private votes for a night's choice |
| [Eyes closed and voice](../rules-schema.md#eyes-closed-and-voice) | Who is awake in a phase, what the sleepers read, who hears whom |
| [Gestures](../rules-schema.md#gestures) | `onGesture`: a wink or emote aimed at someone, and what it means |
| `eliminate`, `$alive` | Players out of the game, and how many of each team are left |
| `win` with `team`, win conditions with `team` | A whole team wins |
| [Secret variables](../rules-schema.md#secret-variables) | Values only the host's rules read (the wolves' victim until dawn) |

To have your agent build one, give it the rulebook and the brief (see [building a game with your agent](building-with-an-agent.md)).
It uses `set_roles` for the roles, `add_action` with a `vote` block for votes, `add_phase` with `awake` for nights, and
`add_event` with `onGesture` for gestures. The build scripts in `tools/mcp/build_*.py` show the full calls for the four
examples.

**One thing to know:** a secret variable is kept from every player, the one it is about included. What one player alone
should know about themselves ("you were winked at") belongs in a hidden trait, a role `set` such as `fate`, which only
that player receives. Wink Murder does it that way.

## Limits today

- In a game with rules, other players' hand cards and face-down cards reach your machine only as backs. On a
  free-play table (an imported TTS game) every machine holds everything and the game only hides it, as in TTS.
- Whoever searches a face-down deck sees its cards, and the others see that they searched. What is inside a bag is not
  secret.
- A vote has no timer of its own; a rule can close it with `closeVote`.
- Voice channels are tested on one machine; the playtest with friends checks them over the internet.
