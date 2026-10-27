"""M10 T11.15b ⚗ (the sandbox check): builds Secret Dictator from docs/rulebooks/secret-dictator.txt through the MCP
tools only, and saves games/secret-dictator.json. The milestone does not name this game: every gap it finds must be
fixed for all games.

    dotnet build mcp-server
    python -I tools/mcp/build_secret_dictator.py [path to TableWorld.Mcp.dll]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mcp_session import ROOT, McpSession, compare  # noqa: E402

OUT = os.path.join(ROOT, "games", "secret-dictator.json")


def count(zone, value, op=">="):
    return {"zoneCount": {"zone": zone, "op": op, "value": value}}


# When fewer than three policies are left to draw, the discards go back in.
RESHUFFLE = {"if": {"condition": count("policies", 3, "<"),
                    "then": [{"move": {"from": "discards", "take": "all", "to": "policies"}}, {"shuffle": {"object": "policies"}}]}}


def enact_top():
    """The top policy goes onto its track as it is (three failed elections)."""
    return [RESHUFFLE,
            {"if": {"condition": compare("$obj.policies.top.meta.type", "liberal"),
                    "then": [{"move": {"from": "policies", "to": "liberal_track", "faceUp": True}}],
                    "else": [{"move": {"from": "policies", "to": "fascist_track", "faceUp": True}}]}},
            {"message": {"text": "Chaos: the top policy is enacted."}},
            {"setVariable": {"name": "tracker", "value": 0}},
            {"setVariable": {"name": "lastChancellor", "value": ""}},
            {"setVariable": {"name": "lastPresident", "value": ""}}]


with McpSession(sys.argv[1] if len(sys.argv) > 1 else None, "build_secret_dictator") as mcp:
    mcp.call("new_game", id="secret-dictator", name="Secret Dictator", players=10, minPlayers=5, maxPlayers=10,
             description="Liberals and hidden fascists pass laws together. A president and a chancellor draw policies in "
                         "secret and say what they like about them. Find the Dictator before it is too late.")

    sizes = {5: (3, 2), 6: (4, 2), 7: (4, 3), 8: (5, 3), 9: (5, 4), 10: (6, 4)}
    mcp.call("set_roles", rolesJson={
        "teams": [{"id": "liberals", "name": "Liberals"}, {"id": "fascists", "name": "Fascists"}],
        "list": [
            {"id": "liberal", "name": "Liberal", "team": "liberals", "count": "fill"},
            {"id": "dictator", "name": "the Dictator", "team": "fascists", "description": "If elected chancellor after 3 fascist policies, the fascists win."},
            {"id": "fascist", "name": "Fascist", "team": "fascists", "count": "fill"},
        ],
        "byPlayers": {str(n): {"liberals": l, "fascists": f} for n, (l, f) in sizes.items()},
        "knows": [
            {"role": "fascist", "sees": {"team": "fascists"}, "as": "role"},
            {"role": "dictator", "sees": {"roles": ["fascist"]}, "as": "role", "maxPlayers": 6},
            {"role": "dictator", "sees": {}, "minPlayers": 7},
        ],
    })

    mcp.call("create_text_deck", id="policies", name="Policies", x=0, z=0, faceUp=False, cards=[
        {"id": "liberal", "title": "Liberal policy", "count": 6, "meta": {"type": "liberal"}},
        {"id": "fascist", "title": "Fascist policy", "count": 11, "meta": {"type": "fascist"}},
    ])
    for zone, x in (("liberal_track", -0.45), ("fascist_track", 0.45), ("discards", 0.0)):
        mcp.call("define_zone", zoneJson={"id": zone, "type": "ScriptingZone", "name": zone.replace("_", " ").capitalize(),
                                          "transform": {"position": [x, 0, 0.2 if zone != "discards" else -0.2]}, "size": [0.3, 0.1, 0.2]})

    mcp.call("set_variables", variablesJson={"chancellor": "", "lastChancellor": "", "lastPresident": "", "tracker": 0})

    mcp.call("add_phase", phaseJson={"id": "nominate", "name": "Nominate a chancellor", "actions": ["nominate"],
                                     "onEnter": [{"setVariable": {"name": "chancellor", "value": ""}},
                                                 {"message": {"text": "{$current.name} is president: nominate a chancellor."}}]})
    mcp.call("add_action", actionJson={
        "id": "nominate", "name": "Nominate", "actor": "current",
        "params": [{"name": "candidate", "type": "seat"}],
        "preconditions": [{"not": compare("$param.candidate", "$actor.id")},
                          {"not": compare("$param.candidate", "$var.lastChancellor")},
                          {"or": [compare("$alive.count", 5, "<="), {"not": compare("$param.candidate", "$var.lastPresident")}]}],
        "effects": [{"setVariable": {"name": "chancellor", "value": "$param.candidate"}},
                    {"message": {"text": "{$actor.name} nominates {$param.candidate.name}."}}, {"endPhase": {}}],
    })

    mcp.call("add_phase", phaseJson={"id": "election", "name": "Vote on the government", "actions": ["elect"]})
    mcp.call("add_action", actionJson={
        "id": "elect", "name": "Vote", "actor": "any",
        "vote": {"options": ["yes", "no"], "labels": {"yes": "Yes", "no": "No"}, "reveal": "named", "decide": "majority", "otherwise": "no",
                 "onResult": [{"if": {
                     "condition": compare("$vote.result", "yes"),
                     "then": [{"setVariable": {"name": "tracker", "value": 0}},
                              {"if": {"condition": {"and": [count("fascist_track", 3), compare("$var.chancellor.role", "dictator")]},
                                      "then": [{"message": {"text": "The Dictator is chancellor."}}, {"win": {"team": "fascists"}}],
                                      "else": [{"gotoPhase": {"phase": "president"}}]}}],
                     "else": [{"setVariable": {"name": "tracker", "delta": 1}},
                              {"if": {"condition": compare("$var.tracker", 3, ">="), "then": enact_top()}},
                              {"endTurn": {}}]}}]},
    })

    mcp.call("add_phase", phaseJson={"id": "president", "name": "The president discards", "actions": ["discard"],
                                     "onEnter": [RESHUFFLE, {"move": {"from": "policies", "count": 3, "to": "$current.hand", "faceUp": True}}]})
    mcp.call("add_action", actionJson={
        "id": "discard", "name": "Discard", "actor": "current",
        "params": [{"name": "card", "type": "object", "filter": {"inZone": "$actor.hand"}}],
        "effects": [{"move": {"object": "$param.card", "to": "discards", "faceUp": False}},
                    {"move": {"from": "$actor.hand", "take": "all", "to": "$var.chancellor.hand", "faceUp": True}},
                    {"endPhase": {}}],
    })

    mcp.call("add_phase", phaseJson={"id": "chancellor", "name": "The chancellor enacts", "actions": ["enact"]})
    mcp.call("add_action", actionJson={
        "id": "enact", "name": "Enact", "actor": "any",
        "params": [{"name": "card", "type": "object", "filter": {"inZone": "$actor.hand"}}],
        "preconditions": [compare("$actor.id", "$var.chancellor")],
        "effects": [
            {"if": {"condition": compare("$param.card.meta.type", "liberal"),
                    "then": [{"move": {"object": "$param.card", "to": "liberal_track", "faceUp": True}},
                             {"message": {"text": "A liberal policy is enacted."}}],
                    "else": [{"move": {"object": "$param.card", "to": "fascist_track", "faceUp": True}},
                             {"message": {"text": "A fascist policy is enacted."}}]}},
            {"move": {"from": "$actor.hand", "take": "all", "to": "discards", "faceUp": False}},
            {"setVariable": {"name": "lastChancellor", "value": "$actor.id"}},
            {"setVariable": {"name": "lastPresident", "value": "$current.id"}},
            {"if": {"condition": {"and": [compare("$param.card.meta.type", "fascist"),
                                          {"or": [count("fascist_track", 4, "=="), count("fascist_track", 5, "==")]}]},
                    "then": [{"gotoPhase": {"phase": "execution"}}], "else": [{"endTurn": {}}]}},
        ],
    })

    mcp.call("add_phase", phaseJson={"id": "execution", "name": "The president executes", "actions": ["execute"]})
    mcp.call("add_action", actionJson={
        "id": "execute", "name": "Execute", "actor": "current",
        "params": [{"name": "target", "type": "seat"}],
        "preconditions": [{"not": compare("$param.target", "$actor.id")}],
        "effects": [{"message": {"text": "{$actor.name} executes {$param.target.name}."}},
                    {"if": {"condition": compare("$param.target.role", "dictator"),
                            "then": [{"revealRole": {"seat": "$param.target"}}, {"win": {"team": "liberals"}}],
                            "else": [{"eliminate": {"seat": "$param.target"}}, {"endTurn": {}}]}}],
    })

    mcp.call("set_turn", phases="nominate,election,president,chancellor,execution", order="clockwise", startingPlayer="random")
    mcp.call("add_event", eventName="onGameStart", effectsJson=[{"shuffle": {"object": "policies"}}])
    mcp.call("set_win_condition", winConditionJson={"when": count("liberal_track", 5), "team": "liberals", "checkAfter": "anyCommand"})
    mcp.call("set_win_condition", winConditionJson={"when": count("fascist_track", 6), "team": "fascists", "checkAfter": "anyCommand"})
    mcp.call("validate_rules")
    mcp.call("save_game", path=OUT)
print(f"saved {OUT}")
