"""Writes the sample rule files games/war.json and games/uno-lite.json (docs/rules-schema.md).

Usage: python -I tools/samples/make_games.py
Both use the builtin standard deck (52 cards, no jokers). Card meta: rank 2..14 (aces high), suit name.
"""
import json, os

SUITS = ["clubs", "diamonds", "hearts", "spades"]
RANKS = ["Ace", "2", "3", "4", "5", "6", "7", "8", "9", "10", "Jack", "Queen", "King"]


def cards():
    out = []
    for i in range(52):
        suit, rank = divmod(i, 13)
        out.append({"id": f"{RANKS[rank].lower()}_{SUITS[suit]}", "faceIndex": i, "name": f"{RANKS[rank]} of {SUITS[suit]}",
                    "meta": {"rank": 14 if rank == 0 else rank + 1, "suit": SUITS[suit]}})
    return out


ASSETS = [
    {"id": "std_deck", "type": "image", "path": "builtin:standard_deck", "columns": 10, "rows": 7},
    {"id": "std_back", "type": "image", "path": "builtin:standard_back"},
]


def deck(id_, x, z, rotation_z=180, tags=None, with_cards=False, name=None):
    d = {"id": id_, "type": "Deck", "faceAsset": "std_deck", "backAsset": "std_back",
         "transform": {"position": [x, 0, z], "rotation": [0, 0, rotation_z]}, "cards": cards() if with_cards else []}
    if tags:
        d["tags"] = tags
    if name:
        d["name"] = name
    return d


def cmp(left, op, right):
    return {"compare": {"left": left, "op": op, "right": right}}


def collect_to(seat, loser):
    # Winner's own pile goes under first, then the loser's, for either seat (a fixed seat order here biased games).
    return [
        {"shuffle": {"object": f"$seat.{seat}.battle"}},
        {"shuffle": {"object": f"$seat.{loser}.battle"}},
        {"move": {"from": f"$seat.{seat}.battle", "take": "all", "to": f"$seat.{seat}.deck", "at": "bottom"}},
        {"move": {"from": f"$seat.{loser}.battle", "take": "all", "to": f"$seat.{seat}.deck", "at": "bottom"}},
        {"message": {"text": f"{seat} wins the battle ({{$seat.{seat}.deck.count}} cards)"}},
    ]


war = {
    "schemaVersion": "0.1",
    "game": {"id": "war", "name": "War", "minPlayers": 2, "maxPlayers": 2,
             "description": "Both players flip their top card; the higher rank takes both. Ties go to war. Take every card to win; after 500 battles the player with more cards wins."},
    "assets": ASSETS,
    "objects": [
        deck("deck_main", 0, 0, with_cards=True, name="Deck"),
        deck("deck_south", -0.15, -0.25, tags=["deck:seat_south"], name="South's deck"),
        deck("battle_south", -0.15, -0.08, rotation_z=0, tags=["battle:seat_south"], name="South's battle pile"),
        deck("deck_north", 0.15, 0.25, tags=["deck:seat_north"], name="North's deck"),
        deck("battle_north", 0.15, 0.08, rotation_z=0, tags=["battle:seat_north"], name="North's battle pile"),
    ],
    "players": {"seats": [{"id": "seat_south", "color": "red"}, {"id": "seat_north", "color": "blue"}]},
    "turn": {"order": "clockwise", "startingPlayer": "first", "phases": ["battle", "resolve"]},
    "phases": [
        {"id": "battle", "name": "Battle", "actions": ["play_top_card"], "advanceWhen": {"allPlayersActed": True}},
        {"id": "resolve", "name": "Resolve", "advanceWhen": {"immediate": True}, "onEnter": [
            {"if": {
                "condition": cmp("$seat.seat_south.battle.top.meta.rank", ">", "$seat.seat_north.battle.top.meta.rank"),
                "then": collect_to("seat_south", "seat_north"),
                "else": [{"if": {
                    "condition": cmp("$seat.seat_south.battle.top.meta.rank", "<", "$seat.seat_north.battle.top.meta.rank"),
                    "then": collect_to("seat_north", "seat_south"),
                    "else": [
                        {"message": {"text": "War!"}},
                        {"forEach": {"players": True, "do": [
                            {"if": {"condition": {"zoneCount": {"zone": "$it.deck", "op": "<", "value": 2}},
                                    "then": [{"message": {"text": "{$it.id} cannot go to war"}}, {"win": {"player": "$other"}}],
                                    "else": [{"move": {"from": "$it.deck", "to": "$it.battle", "faceUp": False}}]}},
                        ]}},
                        {"gotoPhase": {"phase": "battle"}},
                    ]}}]}},
        ]},
    ],
    "actions": [
        {"id": "play_top_card", "name": "Play top card", "actor": "any", "limitPerPhase": 1,
         "preconditions": [{"zoneCount": {"zone": "$actor.deck", "op": ">", "value": 0}}],
         "effects": [{"move": {"from": "$actor.deck", "to": "$actor.battle", "faceUp": True}}]},
    ],
    "events": {
        "onGameStart": [
            {"shuffle": {"object": "deck_main"}},
            {"deal": {"from": "deck_main", "count": 26, "toTag": "deck", "faceDown": True}},
        ],
        "onTurnEnd": [{"setVariable": {"name": "battles", "delta": 1}}],
    },
    "variables": {"battles": 0},
    "ui": {"panels": [{"id": "score", "title": "War", "anchor": "topRight", "elements": [
        {"type": "label", "text": "Battle {$var.battles}"},
        {"type": "counter", "text": "South's deck", "value": "$seat.seat_south.deck.count"},
        {"type": "counter", "text": "North's deck", "value": "$seat.seat_north.deck.count"},
        {"type": "button", "text": "South: play top card", "action": "play_top_card", "seat": "seat_south"},
        {"type": "button", "text": "North: play top card", "action": "play_top_card", "seat": "seat_north"},
    ]}]},
    "winConditions": [
        {"when": {"and": [{"zoneCount": {"zone": "$player.deck", "op": "==", "value": 0}},
                          {"zoneCount": {"zone": "$player.battle", "op": "==", "value": 0}}]},
         "winner": "$other", "checkAfter": "turnEnd"},
        # House rule: War can last for hours. After 500 battles the player with more cards wins.
        {"when": {"and": [{"variable": {"name": "battles", "op": ">=", "value": 500}},
                          cmp("$player.deck.count", ">", "$other.deck.count")]},
         "winner": "$player", "checkAfter": "turnEnd"},
    ],
}

