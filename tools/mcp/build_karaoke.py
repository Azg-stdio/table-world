"""M11: builds Karaoke night through the MCP tools only and saves games/karaoke-night.json: the karaoke room, a turn per
singer, and passing the microphone. The song on the screen and the listeners' delay come with sing mode (T12.5).

    dotnet build mcp-server
    python -I tools/mcp/build_karaoke.py [path to TableWorld.Mcp.dll]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mcp_session import ROOT, McpSession  # noqa: E402

OUT = os.path.join(ROOT, "games", "karaoke-night.json")

HOW = """Karaoke night is a room for singing together: sofas round the table, a big screen on the far wall.

Sit down with the + over a seat, then Start. Whoever's turn it is sings: they hold the microphone and the stage light is on them. When the song is done, press "Pass the microphone" and the next seat sings.

Voice: hold T to talk (Settings, Voice can open the microphone when you speak). Songs on the screen are not there yet."""

with McpSession(sys.argv[1] if len(sys.argv) > 1 else None, name="build_karaoke") as mcp:
    mcp.call("new_game", id="karaoke-night", name="Karaoke night", players=6, minPlayers=1, room="karaoke",
             description="Take turns at the microphone in the karaoke room: whoever's turn it is sings, then passes the microphone.")
    mcp.call("add_action", actionJson={
        "id": "pass_mic", "name": "Pass the microphone", "actor": "current",
        "effects": [{"endTurn": {}}],
    })
    mcp.call("add_phase", phaseJson={"id": "sing", "name": "Singing", "actions": ["pass_mic"], "advanceWhen": {"currentPlayerEnds": True}})
    mcp.call("set_turn", phases="sing", order="clockwise", startingPlayer="first")
    mcp.call("set_notebook", id="how", title="How it works", text=HOW)
    mcp.call("validate_rules")
    mcp.call("save_game", path=OUT)
print(f"saved {OUT}")
