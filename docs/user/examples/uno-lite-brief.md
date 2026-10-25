# Game brief — Match the Pile (simplified UNO)

A filled example of the [game brief template](../game-brief-template.md), for the rulebook in
`docs/rulebooks/uno-lite.txt`. The finished game is `games/uno-lite.json`.

## 1. The game ★

| | |
|---|---|
| Name | Match the Pile |
| One-line pitch | Shed your hand by matching the discard's suit or rank. |
| Players (min–max) | 2–4 |
| Play time | 10–15 min |
| Similar games | UNO, Crazy Eights |
| Version of the rules | v1 |

## 2. How much should Table World enforce? ★

- [x] **Full rules:** the game knows whose turn it is, offers only legal moves, scores and ends the game.

Nothing stays manual.

## 3. Components ★

| Component | Kind | How many | Image file(s) | Size | Notes |
|---|---|---|---|---|---|
| Draw pile | Deck of cards | 52 | the built-in French deck, no jokers | standard poker | face down, centre |
| Discard pile | Deck (starts empty) | 1 | | | face up, next to the draw pile |

### 3.1 Card list

The standard 52 cards: four suits, ranks 2 to 10, J, Q, K, A. Only suit and rank matter, for matching.

## 4. Players and seats

| | |
|---|---|
| Player colours | red, blue, green, yellow |
| Asymmetric roles or factions | none |
| Teams | none |
| Hidden information | hands are secret; the number of cards in each hand is public |
| A game master / host role? | no |

## 5. Table layout

```
              [ North's hand ]
   [ West's hand ]   draw pile  discard   [ East's hand ]
              [ South's hand ]
```

## 6. Setup ★

1. Shuffle the draw pile.
2. Deal 7 cards to each player's hand.
3. Turn the top card of the draw pile face up onto the discard pile.
4. The first seat (south) starts.

## 7. Turns and phases ★

| | |
|---|---|
| Turn order | clockwise (to the left) |
| Who starts | south |
| Phases of a turn | 1. Play |

| Phase | Allowed | Ends when |
|---|---|---|
| Play | play one matching card, or draw one | the player plays or draws |

## 8. Actions ★

| Action | Who | When | Requires | What happens | Limit |
|---|---|---|---|---|---|
| Play a card | current player | Play phase | the card matches the top discard by suit or by rank | the card goes face up onto the discard pile; turn ends | 1 per turn |
| Draw a card | current player | Play phase | — (allowed even with a playable card) | the top card of the draw pile goes to their hand; turn ends | 1 per turn |

## 9. Resources, scores and counters

| Name | Per player or shared | Starts at | Min / max | Shown how |
|---|---|---|---|---|
| Cards in the draw pile | shared | 31–45 | 0 / 52 | counter on the HUD |
| Top discard | shared | | | text on the HUD |

## 10. End of the game and scoring ★

- The game ends when: a player has no cards in hand.
- When it is checked: immediately, after any move.
- Scoring at the end: no points; that player wins.
- Tiebreakers: none needed.

## 11. Edge cases you already know ★

- The draw pile runs out: shuffle the discard pile except its top card into a new draw pile.
- Both the draw pile and the discard (below its top) are empty: drawing just ends the turn.
- A player cannot play: they draw (drawing is always allowed).
- The drawn card could be played: no, the turn ends after drawing.

## 12. On-screen interface

- A panel with the top discard and the draw pile's size.
- Play and Draw buttons for the current player.

## 13. Automation that needs scripting

None.

## 14. Look and mood

| | |
|---|---|
| Room | game night |
| Table surface | green felt |

## 15. Playtest goals and variants

- Is 7 cards too many for 4 players?

## 16. Open questions

-
