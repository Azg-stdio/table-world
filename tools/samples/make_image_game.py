"""Writes a game package whose images are real files, to check asset sync (T3.4) and image loading.

Usage: python -I tools/samples/make_image_game.py <out folder> [--big]
Writes <out>/table.json and <out>/img/*.png: a 4x2 card sheet, a card back, a token, a tile and a board.
--big makes the board 2048x2048 with noise (about 12 MB) to stress chunked transfer. Images are generated here,
so the package is ours to share. Not committed: generate it where you need it.
"""
import json, os, random, struct, sys, zlib


def png(path, width, height, pixel):
    """Minimal RGB PNG writer; pixel(x, y) -> (r, g, b)."""
    rows = bytearray()
    for y in range(height):
        rows.append(0)
        for x in range(width):
            rows.extend(pixel(x, y))

    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xffffffff)

    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
    data += chunk(b"IDAT", zlib.compress(bytes(rows), 6)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(data)


COLORS = [(200, 60, 60), (60, 140, 200), (70, 170, 90), (220, 180, 50), (150, 80, 180), (230, 120, 40), (40, 160, 160), (120, 120, 120)]


def main():
    out = sys.argv[1]
    big = "--big" in sys.argv
    os.makedirs(os.path.join(out, "img"), exist_ok=True)
    noise = random.Random(7)

    cw, ch = 128, 180
    def sheet(x, y):
        cell = (y // ch) * 4 + x // cw
        lx, ly = x % cw, y % ch
        if lx < 6 or ly < 6 or lx >= cw - 6 or ly >= ch - 6:
            return (250, 250, 245)
        r, g, b = COLORS[cell]
        # Cell number as stripes, so faces are told apart at a glance.
        if 40 < ly < 140 and (lx - 20) // 12 <= cell and (lx - 20) % 12 < 8 and lx >= 20:
            return (255, 255, 255)
        return (r, g, b)
    png(os.path.join(out, "img", "cards.png"), cw * 4, ch * 2, sheet)
    png(os.path.join(out, "img", "back.png"), cw, ch, lambda x, y: (40, 50, 110) if (x // 16 + y // 16) % 2 else (70, 80, 150))
    png(os.path.join(out, "img", "token.png"), 128, 128, lambda x, y: (230, 190, 60) if (x - 64) ** 2 + (y - 64) ** 2 < 50 ** 2 else (160, 110, 30))
    png(os.path.join(out, "img", "tile.png"), 256, 256, lambda x, y: (90 + x // 4, 140, 90 + y // 4))
    size = 2048 if big else 512
    def board(x, y):
        n = noise.randrange(40) if big else 0
        return ((x * 255 // size + n) & 255, (y * 255 // size + n) & 255, (128 + n) & 255)
    png(os.path.join(out, "img", "board.png"), size, size, board)

    table = {
        "schemaVersion": "0.1",
        "game": {"id": "image-test", "name": "Image test", "minPlayers": 1, "maxPlayers": 4,
                 "description": "Free play with image files: checks asset loading and sync."},
        "assets": [
            {"id": "cards", "type": "image", "path": "img/cards.png", "columns": 4, "rows": 2},
            {"id": "back", "type": "image", "path": "img/back.png"},
            {"id": "token", "type": "image", "path": "img/token.png"},
            {"id": "tile", "type": "image", "path": "img/tile.png"},
            {"id": "board", "type": "image", "path": "img/board.png"},
        ],
        "objects": [
            {"id": "board_1", "type": "Board", "asset": "board", "size": [0.6, 0.6], "locked": True,
             "transform": {"position": [0, 0, 0.05]}},
            {"id": "deck_1", "type": "Deck", "faceAsset": "cards", "backAsset": "back",
             "transform": {"position": [-0.4, 0, -0.2], "rotation": [0, 0, 180]},
             "cards": [{"id": f"card_{i}", "faceIndex": i, "name": f"Card {i + 1}"} for i in range(8)]},
            {"id": "token_1", "type": "Token", "asset": "token", "shape": "round", "transform": {"position": [0.4, 0, -0.2]}},
            {"id": "token_2", "type": "Token", "asset": "token", "shape": "round", "transform": {"position": [0.45, 0, -0.25]}},
            {"id": "tile_1", "type": "Tile", "asset": "tile", "transform": {"position": [0.4, 0, 0.2]}},
            {"id": "die_1", "type": "Die", "faces": 6, "transform": {"position": [-0.4, 0, 0.25]}},
        ],
        "players": {"seats": [{"id": "seat_south", "color": "white"}, {"id": "seat_north", "color": "red"}]},
    }
    with open(os.path.join(out, "table.json"), "w", encoding="utf-8") as f:
        json.dump(table, f, indent=1)
    total = sum(os.path.getsize(os.path.join(out, "img", n)) for n in os.listdir(os.path.join(out, "img")))
    print(f"wrote {out} ({total // 1024} KB of images)")


if __name__ == "__main__":
    main()
