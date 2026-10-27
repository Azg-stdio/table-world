"""M10 T11.14: builds Werewolf from docs/rulebooks/werewolf.txt through the MCP tools only, and saves games/werewolf.json.

    dotnet build mcp-server
    python -I tools/mcp/build_werewolf.py [path to TableWorld.Mcp.dll]

General pieces only: roles with knows, eyes-closed phases (awake team or role, voice of the awake), a private vote for
the wolves' victim, an open vote for the lynch, eliminate with the role shown, alive counts in team win conditions.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mcp_session import ROOT, McpSession, compare  # noqa: E402

OUT = os.path.join(ROOT, "games", "werewolf.json")

with McpSession(sys.argv[1] if len(sys.argv) > 1 else None, "build_werewolf") as mcp:
    mcp.call("new_game", id="werewolf", name="Werewolf", players=12, minPlayers=6, maxPlayers=12,
             description="A village hides werewolves. By night they kill in secret while the Seer and the Doctor work; "
                         "by day everyone talks and votes to lynch a suspect. The village wins when no wolf is left, "
                         "the wolves when they are as many as the villagers.")

    mcp.call("set_roles", rolesJson={
        "teams": [{"id": "village", "name": "Villagers"}, {"id": "wolves", "name": "Werewolves"}],
        "list": [
            {"id": "seer", "name": "Seer", "team": "village", "description": "Each night, learns one player's side."},
            {"id": "doctor", "name": "Doctor", "team": "village", "optional": True, "description": "Each night, protects one player from the wolves."},
            {"id": "villager", "name": "Villager", "team": "village", "count": "fill"},
            {"id": "wolf", "name": "Werewolf", "team": "wolves", "count": "fill"},
        ],
        "byPlayers": {str(n): {"village": n - (2 if n <= 8 else 3), "wolves": 2 if n <= 8 else 3} for n in range(6, 13)},
        "knows": [{"team": "wolves", "sees": {"team": "wolves"}, "as": "role"}],
    })

    # Whom the wolves chose and whom the Doctor saved stay on the host until dawn.
    mcp.call("set_variables", variablesJson={"night": 1, "victim": "", "saved": ""}, secret="victim,saved")

    # Night 1: the wolves choose a victim in a vote only they know about.
    mcp.call("add_phase", phaseJson={
        "id": "wolves", "name": "Night: the werewolves", "awake": "team:wolves", "voice": "awake",
        "sleepText": "Night {$var.night}. Eyes closed: the werewolves are awake.",
        "actions": ["hunt"],
        "onEnter": [{"setVariable": {"name": "victim", "value": ""}}, {"setVariable": {"name": "saved", "value": ""}}],
    })
    mcp.call("add_action", actionJson={
        "id": "hunt", "name": "Choose the victim", "actor": "any",
        "vote": {"seats": True, "who": "team:wolves", "private": True, "reveal": "result", "otherwise": "none",
                 "onResult": [{"setVariable": {"name": "victim", "value": "$vote.result"}}, {"endPhase": {}}]},
    })

    # The Seer looks at one card: only its side.
    mcp.call("add_phase", phaseJson={
        "id": "seer", "name": "Night: the Seer", "awake": "role:seer", "voice": "none",
        "sleepText": "Night {$var.night}. Eyes closed: the Seer is awake.",
        "actions": ["peek"],
        "onEnter": [{"if": {"condition": compare("$alive.role.seer", 0), "then": [{"endPhase": {}}]}}],
    })
    mcp.call("add_action", actionJson={
        "id": "peek", "name": "Look at a card", "actor": "any",
        "params": [{"name": "target", "type": "seat"}],
        "preconditions": [compare("$actor.role", "seer"), {"not": compare("$param.target", "$actor.id")}],
        "effects": [{"revealRole": {"seat": "$param.target", "to": "$actor", "teamOnly": True}}, {"endPhase": {}}],
    })

    # The Doctor protects one player.
    mcp.call("add_phase", phaseJson={
        "id": "doctor", "name": "Night: the Doctor", "awake": "role:doctor", "voice": "none",
        "sleepText": "Night {$var.night}. Eyes closed: the Doctor is awake.",
        "actions": ["protect"],
        "onEnter": [{"if": {"condition": compare("$alive.role.doctor", 0), "then": [{"endPhase": {}}]}}],
    })
    mcp.call("add_action", actionJson={
        "id": "protect", "name": "Protect", "actor": "any",
        "params": [{"name": "target", "type": "seat"}],
        "preconditions": [compare("$actor.role", "doctor")],
        "effects": [{"setVariable": {"name": "saved", "value": "$param.target"}}, {"endPhase": {}}],
    })

    # Dawn: the victim dies unless protected.
    mcp.call("add_phase", phaseJson={
        "id": "dawn", "name": "Dawn", "actions": [], "advanceWhen": {"immediate": True},
        "onEnter": [{"if": {
            "condition": {"and": [{"not": compare("$var.victim", "")}, {"not": compare("$var.victim", "none")},
                                  {"not": compare("$var.victim", "$var.saved")}]},
            "then": [{"message": {"text": "Dawn: {$var.victim.name} was found dead."}},
                     {"eliminate": {"seat": "$var.victim", "revealRole": True}}],
            "else": [{"message": {"text": "Dawn: nobody died tonight."}}]}}],
    })

    # The day: an open vote to lynch.
    mcp.call("add_phase", phaseJson={"id": "day", "name": "Day: talk and vote", "actions": ["lynch"]})
    mcp.call("add_action", actionJson={
        "id": "lynch", "name": "Lynch", "actor": "any",
        "vote": {"seats": True, "reveal": "named", "otherwise": "none",
                 "onResult": [
                     {"if": {"condition": compare("$vote.result", "none"),
                             "then": [{"message": {"text": "The village could not agree."}}],
                             "else": [{"eliminate": {"seat": "$vote.result", "revealRole": True}}]}},
                     {"setVariable": {"name": "night", "delta": 1}},
                     {"endTurn": {}}]},
    })

    mcp.call("set_turn", phases="wolves,seer,doctor,dawn,day", order="clockwise", startingPlayer="first")
    mcp.call("set_win_condition", winConditionJson={"when": compare("$alive.team.wolves", 0), "team": "village", "checkAfter": "anyCommand"})
    mcp.call("set_win_condition", winConditionJson={"when": compare("$alive.team.wolves", "$alive.team.village", ">="), "team": "wolves",
                                                    "checkAfter": "anyCommand"})
    mcp.call("validate_rules")
    mcp.call("save_game", path=OUT)
print(f"saved {OUT}")
