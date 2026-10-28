# Preparing your assets

For designers getting card art, tokens, boards and 3D models ready for a Table World game. It says what files the
game reads, what sizes they end up on the table, and what to do so they look sharp and behave well. Your agent can
add every file for you (`add_asset`, see [MCP tools by job](mcp-tools-guide.md)); this page is about making the files.

## Where files live

A game is one JSON file plus the files it names. When your agent adds a file that is not already next to the game, it
copies it into a `<game>-assets/` folder beside the game file, so the game keeps working if you move the original:

```text
my-game.json
my-game-assets/
  cards.png        ← a sheet of card faces
  back.png
  board.jpg
  pawn.glb
```

Paths in the game are relative to the game file and must stay inside its folder. When you host, Table World sends
these files to every player who joins, and each machine keeps them in a local cache, so a second session downloads
nothing. A file can also be named by a web address (http or https): each player then downloads it themselves.

| Kind | File types | Used by |
|---|---|---|
| Image | PNG, JPEG | cards, decks, tokens, tiles, boards, figures, custom dice |
| 3D model | GLB (glTF binary), glTF, OBJ | models |
| Sound | MP3, OGG, WAV | music and sound effects |
| Document | PDF | rulebooks and sheets shown on the table |

## Images: the size limit

Every image is shrunk to at most **4096 pixels on its longest side** (2048 on the Low graphics setting). It keeps its
proportions, and a sheet keeps its grid. Bigger files are not an error, only wasted download for your players.

That limit matters most for card sheets: it is shared by every card on the sheet.

| Sheet | Pixels per card at 4096 px | Looks |
|---|---|---|
| 10 × 7 (TTS's default, 70 cards) | about 409 × 585 | fine on the table, soft when you zoom in to read small text |
| 7 × 5 (35 cards) | about 585 × 819 | sharp at the zoom players use to read |
| 5 × 4 (20 cards) | about 819 × 1024 | as sharp as it gets |

If your cards have small print, use several smaller sheets rather than one big one. A deck can mix cards from as many
sheets as you like.

## Cards and decks

- **Size on the table:** a standard card is 63.5 × 88.9 mm (a poker card). Your agent can set another size per deck
  (tarot, mini cards, landscape cards).
- **Proportions:** draw each card in the same proportions as its size on the table (5:7 for a poker card), or it is
  stretched to fit.
- **Sprite sheets:** cards sit in a grid of `columns` × `rows`, read **row by row from the top left**; the first card is
  index 0. This is the same layout as Tabletop Simulator's custom decks, so TTS sheets work as they are. Leave no
  gaps or borders between cells: the grid is the image divided evenly.
- **Backs:** one back image for the whole deck, or a sheet of backs when cards have different backs (give each card
  its back's index).
- **Single images:** a card can also be one image of its own; a sheet is only better because it is one download.
- **Cards made of text:** cards that are only words (a name, a number, a rule) need no image at all: the agent can
  make text cards, and they can be translated (see [Languages](languages.md)).

## Tokens, tiles, boards and figures

| Piece | Default size | Image |
|---|---|---|
| Round or square token | 25 mm across, 3 mm thick | printed on both faces; square for round tokens (the corners are cut off) |
| Image token | 25 mm on its longer side | keeps the image's proportions |
| Tile | 50 mm square (hexes and circles too), 3 mm thick | square, or any proportions when the tile keeps its image's aspect |
| Board | 50 × 50 cm, 4 mm thick | stretched to the board: draw it in the board's proportions |
| Figure (a standee) | 3 cm wide, 5 cm tall, on a round base | the front of the standee |

Every size can be changed per piece. An **image token is cut along its image's transparent edges**, as in Tabletop
Simulator: draw a meeple, a coin or a rune on a transparent background and the piece takes that shape (holes inside
the shape are filled; tiny specks are ignored). The size still counts the whole image, so trim empty margins or the
piece comes out smaller than you meant. A figure is still the rectangle of its image: give it a background you are
happy to see.

Stackable tokens and tiles (chips, coins, damage counters) are drawn as one counted stack, so a pile of 40 coins is one
image, not 40.

## Dice

Standard dice (d4, d6, d8, d10, d12, d20) need no files: give their faces labels if they are not numbers. A custom die
image follows TTS's template for its kind; for a d6 that is a square image whose top third is unused, then two rows of
three faces: `2 4 5` over `1 3 6`, with the 1 and the 6 printed upside down.

## 3D models

Use **GLB** (one file with its textures inside). A `.gltf` that keeps its buffers and textures in separate files loads
from your own disk, but only the `.gltf` itself is sent to players, so they would see a placeholder. OBJ also works
(the format TTS custom models use), with one image for its colour.

- **Scale:** glTF is in metres, so model at real size: a 3 cm pawn is 0.03 units tall. If a model was made in another
  unit, the agent can set its scale (`meshScale`: 0.01 for a model in centimetres).
- **Up and forward:** Y up. The model lands where its origin is and then falls onto the table, so put the origin at the
  bottom centre of the piece.
- **Triangles:** keep a game piece under about **20 000 triangles**; a detailed hero figure can take 50 000 to 100 000.
  The hard limit is 500 000 triangles and 64 MB per file: above that the piece shows as a grey placeholder and a
  warning names the file.
- **Textures:** 1024 or 2048 pixels is plenty for a piece in the hand; base colour, normal map, metal and roughness,
  emission, cut-out and see-through materials are drawn. Animations are not played (models stand still).
- **Parts and physics:** each part of a model (each mesh) gets its own collision shape, wrapped tight around it like
  shrink-wrap. That wrap has no holes or hollows: a cup or a box does not hold dice put into it, and an arch is solid
  underneath. A model of more than 24 parts, or one with a skeleton, collides as one box around it. If a piece must
  hold things, build it from several solid parts (a base and four walls).
- **What a player sees first:** the piece shows as a grey placeholder for the moment its file loads (usually under a
  second on your own machine; on a player's machine after the download).

## Sounds and PDFs

Music and sounds play from MP3, OGG or WAV; OGG is the smallest for the quality. A PDF shows on the table as a sheet
of paper, drawn at 1536 pixels on its longer side, and at 2560 when a player opens it to read; any page size works.

## Before you share the game

- Open every deck and check the card count and order: a sheet with the wrong columns or rows shifts every card.
- Look at a piece up close: if small text is soft, your sheet has too many cards for the 4096 px limit.
- Check the folder size: everything in it is sent to each player who joins. Large JPEG boards (quality 85 to 90) are
  much smaller than PNG and look the same.
- Use only files you made or have the right to use. Playing privately asks nothing; publishing to the catalog (coming later) will ask you to declare that you made them or hold the rights.
