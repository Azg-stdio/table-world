"""Starts a Table World MCP server (the TableWorld.Mcp.exe shipped with a build, or any server command) over stdio,
does the MCP handshake, lists the tools and makes a game in a temporary Games folder.

    python -I tools/mcp/smoke.py "<build>/TableWorld.Mcp.exe"
"""
import subprocess, json, sys, time, tempfile
exe = sys.argv[1]
games = tempfile.mkdtemp()
p = subprocess.Popen([exe, "--no-bridge", "--games", games], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
def send(m): p.stdin.write(json.dumps(m) + "\n"); p.stdin.flush()
def recv():
    while True:
        line = p.stdout.readline()
        if not line: return None
        try: return json.loads(line)
        except: continue
t = time.time()
send({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke","version":"1"}}})
r = recv(); print("initialize:", r["result"]["serverInfo"], f"{time.time()-t:.1f}s")
send({"jsonrpc":"2.0","method":"notifications/initialized"})
send({"jsonrpc":"2.0","id":2,"method":"tools/list"})
r = recv(); print("tools:", len(r["result"]["tools"]))
send({"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"new_game","arguments":{"id":"smoke","name":"Smoke","players":2}}})
r = recv(); print("new_game:", r["result"]["content"][0]["text"][:200])
p.kill()
