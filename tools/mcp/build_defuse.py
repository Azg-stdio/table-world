"""M10, a second sandbox check (asymmetric information): builds Defuse from docs/rulebooks/defuse.txt through the MCP
tools only, and saves games/defuse.json. The Defuser alone sees the bomb (a hidden zone shown to a role); the Experts
alone read the manual (notebook pages for a role); the wire logic is Lua, the format's escape hatch.

    dotnet build mcp-server
    python -I tools/mcp/build_defuse.py [path to TableWorld.Mcp.dll]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mcp_session import ROOT, McpSession, compare  # noqa: E402

OUT = os.path.join(ROOT, "games", "defuse.json")
COLORS = {"red": [0.85, 0.18, 0.15], "blue": [0.2, 0.4, 0.9], "yellow": [0.95, 0.8, 0.15], "white": [0.95, 0.95, 0.95], "black": [0.12, 0.12, 0.12]}

LUA = """
-- Lays 3 to 6 random wires in a row on the bomb, left to right as the Defuser (facing the bomb from the south) sees them.
function setupBomb(ctx)
  local deck = getObjectFromGUID("wires")
  deck.shuffle()
  local zone = getObjectFromGUID("bomb")
  local p = zone.getPosition()
  local count = Game.roll(1, 4)[1] + 2
  local order = {}
  for i = 1, count do
    local x = p.x + (i - (count + 1) / 2) * 2.6
    local card = deck.takeObject({ position = { x, p.y + 0.2, p.z }, rotation = { 0, 180, 0 } })
    order[#order + 1] = card.getGUID()
  end
  Game.setVariable("order", order)
  -- Five minutes: then it explodes (the game window keeps the time).
  Wait.time(function() if not Game.isOver() then Game.log("The bomb explodes.") Game.winTeam("bomb") end end, 300)
end

local function colour(guid)
  local name = string.lower(getObjectFromGUID(guid).getName())
  for _, c in ipairs({ "red", "blue", "yellow", "white", "black" }) do
    if string.find(name, c) then return c end
  end
end

-- The wire the manual says to cut, by position from 1.
function rightWire()
  local order = Game.getVariable("order")
  local colours = {}
  for i, guid in ipairs(order) do colours[i] = colour(guid) end
  local n = #colours
  local function count(c) local k = 0 for _, x in ipairs(colours) do if x == c then k = k + 1 end end return k end
  local function last(c) for i = n, 1, -1 do if colours[i] == c then return i end end end
  if n == 3 then
    if count("red") == 0 then return 2 elseif colours[n] == "white" then return n else return 1 end
  elseif n == 4 then
    if count("red") > 1 then return last("red") elseif count("yellow") == 0 then return 3 else return 1 end
  elseif n == 5 then
    if colours[n] == "black" then return 4 elseif count("red") == 1 then return 1 else return 2 end
  else
    if count("yellow") == 0 then return 3 elseif count("white") > 1 then return 6 else return 5 end
  end
end

function cut(ctx)
  local order = Game.getVariable("order")
  local wire = ctx.params.wire
  local cuts = Game.getVariable("cut")
  for _, done in ipairs(cuts) do if done == wire then return "That wire is already cut." end end
  if wire == order[rightWire()] then
    Game.log("The bomb is defused!")
    Game.winTeam("squad")
    return
  end
  cuts[#cuts + 1] = wire
  Game.setVariable("cut", cuts)
  getObjectFromGUID(wire).flip()
  local strikes = Game.getVariable("strikes") + 1
  Game.setVariable("strikes", strikes)
  Game.log("Strike " .. strikes .. ".")
  if strikes >= 3 then Game.log("The bomb explodes.") Game.winTeam("bomb") end
end
"""

with McpSession(sys.argv[1] if len(sys.argv) > 1 else None, "build_defuse") as mcp:
    mcp.call("new_game", id="defuse", name="Defuse", players=6, minPlayers=2, maxPlayers=6,
             description="One of you sees the bomb, the others read the manual. Talk it through before it explodes.")
    mcp.call("set_roles", rolesJson={
        "teams": [{"id": "squad", "name": "The squad"}, {"id": "bomb", "name": "The bomb"}],
        "list": [
            {"id": "defuser", "name": "Defuser", "team": "squad", "description": "Only you see the bomb. Describe it; cut the wire the Experts tell you."},
            {"id": "expert", "name": "Expert", "team": "squad", "count": "fill", "description": "Only you read the manual. Tell the Defuser which wire to cut."},
        ],
    })
    mcp.call("create_text_deck", id="wires", name="Wires", x=0.55, z=0.3, faceUp=False, cards=[
        {"id": color, "title": f"{color.capitalize()} wire", "count": 4, "meta": {"color": color},
         "style": {"paper": rgb, "ink": [0.05, 0.05, 0.05] if color in ("yellow", "white") else [0.95, 0.95, 0.95]}}
        for color, rgb in COLORS.items()])
    mcp.call("define_zone", zoneJson={"id": "bomb", "type": "HiddenZone", "name": "The bomb", "visibleTo": ["role:defuser"],
                                      "transform": {"position": [0, 0, 0]}, "size": [0.6, 0.2, 0.25]})
    mcp.call("set_variables", variablesJson={"order": [], "cut": [], "strikes": 0}, secret="order")
    mcp.call("set_script", target="global", code=LUA)
    mcp.call("add_phase", phaseJson={"id": "defuse", "name": "Defuse the bomb", "actions": ["cut"],
                                     "onEnter": [{"lua": {"call": "setupBomb"}}]})
    mcp.call("add_action", actionJson={
        "id": "cut", "name": "Cut", "actor": "any",
        "params": [{"name": "wire", "type": "object", "filter": {"inZone": "bomb"}}],
        "preconditions": [compare("$actor.role", "defuser")],
        "effects": [{"lua": {"call": "cut"}}],
    })
    mcp.call("set_turn", phases="defuse", order="clockwise", startingPlayer="first")
    manual = open(os.path.join(ROOT, "docs", "rulebooks", "defuse.txt"), encoding="utf-8").read()
    mcp.call("set_notebook", id="manual", title="Bomb manual", text=manual[manual.index("- 3 wires:"):].strip(), forRole="role:expert")
    mcp.call("set_notebook", id="rules", title="How to play", text=manual[manual.index("Roles"):manual.index("The manual")].strip())
    mcp.call("validate_rules")
    mcp.call("save_game", path=OUT)
print(f"saved {OUT}")
