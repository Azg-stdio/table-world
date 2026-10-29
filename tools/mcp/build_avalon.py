"""M10 T11.6: builds Avalon from docs/rulebooks/avalon.txt through the MCP tools only, the way an agent would, and
saves it as games/avalon.json. Every call is a tools/call to the server over stdio; a failed call stops the build.

    dotnet build mcp-server -c Release      (or Debug: pass the DLL path)
    python -I tools/mcp/build_avalon.py [path to TableWorld.Mcp.dll]

Played with the hands, as at a real table (Miguel, 2026-10-28):
- each player finds their role card in their hand; nobody is told who the others are;
- the ritual: timed eyes-closed steps (the minions see each other, raise their thumbs for Merlin, Merlin and Morgana for
  Percival), with a banner for the awake and a countdown the host can skip;
- the leader clicks the players they choose (again to take one off): an arrow floats over each, a crown over the leader;
- everyone lays an Approve or Reject card face down on their spot; they turn over together when the last is down;
- the team lays Success or Fail cards on the quest pile, which is shuffled and turned over one by one.

The tools used are the general ones (new_game, set_roles, set_variables, set_script, add_phase, add_action, add_event,
define_zone, place_object, create_supply, create_text_deck, set_turn, get_rules_document and set_rules_document for the
score panel and locks, set_translations, translate_game, validate_rules, save_game); nothing in the server knows Avalon.

The mood (M12, 2026-10-29): a tavern room with the parchment theme; blue for the loyal and red for the minions wherever
their names are read, green Approve and red Reject; the tavern playlist under the talk and the dark one through the
ritual and the Assassin's choice, from the player's music library (list_music); the room goes dark and cold when eyes
close, red when the Assassin strikes; a gong, drums, bells and a sting mark the moments.
"""
import json
import math
import os
import subprocess
import sys
import tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DLL = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "mcp-server", "bin", "Debug", "net9.0", "TableWorld.Mcp.dll")
OUT = os.path.join(ROOT, "games", "avalon.json")

games = tempfile.mkdtemp()
server = subprocess.Popen(["dotnet", DLL, "--no-bridge", "--games", games], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                          stderr=subprocess.DEVNULL, text=True, encoding="utf-8")
next_id = [1]


def rpc(method, params=None, notify=False):
    message = {"jsonrpc": "2.0", "method": method}
    if params is not None:
        message["params"] = params
    if not notify:
        message["id"] = next_id[0]
        next_id[0] += 1
    server.stdin.write(json.dumps(message) + "\n")
    server.stdin.flush()
    if notify:
        return None
    while True:
        line = server.stdout.readline()
        if not line:
            raise SystemExit("the server stopped")
        try:
            reply = json.loads(line)
        except ValueError:
            continue
        if reply.get("id") == message["id"]:
            return reply


def call(tool, **arguments):
    reply = rpc("tools/call", {"name": tool, "arguments": arguments})
    text = reply["result"]["content"][0]["text"]
    try:
        body = json.loads(text)
    except ValueError:
        body = {"ok": True, "text": text}
    if body.get("ok") is False:
        print(f"{tool} failed:\n{json.dumps(body, indent=2)}")
        server.kill()
        sys.exit(1)
    print(f"{tool}: ok")
    return body


rpc("initialize", {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "build_avalon", "version": "1"}})
rpc("notifications/initialized", notify=True)

# A round table: ten round it, everyone sees everyone (whose eyes are open, whose thumb is up).
call("new_game", id="avalon", name="Avalon", players=10, minPlayers=5, maxPlayers=10, shape="round",
     description="Hidden loyalties: good completes three quests, evil fails three or finds Merlin at the end. "
                 "A leader picks each quest's team, everyone votes on it, and the team succeeds or fails it in secret.")

# The game's own sentences are strings (@key), so they can be translated like every other text.
STRINGS = {
    'leads': ('{$current.name} leads quest {$var.quest}: click {$var.size} players for the team (again to take one off), then propose it.',
              '{$current.name} lidera la misión {$var.quest}: haz clic en {$var.size} jugadores para el equipo (otra vez para sacar a uno) y propónlo.'),
    'proposes': ('{$actor.name} proposes {$var.team}. Lay Approve or Reject face down on your spot.',
                 '{$actor.name} propone a {$var.team}. Pon Aprobar o Rechazar boca abajo en tu lugar.'),
    'questStarts': ('{$var.team}: lay Success or Fail face down on the quest pile.',
                    '{$var.team}: pongan Éxito o Fracaso boca abajo en el montón de la misión.'),
    'teamFull': ('The team already has {$var.size} players: click one to take them off first.',
                 'El equipo ya tiene {$var.size} jugadores: haz clic en uno para sacarlo antes.'),
    'notYourSpot': ("That is another player's spot.", 'Ese es el lugar de otro jugador.'),
    'oneVote': ('There is a card on your spot already: take it back to change your vote.',
                'Ya hay una carta en tu lugar: retírala para cambiar tu voto.'),
    'onePlayed': ('You have played your card for this quest.', 'Ya jugaste tu carta en esta misión.'),
    'loyalSucceed': ('The loyal servants of Arthur can only play Success.', 'Los leales servidores de Arturo solo pueden jugar Éxito.'),
    'fiveRejected': ('Five teams rejected in a row.', 'Cinco equipos rechazados seguidos.'),
    'questFails': ('Quest {$var.quest} fails.', 'La misión {$var.quest} fracasa.'),
    'questSucceeds': ('Quest {$var.quest} succeeds.', 'La misión {$var.quest} tiene éxito.'),
    'threeFailed': ('Three quests failed.', 'Tres misiones fracasaron.'),
    'threeSucceeded': ('Three quests succeeded. Assassin: click the player you think is Merlin.',
                       'Tres misiones tuvieron éxito. Asesino: haz clic en quien creas que es Merlín.'),
    'wasMerlin': ('{$param.target.name} was Merlin.', '{$param.target.name} era Merlín.'),
    'wasNotMerlin': ('{$param.target.name} was not Merlin.', '{$param.target.name} no era Merlín.'),
}
call("set_translations", language="en", gameLanguage="en",
     translationsJson=json.dumps({f"strings.{key}": text for key, (text, _) in STRINGS.items()}))

