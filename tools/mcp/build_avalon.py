"""M10 T11.6: builds Avalon from docs/rulebooks/avalon.txt through the MCP tools only, the way an agent would, and
saves it as games/avalon.json. Every call is a tools/call to the server over stdio; a failed call stops the build.

    dotnet build mcp-server -c Release      (or Debug: pass the DLL path)
    python -I tools/mcp/build_avalon.py [path to TableWorld.Mcp.dll]

The tools used are the general ones (new_game, set_roles, set_variables, set_script, add_phase, add_action,
set_turn, validate_rules, save_game); nothing in the server knows Avalon.
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
""")


def compare(left, right, op="=="):
    return {"compare": {"left": left, "op": op, "right": right}}


call("add_phase", phaseJson=json.dumps({
    "id": "propose", "name": "Pick the team", "actions": ["pick", "unpick"],
    "onEnter": [
        {"lua": {"call": "setQuest"}},
        {"setVariable": {"name": "team", "value": []}},
        {"message": {"text": "{$current.name} leads quest {$var.quest}: pick {$var.size} players."}},
    ],
    "advanceWhen": {"condition": compare("$var.team.count", "$var.size")},
}))
call("add_action", actionJson=json.dumps({
    "id": "pick", "name": "Pick", "actor": "current",
    "params": [{"name": "member", "type": "seat"}],
    "preconditions": [{"not": {"inList": {"list": "$var.team", "value": "$param.member"}}},
                      compare("$var.team.count", "$var.size", "<")],
    "effects": [{"setVariable": {"name": "team", "add": "$param.member"}},
                {"message": {"text": "{$actor.name} picks {$param.member.name}."}}],
}))
call("add_action", actionJson=json.dumps({
    "id": "unpick", "name": "Take off the team", "actor": "current",
    "params": [{"name": "member", "type": "seat"}],
    "preconditions": [{"inList": {"list": "$var.team", "value": "$param.member"}}],
    "effects": [{"setVariable": {"name": "team", "remove": "$param.member"}}],
}))

call("add_phase", phaseJson=json.dumps({"id": "team_vote", "name": "Vote on the team", "actions": ["team_vote"]}))
call("add_action", actionJson=json.dumps({
    "id": "team_vote", "name": "Team vote", "actor": "any",
    "vote": {
        "options": ["approve", "reject"], "labels": {"approve": "Approve", "reject": "Reject"},
        "reveal": "named", "decide": "majority", "otherwise": "reject",
        "onResult": [{"if": {
            "condition": compare("$vote.result", "approve"),
            "then": [{"setVariable": {"name": "rejections", "value": 0}}, {"gotoPhase": {"phase": "quest"}}],
            "else": [{"setVariable": {"name": "rejections", "delta": 1}},
                     {"if": {"condition": compare("$var.rejections", 5, ">="),
                             "then": [{"message": {"text": "Five teams rejected in a row."}}, {"win": {"team": "evil"}}],
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
        "who": "$var.team", "reveal": "count",
        "onResult": [
            {"if": {"condition": compare("$vote.fail", "$var.needFails", ">="),
                    "then": [{"setVariable": {"name": "fails", "delta": 1}}, {"message": {"text": "Quest {$var.quest} fails."}}],
                    "else": [{"setVariable": {"name": "successes", "delta": 1}}, {"message": {"text": "Quest {$var.quest} succeeds."}}]}},
            {"if": {"condition": compare("$var.fails", 3, ">="),
                    "then": [{"win": {"team": "evil"}}],
                    "else": [{"if": {"condition": compare("$var.successes", 3, ">="),
                                     "then": [{"message": {"text": "Three quests succeeded. The Assassin names Merlin."}},
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
                "then": [{"win": {"team": "evil"}}], "else": [{"win": {"team": "good"}}]}},
    ],
}))

call("set_turn", phases="propose,team_vote,quest,assassinate", order="clockwise", startingPlayer="random")
call("validate_rules")
call("save_game", path=OUT)
server.kill()
print(f"saved {OUT}")
