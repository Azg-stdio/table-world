"""M11: builds Karaoke night through the MCP tools only and saves games/karaoke-night.json: the karaoke room, which has
no table (people walk in it, ADR 0013 section 5), and no rules: the screen, the song queue and the stage come with the
room itself (T12.11-T12.13).

    dotnet build mcp-server
    python -I tools/mcp/build_karaoke.py [path to TableWorld.Mcp.dll]
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mcp_session import ROOT, McpSession  # noqa: E402

OUT = os.path.join(ROOT, "games", "karaoke-night.json")

HOW = """Karaoke night is a room to sing together in: sofas, a big screen on the far wall, no table.

Walk: W A S D (or the arrows), hold Shift to hurry. Look around: hold the right mouse button and move the mouse.
Sit: walk up to a sofa and press C; press C again (or walk) to stand up.
The big screen: paste a YouTube link in the "Big screen" bar and press Play.
Voice: hold T to talk (Settings, Voice can open the microphone when you speak)."""

with McpSession(sys.argv[1] if len(sys.argv) > 1 else None, name="build_karaoke") as mcp:
    mcp.call("new_game", id="karaoke-night", name="Karaoke night", players=8, minPlayers=1, room="karaoke",
             description="A room for singing together: walk around, sit on the sofas, put a song on the big screen.")
    # A room people walk in has no table: the hands new_game laid round one would float in the air.
    doc = mcp.call("get_rules_document")
    doc["zones"] = []
    for seat in doc["players"]["seats"]:
        seat.pop("handZone", None)
    mcp.call("set_rules_document", documentJson=json.dumps(doc))
    mcp.call("set_notebook", id="how", title="How it works", text=HOW)
    mcp.call("validate_rules")
    mcp.call("save_game", path=OUT)
print(f"saved {OUT}")
