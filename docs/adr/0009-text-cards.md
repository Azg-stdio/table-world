# ADR 0009 — Text cards are drawn by the game, from their words

Status: **accepted** (2026-10-25, M2 / T4.11; Miguel chose "the game renders them live").

## Context

Designers prototype long before they have art. Tornaris, the first outside game tried through the MCP, has art for its
board and its 20 monsters but none for its 42 equipment designs, 54 events and 50 day/night cards. TTS cards are always
images, so a TTS designer makes text cards in an image editor or a card generator and re-exports them after every change.

Options:
1. **The game draws the faces.** A card carries its words (`text`); the game lays them out and draws the face on each
   machine when it is first shown.
2. **The MCP server bakes images.** The server renders words into sprite sheets once; the game sees ordinary image
   cards.

## Decision

Option 1.
- **Model (core):** `CardText` (title, kind, body, footer, corner, art + artIndex) on `Card` and `DeckCard`;
  `CardStyle` (paper, ink, accent, backPaper, backInk, backLabel) on `Deck`, `Card` and `DeckCard`, where a card's own
  style overrides its deck's. A card shows its text face when it has `text` and no face image; it shows a drawn back
  when it has no back image and has `text` or a style. Text and style follow cards in and out of decks, splits and groups.
- **Drawing ids:** `TextFaces` names each drawing `text:f<hash>` (face) or `text:b<hash>` (back). The hash covers what
  is printed: words, colours, art and the card's proportions. Identical cards share one drawing, and an edited card
  gets a new one. Views ask the asset library for these ids like any image, and `AssetUse` knows them, so a finished
  drawing redraws exactly the pieces that show it.
- **Drawing (Unity):** `TextCardPainter` lays cards out with UI Toolkit, in the HUD's font, on an offscreen 2048 × 2048
  panel, 8 at a time (cells 512 px wide). Two frames later it reads them back, and `TextureBaker` compresses them and
  makes mipmaps like any decoded image. Layout is `Resources/UI/TextCard.uss`. Colours and fitted font sizes come from
  the card's data, set in code. Text on coloured bands picks dark or light ink by luminance.
- **When:** only when a face or back is first needed (shown on the table, in the hand bar, or in a search). A
  200-card deck costs nothing until its cards are seen. A face with art waits up to 10 s for its image and is drawn
  again when the image arrives later.
- **MCP:** `create_text_deck` builds a deck from a list of cards, with `count` for copies and a deck style.

## Consequences

- Nothing extra crosses the network: the words are part of the table, and every peer draws its own copy.
- Faces are drawn on the main thread: about 8 cards every 2 frames. A first look at a whole spread-out deck fills in
  over a few frames.
- Each drawing costs about 0.25 MB of video memory once compressed (BC1, 512 × 716 with mipmaps).
- TTS has no text cards. The importer never produces them, and an exporter to TTS would have to bake images (option 2)
  at that point.
- Body text is sized from its length, not measured. Very long texts on cards with art get small; the card zoom helps.
  Real measuring (UI Toolkit text metrics) is the next step if designers find the fit poor.
