"""M10 T11.6: builds Avalon from docs/rulebooks/avalon.txt through the MCP tools only, the way an agent would, and
saves it as games/avalon.json. Every call is a tools/call to the server over stdio; a failed call stops the build.

    dotnet build mcp-server -c Release      (or Debug: pass the DLL path)
    python -I tools/mcp/build_avalon.py [path to TableWorld.Mcp.dll]

The tools used are the general ones (new_game, set_roles, set_variables, set_script, add_phase, add_action,
set_turn, get_rules_document and set_rules_document for the score panel, set_translations, validate_rules,
save_game); nothing in the server knows Avalon.
"""
import json
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

call("new_game", id="avalon", name="Avalon", players=10, minPlayers=5, maxPlayers=10,
     description="Hidden loyalties: good completes three quests, evil fails three or finds Merlin at the end. "
                 "A leader picks each quest's team, everyone votes on it, and the team succeeds or fails it in secret.")

# The game's own sentences are strings (@key), so they can be translated like every other text.
STRINGS = {
    'leads': ('{$current.name} leads quest {$var.quest}: pick {$var.size} players.',
                '{$current.name} lidera la misión {$var.quest}: elige {$var.size} jugadores.'),
    'picks': ('{$actor.name} picks {$param.member.name}.',
                '{$actor.name} elige a {$param.member.name}.'),
    'unpicks': ('{$actor.name} takes {$param.member.name} off the team.',
                  '{$actor.name} saca a {$param.member.name} del equipo.'),
    'proposes': ('{$actor.name} proposes {$var.team}. Vote on the team.',
                   '{$actor.name} propone a {$var.team}. Voten el equipo.'),
    'fiveRejected': ('Five teams rejected in a row.',
                       'Cinco equipos rechazados seguidos.'),
    'questFails': ('Quest {$var.quest} fails.',
                     'La misión {$var.quest} fracasa.'),
    'questSucceeds': ('Quest {$var.quest} succeeds.',
                        'La misión {$var.quest} tiene éxito.'),
    'threeFailed': ('Three quests failed.',
                      'Tres misiones fracasaron.'),
    'threeSucceeded': ('Three quests succeeded. The Assassin names Merlin.',
                         'Tres misiones tuvieron éxito. El Asesino nombra a Merlín.'),
    'wasMerlin': ('{$param.target.name} was Merlin.',
                    '{$param.target.name} era Merlín.'),
    'wasNotMerlin': ('{$param.target.name} was not Merlin.',
                       '{$param.target.name} no era Merlín.'),
}
call("set_translations", language="en", gameLanguage="en",
     translationsJson=json.dumps({f"strings.{key}": text for key, (text, _) in STRINGS.items()}))

call("set_roles", rolesJson=json.dumps({
    "teams": [{"id": "good", "name": "Loyal servants of Arthur"}, {"id": "evil", "name": "Minions of Mordred"}],
    "list": [
        {"id": "merlin", "name": "Merlin", "team": "good", "description": "Knows the evil players, except Mordred. Must stay hidden from the Assassin."},
        {"id": "percival", "name": "Percival", "team": "good", "optional": True, "description": "Sees Merlin and Morgana, not which is which."},
        {"id": "servant", "name": "Loyal servant", "team": "good", "count": "fill"},
        {"id": "assassin", "name": "Assassin", "team": "evil", "description": "Names one good player at the end: Merlin means evil wins."},
        {"id": "morgana", "name": "Morgana", "team": "evil", "optional": True, "description": "Appears as Merlin to Percival."},
        {"id": "mordred", "name": "Mordred", "team": "evil", "optional": True, "description": "Hidden from Merlin."},
        {"id": "oberon", "name": "Oberon", "team": "evil", "optional": True, "description": "Unknown to the other minions, and knows none of them."},
        {"id": "minion", "name": "Minion of Mordred", "team": "evil", "count": "fill"},
    ],
    "byPlayers": {"5": {"good": 3, "evil": 2}, "6": {"good": 4, "evil": 2}, "7": {"good": 4, "evil": 3},
                  "8": {"good": 5, "evil": 3}, "9": {"good": 6, "evil": 3}, "10": {"good": 6, "evil": 4}},
    "knows": [
        {"role": "merlin", "sees": {"team": "evil", "except": ["mordred"]}},
        {"team": "evil", "sees": {"team": "evil", "except": ["oberon"]}},
        {"role": "oberon", "sees": {}},
        {"role": "percival", "sees": {"roles": ["merlin", "morgana"]}, "as": "oneOf"},
    ],
}))

call("set_variables", variablesJson=json.dumps({
    "quest": 1, "size": 2, "needFails": 1, "team": [], "successes": 0, "fails": 0, "rejections": 0}))