# Roles without `knows`: nobody is told who the others are; the ritual shows them.
ROLES = [
    {"id": "merlin", "name": "Merlin", "team": "good", "description": "You know the minions. Steer good quietly: if the Assassin finds you, evil wins."},
    {"id": "percival", "name": "Percival", "team": "good", "optional": True, "description": "One of the two thumbs you saw is Merlin. Protect Merlin; the other is Morgana."},
    {"id": "servant", "name": "Loyal servant", "team": "good", "count": "fill", "description": "Always play Success. Reject teams with minions on them."},
    {"id": "assassin", "name": "Assassin", "team": "evil", "description": "Make quests fail. If good wins three, guess Merlin: right, and evil wins."},
    {"id": "morgana", "name": "Morgana", "team": "evil", "optional": True, "description": "Make quests fail. Percival may take you for Merlin: play along."},
    {"id": "mordred", "name": "Mordred", "team": "evil", "optional": True, "description": "Make quests fail. Merlin does not know you are evil."},
    {"id": "oberon", "name": "Oberon", "team": "evil", "optional": True, "description": "Make quests fail. The other minions do not know you, nor you them."},
    {"id": "minion", "name": "Minion of Mordred", "team": "evil", "count": "fill", "description": "Get on teams and play Fail. Pretend to be loyal."},
]
call("set_roles", rolesJson=json.dumps({
    "teams": [{"id": "good", "name": "Loyal servants of Arthur", "color": "#4a8cff"},
              {"id": "evil", "name": "Minions of Mordred", "color": "#d8322a"}],
    "list": ROLES,
    "byPlayers": {"5": {"good": 3, "evil": 2}, "6": {"good": 4, "evil": 2}, "7": {"good": 4, "evil": 3},
                  "8": {"good": 5, "evil": 3}, "9": {"good": 6, "evil": 3}, "10": {"good": 6, "evil": 4}},
}))

call("set_variables", variablesJson=json.dumps({
    "quest": 1, "size": 2, "needFails": 1, "team": [], "successes": 0, "fails": 0, "rejections": 0,
    "played": [], "voted": [], "percival": False}))
# Who opens their eyes with the minions (all of them but Oberon): only the host's rules may hold it.
call("set_variables", variablesJson=json.dumps({"minionEyes": [], "thumbsMerlin": [], "thumbsPercival": []}),
     secret="minionEyes,thumbsMerlin,thumbsPercival")

