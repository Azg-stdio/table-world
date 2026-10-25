# Game brief — War

A filled example of the [game brief template](../game-brief-template.md), for the rulebook in
`docs/rulebooks/war.txt`. The finished game is `games/war.json`.

## 1. The game ★

| | |
|---|---|
| Name | War |
| One-line pitch | Flip your top card; the higher card takes both. |
| Players (min–max) | 2–2 |
| Play time | 5–30 min (it can run long) |
| Similar games | War (the classic), Beggar-my-neighbour |
| Version of the rules | the classic rules |

## 2. How much should Table World enforce? ★

- [x] **Full rules:** the game knows whose turn it is, offers only legal moves, scores and ends the game.

Nothing stays manual: a player only ever flips their top card.

## 3. Components ★

| Component | Kind | How many | Image file(s) | Size | Notes |
|---|---|---|---|---|---|
| Main deck | Deck of cards | 52 | the built-in French deck | standard poker | empty after setup |
| Each player's pile | Deck (starts empty) | 1 per player | | | face down, in front of the player |
| Each player's battle pile | Deck (starts empty) | 1 per player | | | face up, between the players |

### 3.1 Card list

The standard 52 cards: four suits, ranks 2 to 10, J, Q, K, A. Aces are high: compare cards by rank, 2 lowest and
ace highest. Suits never matter.

## 4. Players and seats

| | |
|---|---|
| Player colours | red (south), blue (north) |
| Asymmetric roles or factions | none |
| Teams | none |
| Hidden information | the piles are face down; the cards in a battle are face up |
| A game master / host role? | no |

## 5. Table layout

```
        [ North's pile ]   [ North's battle pile ]
                 (main deck at setup only)
        [ South's battle pile ]   [ South's pile ]
```

## 6. Setup ★

1. Shuffle the main deck.
2. Deal it out face down, one card at a time, into the two players' piles: 26 cards each.
3. South goes first (turn order does not matter much; both players act every battle).

## 7. Turns and phases ★

| | |
|---|---|
| Turn order | clockwise, south then north |
| Who starts | south |
| Phases of a turn | 1. Battle  2. Resolve |

| Phase | Allowed | Ends when |
|---|---|---|
| Battle | each player flips the top card of their pile onto their battle pile | both players have flipped |
| Resolve | nothing: the game compares the two face-up cards | the cards are taken |

Rounds: none. Each battle is one turn.

## 8. Actions ★

| Action | Who | When | Requires | What happens | Limit |
|---|---|---|---|---|---|
| Play top card | each player | Battle phase | cards in their pile | the top card goes face up onto their battle pile | 1 per battle, 2 in a war |

## 9. Resources, scores and counters

| Name | Per player or shared | Starts at | Min / max | Shown how |
|---|---|---|---|---|
| Battles | shared | 0 | 0 / — | label on the HUD |
| Cards in each pile | per player | 26 | 0 / 52 | counter on the HUD |

## 10. End of the game and scoring ★

- The game ends when: a player has no cards in their pile or battle pile, or after 500 battles.
- When it is checked: at the end of each turn.
- Scoring at the end: whoever has every card wins; after 500 battles, the player with more cards wins.
- Tiebreakers: after 500 battles with equal piles, the game goes on until one pile is bigger.

## 11. Edge cases you already know ★

- War (the two cards are equal): each player puts one card face down onto their battle pile, then plays another face
  up; the higher new card takes everything on both battle piles. Equal again: war again.
- A player who has fewer than 2 cards when a war starts cannot go to war and loses.
- Taken cards are shuffled, then go face down to the bottom of the winner's pile, the winner's battle pile first
  (shuffling keeps the game from looping forever).
- The game could last forever: the 500-battle limit above stops it.

## 12. On-screen interface

- A panel with the battle number and each player's pile size.
- A button for each player to play their top card.

## 13. Automation that needs scripting

None.

## 14. Look and mood

| | |
|---|---|
| Room | game night |
| Table surface | green felt |

## 15. Playtest goals and variants

- Is 500 battles a sensible limit?

## 16. Open questions

-