# The quest table depends on the player count and the quest: a lookup, so the Lua escape hatch.
call("set_script", target="global", code="""
SIZES = { [5] = {2, 3, 2, 3, 3}, [6] = {2, 3, 4, 3, 4}, [7] = {2, 3, 3, 4, 4},
          [8] = {3, 4, 4, 5, 5}, [9] = {3, 4, 4, 5, 5}, [10] = {3, 4, 4, 5, 5} }

-- The team size and the fails it takes for the quest about to be played.
function setQuest(ctx)
  local players = #Game.getSeats()
  local quest = Game.getVariable("quest")
  Game.setVariable("size", SIZES[players][quest])
  if quest == 4 and players >= 7 then Game.setVariable("needFails", 2) else Game.setVariable("needFails", 1) end
end

-- The pieces on the table follow the game: the crown before the leader, a shield before each member of the team,
-- a blue or red checker on each quest card once played, the marker on the rejection track.
SHIELDS = {"shield", "shield_2", "shield_3", "shield_4", "shield_5"}
HOME = {}

local function at(id)
  local o = getObjectFromGUID(id)
  return o and o.getPosition()
end

local function put(id, p)
  local o = getObjectFromGUID(id)
  if HOME[id] == nil and o then HOME[id] = o.getPosition() end
  if o and p then o.setPositionSmooth({x = p.x, y = p.y + 1, z = p.z}) end
end

-- Before a seat, between its edge of the table and the middle (share of the way from the middle to its hand).
local function before(seat, share)
  local p = at("hand_" .. seat:match("(%d+)$"))
  return {x = p.x * share, y = p.y, z = p.z * share}
end

function crownToLeader(ctx)
  put("crown", before(Game.getCurrentSeat(), 0.8))
end

function shieldsToTeam(ctx)
  local team = Game.getVariable("team") or {}
  for i, id in ipairs(SHIELDS) do
    put(id, nil)
    if team[i] then put(id, before(team[i], 0.62)) else put(id, HOME[id]) end
  end
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
      position = {x = (-0.36 + 0.18 * (q - 1)) * unit, y = left.y + 0.3, z = 0.08 * unit}, rotation = {x = 0, y = 180, z = 0}})
    QUESTS[q] = card and card.getGUID()
  end
  box.destruct()
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
  put("reject_marker", nil)
  if n == 0 then put("reject_marker", HOME["reject_marker"]) else put("reject_marker", at("reject_" .. n)) end
end
""")


def compare(left, right, op="=="):
    return {"compare": {"left": left, "op": op, "right": right}}


def win(team):
    """A team wins, and every role is shown, as players turn their cards over at the end."""
    return [{"forEach": {"players": True, "do": [{"revealRole": {"seat": "$it"}}]}}, {"win": {"team": team}}]


call("add_phase", phaseJson=json.dumps({
    "id": "propose", "name": "Pick the team", "actions": ["pick", "unpick", "propose"],
    "onEnter": [
        {"lua": {"call": "setQuest"}},
        {"setVariable": {"name": "team", "value": []}},
        {"lua": {"call": "crownToLeader"}},
        {"lua": {"call": "shieldsToTeam"}},
        {"message": {"text": "@leads"}},
    ],
}))
call("add_action", actionJson=json.dumps({
    "id": "pick", "name": "Pick", "actor": "current",
    "params": [{"name": "member", "type": "seat"}],
    "preconditions": [{"not": {"inList": {"list": "$var.team", "value": "$param.member"}}},
                      compare("$var.team.count", "$var.size", "<")],
    "effects": [{"setVariable": {"name": "team", "add": "$param.member"}}, {"lua": {"call": "shieldsToTeam"}},
                {"message": {"text": "@picks"}}],
}))
call("add_action", actionJson=json.dumps({
    "id": "unpick", "name": "Take off the team", "actor": "current",
    "params": [{"name": "member", "type": "seat"}],
    "preconditions": [{"inList": {"list": "$var.team", "value": "$param.member"}}],
    "effects": [{"setVariable": {"name": "team", "remove": "$param.member"}}, {"lua": {"call": "shieldsToTeam"}},
                {"message": {"text": "@unpicks"}}],
}))
# The table talks the team over before the vote: the leader sends it when ready, and can change it until then.
call("add_action", actionJson=json.dumps({
    "id": "propose", "name": "Propose this team", "actor": "current",
    "preconditions": [compare("$var.team.count", "$var.size")],
    "effects": [{"message": {"text": "@proposes"}}, {"endPhase": {}}],
}))