call("set_script", target="global", code=r"""
SIZES = { [5] = {2, 3, 2, 3, 3}, [6] = {2, 3, 4, 3, 4}, [7] = {2, 3, 3, 4, 4},
          [8] = {3, 4, 4, 5, 5}, [9] = {3, 4, 4, 5, 5}, [10] = {3, 4, 4, 5, 5} }

-- The team size and the fails it takes for the quest about to be played.
function setQuest(ctx)
  local players = #Game.getSeats()
  local quest = Game.getVariable("quest")
  Game.setVariable("size", SIZES[players][quest])
  if quest == 4 and players >= 7 then Game.setVariable("needFails", 2) else Game.setVariable("needFails", 1) end
end

-- ---- where things are -------------------------------------------------------------------------------------------

HOME = {}

local function at(id)
  local o = getObjectFromGUID(id)
  return o and o.getPosition()
end

local function put(id, p)
  local o = getObjectFromGUID(id)
  if o and p then o.setPositionSmooth({x = p.x, y = p.y + 1, z = p.z}) end
end

local function number(seat) return seat:match("(%d+)$") end

-- Before a seat: a share of the way from the middle of the table to its hand, and to the player's left by `left`.
local function before(seat, share, left)
  local p = at("hand_" .. number(seat))
  local x, z = p.x * share, p.z * share
  if left then
    local length = math.sqrt(p.x * p.x + p.z * p.z)
    x, z = x + p.z / length * left, z - p.x / length * left
  end
  return {x = x, y = p.y, z = z}
end

local function startsWith(text, prefix) return text and text:sub(1, #prefix) == prefix end

-- Every card on the table whose own id starts with one of `prefixes`, in hands and on the given zones.
local function cardsIn(zones, prefixes)
  local found = {}
  for _, zone in ipairs(zones) do
    local z = getObjectFromGUID(zone)
    if z then
      for _, o in ipairs(z.getObjects()) do
        for _, prefix in ipairs(prefixes) do
          if startsWith(o.getGUID(), prefix) then found[#found + 1] = o end
        end
      end
    end
  end
  return found
end

local function hands()
  local list = {}
  for _, seat in ipairs(Game.getSeats()) do list[#list + 1] = "hand_" .. number(seat) end
  return list
end

-- A card of a deck into a seat's hand (the deck names it by the card's own id).
local function deal(deck, id, seat)
  getObjectFromGUID(deck).takeObject({guid = id, position = at("hand_" .. number(seat)), rotation = {x = 0, y = 0, z = 0}})
end

-- ---- the start ------------------------------------------------------------------------------------------------------

PIECES = {"reject_marker"}

-- Each player's role card into their hand, the minions' eyes for the ritual, where every piece starts.
function setUp(ctx)
  for _, id in ipairs(PIECES) do HOME[id] = at(id) end
  local next = {}
  local eyes = {}
  local forMerlin = {}
  local forPercival = {}
  for _, seat in ipairs(Game.getSeats()) do
    local role = Game.getRole(seat)
    next[role] = (next[role] or 0) + 1
    local id = "role_" .. role
    if next[role] > 1 then id = id .. "_" .. next[role] end
    deal("role_deck", id, seat)
    if Game.getTeam(seat) == "evil" and role ~= "oberon" then eyes[#eyes + 1] = seat end
    -- The thumbs the game raises: every minion's but Mordred's for Merlin; Merlin's and Morgana's for Percival.
    if Game.getTeam(seat) == "evil" and role ~= "mordred" then forMerlin[#forMerlin + 1] = seat end
    if role == "merlin" or role == "morgana" then forPercival[#forPercival + 1] = seat end
    if role == "percival" then Game.setVariable("percival", true) end
  end
  Game.setVariable("minionEyes", eyes)
  Game.setVariable("thumbsMerlin", forMerlin)
  Game.setVariable("thumbsPercival", forPercival)
end

-- The five quest cards for this many players, out of the box onto the middle of the table; the box goes with the rest.
QUESTS = {}
function layQuests(ctx)
  local players = #Game.getSeats()
  local group = players >= 8 and 8 or players
  local left, right = at("reject_1"), at("reject_5")
  local unit = (right.x - left.x) / 0.32   -- the track's ends are 0.32 m apart: table units per metre
  local box = getObjectFromGUID("quest_box")
  for q = 1, 5 do
    local card = box.takeObject({guid = "quest_" .. q .. "_" .. group,
      position = {x = (-0.36 + 0.18 * (q - 1)) * unit, y = left.y + 0.3, z = 0.05 * unit}, rotation = {x = 0, y = 180, z = 0}})
    QUESTS[q] = card and card.getGUID()
  end
  box.destruct()
end

-- ---- choosing the team ------------------------------------------------------------------------------------------

-- ---- the team vote: Approve and Reject cards ----------------------------------------------------------------------

local function spots()
  local list = {}
  for _, seat in ipairs(Game.getSeats()) do list[#list + 1] = "vote_" .. number(seat) end
  return list
end

-- Every vote card back into its deck: those on the spots and those left in hands.
VOTES_SHOWN = 0
local function collectVotes()
  VOTES_SHOWN = VOTES_SHOWN + 1
  local deck = getObjectFromGUID("vote_deck")
  local all = cardsIn(spots(), {"aye", "nay"})
  for _, card in ipairs(cardsIn(hands(), {"aye", "nay"})) do all[#all + 1] = card end
  for _, card in ipairs(all) do deck.putObject(card) end
end

function votesOut(ctx)
  -- A quick new vote: last one's cards come back first.
  collectVotes()
  for k, seat in ipairs(Game.getSeats()) do
    deal("vote_deck", k == 1 and "aye" or ("aye_" .. k), seat)
    deal("vote_deck", k == 1 and "nay" or ("nay_" .. k), seat)
  end
end

-- The cards laid down turn over together, and go back to their deck a moment later with those left in hands.
function votesShown(ctx)
  for _, card in ipairs(cardsIn(spots(), {"aye", "nay"})) do card.flip() end
  local shown = VOTES_SHOWN
  Wait.time(function() if VOTES_SHOWN == shown then collectVotes() end end, 5)
end

-- ---- the quest: Success and Fail cards ------------------------------------------------------------------------------

-- Every quest card back into its deck: from the pile, turned over, or left in hands. A turning-over still running stops.
QUEST_SHOWN = 0
SHOWN = {}
local function collectQuest()
  QUEST_SHOWN = QUEST_SHOWN + 1
  local deck = getObjectFromGUID("quest_deck")
  local pile = getObjectFromGUID("quest_pile")
  for _ = 1, #pile.getObjects() do
    local card = pile.takeObject({position = at("quest_drop")})
    if card then deck.putObject(card) end
  end
  for _, card in ipairs(SHOWN) do deck.putObject(card) end
  SHOWN = {}
  for _, card in ipairs(cardsIn(hands(), {"success", "fail"})) do deck.putObject(card) end
end

function questOut(ctx)
  collectQuest()
  local team = Game.getVariable("team") or {}
  -- Two cards each, so every hand looks the same: the loyal can only succeed (two Success), a minion chooses.
  local fails, successes = 0, 0
  local function nextSuccess()
    successes = successes + 1
    return successes == 1 and "success" or ("success_" .. successes)
  end
  for _, seat in ipairs(team) do
    deal("quest_deck", nextSuccess(), seat)
    if Game.getTeam(seat) == "evil" then
      fails = fails + 1
      deal("quest_deck", fails == 1 and "fail" or ("fail_" .. fails), seat)
    else
      deal("quest_deck", nextSuccess(), seat)
    end
  end
end

-- A card laid on the quest pile goes into it, face down: nobody can tell later whose it was.
function toPile(ctx)
  local card = getObjectFromGUID(ctx.it)
  if card then getObjectFromGUID("quest_pile").putObject(card) end
end

-- The pile is shuffled and its cards turned over one by one; then they and the cards not played go back.
function questShown(ctx)
  local shown = QUEST_SHOWN
  -- The cards not played leave the hands at once: everyone has played.
  local deck = getObjectFromGUID("quest_deck")
  for _, card in ipairs(cardsIn(hands(), {"success", "fail"})) do deck.putObject(card) end
  Wait.time(function()
    if QUEST_SHOWN ~= shown then return end
    local pile = getObjectFromGUID("quest_pile")
    pile.shuffle()
    local count = #pile.getObjects()
    local row = at("reveal_row")
    local unit = (at("reject_5").x - at("reject_1").x) / 0.32
    for i = 1, count do
      Wait.time(function()
        if QUEST_SHOWN ~= shown then return end
        local card = getObjectFromGUID("quest_pile").takeObject({
          position = {x = row.x + (i - (count + 1) / 2) * 0.08 * unit, y = row.y + 0.3, z = row.z},
          rotation = {x = 0, y = 0, z = 0}})
        if card then
          SHOWN[#SHOWN + 1] = card
          local name = card.getName()
          playSound((name == "Fail" or name == "Fracaso") and "sting" or "drum", 0.7)
        end
      end, 0.9 * i)
    end
    Wait.time(function() if QUEST_SHOWN == shown then collectQuest() end end, 0.9 * count + 5)
  end, 0.5)
end

function markQuest(ctx)
  local quest = math.floor(Game.getVariable("quest"))
  if ctx.args.result == "fail" then
    local n = math.floor(Game.getVariable("fails"))
    put(n == 1 and "evil" or ("evil_" .. n), at(QUESTS[quest]))
  else
    local n = math.floor(Game.getVariable("successes"))
    put(n == 1 and "good" or ("good_" .. n), at(QUESTS[quest]))
  end
end

function rejectTrack(ctx)
  local n = math.floor(Game.getVariable("rejections"))
  if n == 0 then put("reject_marker", HOME["reject_marker"]) else put("reject_marker", at("reject_" .. n)) end
end

-- ---- the end ----------------------------------------------------------------------------------------------------------

-- Every role card turned over in front of its player.
function rolesShown(ctx)
  for _, seat in ipairs(Game.getSeats()) do
    for _, card in ipairs(cardsIn({"hand_" .. number(seat)}, {"role_"})) do
      local p = before(seat, 0.56)
      card.setPositionSmooth({x = p.x, y = p.y + 1, z = p.z})
      card.setRotationSmooth({x = 0, y = 0, z = 0})
    end
  end
end
""")


