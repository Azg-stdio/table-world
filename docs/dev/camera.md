# The camera: seated in first person, or lifted

For whoever changes the camera, the player's own chalker or how hands of cards are shown. The player's camera has two
heights. **Seated** (the default) it looks out of the player's own chalker's eyes in their chair. **Lifted** it is
the TTS-style orbit camera over the table. Neither is a separate camera: lifting and sitting blend one camera between
the two over 0.4 s, so the view rises out of your head instead of cutting.

```mermaid
flowchart LR
    input["Mouse, keys"]
    cam["TableCamera<br/>seated / lifted"]
    interaction["TableInteraction<br/>O, Space, H, hand up"]
    chalkers["ChalkersView<br/>your chalker + the others"]
    chalker["Chalker<br/>pose, hold, reach, first person"]
    model["ChalkerModel<br/>bones follow the points"]
    hold["HandHold<br/>fan of cards"]
    table["TableView<br/>HoldHand: cards follow the hands"]
    bar["HandBar<br/>steps aside while seated"]
    presence["Presence frame<br/>hand up, lean"]

    input --> cam
    input --> interaction
    interaction -- "Sit / Lift / HandUp" --> cam
    cam -- "Lean, head turn" --> chalkers
    interaction -- "HandUp, held object" --> chalkers
    chalkers --> chalker
    chalker --> hold
    chalker --> model
    chalkers -- "CardHold" --> table
    chalkers -- "Eyes" --> cam
    cam --> bar
    interaction --> presence
    presence -- "other players" --> chalkers
```

| Box | Files |
|---|---|
| TableCamera | [TableCamera.cs](../../unity/Assets/_Project/Scripts/View/TableCamera.cs): `Sit`, `Lift`, `ToggleSeated`, `SetSeat`, `FirstPerson`, `Lean`, the seated controls (`Seated`) |
| TableInteraction | [TableInteraction.cs](../../unity/Assets/_Project/Scripts/View/TableInteraction.cs): `ToggleSeatView` (O), `HandUp`, `ToggleHandUp` (H), `UpdateHand` |
| ChalkersView | [ChalkersView.cs](../../unity/Assets/_Project/Scripts/View/Chalkers/ChalkersView.cs): `Yours`, `Yourself`, `Covers`, `Prepare`, `HoldCards` |
| Chalker | [Chalker.cs](../../unity/Assets/_Project/Scripts/View/Chalkers/Chalker.cs): `HoldingCards`, `RaiseHand`, `CardHold`, `ReachFor`, `LeanIn`, `SteadyEyes`, `HideHead`, `Ghost`; [ChalkerGhost.cs](../../unity/Assets/_Project/Scripts/View/Chalkers/ChalkerGhost.cs), [Ghost.shader](../../unity/Assets/_Project/Shaders/Ghost.shader) |
| ChalkerModel | [ChalkerModel.cs](../../unity/Assets/_Project/Scripts/View/Chalkers/ChalkerModel.cs): `HideBody`, holding hands, `FaceDepth` |
| HandHold | [HandHold.cs](../../unity/Assets/_Project/Scripts/View/Chalkers/HandHold.cs), [TwoBoneIk.cs](../../unity/Assets/_Project/Scripts/View/Chalkers/TwoBoneIk.cs) |
| TableView | [TableView.cs](../../unity/Assets/_Project/Scripts/View/TableView.cs): `HoldHand`, `HeldByChalker`, `HandCardCount` |
| HandBar | [HandBar.cs](../../unity/Assets/_Project/Scripts/View/Hud/HandBar.cs) |
| Presence frame | [Presence.cs](../../core/Net/Presence.cs) (`HandUp`, `Lean`), [NetworkTableSession.Presence.cs](../../unity/Assets/_Project/Scripts/View/Net/NetworkTableSession.Presence.cs) |
| Tests | [FirstPersonTests.cs](../../unity/Assets/_Project/Tests/PlayMode/FirstPersonTests.cs), [SeatingTests.cs](../../unity/Assets/_Project/Tests/PlayMode/SeatingTests.cs), [PresenceTests.cs](../../core.tests/Net/PresenceTests.cs) |

## Seated and lifted

| | Seated | Lifted |
|---|---|---|
| Right drag | turns the head (110° each way, 35° up, 80° down) | orbits |
| Wheel | leans in over the table (four steps); sitting back, three steps out in a row lift you (each gives a little; a pause settles back) | zooms |
| WASD, middle drag, Shift + right drag | lifts you, then pans | pans |
| Q / E (nothing hovered) | turns the head | turns the camera |
| Space | stays seated, head straight | sits you (stays lifted with the free camera setting) |
| O | lifts you | sits you |
| P, Z | top-down, quick zoom; again goes back to the seat | as before |

