"""Writes unity/Assets/_Project/Resources/Tables/sample.json: one of every object type on the default table.

Usage: python -I tools/samples/make_sample_table.py
Card indices follow the builtin standard deck (see BuiltinTextures.cs): suit * 13 + rank - 1,
suits clubs, diamonds, hearts, spades; 52/53 jokers.
"""
import json, os

SUITS = ["clubs", "diamonds", "hearts", "spades"]
RANKS = ["Ace", "2", "3", "4", "5", "6", "7", "8", "9", "10", "Jack", "Queen", "King"]

def card_entry(index):
    if index >= 52:
        return {"faceIndex": index, "name": "Red joker" if index == 52 else "Black joker", "tags": ["joker"]}
    suit, rank = divmod(index, 13)
    value = 14 if rank == 0 else rank + 1  # War: aces high
    return {"faceIndex": index, "name": f"{RANKS[rank]} of {SUITS[suit]}", "meta": {"rank": value, "suit": SUITS[suit]}}

def loose_card(id_, index, x, z):
    e = card_entry(index)
    return {"id": id_, "type": "Card", "name": e["name"], "faceAsset": "std_deck", "faceIndex": index, "backAsset": "std_back",
            "transform": {"position": [x, 0, z]}, **({"meta": e["meta"]} if "meta" in e else {})}

def at(x, z, **extra):
    return {"transform": {"position": [x, 0, z], **extra}}

