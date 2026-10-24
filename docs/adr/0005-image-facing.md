# ADR 0005 — Flat images face +Z at yaw 0, as in TTS

Status: **proposed** (2026-10-24, M7 / T8.1; written by the agent, to be confirmed by Miguel).

## Context

We used to draw a card's, tile's, token's or board's image upright for a viewer at −Z (the south seat) when the piece
had yaw 0. A card dealt into a hand took the hand zone's yaw.

Imported TTS tables showed every image upside down to its owner. The saves are consistent about the reason. In Riftbound:
- Red's hand zone (south, z −37.5) has yaw 0, and red's playmat (a `Custom_Tile`) has yaw 180.
- Green's hand zone (north) has yaw 180, and green's playmat has yaw 0.

So TTS draws images upright from +Z at yaw 0, and a piece that faces the south seat has yaw 180. Cards in a TTS hand
are likewise turned 180° from the hand zone.

Buttons follow the same rule. Riftbound's evidence:
- Every south-side Recycle button and Points counter lies at yaw 180 with unrotated buttons; the north-side ones lie at yaw 0.
- Its deck counts use rotation `{0,0,180}` only when the deck is face down.

(An earlier reading paired objects with the nearest hand zone and concluded the opposite; it was corrected on 2026-10-24.)

Object XmlUI is the exception: it reads upright from −Z at yaw 0. The evidence:
- Riftbound's playmat menus have no rotation and sit on unturned holders by the south seats.
- Gloomhaven adds `rotation="0 0 180"` to UI on objects turned 180°.

## Decision

Rotations in our table model are TTS's, unchanged (principle 5), and the renderer follows TTS's conventions:
- **Images:** card, deck, tile, token and board faces (`MeshFactory.Image*`) turn their images 180°, both sides.
  Figures, counters, clocks and notecards keep their own layout.
- **Facing a zone:** anything placed to face a zone gets the zone's yaw + 180 (`Geometry.FacingYaw`). That covers a deal
  into a hand, a drop into a hand, rules moves with a face, and layout zones.
- **Hands:** cards standing in a hand are turned to face the owner.
- **Inspection:** the inspect camera takes an image's top edge as the piece's −Z side.
- **Buttons and inputs:** they read upright from +Z at rotation 0, like images (`ScriptButtonsView`, `ScriptInputsView`).
  Buttons drawn in the same place show in the order the script made them (TTS's outlined-text trick).
- **Object XmlUI:** it reads upright from −Z (`ObjectUiView`).
- **Our own content:** the sample table, War, UNO-lite and the MCP server's `create_deck` place pieces at yaw 180 for
  the south seat and 0 for the north.

## Consequences

- Imported tables look as they do in TTS. TTS scripts that compute rotations (`setRotation({0,180,0})` to face a
  player) behave as written.
- Tables saved before this change show their cards and tiles turned 180°. Only the samples existed; they were
  regenerated.
- Anyone placing pieces by hand (agents, rules) must use yaw 180 to face the south seat. The MCP tools do this for them.

## Not decided

Whether TTS custom figures (`Figurine_Custom`) and custom boards follow the same rule for every image layout. No local
mod shows a difference yet.
