# Playtest guide (T3.6)

The M3-lite playtest: you host War and two friends join over the internet with a join code. It closes the playtest gate. This page covers what to send, what each person does, and what to write down.

## 1. Build and send

1. With the Editor open: `unity command build run --target StandaloneWindows64 --outputPath Builds/Win64/TableWorld.exe --confirm true` (or File → Build Profiles → Windows → Build).
2. Zip `unity/Builds/Win64/` **without** the `TableWorld_BackUpThisFolder_ButDontShipItWithYourGame` folder (about 105 MB). The sample games (War, Simplified UNO) are already inside, in `TableWorld_Data/StreamingAssets/Games`.
3. Send the zip. Windows SmartScreen will warn because the exe is unsigned: **More info → Run anyway**.

## 2. Host (you)

1. Start `TableWorld.exe`.
2. **Games → War**. The table loads with one deck.
3. **Host online**. The address field shows a 6-character code, already copied to the clipboard. Paste it in your chat.
4. Wait until the top bar says `code XXXXXX, 2 player(s) joined`. You play South; joiners get North, then any free seat.
5. **Start game**, in the panel at the bottom left. Each player presses **Play top card** for their own seat (top right, or the bottom-left panel).

War has two seats, so the third person gets no seat and can only watch and move objects. For three players, also try **Simplified UNO** (load it before **Host online**, or load it while hosting: everyone receives the new table).

## 3. Friends

1. Start `TableWorld.exe`.
2. Type the code in the address field (replacing `127.0.0.1:7777`) and press **Join**.
3. The top bar shows `playing as seat_north` once the table arrives.

## 4. What to write down

Note these in `docs/milestones/M3-multiplayer.md` (simulation record) or tell the agent:

- Did everyone join on the first try? Any error text in the top bar?
- Lag: time between pressing a button and seeing the card move, for the host and for the friends.
- Anything that differed between screens: card positions, the War counters, who won.
- Confusing controls (the full list is in `docs/tts-interactions.md`; **Alt** looks at a card, right-click opens menus).
- Logs, if something broke: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Table World\Player.log` on each machine.

## Known limits

- Relay throughput was measured at about 200–240 KB/s per joining player, so custom games with large images take a while to arrive. Objects show plain colours until their image lands. The sample games use generated cards and need no download.
- If the host quits, everyone else is disconnected; there is no host migration.
- Undo/redo are off while playing online.