call("add_phase", phaseJson=json.dumps({"id": "team_vote", "name": "Vote on the team", "actions": ["team_vote"]}))
call("add_action", actionJson=json.dumps({
    "id": "team_vote", "name": "Team vote", "actor": "any",
    "vote": {
        "options": ["approve", "reject"], "labels": {"approve": "Approve", "reject": "Reject"},
        "reveal": "named", "decide": "majority", "otherwise": "reject",
        "onResult": [{"if": {
            "condition": compare("$vote.result", "approve"),
            "then": [{"setVariable": {"name": "rejections", "value": 0}}, {"lua": {"call": "rejectTrack"}}, {"gotoPhase": {"phase": "quest"}}],
            "else": [{"setVariable": {"name": "rejections", "delta": 1}}, {"lua": {"call": "rejectTrack"}},
                     {"if": {"condition": compare("$var.rejections", 5, ">="),
                             "then": [{"message": {"text": "@fiveRejected"}}] + win("evil"),
                             "else": [{"endTurn": {}}]}}]}}],
    },
}))

call("add_phase", phaseJson=json.dumps({"id": "quest", "name": "Quest", "actions": ["quest"]}))
call("add_action", actionJson=json.dumps({
    "id": "quest", "name": "Quest", "actor": "any",
    # Good players can only succeed; the legal choices already show them success alone.
    "preconditions": [{"or": [compare("$actor.team", "evil"), compare("$param.choice", "success")]}],
    "vote": {
        "options": ["success", "fail"], "labels": {"success": "Success", "fail": "Fail"},
        # The rules judge the fail cards (one fails it, two on the fourth quest with 7 or more): no majority result.
        "who": "$var.team", "reveal": "count", "decide": "none",
        "onResult": [
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

call("add_phase", phaseJson=json.dumps({"id": "assassinate", "name": "The Assassin strikes", "actions": ["assassinate"]}))
call("add_action", actionJson=json.dumps({
    "id": "assassinate", "name": "Name Merlin", "actor": "any",
    "params": [{"name": "target", "type": "seat"}],
    "preconditions": [compare("$actor.role", "assassin"), {"not": compare("$param.target", "$actor.id")}],
    "effects": [
        {"revealRole": {"seat": "$param.target"}},
        {"if": {"condition": compare("$param.target.role", "merlin"),
                "then": [{"message": {"text": "@wasMerlin"}}] + win("evil"),
                "else": [{"message": {"text": "@wasNotMerlin"}}] + win("good")}},
    ],
}))

# The table: the quests' cards with their team sizes, the rejection track, and the pieces the rules move.
# One card per quest and player count (8, 9 and 10 share theirs), kept in a box: the game lays out the five for the
# players at the table when it starts (layQuests), so each card shows only its own team size.
SIZES = {5: [2, 3, 2, 3, 3], 6: [2, 3, 4, 3, 4], 7: [2, 3, 3, 4, 4], 8: [3, 4, 4, 5, 5]}
CARD = {"paper": [0.93, 0.89, 0.78], "ink": [0.2, 0.15, 0.1], "accent": [0.55, 0.38, 0.12]}
call("place_object", objectJson=json.dumps({
    "id": "quest_box", "type": "Bag", "name": "Quest cards", "tint": [0.45, 0.3, 0.15],
    "transform": {"position": [0.62, 0, -0.3]}}))
for players, sizes in SIZES.items():
    for q in range(1, 6):
        text = {"title": f"Quest {q}", "kind": "Team of", "body": str(sizes[q - 1])}
        if q == 4 and players >= 7:
            text["footer"] = "2 fails to sink it"
        call("place_object", objectJson=json.dumps({
            "id": f"quest_{q}_{players}", "type": "Card", "name": f"Quest {q}", "locked": True, "size": [0.14, 0.19],
            "text": text, "style": CARD, "container": "quest_box", "transform": {"position": [0.62, 0, -0.3]}}))
for n in range(1, 6):
    call("place_object", objectJson=json.dumps({
        "id": f"reject_{n}", "type": "Card", "name": f"Rejected team {n}", "locked": True, "size": [0.06, 0.06],
        "text": {"title": "Evil wins" if n == 5 else "Rejected", "body": str(n)},
        "style": {"paper": [0.75, 0.72, 0.68], "ink": [0.2, 0.15, 0.1], "accent": [0.6, 0.15, 0.12]},
        "transform": {"position": [-0.16 + 0.08 * (n - 1), 0, -0.17], "rotation": [0, 180, 0]}}))
call("create_supply", id="reject_marker", piece="cube", color="silver", mode="pile", count=1, name="Rejections", x=-0.26, z=-0.17)
call("create_supply", id="crown", piece="pawn", color="gold", mode="pile", count=1, name="Leader", x=0.3, z=-0.3)
call("create_supply", id="shield", piece="gem", color="silver", mode="pile", count=5, name="Team", x=-0.06, z=-0.3)
call("create_supply", id="good", piece="checker", color="blue", mode="pile", count=3, name="Quest succeeded", x=-0.62, z=0.06)
call("create_supply", id="evil", piece="checker", color="red", mode="pile", count=3, name="Quest failed", x=0.56, z=0.06)

call("add_event", eventName="onGameStart", effectsJson=json.dumps([{"lua": {"call": "layQuests"}}]))
call("set_turn", phases="propose,team_vote,quest,assassinate", order="clockwise", startingPlayer="random")
# Where the game stands, for everyone: the panel reads the variables the rules keep.
document = call("get_rules_document")
# The pieces bigger than a supply's default, so they read from across the table.
SCALES = {"crown": 2.4, "shield": 2.2, "good": 1.8, "evil": 1.8, "reject_marker": 1.6}
for obj in document["objects"]:
    base = obj["id"].split("_")[0] if obj["id"] != "reject_marker" else obj["id"]
    if base in SCALES:
        obj["transform"]["scale"] = [SCALES[base]] * 3
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
call("set_rules_document", documentJson=json.dumps(document))

# Spanish, for every text translate_game lists.
ES = {
    'game.name': 'Avalon',
    'game.description': 'Lealtades ocultas: el bien completa tres misiones; el mal hace fracasar tres o encuentra a Merlín al final. Un líder elige el equipo de cada misión, todos lo votan y el equipo la cumple o la sabotea en secreto.',
    'actions.pick.name': 'Elegir',
    'actions.unpick.name': 'Sacar del equipo',
    'actions.propose.name': 'Proponer este equipo',
    'actions.team_vote.name': 'Voto del equipo',
    'votes.team_vote.approve': 'Aprobar',
    'votes.team_vote.reject': 'Rechazar',
    'actions.quest.name': 'Misión',
    'votes.quest.success': 'Éxito',
    'votes.quest.fail': 'Fracaso',
    'actions.assassinate.name': 'Nombrar a Merlín',
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
    'roles.merlin.name': 'Merlín',
    'roles.merlin.description': 'Conoce a los malvados, salvo a Mordred. Debe esconderse del Asesino.',
    'roles.percival.name': 'Percival',
    'roles.percival.description': 'Ve a Merlín y a Morgana, sin saber quién es quién.',
    'roles.servant.name': 'Leal servidor',
    'roles.assassin.name': 'Asesino',
    'roles.assassin.description': 'Al final nombra a un jugador bueno: si es Merlín, gana el mal.',
    'roles.morgana.name': 'Morgana',
    'roles.morgana.description': 'Ante Percival parece Merlín.',
    'roles.mordred.name': 'Mordred',
    'roles.mordred.description': 'Oculto para Merlín.',
    'roles.oberon.name': 'Oberón',
    'roles.oberon.description': 'Los demás esbirros no lo conocen, y él no conoce a ninguno.',
    'roles.minion.name': 'Esbirro de Mordred',
}
ES.update({f"strings.{key}": text for key, (_, text) in STRINGS.items()})
ES["objects.quest_box.name"] = "Cartas de misión"
for players, sizes in SIZES.items():
    for q in range(1, 6):
        card = f"objects.quest_{q}_{players}"
        ES[f"{card}.name"] = ES[f"{card}.text.title"] = f"Misión {q}"
        ES[f"{card}.text.kind"] = "Equipo de"
        ES[f"{card}.text.body"] = str(sizes[q - 1])
        if q == 4 and players >= 7:
            ES[f"{card}.text.footer"] = "2 fracasos para hundirla"
for n in range(1, 6):
    ES[f"objects.reject_{n}.name"] = f"Equipo rechazado {n}"
    ES[f"objects.reject_{n}.text.title"] = "Gana el mal" if n == 5 else "Rechazado"
    ES[f"objects.reject_{n}.text.body"] = str(n)
ES["objects.reject_marker.name"] = "Rechazos"
ES["objects.crown.name"] = "Líder"
for i in ["", "_2", "_3", "_4", "_5"]:
    ES[f"objects.shield{i}.name"] = "Equipo"
for i in ["", "_2", "_3"]:
    ES[f"objects.good{i}.name"] = "Misión cumplida"
    ES[f"objects.evil{i}.name"] = "Misión fracasada"
call("set_translations", language="es", translationsJson=json.dumps(ES, ensure_ascii=False))
left = call("translate_game", language="es")
if left.get("left", 0):
    raise SystemExit(f"untranslated: {left.get('missing')}")

call("validate_rules")
call("save_game", path=OUT)
server.kill()
print(f"saved {OUT}")
