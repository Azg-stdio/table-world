"""M10 T11.15: builds Wink Murder from docs/rulebooks/wink-murder.txt through the MCP tools only, and saves
games/wink-murder.json.

    dotnet build mcp-server
    python -I tools/mcp/build_wink_murder.py [path to TableWorld.Mcp.dll]

General pieces only: roles, onGesture (a wink at the face the murderer looks at), a private line to the target, a second
hidden trait (fate) that only its player sees, eliminate with the role shown, and win conditions on who is left.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mcp_session import ROOT, McpSession, compare  # noqa: E402

OUT = os.path.join(ROOT, "games", "wink-murder.json")

with McpSession(sys.argv[1] if len(sys.argv) > 1 else None, "build_wink_murder") as mcp:
    mcp.call("new_game", id="wink-murder", name="Wink Murder", players=12, minPlayers=5, maxPlayers=12,
             description="One of you is the Murderer and kills by winking. Watch the faces: catch the wink before "
                         "everyone is gone. Look at someone and press K to wink.")

    mcp.call("set_roles", rolesJson={
        "teams": [{"id": "town", "name": "The town"}, {"id": "murderer", "name": "The Murderer"}],
        "list": [
            {"id": "murderer", "name": "Murderer", "team": "murderer", "description": "Wink (K) at a player looking at you to kill them."},
            {"id": "detective", "name": "Detective", "team": "town", "optional": True, "description": "Accuse the Murderer; a wrong guess puts you out."},
            {"id": "guest", "name": "Guest", "team": "town", "count": "fill"},
            # Each player's fate is a second hidden trait: only they learn they were winked at, so only they get Fall.
            {"id": "alive", "name": "Alive", "set": "fate", "count": "fill"},
            {"id": "winked", "name": "Winked at", "set": "fate", "count": 0},
        ],
    })

    mcp.call("add_phase", phaseJson={"id": "party", "name": "The party", "actions": ["fall", "accuse"]})

    mcp.call("add_event", eventName="onGesture", effectsJson=[{"if": {
        "condition": {"and": [compare("$param.gesture", "wink"), compare("$actor.role", "murderer"),
                              {"not": compare("$param.target", None)}, {"not": compare("$param.target.out", True)}]},
        "then": [{"setRole": {"seat": "$param.target", "role": "winked", "set": "fate"}},
                 {"message": {"text": "You have been winked at. Count to five, then fall.", "to": "$param.target"}}]}}])

    mcp.call("add_action", actionJson={
        "id": "fall", "name": "Fall", "actor": "any",
        "preconditions": [compare("$actor.role.fate", "winked")],
        "effects": [{"message": {"text": "{$actor.name} falls to the floor."}},
                    {"eliminate": {"seat": "$actor", "revealRole": True}}],
    })
    mcp.call("add_action", actionJson={
        "id": "accuse", "name": "Accuse", "actor": "any",
        "params": [{"name": "suspect", "type": "seat"}],
        "preconditions": [{"not": compare("$param.suspect", "$actor.id")},
                          {"or": [compare("$actor.role", "detective"), compare("$alive.role.detective", 0)]}],
        "effects": [
            {"message": {"text": "{$actor.name} accuses {$param.suspect.name}!"}},
            {"if": {"condition": compare("$param.suspect.role", "murderer"),
                    "then": [{"revealRole": {"seat": "$param.suspect"}}, {"win": {"team": "town"}}],
                    "else": [{"message": {"text": "Wrong: {$param.suspect.name} is innocent."}},
                             {"eliminate": {"seat": "$actor", "revealRole": True}}]}}],
    })

    mcp.call("set_turn", phases="party", order="clockwise", startingPlayer="first")
    mcp.call("set_win_condition", winConditionJson={"when": compare("$alive.count", 2, "<="), "team": "murderer", "checkAfter": "anyCommand"})
    mcp.call("validate_rules")
    mcp.call("save_game", path=OUT)
print(f"saved {OUT}")
