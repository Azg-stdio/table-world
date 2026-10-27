# Game brief — template

Fill this in before you ask your agent to build your game in Table World. A rulebook explains a game to players who sit at a real table; this brief tells your agent what a rulebook usually leaves out: every component with its data, what the game should enforce, and the edge cases you already know about. The more you fill in, the fewer questions the agent asks and the faster you get a playable table.

**How to use it**

1. Copy this file next to your rulebook and your images (for example `my-game/brief.md`).
2. Fill in what you know. Leave a section empty or write "not sure" when you have not decided: the agent will ask you about it instead of guessing.
3. Give both to your agent:

   > Here is the brief and the rulebook of my game, and the folder with its images. Build it in Table World with the table-world tools: read get_rules_reference first, follow the brief where it is more precise than the rulebook, validate the rules, and ask me (ask_host) about anything both leave open. Then start a game so I can play it.

Sections marked ★ matter most. Delete the examples in *italics* as you go.

---

## 1. The game ★

| | |
|---|---|
| Name | *Match the Pile* |
| One-line pitch | *Shed your hand by matching suit or rank.* |
| Players (min–max) | *2–4* |
| Play time | *10–15 min* |
| Similar games (helps the agent a lot) | *UNO, Crazy Eights* |
| Version of the rules | *v0.3, 2026-10-20* |

## 2. How much should Table World enforce? ★

Pick one. This changes what the agent builds more than anything else in this brief.

- [ ] **Full rules:** the game knows whose turn it is, offers only legal moves, scores and ends the game.
- [ ] **Helpers only:** free play like a real table, plus the boring parts automated (setup, dealing, shuffling, a score panel, turn order).
- [ ] **Sandbox:** just lay out the components; we play by hand.

Anything that must stay manual even with full rules (for example "players negotiate trades freely"):

## 3. Components ★

One row per kind of component. For decks, list the cards in section 3.1.

| Component | Kind | How many | Image file(s) | Size | Notes |
|---|---|---|---|---|---|
| *Main deck* | *Deck of cards* | *52* | *`cards.png` (sheet 10 × 6), back `back.png`* | *standard poker* | *see 3.1* |
| *Gold coin* | *Token, round* | *30* | *`coin.png`* | *2 cm* | *stackable* |
| *Board* | *Board* | *1* | *`board.jpg`* | *50 × 50 cm* | |
| *Six-sided die* | *Die* | *2* | *(default)* | | |
| *Score track marker* | *Figure or token* | *1 per player* | *`pawn.glb`* | | *player colour* |

Kinds Table World knows: card, deck, token (round, square, cut-out image), tile (square, hex, circle, rounded), die (4, 6, 8, 10, 12, 20 faces, or custom faces), board, figure, 3D model (glTF), bag, infinite bag (endless supply), counter, clock or timer. If yours is something else, describe it.

**Sprite sheets:** if many cards share one image, give the grid (columns × rows) and the order (left to right, top to bottom, first card top-left).

### 3.1 Card list

Best as a table (or attach a CSV with the same columns). Add any columns your rules talk about: cost, colour, type, power, text.

| Card | Copies | Sheet position | Properties |
|---|---|---|---|
| *Fireball* | *2* | *`cards.png` #0* | *type: spell, cost: 3, damage: 4* |
| *Shield* | *3* | *`cards.png` #1* | *type: item, cost: 2* |

## 4. Players and seats

| | |
|---|---|
| Player colours | *red, blue, green, yellow* |
| Asymmetric roles or factions | *none / list them and what differs* |
| Teams | *none / 2 vs 2, partners sit opposite* |
| Hidden information | *hands are secret; the scoring pile is face down but its size is public* |
| A game master / host role? | *no / the host controls the monsters* |

**Hidden roles** (fill in for social games such as Avalon or Werewolf; skip otherwise):

| | |
|---|---|
| Roles and their teams | *Merlin (good), Assassin (evil), Loyal servant (good, the rest)…* |
| How many of each, by player count | *5 players: 3 good, 2 evil; 6: 4 good, 2 evil…* |
| Optional roles the host may add | *Percival, Morgana* |
| Who knows whom at the start | *evil sees evil except Oberon; Merlin sees evil except Mordred* |
| Can a role change during play? | *no / a bitten villager becomes a wolf, and the wolves learn it* |
| Votes: who votes, shown how, decided how | *team vote: everyone, by name, majority; quest: the team, only the count* |
| Eyes-closed phases: who wakes, in what order | *night: wolves, then the Seer, then the Doctor* |
| Who may talk when | *the wolves whisper at night; the dead stay silent* |
| Gestures that count | *a wink from the Murderer kills* |

