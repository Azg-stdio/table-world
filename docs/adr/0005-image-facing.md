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

Buttons and object XmlUI are the exception: they read upright from −Z at yaw 0. The evidence:
- Riftbound's buttons have no rotation on objects with yaw 0 by south seats and yaw 180 by north seats (38 each).
- Riftbound's playmat menus have no rotation either.
- Gloomhaven adds `rotation="0 0 180"` or `{0,180,0}` to UI on objects turned 180°.

## Decision

Rotations in our table model are TTS's, unchanged (principle 5), and the renderer follows TTS's conventions:
- **Images:** card, deck, tile, token and board faces (`MeshFactory.Image*`) turn their images 180°, both sides.
  Figures, counters, clocks and notecards keep their own layout.
- **Facing a zone:** anything placed to face a zone gets the zone's yaw + 180 (`Geometry.FacingYaw`). That covers a deal
  into a hand, a drop into a hand, rules moves with a face, and layout zones.
- **Hands:** cards standing in a hand are turned to face the owner.
- **Inspection:** the inspect camera takes an image's top edge as the piece's −Z side.
- **Buttons and object UI:** they keep reading upright from −Z.
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