def compare(left, right, op="=="):
    return {"compare": {"left": left, "op": op, "right": right}}


def win(team):
    """A team wins, and every role is shown, as players turn their cards over at the end."""
    return [{"lua": {"call": "rolesShown"}}, {"forEach": {"players": True, "do": [{"revealRole": {"seat": "$it"}}]}},
            {"light": {"part": "room", "seconds": 3}}, {"music": {"playlist": "tavern", "crossfade": 4}},
            {"sound": {"play": "fanfare" if team == "good" else "gong"}}, {"win": {"team": team}}]


def refuse(key):
    return {"refuse": {"reason": f"@{key}"}}


# ---- the ritual: timed steps before the first quest, eyes closed but for those it names --------------------------------
RITUAL = [
    # Waits for the host: everyone reads their card at their own pace, then the host starts the ritual.
    {"id": "ritual_look", "name": "Your role", "hostEnds": True, "next": "ritual_close",
     "banner": "Look at your role card in your hand and keep it to yourself. The host starts the ritual with Next."},
    {"id": "ritual_close", "name": "Eyes closed", "seconds": 4, "next": "ritual_minions", "awake": "nobody", "voice": "none",
     "sleepText": "Everyone, close your eyes."},
    {"id": "ritual_minions", "name": "The minions", "seconds": 10, "next": "ritual_merlin", "awake": "$var.minionEyes", "voice": "none",
     "sleepText": "Minions of Mordred, open your eyes and find each other.",
     "banner": "Minions of Mordred: whoever has their eyes open is on your side. Look around!"},
    {"id": "ritual_merlin", "name": "Merlin", "seconds": 10, "next": "ritual_percival", "awake": "role:merlin", "voice": "none",
     "raised": "$var.thumbsMerlin",
     "sleepText": "The minions raise their thumbs (not Mordred). Merlin, open your eyes.",
     "banner": "Merlin: the raised thumbs are the minions (Mordred keeps his down)."},
    {"id": "ritual_percival", "name": "Percival", "seconds": 10, "next": "ritual_open", "awake": "role:percival", "voice": "none",
     "raised": "$var.thumbsPercival", "advanceWhen": {"condition": compare("$var.percival", False)},
     "sleepText": "Merlin and Morgana raise their thumbs. Percival, open your eyes.",
     "banner": "Percival: one raised thumb is Merlin, the other Morgana."},
    {"id": "ritual_open", "name": "Eyes open", "seconds": 4, "next": "propose", "banner": "Everyone, open your eyes."},
]
NIGHT = [{"light": {"part": "room", "scale": 0.08, "tint": [0.6, 0.7, 1.0], "seconds": 2.5}},
         {"light": {"part": "table", "scale": 0.4, "tint": [0.75, 0.82, 1.0], "seconds": 2.5}},
         {"music": {"playlist": "ritual", "crossfade": 3}}]
DAWN = [{"light": {"part": "room", "seconds": 3}}, {"light": {"part": "table", "seconds": 3}},
        {"music": {"playlist": "tavern", "crossfade": 4}}]
MOOD = {
    "ritual_close": NIGHT + [{"sound": {"play": "gong"}}],
    "ritual_minions": [{"sound": {"play": "drum", "to": "awake", "volume": 0.6}}],
    "ritual_merlin": [{"sound": {"play": "bell", "to": "awake", "volume": 0.5}}],
    "ritual_percival": [{"sound": {"play": "bell", "to": "awake", "volume": 0.5}}],
    "ritual_open": DAWN + [{"sound": {"play": "chime"}}],
}
for phase in RITUAL:
    phase["onEnter"] = MOOD.get(phase["id"], []) + phase.get("onEnter", [])
    # The whole ritual is watched from your own chair: faces, open eyes and thumbs are what it is about.
    phase["seated"] = True
    call("add_phase", phaseJson=json.dumps(phase))

# ---- choosing the team: the leader clicks players --------------------------------------------------------------------
call("add_phase", phaseJson=json.dumps({
    "id": "propose", "name": "Pick the team", "actions": ["choose", "propose"],
    "onEnter": [
        {"lua": {"call": "setQuest"}},
        {"setVariable": {"name": "team", "value": []}},
        {"message": {"text": "@leads"}},
    ],
}))
call("add_action", actionJson=json.dumps({
    "id": "propose", "name": "Propose this team", "actor": "current",
    "preconditions": [compare("$var.team.count", "$var.size")],
    "effects": [{"message": {"text": "@proposes"}}, {"endPhase": {}}],
}))
# Clicking a player puts them on the team, or takes them off.
call("add_action", actionJson=json.dumps({
    "id": "choose", "name": "Choose for the team", "actor": "current", "onPlayer": True,
    "params": [{"name": "member", "type": "seat"}],
    "effects": [{"if": {"condition": {"inList": {"list": "$var.team", "value": "$param.member"}},
                        "then": [{"setVariable": {"name": "team", "remove": "$param.member"}}],
                        "else": [{"if": {"condition": compare("$var.team.count", "$var.size", ">="), "then": [refuse("teamFull")]}},
                                 {"setVariable": {"name": "team", "add": "$param.member"}}]}}],
}))

# ---- the team vote: Approve and Reject cards laid face down ------------------------------------------------------------
call("add_phase", phaseJson=json.dumps({"id": "team_vote", "name": "Vote on the team", "actions": ["team_vote"],
                                        "onEnter": [{"setVariable": {"name": "voted", "value": []}}, {"lua": {"call": "votesOut"}}]}))
