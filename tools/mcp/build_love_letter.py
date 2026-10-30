"""Builds Love Letter through the MCP tools only, the way an agent would, and saves it as games/love-letter.json. Every
call is a tools/call to the server over stdio; a failed call stops the build.

    dotnet build mcp-server                  (or -c Release: pass the DLL path)
    python -X utf8 -I tools/mcp/build_love_letter.py [path to TableWorld.Mcp.dll]

The card art is the TTS mod's (Workshop 412185866, the 2012 AEG edition): the game names the two sheets by their web
address and every machine downloads them when the table loads; nothing of it is in the repository (CLAUDE.md §7).

Played with the hands (Miguel, 2026-10-29):
- your turn starts with a card drawn into your hand; drag one of your two cards onto the table to play it, face up on
  your discard row;
- a card that picks a player asks for a click on that player; the Guard then asks which card to name;
- the Priest's look shows the card big to the one who looks only; the Baron shows each hand to the other;
- the round ends when one player is left or the deck runs out; the highest hand (then the highest discards) takes an
  affection token and the hands are turned up for ten seconds (the host may deal sooner); the winner starts the next.

The general pieces it needed (2026-10-29): `revive` and `turnTo` effects for rounds, `show` with `to` for a private look.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mcp_session import McpSession, compare  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DLL = sys.argv[1] if len(sys.argv) > 1 else None
OUT = os.path.join(ROOT, "games", "love-letter.json")

# The mod's sheets: 10 x 7 faces (the first 17 used), and one back.
FACES = "http://i.imgur.com/tysJ4C9.jpg"
BACK = "http://i.imgur.com/YhHoTYN.jpg"
# kind, value, copies, first index on the sheet, English and Spanish names, what the card does.
CARDS = [
    ("guard", 1, 5, 0, "Guard", "Guardia", "Name a card other than Guard and choose a player: if they hold it, they are out of the round.",
     "Nombra una carta que no sea Guardia y elige a un jugador: si la tiene, queda fuera de la ronda."),
    ("priest", 2, 2, 5, "Priest", "Sacerdote", "Look at another player's hand.", "Mira la mano de otro jugador."),
    ("baron", 3, 2, 7, "Baron", "Barón", "You and another player compare hands in secret: the lower one is out of the round.",
     "Tú y otro jugador comparan sus manos en secreto: el de la carta menor queda fuera de la ronda."),
    ("handmaid", 4, 2, 10, "Handmaid", "Doncella", "Until your next turn, other players' cards cannot choose you.",
     "Hasta tu próximo turno, las cartas de los demás no pueden elegirte."),
    ("prince", 5, 2, 12, "Prince", "Príncipe", "Choose any player (you too): they discard their hand and draw a new card.",
     "Elige a cualquier jugador (también a ti): descarta su mano y roba una carta nueva."),
    ("king", 6, 1, 14, "King", "Rey", "Trade hands with another player.", "Intercambia tu mano con la de otro jugador."),
    ("countess", 7, 1, 15, "Countess", "Condesa", "If you hold her with the King or a Prince, you must play her.",
     "Si la tienes junto al Rey o un Príncipe, debes jugarla."),
    ("princess", 8, 1, 16, "Princess", "Princesa", "If you discard her, you are out of the round.", "Si la descartas, quedas fuera de la ronda."),
]
REFERENCE_INDEX = 9
NAMES = {kind: (en, es) for kind, _, _, _, en, es, _, _ in CARDS}

# The game's own sentences are strings (@key), so they are translated like every other text.
STRINGS = {
    'plays': ('{$actor.name} plays the {$param.card.name}.', '{$actor.name} juega {$param.card.name}.'),
    'protected': ('{$actor.name} is protected until their next turn.', '{$actor.name} queda protegido hasta su próximo turno.'),
    'noTarget': ('Everyone else is protected: the card has no effect.', 'Todos los demás están protegidos: la carta no tiene efecto.'),
    'chooseOther': ('{$actor.name}: click the player you choose.', '{$actor.name}: haz clic en el jugador que eliges.'),
    'notYourself': ('Choose another player.', 'Elige a otro jugador.'),
    'isProtected': ('{$param.target.name} is protected by the Handmaid.', '{$param.target.name} está protegido por la Doncella.'),
    'guardAims': ('{$actor.name} points the Guard at {$var.target.name}: name a card.', '{$actor.name} apunta la Guardia a {$var.target.name}: nombra una carta.'),
    'guardRight': ('Right: {$var.target.name} held it.', 'Acierto: {$var.target.name} la tenía.'),
    'guardWrong': ('Wrong: {$var.target.name} does not hold it.', 'Fallo: {$var.target.name} no la tiene.'),
    'priestLooks': ("{$actor.name} looks at {$param.target.name}'s hand.", '{$actor.name} mira la mano de {$param.target.name}.'),
    'priestSees': ('{$param.target.name} holds the {$param.target.hand.top.name}.', '{$param.target.name} tiene {$param.target.hand.top.name}.'),
    'baronCompares': ('{$actor.name} and {$param.target.name} compare hands in secret.', '{$actor.name} y {$param.target.name} comparan sus manos en secreto.'),
    'baronTie': ('A tie: both stay in.', 'Empate: los dos siguen.'),
    'baronSeesTarget': ('{$param.target.name} holds the {$param.target.hand.top.name}.', '{$param.target.name} tiene {$param.target.hand.top.name}.'),
    'baronSeesActor': ('{$actor.name} holds the {$actor.hand.top.name}.', '{$actor.name} tiene {$actor.hand.top.name}.'),
    'princeTargets': ('{$actor.name} makes {$param.target.name} discard their hand.', '{$actor.name} hace que {$param.target.name} descarte su mano.'),
    'kingTrades': ('{$actor.name} and {$param.target.name} trade hands.', '{$actor.name} y {$param.target.name} intercambian sus manos.'),
    'countess': ('You hold the Countess with the King or a Prince: you must play the Countess.',
                 'Tienes a la Condesa con el Rey o un Príncipe: debes jugar la Condesa.'),
    'roundOver': ('Round {$var.round} is over: the hands are turned up.', 'Termina la ronda {$var.round}: se muestran las manos.'),
    'winsRound': ('{$it.name} wins the round and an affection token.', '{$it.name} gana la ronda y una ficha de afecto.'),
    'roundStarts': ('Round {$var.round}: {$var.toWin} tokens win the game.', 'Ronda {$var.round}: {$var.toWin} fichas ganan la partida.'),
    'nextRound': ('The next round is dealt in a moment.', 'La siguiente ronda se reparte en un momento.'),
}
for kind, (en, es) in NAMES.items():
    if kind != "guard":
        STRINGS[f"guess_{kind}"] = (f"{{$actor.name}} names the {en}.", f"{{$actor.name}} nombra {es}.")


def at(text):
    return {"message": {"text": "@" + text}}


def tell(text, to):
    return {"message": {"text": "@" + text, "to": to}}


def kind_is(ref, kind):
    return compare(ref, kind)


def out(seat):
    """A seat out of the round: its hand goes face up onto its discard row."""
    return [{"eliminate": {"seat": seat}},
            {"move": {"from": f"{seat}.hand", "take": "all", "to": f"{seat}.discard", "faceUp": True}}]


# After a card's effect: the round ends when one player is left or the deck is empty; otherwise the next turn.
FINISH = [{"if": {"condition": {"or": [compare("$alive.count", 1, "<="), {"zoneCount": {"zone": "deck", "op": "==", "value": 0}}]},
                  "then": [{"gotoPhase": {"phase": "round_end"}}],
                  "else": [{"endTurn": {}}]}}]

# Everything back into the deck, everyone back in, a card set aside face down (three more face up with two players),
# one card each.
NEW_ROUND = [
    {"forEach": {"players": True, "do": [
        {"move": {"from": "$it.hand", "take": "all", "to": "deck"}},
        {"move": {"from": "$it.discard", "take": "all", "to": "deck"}},
        {"revive": {"seat": "$it"}},
    ]}},
    {"move": {"from": "burn", "take": "all", "to": "deck"}},
    {"move": {"from": "aside", "take": "all", "to": "deck"}},
    {"setVariable": {"name": "protected", "value": []}},
    {"setVariable": {"name": "round", "delta": 1}},
    {"shuffle": {"object": "deck"}},
    {"move": {"from": "deck", "to": "burn", "faceUp": False}},
    {"if": {"condition": compare("$alive.count", 2), "then": [{"move": {"from": "deck", "count": 3, "to": "aside", "faceUp": True}}]}},
    {"deal": {"from": "deck", "count": 1, "toZone": "$it.hand"}},
    at("roundStarts"),
]

LUA = r"""
-- A card's value, from its tag v1..v8.
local function value(o)
  for _, tag in ipairs(o.getTags()) do
    local v = tag:match("^v(%d)$")
    if v then return tonumber(v) end
  end
  return 0