## 5. Table layout

Where everything sits at the start. A sketch is perfect: draw it, photograph it, or use text.

```
            [ North's hand ]
       North's deck   North's score
   +-------------------------------+
   |   draw pile   discard pile    |
   |          market (5 cards)     |
   +-------------------------------+
       South's deck   South's score
            [ South's hand ]
```

- Shared areas (draw pile, market, board spaces):
- Per-player areas (deck, discard, tableau, reserve):
- Places a piece should snap into (board spaces, card slots):

## 6. Setup ★

Numbered, in the order a person would do it.

1. *Shuffle the main deck.*
2. *Deal 7 cards to each player.*
3. *Turn the top card of the deck face up to start the discard pile.*
4. *The youngest player goes first.* (Table World has no ages: say what to do instead: random, the host, a fixed seat.)

## 7. Turns and phases ★

| | |
|---|---|
| Turn order | *clockwise / counterclockwise / simultaneous / by initiative* |
| Who starts | *random / fixed seat / the host / last game's loser* |
| Phases of a turn | *1. Draw  2. Play  3. Discard* |

For each phase: what the player may do, and when the phase ends.

| Phase | Allowed | Ends when |
|---|---|---|
| *Draw* | *draw 1 card (mandatory)* | *the card is drawn* |
| *Play* | *play any number of cards* | *the player says "done"* |

Rounds (if the game has them): what ends a round, what happens between rounds.

## 8. Actions ★

Everything a player can do, one per row.

| Action | Who | When | Requires | What happens | Limit |
|---|---|---|---|---|---|
| *Play a card* | *current player* | *Play phase* | *matches the top discard by suit or rank* | *card goes onto the discard pile; turn ends* | *1 per turn* |
| *Draw* | *current player* | *Play phase* | *—* | *take the top card of the draw pile; turn ends* | *1 per turn* |

Out-of-turn actions, interrupts or reactions ("any player may slap the pile when..."):

## 9. Resources, scores and counters

| Name | Per player or shared | Starts at | Min / max | Shown how |
|---|---|---|---|---|
| *Gold* | *per player* | *3* | *0 / 99* | *counter on the HUD* |
| *Round* | *shared* | *1* | *1 / 5* | *text on the HUD* |

## 10. End of the game and scoring ★

- The game ends when: *a player has no cards in hand / after 5 rounds / someone reaches 10 points*
- When it is checked: *immediately / at the end of the turn / at the end of the round*
- Scoring at the end:
- Tiebreakers:

## 11. Edge cases you already know ★

The questions players ask in the middle of a game. Answer the ones you can.

- The draw pile runs out: *shuffle the discard pile except its top card into a new draw pile*
- A player cannot do anything legal on their turn:
- Two things trigger at the same time:
- Ties (in combat, in scores, for first player):
- A player leaves mid-game:

## 12. On-screen interface

Panels, buttons or text you want on screen besides the table itself.

- *Score panel with every player's gold and the round number*
- *An "End turn" button for the current player*
- *A message when a round ends*

## 13. Automation that needs scripting

Things that react to the table rather than to a button, or effects too special for a rules table: "when a card enters the battlefield, its owner gains 1 gold", "the board rotates every round". Table World can run sandboxed Lua scripts for these; the agent will tell you if one is needed.

## 14. Look and mood

| | |
|---|---|
| Room | *game night (dim room, warm lamp) / plain background* |
| Table surface | *green felt / wood / my image `mat.jpg`* |
| Lighting notes | *keep the art's colours true; darker room for a horror game* |

## 15. Playtest goals and variants

- What you want to learn from playtests: *is 7 cards too many for 4 players?*
- Variants to switch on and off: *"stacking draw-two" house rule (off by default)*
- Numbers you expect to tweak: *starting hand size, points to win*

## 16. Open questions

Things you have not decided yet and want the agent to raise, or ideas you want its opinion on.

-