call("add_action", actionJson=json.dumps({
    "id": "team_vote", "name": "Team vote", "actor": "any", "button": False,
    "vote": {
        "options": ["approve", "reject"], "labels": {"approve": "Approve", "reject": "Reject"},
        "colors": {"approve": "#3fae5a", "reject": "#d8322a"},
        "reveal": "named", "decide": "majority", "otherwise": "reject",
        "onResult": [{"lua": {"call": "votesShown"}}, {"sound": {"play": "drum", "volume": 0.8}}, {"if": {
            "condition": compare("$vote.result", "approve"),
            "then": [{"setVariable": {"name": "rejections", "value": 0}}, {"lua": {"call": "rejectTrack"}}, {"gotoPhase": {"phase": "quest"}}],
            "else": [{"setVariable": {"name": "rejections", "delta": 1}}, {"lua": {"call": "rejectTrack"}},
                     {"if": {"condition": compare("$var.rejections", 5, ">="),
                             "then": [{"message": {"text": "@fiveRejected"}}] + win("evil"),
                             "else": [{"endTurn": {}}]}}]}}],
    },
}))

# ---- the quest: Success and Fail cards on the pile ---------------------------------------------------------------------
call("add_phase", phaseJson=json.dumps({"id": "quest", "name": "Quest", "actions": ["quest"],
                                        "onEnter": [{"setVariable": {"name": "played", "value": []}}, {"lua": {"call": "questOut"}},
                                                    {"message": {"text": "@questStarts"}}]}))
call("add_action", actionJson=json.dumps({
    "id": "quest", "name": "Quest", "actor": "any", "button": False,
    "preconditions": [{"or": [compare("$actor.team", "evil"), compare("$param.choice", "success")]}],
    "vote": {
        "options": ["success", "fail"], "labels": {"success": "Success", "fail": "Fail"},
        "colors": {"success": "#4a8cff", "fail": "#d8322a"},
        # The rules judge the fail cards (one fails it, two on the fourth quest with 7 or more): no majority result.
        "who": "$var.team", "reveal": "count", "decide": "none",
        "onResult": [
            {"lua": {"call": "questShown"}},
            {"if": {"condition": compare("$vote.fail", "$var.needFails", ">="),
                    "then": [{"setVariable": {"name": "fails", "delta": 1}}, {"lua": {"call": "markQuest", "args": {"result": "fail"}}},
                             {"message": {"text": "@questFails"}}],
                    "else": [{"setVariable": {"name": "successes", "delta": 1}}, {"lua": {"call": "markQuest", "args": {"result": "success"}}},
                             {"message": {"text": "@questSucceeds"}}]}},
            {"if": {"condition": compare("$var.fails", 3, ">="),
                    "then": [{"message": {"text": "@threeFailed"}}] + win("evil"),
                    "else": [{"if": {"condition": compare("$var.successes", 3, ">="),
                                     "then": [{"message": {"text": "@threeSucceeded"}},
                                              {"gotoPhase": {"phase": "assassinate"}}],
                                     "else": [{"setVariable": {"name": "quest", "delta": 1}}, {"endTurn": {}}]}}]}},
        ],
    },
}))

call("add_phase", phaseJson=json.dumps({"id": "assassinate", "name": "The Assassin strikes", "actions": ["assassinate"], "onEnter": [
    {"light": {"part": "room", "scale": 0.3, "tint": [1.0, 0.55, 0.5], "seconds": 2}}, {"music": {"playlist": "ritual", "crossfade": 3}},
    {"sound": {"play": "sting"}}]}))
call("add_action", actionJson=json.dumps({
    "id": "assassinate", "name": "Name Merlin", "actor": "any", "onPlayer": True,
    "params": [{"name": "target", "type": "seat"}],
    "preconditions": [compare("$actor.role", "assassin"), {"not": compare("$param.target", "$actor.id")}],
    "effects": [
        {"revealRole": {"seat": "$param.target"}},
        {"sound": {"play": "bell"}},
        {"if": {"condition": compare("$param.target.role", "merlin"),
                "then": [{"message": {"text": "@wasMerlin"}}] + win("evil"),
                "else": [{"message": {"text": "@wasNotMerlin"}}] + win("good")}},
    ],
}))

# ---- the place and the soundtrack (M12) ---------------------------------------------------------------------------------
call("set_environment", room="tavern", theme="parchment")
library = call("list_music", limit=200)
tracks = {t["url"] for t in library["tracks"]}
VOL3 = "library:fantasy-rpg-music-pack-vol-3/"
tavern = [VOL3 + f"ambient-{n}-loop" for n in range(1, 11)]
ritual = [VOL3 + f"dark-{n}" for n in range(1, 6)]
missing = [t for t in tavern + ritual if t not in tracks]
if missing:
    print(f"note: {len(missing)} tracks are not in this computer's music library (import AlkaKrab's Fantasy RPG Music Pack Vol.3 with import_music)")
call("set_music", playlistsJson=json.dumps({"tavern": tavern, "ritual": ritual}), start="tavern", shuffle=True, volume=0.6)

# ---- the table ----------------------------------------------------------------------------------------------------------
# One card per quest and player count (8, 9 and 10 share theirs), kept in a box: the game lays out the five for the
# players at the table when it starts (layQuests), so each card shows only its own team size.
SIZES = {5: [2, 3, 2, 3, 3], 6: [2, 3, 4, 3, 4], 7: [2, 3, 3, 4, 4], 8: [3, 4, 4, 5, 5]}
CARD = {"paper": [0.93, 0.89, 0.78], "ink": [0.2, 0.15, 0.1], "accent": [0.55, 0.38, 0.12]}
call("place_object", objectJson=json.dumps({
    "id": "quest_box", "type": "Bag", "name": "Quest cards", "tint": [0.45, 0.3, 0.15],
    "transform": {"position": [0.5, 0, 0.25]}}))
