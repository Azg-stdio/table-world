# ADR 0012 — Social games: secrets per seat, roles, eyes-closed phases, gestures

Status: **proposed** (2026-10-26, M10; asked for by Miguel: "the other type of games, like Avalon, the wolf, where you
have to wink to kill people"; seated chalkers at the table first-class for them).

## Context

Hidden-role games (Avalon, Werewolf, Secret Hitler, Blood on the Clocktower, Wink Murder) are played by talking,
looking at faces and voting. They share needs the table and rules do not cover:

- **A secret role per seat**, dealt at random, and rules about **who knows whom** (Merlin sees the evil team except
  Mordred; Percival sees Merlin and Morgana without knowing which is which; wolves see each other).
- **Eyes-closed phases**: only some roles are awake, the rest must not see or hear what happens.
- **Private voice**: the wolves whisper at night; the village does not hear them.
- **Votes**: secret until everyone has voted; revealed by name (Avalon's team vote) or only as a count, shuffled
  (Avalon's quest cards).
- **Faces as moves**: in Wink Murder a wink at the player you look at eliminates them.

Today hiding is done by the view only (ADR 0010, M3 T3.6 prep): every client receives the whole table, and the snapshot
carries the random generator's state, so a client can also recompute every shuffle and deal. For cards this is TTS's own
honest-play standard. For a hidden-role game it is not: one modified client or a debugging tool reveals who the werewolf
is, and the game is over.

## Decision

### 1. Secrets per seat in the protocol

The host keeps the whole state. Each client receives a **view for its seat**:

- **Redaction.** `SeatView.For(state, seat)` returns a copy without what the seat may not know:
  - other seats' roles, except what the `knows` rules give it (as a role, a team, or "one of these");
  - other seats' secret choices (`Session.Secrets`) and votes not yet revealed;
  - private log lines addressed to other seats;
  - the random generator's state.
  The host's snapshot (`SessionHost.Snapshot`) sends `SeatView.For` instead of the full state.
- **Host-only randomness.** Without the generator state a client cannot replay a shuffle, a deal or a role draw. Such
  commands become host-run, like Lua-run commands already are (ADR 0010): the host runs them and sends the result as
  part of the order (the moved objects, or a public diff), not as a command to replay.
- **Hash over what everyone sees.** `TableJson.Hash` is taken over the public part of the view, so peers with different
  secrets still agree.
- **Private messages.** A log line may name its readers (`to: [seats]`); the host sends it only to them. "You are
  Merlin. Evil: Red, Blue." is one.
- **Presence and voice follow the same rule.** During an eyes-closed phase the host does not forward the awake
  seats' pointers, held pieces, gaze or emotes to sleeping seats. Voice is routed by channel (section 4).

The table's objects are a second step, built on the same redaction: hand cards and pieces in a `HiddenZone` reach
other seats as a blank object of the same kind and size (back only, no name, no face), so a seat can see that a card
is there, but not which one. Social games do not need this step, so it can come later; card games gain cheat-proof
hands from it.

**The host player sees only their seat too** (Miguel, 2026-10-26). The host's machine must hold everything to run the
rules, but nothing on its screen shows more than its seat's view: the HUD, the log, the object inspector, GM notes of
role objects, the Lua console and debug panels all read `SeatView.For(state, hostSeat)`, never the engine's state.
The MCP agent connected to the host is a player too: while a game with roles runs, its reads (`get_state`, `run_lua`
and the rest) are redacted to the host's seat, unless the host has no seat (narrator). A host who digs into memory or
the save file can still cheat; Table World is meant for friends, and there is nothing to do against that short of a
dedicated server. What matters is that no one sees a secret by accident or with one click.

### 2. Roles in the rules format

```json
"roles": {
  "teams": [ { "id": "good", "name": "Loyal servants" }, { "id": "evil", "name": "Minions of Mordred" } ],
  "list": [
    { "id": "merlin",   "team": "good", "count": 1 },
    { "id": "percival", "team": "good", "count": 1, "optional": true },
    { "id": "servant",  "team": "good", "count": "fill" },
    { "id": "assassin", "team": "evil", "count": 1 },
    { "id": "morgana",  "team": "evil", "count": 1, "optional": true },
    { "id": "mordred",  "team": "evil", "count": 1, "optional": true },
    { "id": "minion",   "team": "evil", "count": "fill" }
  ],
  "byPlayers": { "5": { "good": 3, "evil": 2 }, "6": { "good": 4, "evil": 2 }, "7": { "good": 4, "evil": 3 } },
  "knows": [
    { "role": "merlin",   "sees": { "team": "evil", "except": ["mordred"] }, "as": "team" },
    { "team": "evil",     "sees": { "team": "evil", "except": ["oberon"] },  "as": "team" },
    { "role": "percival", "sees": { "roles": ["merlin", "morgana"] },        "as": "oneOf" }
  ]
}
```

- **Dealt at start**, at random, on the host. `byPlayers` sets team sizes for the number of seated players; required
  roles are placed first, `fill` completes each team. The host ticks optional roles in the Start dialog (and
  `start_game roles=` over MCP).
- **What a seat knows** is computed once at the deal from `knows` and kept in the session per seat. `as` is `role`
  (name shown), `team` (only the team), or `oneOf` (the seat sees the set, not who is who). A role's own rules replace
  its team's (Oberon is evil and sees no other minion).
- **Each seat is told privately** at the deal ("You are Merlin (Loyal servants of Arthur).", "Red and Blue are Minions
  of Mordred."): a `PrivateMessage` event the host sends to that seat only; the shared log never holds it. The HUD shows
  the same lines above the game log.
- **Rules read roles** as seat properties (built: `$actor.role`, `$seat.<id>.team`, `$param.target.role`), so a
  precondition can say only the assassin may assassinate. `revealRole` (`seat`, `to` a seat or every seat, `teamOnly`)
  covers "the seer looks at a card" and "show everyone the dead player's role".
- **Reading a role makes a command host-run** (`CommandResult.ReadsSecrets`): peers lack other seats' roles, so they
  could not replay it. A client whose own prediction is refused for that reason sends the command unpredicted.
- Lua: `Game.getRole(seat)`, `Game.getTeam(seat)` (scripts run on the host), `Game.revealRole(seat, to, teamOnly)`.
- **Sandbox, not Avalon** (Miguel, 2026-10-26: the mechanics stay general; Avalon is one example). After T11.4: roles
  change during play (`setRole`, `swapRoles`, `setTeam`, each optionally telling the player), groups are told
  privately (`to`: a seat, `team:<id>`, `role:<id>`, on `revealRole` and `message`), and roles with a `set` deal more
  hidden traits beside the role (`$actor.role.<set>`). Each game the milestone builds is a test of the format: a gap
  it finds is fixed for every game, never for that one.
- **What building Avalon through MCP found (T11.6)**, each fixed in general: list variables (`setVariable` `add` /
  `remove`, `.count`, items by position, the `inList` condition) for a team picked one seat at a time; `win` with a
  `team`; a vote's `who` taking a list. The quest table (size by player count and quest) went to Lua, the format's
  own escape hatch. A bug any game would have hit: Lua's `Game.getSeats()` and `getLegalActions()` returned their ids
  as object handles, so every entry was nil; they return plain strings now.
- **What building Werewolf found (T11.14)**, fixed in general: players out of the game (`eliminate`, `$alive`
  counts, `.out`; out seats cannot act or vote, sleep, are not heard, are passed by turns and seat choices); win
  conditions by team; a seat id in a variable followed like the seat; private votes (the wolves' hunt left a public
  "Red voted." and its ballots in the session, naming the wolves); secret variables (the victim sat in a public
  variable until dawn). The protocol tests found both leaks before any player could.
- **What building Wink Murder found (T11.15):** a secret variable cannot drive a player's own buttons, because no
  player's machine holds it, not even the one it is about. What one player alone may know about themselves belongs in a
  hidden trait (a role `set`, here `fate`: alive or winked at), which reaches that player only. The docs say so where
  they describe secret variables.
- **Cards secret on the wire (T11.16, as built):** only on tables with rules or roles; free-play tables keep TTS's
  honest play, so big imports pay nothing. Hidden cards and face-down decks reach a machine as unknown cards, a drawn
  card gets a fresh id (its deck id would name it), and every command touching such a card is host-run. In card games
  that is every order: about 3.5 kB per client each, roughly 4.6 ms of work across host and clients, no resyncs. The searcher of a face-down
  deck receives its cards; bag contents stay public, left for later.
- **The sandbox check (T11.15b, Secret Dictator: plays like Secret Hitler, a game the milestone does not name)**
  needed one more general piece: `knows` rules that hold only for some player counts (`minPlayers`, `maxPlayers`: the
  Dictator knows the fascists only at 5–6). Everything else (a policy deck passed hand to hand, chained phases, term
  limits, executions, chaos after three failed elections) was already in the format. It also showed why hand cards must
  stop reaching every machine (T11.16): the policies a president passes are the game's whole secret.

### 3. Eyes-closed phases

A phase may say who is **awake**: `"awake": { "roles": ["werewolf"] }`, `{ "team": "evil" }`, or `"nobody"`.

- **Sleeping seats** see a dark screen with the phase's text ("Night. Eyes closed.") and their chalker closes its eyes
  and lowers its head on every screen. They take no actions; the host does not forward the awake seats' presence to them.
- **Awake seats** see the table and each other, and the phase's prompt. For them the sleepers are visibly asleep.
- A night is several phases in a row (wolves, then seer, then doctor); `next` moves on when the awake seats have acted
  or a timer (`Clock`) runs out.
- The opening of Avalon is one phase awake for `team: evil` (without Oberon), with `knows` already shown privately; the
  phase only gives the moment its feel.

### 4. Voice channels

Voice is relayed through the host (NGO's star topology), so the host routes it: each phase may set
`"voice": "all" | "awake" | "none"`, and a team may have its own channel (`"voice": "team"`: the wolves hear each other,
nobody else hears them). Spectators and the narrator hear everything, unless the game says otherwise.

### 5. Votes

A `vote` action is a `secret` action with a tally:

```json
{ "id": "approve_team", "vote": { "options": ["approve", "reject"], "who": "allSeated", "reveal": "named",
  "result": "majority", "ties": "reject" } }
```

- `reveal`: `named` (everyone sees who voted what), `count` (only totals; Avalon's quest cards, shuffled), `secret`
  (only the result).
- The reveal happens when every seat in `who` has voted, or at a timer. The tally lands in variables
  (`$vote.approve`, `$vote.result`) for the next effect.
- A vote can target seats (`"options": "seats"`): the Werewolf lynch, the Wink Murder accusation.

### 6. Faces and gaze as moves

The chalkers become part of the game:

- **Gaze targets a face.** Each client computes which seated chalker the local player looks at (a cone from the head
  through the camera's centre, nearest face). Presence carries it as `lookingAt: seat`.
- **Gestures**: `wink` (one eye), `eyesClosed`, plus the existing emotes. A gesture is sent with the seat the
  player was looking at when making it; the sender names its own target, as when choosing a card, so trusting it costs
  nothing.
- **Rules event** `onGesture` with `$actor`, `$param` (the gesture) and `$target`. Wink Murder: `onGesture` where the
  gesture is `wink` and `$actor.role == murderer` sends the target a private "you have been winked at" and starts a
  short timer before the target is out.
- Who sees a wink is not a rule: anyone whose camera is on the winker's face at that moment sees it, as at a real table.
  Gestures must play at full presence rate, so a quick wink is not lost between frames.

### 7. The chalkers seated at the table

The placeholder chalker stands behind its seat's hand. Social games are played across the table, face to face, so:

- **Seats get chairs**, placed around the table from the seats' hand zones (or the table's edge when a seat has none),
  and the chalker **sits**: hips on the chair, hands on the table, head at a height where the faces across are in view
  from the default camera.
- **A seated chalker built from simple shapes** replaces the standing capsule (Miguel, 2026-10-26: an artist makes the
  final model later): a body, arms and legs in code that can sit, and the chalkboard face with eyes drawn separately so
  each one can open, close and wink. Personalities, emotes, mouth and gaze keep working on it. The pose and face
  drive named points (hips, hands, head, eyes), so the artist's model later plugs into the same points without changing
  the rules or the network.
- **The player's view starts from their chalker's head**, as if sitting there; the table camera keeps working as today.

## Consequences

- Redaction is a change in the sync protocol, the riskiest part. It comes first and gets its own simulation (two
  clients, redacted views, a game of Avalon played to the end with no desync and no secret in any client's received
  data; checked by searching the received messages for the hidden roles).
- Commands that use the random generator become host-run. Each one costs peers one message with the result instead of a
  local replay. At board-game pace this is not felt; physics and moves keep their client prediction.
- As built (T11.2, 2026-10-26): a host-run order carries, per peer, a patch (the objects the command's events name,
  their contents and list positions, the seat's view of the session, the blind round), the events themselves (the log
  and the animations read them) and the public hash; a change beyond objects and session (lighting, zones, scripts that
  ran) sends the whole seat view. In UNO and War about 1 order in 70 is host-run, 9–16 kB each; the rest are replayed
  as before (about 200 bytes). A client's own rolls and shuffles are not predicted (it does not hold the generator):
  they play when the host's result arrives, one round trip later, rather than landing on a guess and jumping. Lua inside
  rules no longer costs a resync (ADR 0010): it is host-run too.
- The same work makes card games fair (cheat-proof hands, decks whose order a client cannot know), once table objects
  are redacted (section 1, second step).
- TTS has no roles, gestures or eyes-closed phases; imported TTS games keep working as before (no `roles`, no redaction
  beyond what they had).
- The MCP agent can build these games: roles, `knows`, `awake`, `voice`, `vote` and `onGesture` are rules data, so an
  agent turns an Avalon or Werewolf rulebook into a game with the same tools.

## Alternatives considered

- **Keep view-only hiding** and rely on honest play, as TTS does: fine for cards among friends, but social deduction
  collapses the moment one player looks. Rejected.
- **Encrypt secrets per seat** inside one shared state: every client could keep replaying everything, but a client
  replaying a deal would still compute the result unless the generator stays on the host, and keys add complexity
  without removing host-run commands. Rejected.
- **Roles as cards in hand** (as on TTS): works with no new rules, but the rules cannot read a role, `knows` needs a
  script for each game, and nothing stops a peek. Kept only as how imported TTS games do it.
- **Separate social-game mode outside the rules engine**: faster for Werewolf alone, but every game would be its own
  code path. Rejected; these games are rules data like any other.