table = {
    "schemaVersion": "0.1",
    "assets": [
        {"id": "std_deck", "type": "image", "path": "builtin:standard_deck", "columns": 10, "rows": 7},
        {"id": "std_back", "type": "image", "path": "builtin:standard_back"},
        {"id": "placeholder_model", "type": "model", "path": "builtin:placeholder"},
        {"id": "d6_template", "type": "image", "path": "builtin:d6_template"},
        {"id": "pawn_model", "type": "model", "path": "builtin:pawn"},
        {"id": "block_model", "type": "model", "path": "builtin:block_square"},
        {"id": "checker_model", "type": "model", "path": "builtin:checker"},
    ],
    "objects": [
        {"id": "deck_main", "type": "Deck", "name": "Standard deck", "faceAsset": "std_deck", "backAsset": "std_back",
         **at(-0.35, 0.1, rotation=[0, 0, 180]), "cards": [card_entry(i) for i in range(54)]},
        {"id": "deck_up", "type": "Deck", "name": "Face-up clubs", "faceAsset": "std_deck", "backAsset": "std_back",
         **at(-0.2, 0.1), "cards": [card_entry(i) for i in (1, 2, 3)]},
        loose_card("ace_spades", 39, -0.3, -0.12),
        loose_card("king_hearts", 38, -0.2, -0.12),
        loose_card("ten_diamonds", 22, -0.1, -0.12),
        loose_card("queen_clubs", 11, 0.0, -0.12),
        loose_card("red_joker", 52, 0.1, -0.12),
        {"id": "token_red", "type": "Token", "shape": "round", "tint": [0.8, 0.15, 0.15], **at(0.05, 0.22)},
        {"id": "token_blue", "type": "Token", "shape": "round", "tint": [0.15, 0.3, 0.8], **at(0.09, 0.22)},
        {"id": "token_square", "type": "Token", "shape": "square", "tint": [0.9, 0.75, 0.1], **at(0.13, 0.22)},
        {"id": "tile_square", "type": "Tile", "shape": "square", **at(0.05, 0.3)},
        {"id": "tile_hex", "type": "Tile", "shape": "hex", **at(0.12, 0.3)},
        {"id": "tile_circle", "type": "Tile", "shape": "circle", **at(0.19, 0.3)},
        # Built-in pieces (G38): a pawn, a block and a checker, as TTS has them without files.
        {"id": "pawn_red", "type": "Model", "name": "Red pawn", "asset": "pawn_model", "tint": [0.8, 0.15, 0.15], **at(0.6, -0.3)},
        {"id": "block", "type": "Model", "name": "Block", "asset": "block_model", "tint": [0.25, 0.45, 0.85], **at(0.66, -0.3)},
        {"id": "checker", "type": "Model", "name": "Checker", "asset": "checker_model", "kind": "chip", "tint": [0.1, 0.1, 0.1], **at(0.72, -0.3)},
        # A notecard (G27): its name is the title, its description the text printed on it.
        {"id": "house_rules", "type": "Notecard", "name": "House rules",
         "description": "Draw two at the start of your turn.\nJokers are wild.\nThe loser shuffles.", **at(0.5, 0.15)},
        # A custom die (G21): its image follows the TTS d6 template, and its faces have names.
        {"id": "event_die", "type": "Die", "name": "Event die", "faces": 6, "asset": "d6_template", "value": 2,
         "labels": ["Draw a card", "Move 2", "Swap seats", "Move 4", "Roll again", "Lose a turn"], **at(0.02, -0.28)},
        # Counted stacks (G14): drop the loose chip on the stack, drag one off, right-click to split.
        {"id": "chips_gold", "type": "Token", "name": "Gold chip", "shape": "round", "stackable": True, "count": 10,
         "tint": [0.95, 0.75, 0.2], **at(0.1, -0.28)},
        {"id": "chip_gold", "type": "Token", "name": "Gold chip", "shape": "round", "stackable": True,
         "tint": [0.95, 0.75, 0.2], **at(0.16, -0.28)},
        {"id": "wounds", "type": "Tile", "name": "Wound", "shape": "hex", "stackable": True, "count": 4,
         "tint": [0.75, 0.2, 0.2], **at(0.24, -0.28)},
    ] + [
        {"id": f"d{f}", "type": "Die", "faces": f, "value": f, **at(-0.3 + i * 0.05, -0.28)}
        for i, f in enumerate((4, 6, 8, 10, 12, 20))
    ] + [
        {"id": "bag", "type": "Bag", "name": "Bag of tokens", **at(0.35, 0.3)},
        {"id": "bag_token_1", "type": "Token", "container": "bag", "tint": [0.2, 0.7, 0.3]},
        {"id": "bag_token_2", "type": "Token", "container": "bag", "tint": [0.2, 0.7, 0.3]},
        {"id": "bag_token_3", "type": "Token", "container": "bag", "tint": [0.2, 0.7, 0.3]},
        {"id": "coin_supply", "type": "InfiniteBag", "name": "Coins", **at(0.5, 0.3)},
        {"id": "coin", "type": "Token", "name": "Coin", "container": "coin_supply", "stackable": True, "tint": [0.95, 0.8, 0.2]},
        {"id": "board", "type": "Board", "size": [0.3, 0.2], **at(0.45, -0.05)},
        {"id": "hero", "type": "Figure", "name": "Hero", "tint": [0.6, 0.75, 0.95], **at(0.3, -0.25)},
        {"id": "statue", "type": "Model", "asset": "placeholder_model", **at(0.4, -0.25)},
        loose_card("secret_card", 26, -0.6, 0.15),
        {"id": "score", "type": "Counter", "name": "Score", **at(0.62, 0.15)},
        {"id": "timer", "type": "Clock", "name": "Turn timer", "mode": "timer", "seconds": 60, "duration": 60, **at(-0.6, -0.12)},
    ],
    "zones": [
        {"id": "hand_south", "type": "HandZone", "owner": "seat_south", "transform": {"position": [0, 0.05, -0.42]}, "size": [0.6, 0.1, 0.12]},
        {"id": "hand_north", "type": "HandZone", "owner": "seat_north", "transform": {"position": [0, 0.05, 0.42], "rotation": [0, 180, 0]}, "size": [0.6, 0.1, 0.12]},
        {"id": "discard", "type": "SnapPoint", "transform": {"position": [-0.05, 0, 0.1]}},
        # Only the north seat sees inside (the default south player sees fog).
        {"id": "north_secret", "type": "HiddenZone", "name": "North's secret area", "visibleTo": ["seat_north"],
         "transform": {"position": [-0.6, 0.05, 0.15]}, "size": [0.16, 0.1, 0.2]},
        # G18: cards dropped in the market line up in a row; right-click the tiles' area to randomize where they lie.
        {"id": "market", "type": "LayoutZone", "name": "Market", "facing": "faceUp",
         "transform": {"position": [-0.56, 0.05, -0.36]}, "size": [0.3, 0.1, 0.11]},
        {"id": "tile_mix", "type": "RandomizeZone", "name": "Tile mixer",
         "transform": {"position": [0.12, 0.05, 0.3]}, "size": [0.22, 0.1, 0.08]},
    ],
    "players": {"seats": [
        {"id": "seat_south", "color": "red", "handZone": "hand_south"},
        {"id": "seat_north", "color": "blue", "handZone": "hand_north"},
    ]},
}

root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
out = os.path.join(root, "unity", "Assets", "_Project", "Resources", "Tables", "sample.json")
os.makedirs(os.path.dirname(out), exist_ok=True)
with open(out, "w", encoding="utf-8", newline="\n") as f:
    json.dump(table, f, indent=1)
    f.write("\n")
print(f"wrote {out} ({len(table['objects'])} objects)")