for players, sizes in SIZES.items():
    for q in range(1, 6):
        text = {"title": f"Quest {q}", "kind": "Players who go", "body": str(sizes[q - 1]),
                "footer": "2 Fails sink it" if q == 4 and players >= 7 else "1 Fail sinks it"}
        call("place_object", objectJson=json.dumps({
            "id": f"quest_{q}_{players}", "type": "Card", "name": f"Quest {q}", "locked": True, "size": [0.14, 0.19],
            "text": text, "style": CARD, "container": "quest_box", "transform": {"position": [0.5, 0, 0.25]}}))
for n in range(1, 6):
    call("place_object", objectJson=json.dumps({
        "id": f"reject_{n}", "type": "Card", "name": f"Rejected team {n}", "locked": True, "size": [0.06, 0.06],
        "text": {"title": "Evil wins" if n == 5 else "Rejected", "body": str(n)},
        "style": {"paper": [0.75, 0.72, 0.68], "ink": [0.2, 0.15, 0.1], "accent": [0.6, 0.15, 0.12]},
        "transform": {"position": [-0.16 + 0.08 * (n - 1), 0, -0.13], "rotation": [0, 180, 0]}}))
call("create_supply", id="reject_marker", piece="cube", color="silver", mode="pile", count=1, name="Rejections", x=-0.25, z=-0.13)
call("create_supply", id="good", piece="checker", color="blue", mode="pile", count=3, name="Quest succeeded", x=-0.52, z=-0.03)
call("create_supply", id="evil", piece="checker", color="red", mode="pile", count=3, name="Quest failed", x=0.47, z=-0.03)

# The cards players handle: their role, their votes, their quest cards. Decks stay locked; the rules deal from them.
GOOD = {"paper": [0.88, 0.92, 0.98], "ink": [0.1, 0.16, 0.3], "accent": [0.2, 0.36, 0.7]}
EVIL = {"paper": [0.98, 0.9, 0.88], "ink": [0.3, 0.08, 0.06], "accent": [0.66, 0.14, 0.12]}
BACK = {"backPaper": [0.16, 0.12, 0.1], "backInk": [0.85, 0.7, 0.4]}
COUNTS = {"servant": 5, "minion": 3}
role_cards = []
for role in ROLES:
    team = role["team"]
    role_cards.append({"id": f"role_{role['id']}", "title": role["name"],
                       "kind": "Good side" if team == "good" else "Evil side",
                       "body": role["description"], "count": COUNTS.get(role["id"], 1), "style": GOOD if team == "good" else EVIL,
                       "tags": ["role"]})
call("create_text_deck", id="role_deck", name="Roles", cards=json.dumps(role_cards), x=-0.18, z=0.25,
     style=json.dumps(dict(BACK, backLabel="AVALON")))
call("create_text_deck", id="vote_deck", name="Votes", x=0.34, z=0.25, style=json.dumps(dict(BACK, backLabel="VOTE")), cards=json.dumps([
    {"id": "aye", "title": "Approve", "body": "YES", "footer": "Face down on your spot", "count": 10, "meta": {"vote": "approve"}, "tags": ["vote"],
     "style": {"paper": [0.86, 0.95, 0.86], "ink": [0.08, 0.3, 0.1], "accent": [0.2, 0.55, 0.25]}},
    {"id": "nay", "title": "Reject", "body": "NO", "footer": "Face down on your spot", "count": 10, "meta": {"vote": "reject"}, "tags": ["vote"],
     "style": {"paper": [0.98, 0.88, 0.86], "ink": [0.35, 0.08, 0.06], "accent": [0.66, 0.14, 0.12]}},
]))
call("create_text_deck", id="quest_deck", name="Quest cards", x=-0.34, z=0.25, style=json.dumps(dict(BACK, backLabel="QUEST")), cards=json.dumps([
    {"id": "success", "title": "Success", "body": "SUCCESS", "footer": "Onto the quest pile", "count": 15, "meta": {"quest": "success"}, "tags": ["quest"], "style": GOOD},
    {"id": "fail", "title": "Fail", "body": "FAIL", "footer": "Minions only: sinks it", "count": 10, "meta": {"quest": "fail"}, "tags": ["quest"], "style": EVIL},
]))
# Where the quest cards are laid: the pile (a deck they go into face down) and the row they are turned over on.
call("place_object", objectJson=json.dumps({"id": "quest_pile", "type": "Deck", "name": "Quest pile", "cards": [], "locked": True,
                                            "style": dict(BACK, backLabel="QUEST"), "transform": {"position": [0.17, 0, 0.25], "rotation": [0, 0, 180]}}))
call("define_zone", zoneJson=json.dumps({
    "id": "quest_drop", "type": "LayoutZone", "name": "Quest pile", "facing": "faceDown",
    "catch": {"tag": "quest", "for": "$var.team"},
    "transform": {"position": [0.04, 0.05, 0.25]}, "size": [0.12, 0.1, 0.13],
    "onEnter": [{"if": {"condition": {"hasTag": {"object": "$it", "tag": "quest"}}, "then": [
        {"if": {"condition": {"inList": {"list": "$var.played", "value": "$actor"}}, "then": [refuse("onePlayed")]}},
        {"if": {"condition": {"and": [compare("$it.meta.quest", "fail"), compare("$actor.team", "evil", "!=")]}, "then": [refuse("loyalSucceed")]}},
        {"castBallot": {"action": "quest", "seat": "$actor", "choice": "$it.meta.quest"}},
        {"setVariable": {"name": "played", "add": "$actor"}},
        {"lua": {"call": "toPile"}},
    ]}}]}))
call("define_zone", zoneJson=json.dumps({"id": "reveal_row", "type": "ScriptingZone", "name": "Quest cards shown",
                                         "transform": {"position": [-0.12, 0.05, -0.25]}, "size": [0.42, 0.1, 0.12]}))

