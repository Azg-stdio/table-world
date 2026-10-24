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
- [ ] Rolling several dice at once (select or pick them up together, R) reads clearly for everyone.
- [ ] Hidden zone (the sample table's dark block on the left belongs to the north seat): the north player sees the Ace of hearts inside, the south player sees only the block; dropping a card in hides it from the other seat.
- [ ] Physics modes (host: right-click the felt → Physics): with semi-lock nothing gets knocked over, with locked dropped pieces stay exactly where they are put; friends see the same; a friend has no Physics row.
- [ ] Free-play turns (host: right-click the felt → Turns: In order): everyone sees whose turn it is, only that player has End turn, and "Your turn" shows up when it comes round.
- [ ] Counter and timer (sample table: Score on the right, Turn timer on the left): everyone sees the same score; a running timer shows about the same time on every screen and rings for everyone at zero.
- [ ] Flips, draws and deals look smooth on every screen, not only for the player who did them.
- [ ] Seats: a friend clicks the other free colour and sits there; clicking yours is refused; Watch turns them into a spectator who can look but not touch, and they can sit again.
- [ ] Sounds: picking up, dropping, shuffling, rolling, dealing feel right and are not too loud or repetitive; friends' actions are heard too.
- [ ] Escape menu and settings in the built game: window modes and resolutions switch cleanly, VSync and frame limit feel right, interface size is readable on each friend's monitor, volume sliders work, settings are kept after restarting.
- [ ] Chip stacks (sample table: gold chips and wound tiles at the front right): dropping a chip on the stack, dragging one off and lifting the whole stack feel like TTS; everyone sees the same count.
- [ ] Players (top bar, host): everyone shows with the name they set in Settings; promote a friend and they get Physics and Turns in the table menu; make a friend Game Master and they see your hand and north's secret area; put two friends on a team and `/t` reaches only them; mute stops a friend's chat; Remove sends them back to their own table with a message.
- [ ] Rewind (host: right-click the felt → Rewind): after a few minutes of play, stepping back puts everyone's table where it was, forward brings it back, and friends see the notice.
- [ ] Grid (host: right-click the felt → Grid): squares and hexes look right on everyone's screen and drops snap where people expect.
- [ ] Drawing (left tool bar): friends see your lines and arrows appear in your colour; Erase and "Clear my drawings" work; drawing feels smooth with a real mouse.
- [ ] Saves (host: Escape → Save & load): a named save of a game with your own images loads back with its pictures after restarting; an autosave appears after 10 minutes of play.
- [ ] Blindfold (B) and peek (Alt + Shift over a face-down card): friends read the notices; nobody finds them annoying or missing.
- [ ] Script buttons (a table whose script calls `createButton`): every player sees the buttons on the object, they darken under the pointer, a click (and a right click) does what the script says for whoever clicked, and nothing gets picked up by accident.
- [ ] Script UI (a table whose script calls `UI.setXml`, e.g. a counter with a button): it sits where the script put it on every screen size, buttons darken under the pointer and react for whoever clicks, typing in its fields never moves pieces, and clicks on it never reach the table underneath.\n- [ ] Script inputs (a table whose script calls `createInput`): clicking the field opens a box at the pointer, typing shows on every screen as you type, Enter or clicking away finishes, and keys typed there never move pieces.
- [ ] Import from Tabletop Simulator (Games → Import from Tabletop Simulator, on a computer with TTS games): a game you know imports, opens from Games, looks like it does in TTS (pieces where TTS has them, figures and boards with their images), and its buttons and panels work. Note anything that is missing or looks different. Then **Download the missing files** (Miguel first, on his own connection: it fetches the files the save names from Steam's servers, as TTS does) and reopen the game: the pictures and models fill in.\n- [ ] Look (first build): white cards, boards and papers show their shading under the lamp, as in the Editor (tone mapping, `Resources/Look/LookProfile`); if they are flat white, URP stripped the tone mapping from the build.
- [ ] Chat: Enter, type, Enter: everyone sees it with your seat colour; `/roll 2d6` shows the same dice for everyone; the Log tab reads the same on every screen and never names a card someone could not see; typing never flips or moves anything.

**After the session**
- [ ] Each person sends their `Player.log` if anything broke (path below).

## Known limits

- Relay throughput was measured at about 200–240 KB/s per joining player, so custom games with large images take a while to arrive. Objects show plain colours until their image lands. The sample games use generated cards and need no download.
- If the host quits, everyone else is disconnected; there is no host migration.
- Undo/redo are off while playing online.
- Hidden zones and other players' hands hide what is inside on screen, but every machine still receives the data (as in TTS): fine among friends, not a protection against cheating.
