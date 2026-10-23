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
    ] + [
        {"id": f"d{f}", "type": "Die", "faces": f, "value": f, **at(-0.3 + i * 0.05, -0.28)}
        for i, f in enumerate((4, 6, 8, 10, 12, 20))
    ] + [
        {"id": "bag", "type": "Bag", "name": "Bag of tokens", **at(0.35, 0.3)},
        {"id": "bag_token_1", "type": "Token", "container": "bag", "tint": [0.2, 0.7, 0.3]},
        {"id": "bag_token_2", "type": "Token", "container": "bag", "tint": [0.2, 0.7, 0.3]},
        {"id": "bag_token_3", "type": "Token", "container": "bag", "tint": [0.2, 0.7, 0.3]},
        {"id": "coin_supply", "type": "InfiniteBag", "name": "Coins", **at(0.5, 0.3)},
        {"id": "coin", "type": "Token", "container": "coin_supply", "tint": [0.95, 0.8, 0.2]},
        {"id": "board", "type": "Board", "size": [0.3, 0.2], **at(0.45, -0.05)},
        {"id": "hero", "type": "Figure", "name": "Hero", "tint": [0.6, 0.75, 0.95], **at(0.3, -0.25)},
        {"id": "statue", "type": "Model", "asset": "placeholder_model", **at(0.4, -0.25)},
    ],
    "zones": [
        {"id": "hand_south", "type": "HandZone", "owner": "seat_south", "transform": {"position": [0, 0.05, -0.42]}, "size": [0.6, 0.1, 0.12]},
        {"id": "hand_north", "type": "HandZone", "owner": "seat_north", "transform": {"position": [0, 0.05, 0.42], "rotation": [0, 180, 0]}, "size": [0.6, 0.1, 0.12]},
        {"id": "discard", "type": "SnapPoint", "transform": {"position": [-0.05, 0, 0.1]}},
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