# Before each seat: the spot for a vote card.
document = call("get_rules_document")
hands = {z["id"]: z for z in document["zones"] if z["type"] == "HandZone"}
for seat in document["players"]["seats"]:
    hand = hands[seat["handZone"]]
    hx, _, hz = hand["transform"]["position"]
    turn = hand["transform"].get("rotation", [0, 0, 0])
    # A step in front of the hand, towards the middle (yaw 0 faces a seat on the south edge, so inward is +z).
    inward = (math.sin(math.radians(turn[1])), math.cos(math.radians(turn[1])))
    vx, vz = hx + inward[0] * 0.19, hz + inward[1] * 0.19
    n = seat["id"].split("_")[1]
    call("define_zone", zoneJson=json.dumps({
        "id": f"vote_{n}", "type": "LayoutZone", "name": "Vote", "facing": "faceDown",
        # A vote card let go anywhere but the hand comes here, face down; the spot lights up while it is carried.
        "catch": {"tag": "vote", "for": seat["id"]},
        "transform": {"position": [round(vx, 3), 0.05, round(vz, 3)], "rotation": turn}, "size": [0.11, 0.1, 0.13],
        "onEnter": [{"if": {"condition": {"hasTag": {"object": "$it", "tag": "vote"}}, "then": [
            {"if": {"condition": compare("$actor.id", seat["id"], "!="), "then": [refuse("notYourSpot")]}},
            {"if": {"condition": {"inList": {"list": "$var.voted", "value": seat["id"]}}, "then": [refuse("oneVote")]}},
            {"castBallot": {"action": "team_vote", "seat": seat["id"], "choice": "$it.meta.vote"}},
            {"setVariable": {"name": "voted", "add": seat["id"]}},
        ]}}],
        # Taking the card back off the spot frees it for the other one (the ballot stands until then).
        "onLeave": [{"if": {"condition": {"and": [{"hasTag": {"object": "$it", "tag": "vote"}},
                                                  {"inList": {"list": "$var.voted", "value": seat["id"]}}]},
                            "then": [{"setVariable": {"name": "voted", "remove": seat["id"]}}]}}],
    }))

call("add_event", eventName="onGameStart", effectsJson=json.dumps([
    {"lua": {"call": "setUp"}}, {"lua": {"call": "layQuests"}}, {"gotoPhase": {"phase": "ritual_look"}}]))
call("set_turn", phases="propose,team_vote,quest,assassinate", order="clockwise", startingPlayer="random")

# Where the game stands, for everyone: the panel reads the variables the rules keep. The decks and the pile stay put.
document = call("get_rules_document")
SCALES = {"good": 1.8, "evil": 1.8, "reject_marker": 1.6}
for obj in document["objects"]:
    base = obj["id"].split("_")[0] if obj["id"] != "reject_marker" else obj["id"]
    if base in SCALES:
        obj["transform"]["scale"] = [SCALES[base]] * 3
    if obj["id"] in ("role_deck", "vote_deck", "quest_deck"):
        obj["locked"] = True
# Over the players: a crown on the leader, an arrow over each one chosen for the team, for everyone to see.
document["marks"] = [
    {"id": "leader", "seats": "$current", "shape": "crown", "color": [0.98, 0.78, 0.2]},
    {"id": "team", "seats": "$var.team", "shape": "arrow", "color": [0.85, 0.9, 1.0]},
]
document["ui"] = {"panels": [{
    "id": "quests", "title": "Avalon", "anchor": "right", "visibleTo": [],
    "elements": [
        {"type": "label", "text": "Quest {$var.quest} of 5: a team of {$var.size}"},
        {"type": "label", "text": "Fails that sink it: {$var.needFails}"},
        {"type": "label", "text": "Succeeded {$var.successes} · Failed {$var.fails}"},
        {"type": "label", "text": "Rejected teams: {$var.rejections} of 5"},
        {"type": "label", "text": "Leader: {$current.name}"},
        {"type": "label", "text": "Team: {$var.team}"},
    ]}]}
# The score panel's words in their sides' colours, in both languages (the engine colours seats, teams and options).
document["game"]["highlights"] = {"Succeeded": "#4a8cff", "Éxitos": "#4a8cff", "Failed": "#d8322a", "Fracasos": "#d8322a"}
call("set_rules_document", documentJson=json.dumps(document))