The game starts seated. The setting **Camera: Free camera only** (`GameSettings.FreeCamera`) keeps the orbit camera for
Space and the start of a game, for motion sickness or taste; O still sits you. Your hand then stands at your edge of
the table as in TTS instead of in your chalker's hands. A spectator has no seat and stays lifted.

The camera does not own the seat. Every frame `ChalkersView` poses your own chalker (leaning with `TableCamera.Lean`,
the head turned where you look) and hands its eyes back with `SetSeat`. The eye point is the shapes' eyes with the head
held straight (`Chalker.SteadyEyes`), whatever model is worn, so every model holds the cards in the same place in your
view. Only leaning in carries it (gliding, `Chalker.LeanRate`): turning the head turns the view in place instead of
swinging it round the neck, and emotes, sway and breathing leave it still.

## Your own chalker

Every seat has a chair, yours too, and your chalker sits in it. Lifted, you see it from outside: that one is you.
Seated, the camera is inside its head:

- The head draws its shadow only (`Chalker.HideHead`), and a worn model draws only its shadow
  (`ChalkerModel.HideBody`), except its own forearms and hands. Those are cut out of its meshes the first time (the
  triangles its forearm and hand bones carry, on the same skeleton) and drawn alone, so you see the model's real hands
  holding the cards. Cutting needs the mesh readable: an imported model needs Read/Write on (Tom has it).
- Without a model, or with one that cannot be cut, the shapes' forearms and fists show instead, slimmer and smaller,
  the arms 20 % longer (`Chalker.FirstPersonReach`, `FirstPersonArm`, `FirstPersonHand`).
- Your shadow on the table is still the whole model.

**Seen through when lifted.** Where your chalker stands between the lifted camera and the table
(`ChalkersView.Covers`: points on the table top behind it, inside the screen box its body covers), it fades to 30 %
(`ChalkerGhost.SeeThrough`) and a thin rim in your seat's colour goes round it at full strength, so the table shows
through and you still know which one is you. Only your own, only on your screen; from across the table it is solid.

- Lit materials are swapped for `TableWorld/Ghost`: a depth-only pass, then URP Lit's own forward pass made
  transparent. Only the nearest surface of each mesh shows, so the body does not show its insides through itself, and
  the lighting is the same as when solid.
- The rim is the selection outline's shader on a copy of each mesh (a skinned model's baked each frame), its rim pass
  depth-tested (`_ZTest` LessEqual) so the cards it holds cover it.

Your mouth moves with your microphone, your emotes play on it, and while you carry something its right arm reaches for
it (`Chalker.ReachFor`, within 1.1 m). Other players' chalkers reach for what they carry the same way.

## Holding cards

A seat whose chalker is shown holds its hand's cards in a fan (`HandHold`) instead of standing them at its hand zone.
The cards stay in the hand zone as far as the rules are concerned: this is view only, like the TTS stand it replaces.
`TableView.HoldHand` places each card every frame, at the render rate, so cards move with the hands. A seat nobody sits
in keeps its cards standing at the zone, and so does yours with the free camera setting: read from above, TTS style,
with the hand bar.

- **Lowered**: the grip over the table's edge at chest height, the cards leaning back 40°. Seated, they peek in at the
  bottom of the view.
- **Raised**: under the chin, the cards nearly upright, facing the eyes. A face further forward (a snout) pushes the fan
  out to stay 0.12 m in front of it (`HandHold.FaceClearance`, `ChalkerModel.FaceDepth`).

The fan opens up to 64°, 9° between cards. A worn model's wrists sit well below the fan's foot and a little behind it,
its fingers lying up the cards' backs, so the faces stay readable.

**Resting hands.** A seated worn model whose hand rests on the table (lower than `ChalkerModel.RestHeight` over the
table top, holding nothing) lays it flat: palm down, fingers forward and a little out, lifted by the hand's own
thickness, so a cartoon hand does not sink into the table's rim. The back of the hand is measured in the bind pose.

**Raising the hand.** The cards come up when the pointer goes onto one of them. They stay up while the pointer is over
them, or across their width and no higher than their top. They drop 0.35 s after it leaves. H
(`GameAction.HandUp`) keeps them up until pressed again. Other players see your hand go up and you lean in (presence
`hand`, `lean`).

**The hand bar** (the TTS hand at the bottom of the screen) steps aside while you are seated and your chalker holds the
cards, and comes back when you lift out.

## Checking it

`FirstPersonTests` covers the following, with `SeatingTests` for the seat view:

- the cards held in the fan;
- the hand bar stepping aside;
- raising (the cards in front of the eyes, facing them, the hand on the fan);
- lifting out;
- turning the head without moving the eyes;
- fading where it covers the table, solid again from across it;
- the free camera setting.

To look at it, load a table with a hand of cards in Play mode and render `Camera.main` into a texture: `capture
game_view` can return a stale frame while the Editor is in the background.
