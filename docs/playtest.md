# Playtest guide (T3.6)

The friends playtest: you host a game and two friends join over the internet with a join code. It is the condition for paying the Steam fee (T0.6). This page covers what to send, what each person does, and what to write down.

## 1. Build and send

1. With the Editor open: `unity command build run --target StandaloneWindows64 --outputPath Builds/Win64/TableWorld.exe --confirm true` (or File → Build Profiles → Windows → Build).
2. Zip `unity/Builds/Win64/` **without** the `TableWorld_BackUpThisFolder_ButDontShipItWithYourGame` folder (about 105 MB). The sample games (War, Simplified UNO) are already inside, in `TableWorld_Data/StreamingAssets/Games`.
3. Send the zip. Windows SmartScreen will warn because the exe is unsigned: **More info → Run anyway**.

## 2. Host (you)

1. Start `TableWorld.exe`.
2. **Games → War**. The table loads with one deck.
3. **Host online**. The address field shows a 6-character code, already copied to the clipboard. Paste it in your chat.
4. Wait until the top bar says `code XXXXXX, 2 player(s) joined`. You play South; joiners get North, then any free seat.
5. **Start game**, in the panel at the bottom left.
6. Play with your hands. In War, drag your own deck to play its top card. In UNO, drag a card from your hand onto the table to play it, or drag the draw pile to draw. If a move is not allowed (not your turn, card does not match), the card goes back and the bottom-left line says why. The buttons (top right, and at the top centre) still work.

Both sample games (War and **Simplified UNO**) have two seats, so a third person gets no seat and can only watch and move free objects. To switch games, open the other one from **Games** while hosting: everyone receives the new table.

## 3. Friends

1. Start `TableWorld.exe`.
2. Type the code in the address field (replacing `127.0.0.1:7777`) and press **Join**.
3. The top bar shows `playing as seat_north` once the table arrives. The camera starts behind your own seat; your hand stands at your edge of the table, and the other players' hands show only backs.

## 4. What to write down

Note these in `docs/milestones/M3-multiplayer.md` (simulation record) or tell the agent:

- Did everyone join on the first try? Any error text in the top bar?
- Lag: time between pressing a button and seeing the card move, for the host and for the friends.
- Anything that differed between screens: card positions, the War counters, who won.
- Confusing controls (the full list is in `docs/tts-interactions.md`; **Alt** looks at a card, right-click opens menus).
- Logs, if something broke: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Table World\Player.log` on each machine.

## 5. Checklist: what only a test with friends can confirm

Everything below already passes automated tests on one machine (two processes, simulated input). What it cannot check is real networks, real hands on real mice, and how it feels. Tick each one and note anything odd. New items are added here whenever a feature lands that needs people to confirm it.

**Joining and staying connected**
- [ ] Join by code from different homes (different routers), first try, each friend.
- [ ] A friend leaves and rejoins mid-game: they get the table back and a seat.
- [ ] A friend's window in the background (alt-tab) keeps the game in sync.
- [ ] Lag feels acceptable: picking up, dropping, flipping, playing a card (note rough seconds if it does not).
- [ ] A game with your own large images: how long until everyone sees them.

**Seeing each other (live presence)**
- [ ] Each friend's pointer shows in their colour with their seat name, and moves smoothly.
- [ ] When someone carries a card or die, everyone sees it move live and fall where it was dropped.
- [ ] Trying to grab something a friend is holding is refused with their name.
- [ ] Two people grabbing the same thing at the same moment: what happens (should end the same on all screens).

**Hands and hidden information**
- [ ] Your own hand: the bar at the bottom shows your cards; hover enlarges; drag out plays or places; drop back reorders.
- [ ] Hide hand / Show hand works and is remembered next time.
- [ ] Friends see only the backs of your hand cards, at your edge of the table, and cannot pick them up or read their names.
- [ ] The camera starts behind each person's own seat.

**Playing by the rules (War, UNO)**
- [ ] Drag a playable card from your hand: it plays and the turn passes on everyone's screen.
- [ ] An unplayable card or out-of-turn move comes back with a readable reason.
- [ ] Dragging the draw pile draws; the discard pile refuses.
- [ ] Nobody can play for someone else's seat.
- [ ] A full game to the end: same winner on every screen.

**Feel (only real hands can judge)**
- [ ] Dropping cards anywhere (on decks, near hands, half on dice): nothing slides weirdly or ends propped up.
- [ ] Decks and bags: drag at once takes the top card; press, hold still, then drag lifts the whole thing.
- [ ] Rotation: tapping Q/E snaps to 15° steps, holding turns smoothly.
- [ ] Throwing dice feels right and they settle flat.
- [ ] The room and light: cards look white, text is readable, nothing too dark.
- [ ] The controls list (F1) explains what people try to do.
- [ ] Search a deck (right click → Search): the card grid is readable, and pressing a card and dragging it to the table feels natural.
- [ ] Hovering a deck or bag shows how many cards or items it holds.
- [ ] Sounds: picking up, dropping, shuffling, rolling, dealing feel right and are not too loud or repetitive; friends' actions are heard too.

**After the session**
- [ ] Each person sends their `Player.log` if anything broke (path below).

## Known limits

- Relay throughput was measured at about 200–240 KB/s per joining player, so custom games with large images take a while to arrive. Objects show plain colours until their image lands. The sample games use generated cards and need no download.
- If the host quits, everyone else is disconnected; there is no host migration.
- Undo/redo are off while playing online.