# Spanish, for every text translate_game lists.
ES = {
    'game.name': 'Avalon',
    'game.description': 'Lealtades ocultas: el bien completa tres misiones; el mal hace fracasar tres o encuentra a Merlín al final. Un líder elige el equipo de cada misión, todos lo votan y el equipo la cumple o la sabotea en secreto.',
    'actions.propose.name': 'Proponer este equipo',
    'actions.team_vote.name': 'Voto del equipo',
    'votes.team_vote.approve': 'Aprobar',
    'votes.team_vote.reject': 'Rechazar',
    'actions.quest.name': 'Misión',
    'votes.quest.success': 'Éxito',
    'votes.quest.fail': 'Fracaso',
    'actions.assassinate.name': 'Nombrar a Merlín',
    'phases.ritual_look.name': 'Tu rol',
    'phases.ritual_look.banner': 'Mira tu carta de rol en tu mano y no se la muestres a nadie. El anfitrión empieza el ritual con Siguiente.',
    'phases.ritual_close.name': 'Ojos cerrados',
    'phases.ritual_close.sleepText': 'Todos, cierren los ojos.',
    'phases.ritual_minions.name': 'Los esbirros',
    'phases.ritual_minions.sleepText': 'Esbirros de Mordred, abran los ojos y reconózcanse.',
    'phases.ritual_minions.banner': 'Esbirros de Mordred: quien tenga los ojos abiertos es de tu bando. ¡Miren alrededor!',
    'phases.ritual_merlin.name': 'Merlín',
    'phases.ritual_merlin.sleepText': 'Los esbirros levantan el pulgar (menos Mordred). Merlín, abre los ojos.',
    'phases.ritual_merlin.banner': 'Merlín: los pulgares levantados son los esbirros (Mordred no lo levanta).',
    'phases.ritual_percival.name': 'Percival',
    'phases.ritual_percival.sleepText': 'Merlín y Morgana levantan el pulgar. Percival, abre los ojos.',
    'phases.ritual_percival.banner': 'Percival: uno de los pulgares es Merlín; el otro, Morgana.',
    'phases.ritual_open.name': 'Ojos abiertos',
    'phases.ritual_open.banner': 'Todos, abran los ojos.',
    'phases.propose.name': 'Elegir el equipo',
    'phases.team_vote.name': 'Votar el equipo',
    'phases.quest.name': 'Misión',
    'phases.assassinate.name': 'El Asesino ataca',
    'ui.quests.title': 'Avalon',
    'ui.quests.0.text': 'Misión {$var.quest} de 5: equipo de {$var.size}',
    'ui.quests.1.text': 'Fracasos que la hunden: {$var.needFails}',
    'ui.quests.2.text': 'Éxitos {$var.successes} · Fracasos {$var.fails}',
    'ui.quests.3.text': 'Equipos rechazados: {$var.rejections} de 5',
    'ui.quests.4.text': 'Líder: {$current.name}',
    'ui.quests.5.text': 'Equipo: {$var.team}',
    'teams.good.name': 'Leales servidores de Arturo',
    'teams.evil.name': 'Esbirros de Mordred',
}
ROLES_ES = {
    "merlin": ("Merlín", "Sabes quiénes son los esbirros. Guía al bien sin delatarte: si el Asesino te encuentra, gana el mal."),
    "percival": ("Percival", "Uno de los dos pulgares que viste es Merlín: protégelo. El otro es Morgana."),
    "servant": ("Leal servidor", "Juega siempre Éxito. Rechaza los equipos con esbirros."),
    "assassin": ("Asesino", "Haz fracasar misiones. Si el bien gana tres, adivina quién es Merlín: si aciertas, gana el mal."),
    "morgana": ("Morgana", "Haz fracasar misiones. Percival puede creer que eres Merlín: síguele el juego."),
    "mordred": ("Mordred", "Haz fracasar misiones. Merlín no sabe que eres del mal."),
    "oberon": ("Oberón", "Haz fracasar misiones. Los otros esbirros no te conocen, ni tú a ellos."),
    "minion": ("Esbirro de Mordred", "Entra en los equipos y juega Fracaso. Finge ser leal."),
}
for role, (name, text) in ROLES_ES.items():
    ES[f"roles.{role}.name"] = name
    ES[f"roles.{role}.description"] = text
    team = next(r["team"] for r in ROLES if r["id"] == role)
    for k in range(1, COUNTS.get(role, 1) + 1):
        card = f"cards.role_{role}" + ("" if k == 1 else f"_{k}")
        ES[f"{card}.text.title"] = name
        ES[f"{card}.text.kind"] = "Bando del bien" if team == "good" else "Bando del mal"
        ES[f"{card}.text.body"] = text
for k in range(1, 11):
    suffix = "" if k == 1 else f"_{k}"
    ES[f"cards.aye{suffix}.text.title"], ES[f"cards.aye{suffix}.text.body"] = "Aprobar", "SÍ"
    ES[f"cards.nay{suffix}.text.title"], ES[f"cards.nay{suffix}.text.body"] = "Rechazar", "NO"
    ES[f"cards.aye{suffix}.text.footer"] = ES[f"cards.nay{suffix}.text.footer"] = "Boca abajo en tu sitio"
    ES[f"cards.fail{suffix}.text.title"], ES[f"cards.fail{suffix}.text.body"] = "Fracaso", "FRACASO"
    ES[f"cards.fail{suffix}.text.footer"] = "Solo esbirros: la hunde"
# Two Success cards for every loyal player on a team of up to five, and the minions' own.
for k in range(1, 16):
    suffix = "" if k == 1 else f"_{k}"
    ES[f"cards.success{suffix}.text.title"], ES[f"cards.success{suffix}.text.body"] = "Éxito", "ÉXITO"
    ES[f"cards.success{suffix}.text.footer"] = "Al montón de la misión"
# A deck card's name and description are its title and body (tooltips, search).
for key in [k for k in ES if k.startswith("cards.") and k.endswith(".text.title")]:
    ES[key.replace(".text.title", ".name")] = ES[key]
for key in [k for k in ES if k.startswith("cards.") and k.endswith(".text.body")]:
    ES[key.replace(".text.body", ".description")] = ES[key]
ES.update({f"strings.{key}": text for key, (_, text) in STRINGS.items()})
ES["objects.quest_box.name"] = "Cartas de misión"
for players, sizes in SIZES.items():
    for q in range(1, 6):
        card = f"objects.quest_{q}_{players}"
        ES[f"{card}.name"] = ES[f"{card}.text.title"] = f"Misión {q}"
        ES[f"{card}.text.kind"] = "Jugadores que van"
        ES[f"{card}.text.body"] = str(sizes[q - 1])
        ES[f"{card}.text.footer"] = "La hunden 2 Fracasos" if q == 4 and players >= 7 else "La hunde 1 Fracaso"
for n in range(1, 6):
    ES[f"objects.reject_{n}.name"] = f"Equipo rechazado {n}"
    ES[f"objects.reject_{n}.text.title"] = "Gana el mal" if n == 5 else "Rechazado"
    ES[f"objects.reject_{n}.text.body"] = str(n)
ES["objects.reject_marker.name"] = "Rechazos"
ES["actions.choose.name"] = "Elegir para el equipo"
for i in ["", "_2", "_3"]:
    ES[f"objects.good{i}.name"] = "Misión cumplida"
    ES[f"objects.evil{i}.name"] = "Misión fracasada"
ES["objects.role_deck.name"] = "Roles"
ES["objects.vote_deck.name"] = "Votos"
ES["objects.quest_deck.name"] = "Cartas de misión"
ES["objects.quest_pile.name"] = "Montón de la misión"
ES["zones.quest_drop.name"] = "Montón de la misión"
ES["zones.reveal_row.name"] = "Cartas de la misión"
for seat in document["players"]["seats"]:
    n = seat["id"].split("_")[1]
    ES[f"zones.vote_{n}.name"] = "Voto"
call("set_translations", language="es", translationsJson=json.dumps(ES, ensure_ascii=False))
left = call("translate_game", language="es")
if left.get("left", 0):
    raise SystemExit(f"untranslated: {left.get('missing')}")

call("validate_rules")
call("save_game", path=OUT)
server.kill()
print(f"saved {OUT}")