uno = {
    "schemaVersion": "0.1",
    "game": {"id": "uno-lite", "name": "Simplified UNO (standard deck)", "minPlayers": 2, "maxPlayers": 4,
             "description": "Play a card matching the suit or rank of the top discard, or draw one. First to empty their hand wins."},
    "assets": ASSETS,
    "objects": [
        deck("draw", -0.08, 0, with_cards=True, name="Draw pile"),
        deck("discard", 0.08, 0, rotation_z=0, name="Discard pile"),
    ],
    "zones": [
        {"id": "hand_south", "type": "HandZone", "owner": "seat_south", "transform": {"position": [0, 0.05, -0.42]}, "size": [0.6, 0.1, 0.12]},
        {"id": "hand_north", "type": "HandZone", "owner": "seat_north", "transform": {"position": [0, 0.05, 0.42], "rotation": [0, 180, 0]}, "size": [0.6, 0.1, 0.12]},
    ],
    "players": {"seats": [{"id": "seat_south", "color": "red", "handZone": "hand_south"},
                          {"id": "seat_north", "color": "blue", "handZone": "hand_north"}]},
    "turn": {"order": "clockwise", "startingPlayer": "first", "phases": ["play"]},
    "phases": [{"id": "play", "name": "Play or draw", "actions": ["play_card", "draw_card"], "advanceWhen": {"currentPlayerEnds": True}}],
    "actions": [
        {"id": "play_card", "name": "Play a card", "actor": "current",
         "params": [{"name": "card", "type": "object", "filter": {"inZone": "$actor.hand", "type": "Card"}}],
         "preconditions": [{"or": [
             cmp("$param.card.meta.suit", "==", "$obj.discard.top.meta.suit"),
             cmp("$param.card.meta.rank", "==", "$obj.discard.top.meta.rank"),
         ]}],
         "effects": [
             {"move": {"object": "$param.card", "to": "discard"}},
             {"message": {"text": "{$actor.id} plays {$obj.discard.top.name}"}},
             {"endTurn": {}},
         ]},
        {"id": "draw_card", "name": "Draw a card", "actor": "current",
         "effects": [
             {"if": {"condition": {"zoneCount": {"zone": "draw", "op": "==", "value": 0}},
                     "then": [{"move": {"from": "discard", "take": "allButTop", "to": "draw"}}, {"shuffle": {"object": "draw"}}]}},
             {"if": {"condition": {"zoneCount": {"zone": "draw", "op": ">", "value": 0}},
                     "then": [{"move": {"from": "draw", "to": "$actor.hand", "faceUp": True}}]}},
             {"endTurn": {}},
         ]},
    ],
    "events": {"onGameStart": [
        {"shuffle": {"object": "draw"}},
        {"deal": {"from": "draw", "count": 7, "toZone": "$it.hand"}},
        {"move": {"from": "draw", "to": "discard", "faceUp": True}},
    ]},
    "winConditions": [{"when": {"zoneCount": {"zone": "$player.hand", "op": "==", "value": 0}}, "winner": "$player", "checkAfter": "anyCommand"}],
    "ui": {"panels": [{"id": "table", "title": "UNO (standard deck)", "anchor": "topRight", "elements": [
        {"type": "label", "text": "Discard: {$obj.discard.top.name}"},
        {"type": "counter", "text": "Draw pile", "value": "$obj.draw.count"},
        {"type": "counter", "text": "Cards in your hand", "value": "$viewer.hand.count"},
        {"type": "button", "text": "Draw a card", "action": "draw_card"},
    ]}]},
}

root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
for name, game in (("war", war), ("uno-lite", uno)):
    out = os.path.join(root, "games", f"{name}.json")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(game, f, indent=1)
        f.write("\n")
    print(f"wrote {out}")