end

local function side(seat) return seat:match("^seat_(.+)$") end

local function cards(zone)
  local z = getObjectFromGUID(zone)
  return z and z.getObjects() or {}
end

-- The Countess must be played when the other card is the King or a Prince.
function checkPlay(ctx)
  local card = getObjectFromGUID(ctx.params.card)
  local v = card and value(card) or 0
  if v ~= 5 and v ~= 6 then return end
  for _, o in ipairs(cards("hand_" .. side(ctx.actor))) do
    if value(o) == 7 then return false, Game.text("countess") end
  end
end

-- Who wins the round: the highest card in hand among those still in, then the highest total of discards; a tie past
-- both shares the round.
function scoreRound(ctx)
  local best, winners = -1, {}
  for _, seat in ipairs(Game.getSeats()) do
    if not Game.isOut(seat) then
      local high, discards = 0, 0
      for _, o in ipairs(cards("hand_" .. side(seat))) do high = math.max(high, value(o)) end
      for _, o in ipairs(cards("discard_" .. side(seat))) do discards = discards + value(o) end
      local score = high * 100 + discards
      if score > best then
        best, winners = score, {seat}
      elseif score == best then
        winners[#winners + 1] = seat
      end
    end
  end
  Game.setVariable("winners", winners)
end
"""

with McpSession(DLL, name="build_love_letter") as mcp:
    call = mcp.call
    call("new_game", id="love-letter", name="Love Letter", players=4, minPlayers=2, maxPlayers=4, shape="round",
         description="Get your letter to the Princess: hold the highest card when the deck runs out, or be the last one "
                     "in the round. Each turn draw a card and play one of your two; each card does something to another "
                     "player. Win rounds for affection tokens: 7 win with two players, 5 with three, 4 with four.")
    call("set_translations", language="en", gameLanguage="en",
         translationsJson=json.dumps({f"strings.{key}": text for key, (text, _) in STRINGS.items()}))

    call("add_asset", id="faces", url=FACES, type="image", columns=10, rows=7)
    call("add_asset", id="back", url=BACK, type="image")

    call("set_variables", variablesJson=json.dumps({
        "round": 0, "toWin": 4, "played": "", "target": "", "protected": [], "winners": [], "targets": 0}))
    call("add_resource", resourceJson=json.dumps({"id": "tokens", "name": "Affection tokens", "initial": 0, "min": 0}))
    call("set_script", target="global", code=LUA)

    # ---- the table ----------------------------------------------------------------------------------------------------
    # Small and close (Miguel, 2026-10-29): all a player needs is the others' faces and the cards between them, so the
    # round table is 1.2 m across (new_game makes 1.6) and the hands move in with its edge.
    import math
    TABLE = 1.2
    HAND_RADIUS = TABLE / 2 - 0.12
    document = call("get_rules_document")
    for zone in document["zones"]:
        if zone["type"] == "HandZone":
            x, y, z = zone["transform"]["position"]
            scale = HAND_RADIUS / math.hypot(x, z)
            zone["transform"]["position"] = [round(x * scale, 3), y, round(z * scale, 3)]
            zone["size"] = [0.4, zone["size"][1], zone["size"][2]]
    call("set_rules_document", documentJson=json.dumps(document))
    call("set_environment", room="tavern", theme="parchment", json=json.dumps({"table": {"size": [TABLE, TABLE]}}))

    deck = []
    for kind, value, copies, first, en, _, does, _ in CARDS:
        for n in range(copies):
            deck.append({"id": kind if n == 0 else f"{kind}_{n + 1}", "name": en, "description": does, "faceIndex": first + n,
                         "meta": {"kind": kind, "value": value}, "tags": [kind, f"v{value}"]})
    # In the middle: the deck, the card set aside face down beside it, the three face up (two players) in a row below.
    call("create_deck", id="deck", name="Deck", cards=deck, faceAsset="faces", backAsset="back", x=-0.045, z=0.03)
    call("define_zone", zoneJson=json.dumps({"id": "burn", "type": "ScriptingZone", "name": "Set aside",
                                             "transform": {"position": [0.045, 0.05, 0.03]}, "size": [0.08, 0.1, 0.11]}))
    call("define_zone", zoneJson=json.dumps({"id": "aside", "type": "LayoutZone", "name": "Set aside face up", "facing": "faceUp",
                                             "direction": "rightThenDown", "maxPerRow": 3, "spacing": 0.006,
                                             "transform": {"position": [0, 0.05, -0.085]}, "size": [0.21, 0.1, 0.1]}))
    # Where the King's trade passes the cards (inside one move, never seen).
    call("define_zone", zoneJson=json.dumps({"id": "swap", "type": "ScriptingZone", "name": "Trade",
                                             "transform": {"position": [0.7, 0.05, 0.42]}, "size": [0.08, 0.1, 0.1]}))
    # The affection tokens wait in a bag beside the deck; a round's winner gets one in front of them.
    call("create_supply", id="token_pool", piece="gem", color="red", mode="bag", count=13, name="Affection tokens", x=0.14, z=0.06)

    # Before each seat: the discard row, the tokens won, and a list of the cards.
    document = call("get_rules_document")
    hands = {z["id"]: z for z in document["zones"] if z["type"] == "HandZone"}
    for seat in document["players"]["seats"]:
        hand = hands[seat["handZone"]]
        hx, _, hz = hand["transform"]["position"]
        turn = hand["transform"].get("rotation", [0, 0, 0])
        yaw = math.radians(turn[1])
        inward = (math.sin(yaw), math.cos(yaw))
        right = (math.cos(yaw), -math.sin(yaw))

        def spot(ahead, along):
            return [round(hx + inward[0] * ahead + right[0] * along, 3), 0.05, round(hz + inward[1] * ahead + right[1] * along, 3)]

        side = seat["id"].split("_")[1]
        call("define_zone", zoneJson=json.dumps({
            "id": f"discard_{side}", "type": "LayoutZone", "name": "Discards", "tags": [f"discard:{seat['id']}"],
            "facing": "faceUp", "direction": "rightThenDown", "maxPerRow": 4, "spacing": 0.004, "manualOnly": True,
            "transform": {"position": spot(0.155, -0.035), "rotation": turn}, "size": [0.28, 0.1, 0.19]}))
        call("define_zone", zoneJson=json.dumps({
            "id": f"tokens_{side}", "type": "ScriptingZone", "name": "Affection tokens", "tags": [f"tokens:{seat['id']}"],
            "transform": {"position": spot(0.1, 0.2), "rotation": turn}, "size": [0.09, 0.1, 0.06]}))
        call("place_object", seat=seat["id"], inward=0.21, along=0.17, objectJson=json.dumps({
            "id": f"reference_{side}", "type": "Card", "name": "List of cards", "faceAsset": "faces", "backAsset": "back",
            "faceIndex": REFERENCE_INDEX, "locked": True}))

    # ---- the turn -----------------------------------------------------------------------------------------------------
    call("add_phase", phaseJson=json.dumps({
        "id": "play", "name": "Play a card", "actions": ["play_card"],
        # The Handmaid's protection lasts until the protected player's next turn; then a card is drawn.
        "onEnter": [{"setVariable": {"name": "protected", "remove": "$current"}},
                    {"move": {"from": "deck", "to": "$current.hand", "faceUp": True}}]}))
    call("add_phase", phaseJson=json.dumps({"id": "choose", "name": "Choose a player", "actions": ["target"]}))
    call("add_phase", phaseJson=json.dumps({"id": "guess", "name": "Name a card", "actions": ["guess"]}))
    # Everyone sees the hands for a few seconds (the host can deal sooner with Next), then the next round is dealt and
    # its winner starts it. Nobody needs to press anything: a player knocked out may be the one at the screen.
    call("add_phase", phaseJson=json.dumps({"id": "round_end", "name": "Round over", "actions": [], "seconds": 10, "hostEnds": True,
                                            "next": "new_round", "onEnter": [
        at("roundOver"),
        {"lua": {"call": "scoreRound"}},
        {"forEach": {"players": True, "do": [
            {"if": {"condition": {"not": {"compare": {"left": "$it.out", "op": "==", "right": True}}},
                    "then": [{"move": {"from": "$it.hand", "take": "all", "to": "$it.discard", "faceUp": True}}]}},
            {"if": {"condition": {"inList": {"list": "$var.winners", "value": "$it"}}, "then": [
                {"setResource": {"player": "$it", "resource": "tokens", "delta": 1}},
                {"move": {"from": "token_pool", "to": "$it.tokens"}},
                at("winsRound"),
            ]}},
        ]}},
        {"sound": {"play": "fanfare", "volume": 0.6}},
        at("nextRound"),
    ]}))

    # A card that chooses another player looks for one first: everyone else out or protected, and it does nothing.
    count_targets = [
        {"setVariable": {"name": "targets", "value": 0}},
        {"forEach": {"players": True, "do": [{"if": {"condition": {"and": [
            {"not": {"compare": {"left": "$it.out", "op": "==", "right": True}}},
            compare("$it.id", "$actor.id", "!="),
            {"not": {"inList": {"list": "$var.protected", "value": "$it"}}}]},
            "then": [{"setVariable": {"name": "targets", "delta": 1}}]}}]}},
    ]
    call("add_action", actionJson=json.dumps({
        "id": "play_card", "name": "Play a card", "actor": "current",
        "params": [{"name": "card", "type": "object", "filter": {"inZone": "$actor.hand", "type": "Card"}}],
        "effects": [
            {"lua": {"call": "checkPlay"}},
            {"move": {"object": "$param.card", "to": "$actor.discard", "faceUp": True}},
            {"setVariable": {"name": "played", "value": "$param.card.meta.kind"}},
            at("plays"),
            {"sound": {"play": "page", "volume": 0.5}},
            {"if": {"condition": kind_is("$var.played", "princess"), "then": out("$actor") + FINISH}},
            {"if": {"condition": kind_is("$var.played", "countess"), "then": FINISH}},
            {"if": {"condition": kind_is("$var.played", "handmaid"),
                    "then": [{"setVariable": {"name": "protected", "add": "$actor"}}, at("protected")] + FINISH}},
            # The Prince may pick the player too: with nobody else to pick, the game picks them by itself (auto).
            {"if": {"condition": kind_is("$var.played", "prince"), "then": count_targets + [
                {"if": {"condition": compare("$var.targets", 0, ">"), "then": [at("chooseOther")]}},
                {"gotoPhase": {"phase": "choose"}}]}},
            {"if": {"condition": {"or": [kind_is("$var.played", k) for k in ("guard", "priest", "baron", "king")]}, "then": count_targets + [
                {"if": {"condition": compare("$var.targets", 0),
                        "then": [at("noTarget")] + FINISH,
                        "else": [
                            # Only when there is a real choice: one possible player is picked by itself.
                            {"if": {"condition": compare("$var.targets", 1, ">"), "then": [at("chooseOther")]}},
                            {"gotoPhase": {"phase": "choose"}}]}}]}},
        ]}))

    def refuse(key):
        return {"refuse": {"reason": "@" + key}}

    call("add_action", actionJson=json.dumps({
        # Whom a card may pick are preconditions (with what the player reads when they click someone else), so when only
        # one player can be picked the game picks them by itself (auto): two players left, the others protected.
        "id": "target", "name": "Choose", "actor": "current", "onPlayer": True, "auto": True,
        "params": [{"name": "target", "type": "seat"}],
        "preconditions": [
            {"check": {"condition": {"not": {"and": [compare("$param.target.id", "$actor.id"), compare("$var.played", "prince", "!=")]}},
                       "reason": "@notYourself"}},
            {"check": {"condition": {"not": {"and": [compare("$param.target.id", "$actor.id", "!="),
                                                     {"inList": {"list": "$var.protected", "value": "$param.target"}}]}},
                       "reason": "@isProtected"}},
        ],
        "effects": [
            {"setVariable": {"name": "target", "value": "$param.target.id"}},
            {"if": {"condition": kind_is("$var.played", "guard"), "then": [at("guardAims"), {"gotoPhase": {"phase": "guess"}}]}},
            {"if": {"condition": kind_is("$var.played", "priest"), "then": [
                at("priestLooks"),
                {"show": {"object": "$param.target.hand.top", "to": "$actor"}},
                tell("priestSees", "$actor"),
            ] + FINISH}},
            {"if": {"condition": kind_is("$var.played", "baron"), "then": [
                at("baronCompares"),
                {"show": {"object": "$param.target.hand.top", "to": "$actor"}},
                {"show": {"object": "$actor.hand.top", "to": "$param.target"}},
                tell("baronSeesTarget", "$actor"),
                tell("baronSeesActor", "$param.target"),
                {"if": {"condition": compare("$actor.hand.top.meta.value", "$param.target.hand.top.meta.value", ">"),
                        "then": out("$param.target"),
                        "else": [{"if": {"condition": compare("$actor.hand.top.meta.value", "$param.target.hand.top.meta.value", "<"),
                                         "then": out("$actor"), "else": [at("baronTie")]}}]}},
            ] + FINISH}},
            {"if": {"condition": kind_is("$var.played", "prince"), "then": [
                at("princeTargets"),
                {"if": {"condition": kind_is("$param.target.hand.top.meta.kind", "princess"),
                        "then": out("$param.target"),
                        "else": [
                            {"move": {"from": "$param.target.hand", "take": "all", "to": "$param.target.discard", "faceUp": True}},
                            {"if": {"condition": {"zoneCount": {"zone": "deck", "op": ">", "value": 0}},
                                    "then": [{"move": {"from": "deck", "to": "$param.target.hand", "faceUp": True}}],
                                    "else": [{"move": {"from": "burn", "to": "$param.target.hand", "faceUp": True}}]}},
                        ]}},
            ] + FINISH}},
            {"if": {"condition": kind_is("$var.played", "king"), "then": [
                at("kingTrades"),
                {"move": {"from": "$actor.hand", "take": "all", "to": "swap"}},
                {"move": {"from": "$param.target.hand", "take": "all", "to": "$actor.hand", "faceUp": True}},
                {"move": {"from": "swap", "take": "all", "to": "$param.target.hand", "faceUp": True}},
            ] + FINISH}},
        ]}))

    guesses = [k for k, *_ in CARDS if k != "guard"]
    call("add_action", actionJson=json.dumps({
        "id": "guess", "name": "Name a card", "actor": "current", "button": True,
        "params": [{"name": "card", "type": "choice", "options": guesses,
                    "labels": {k: f"{NAMES[k][0]} ({v})" for k, v, *_ in CARDS if k != "guard"}}],
        "effects": [{"if": {"condition": compare("$param.card", k), "then": [at(f"guess_{k}")]}} for k in guesses] + [
            {"if": {"condition": compare("$var.target.hand.top.meta.kind", "$param.card"),
                    "then": [at("guardRight")] + out("$var.target"),
                    "else": [at("guardWrong")]}},
        ] + FINISH}))

    call("add_phase", phaseJson=json.dumps({"id": "new_round", "name": "New round", "actions": [],
                                            "onEnter": NEW_ROUND + [{"turnTo": {"seat": "$var.winners.0"}}]}))

    call("add_event", eventName="onGameStart", effectsJson=json.dumps([
        {"if": {"condition": compare("$alive.count", 2), "then": [{"setVariable": {"name": "toWin", "value": 7}}]}},
        {"if": {"condition": compare("$alive.count", 3), "then": [{"setVariable": {"name": "toWin", "value": 5}}]}},
    ] + NEW_ROUND))
    call("set_turn", phases="play,choose,guess", order="clockwise", startingPlayer="random")
    call("set_win_condition", winConditionJson=json.dumps({
        "when": compare("$player.res.tokens", "$var.toWin", ">="), "winner": "$player", "checkAfter": "anyCommand"}))

    # ---- the place, the panel, the marks ------------------------------------------------------------------------------
    library = call("list_music", limit=200)
    tracks = {t["url"] for t in library["tracks"]}
    VOL3 = "library:fantasy-rpg-music-pack-vol-3/"
    tavern = [VOL3 + f"ambient-{n}-loop" for n in range(1, 11)]
    if any(t not in tracks for t in tavern):
        print("note: some tracks are not in this computer's music library (import AlkaKrab's Fantasy RPG Music Pack Vol.3 with import_music)")
    call("set_music", playlistsJson=json.dumps({"tavern": tavern}), start="tavern", shuffle=True, volume=0.8)

    document = call("get_rules_document")
    for obj in document["objects"]:
        if obj["id"] == "deck":
            obj["locked"] = True
        # A small pouch, not a jar: it sits low between the deck and the players.
        if obj["id"] == "token_pool":
            obj["transform"]["scale"] = [0.6, 0.6, 0.6]
    # A silver arrow over each player the Handmaid protects.
    document["marks"] = [{"id": "protected", "seats": "$var.protected", "shape": "arrow", "color": [0.85, 0.9, 1.0]}]
    document["ui"] = {"panels": [{
        "id": "score", "title": "Love Letter", "anchor": "right", "visibleTo": [],
        "elements": [
            {"type": "label", "text": "Round {$var.round} · {$var.toWin} tokens win"},
            {"type": "label", "text": "Cards left in the deck: {$obj.deck.count}"},
            {"type": "label", "text": "Protected: {$var.protected}"},
        ] + [{"type": "label", "text": f"{{$seat.{s['id']}.name}}: {{$seat.{s['id']}.res.tokens}}"} for s in document["players"]["seats"]],
    }]}
    call("set_rules_document", documentJson=json.dumps(document))

    # ---- Spanish ------------------------------------------------------------------------------------------------------
    ES = {
        "game.name": "Love Letter",
        "game.description": "Haz llegar tu carta a la Princesa: ten la carta más alta cuando se acabe el mazo, o sé el último en la "
                            "ronda. En tu turno roba una carta y juega una de tus dos; cada carta le hace algo a otro jugador. "
                            "Gana rondas para conseguir fichas de afecto: ganan 7 con dos jugadores, 5 con tres y 4 con cuatro.",
        "actions.play_card.name": "Jugar una carta",
        "actions.target.name": "Elegir",
        "actions.guess.name": "Nombrar una carta",
        "phases.new_round.name": "Nueva ronda",
        "phases.play.name": "Juega una carta",
        "phases.choose.name": "Elige a un jugador",
        "phases.guess.name": "Nombra una carta",
        "phases.round_end.name": "Fin de la ronda",
        "resources.tokens.name": "Fichas de afecto",
        "objects.deck.name": "Mazo",
        "zones.burn.name": "Apartada",
        "zones.aside.name": "Apartadas boca arriba",
        "zones.swap.name": "Intercambio",
        "objects.token_pool.name": "Fichas de afecto",
    }
    ES.update({f"strings.{key}": text for key, (_, text) in STRINGS.items()})
    for kind, value, *_ in CARDS:
        ES[f"params.guess.card.{kind}"] = f"{NAMES[kind][1]} ({value})" if kind != "guard" else None
    ES = {k: v for k, v in ES.items() if v is not None}
    for card in deck:
        es = next(c for c in CARDS if c[0] == card["meta"]["kind"])
        ES[f"cards.{card['id']}.name"] = es[5]
        ES[f"cards.{card['id']}.description"] = es[7]
    ES["ui.score.title"] = "Love Letter"
    ES["ui.score.0.text"] = "Ronda {$var.round} · ganan {$var.toWin} fichas"
    ES["ui.score.1.text"] = "Cartas en el mazo: {$obj.deck.count}"
    ES["ui.score.2.text"] = "Protegidos: {$var.protected}"
    for n, seat in enumerate(document["players"]["seats"]):
        ES[f"ui.score.{n + 3}.text"] = f"{{$seat.{seat['id']}.name}}: {{$seat.{seat['id']}.res.tokens}}"
    for obj in document["objects"]:
        if obj.get("container") == "token_pool":
            ES[f"objects.{obj['id']}.name"] = "Ficha de afecto"
    for seat in document["players"]["seats"]:
        side = seat["id"].split("_")[1]
        ES[f"objects.reference_{side}.name"] = "Lista de cartas"
        ES[f"zones.discard_{side}.name"] = "Descartes"
        ES[f"zones.tokens_{side}.name"] = "Fichas de afecto"
    call("set_translations", language="es", translationsJson=json.dumps(ES, ensure_ascii=False))
    left = call("translate_game", language="es")
    if left.get("left", 0):
        raise SystemExit("untranslated:\n" + "\n".join(f"{k}: {v}" for k, v in left.get("missing", {}).items()))

    call("validate_rules")
    call("save_game", path=OUT)
    print(f"saved {OUT}")
