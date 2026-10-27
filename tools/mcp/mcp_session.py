"""A Table World MCP server over stdio, for build scripts (tools/mcp/build_*.py): start it headless in a temporary
Games folder, call tools by name, stop at the first refusal.

    from mcp_session import McpSession
    with McpSession() as mcp:
        mcp.call("new_game", id="x", name="X", players=4)
"""
import json
import os
import subprocess
import sys
import tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DEFAULT_DLL = os.path.join(ROOT, "mcp-server", "bin", "Debug", "net9.0", "TableWorld.Mcp.dll")


class McpSession:
    def __init__(self, dll=None, name="build"):
        self._dll = dll or DEFAULT_DLL
        self._name = name
        self._next = 1
        self._server = None

    def __enter__(self):
        games = tempfile.mkdtemp()
        self._server = subprocess.Popen(["dotnet", self._dll, "--no-bridge", "--games", games], stdin=subprocess.PIPE,
                                        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, encoding="utf-8")
        self._rpc("initialize", {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": self._name, "version": "1"}})
        self._rpc("notifications/initialized", notify=True)
        return self

    def __exit__(self, *_):
        self._server.kill()

    def _rpc(self, method, params=None, notify=False):
        message = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            message["params"] = params
        if not notify:
            message["id"] = self._next
            self._next += 1
        self._server.stdin.write(json.dumps(message) + "\n")
        self._server.stdin.flush()
        if notify:
            return None
        while True:
            line = self._server.stdout.readline()
            if not line:
                raise SystemExit("the server stopped")
            try:
                reply = json.loads(line)
            except ValueError:
                continue
            if reply.get("id") == message["id"]:
                return reply

    def call(self, tool, **arguments):
        """Calls a tool; JSON-valued arguments may be given as Python objects. Exits on a refusal."""
        for key, value in list(arguments.items()):
            if key.endswith("Json") and not isinstance(value, str):
                arguments[key] = json.dumps(value)
        reply = self._rpc("tools/call", {"name": tool, "arguments": arguments})
        text = reply["result"]["content"][0]["text"]
        try:
            body = json.loads(text)
        except ValueError:
            body = {"ok": True, "text": text}
        if body.get("ok") is False:
            print(f"{tool} failed:\n{json.dumps(body, indent=2)}")
            self._server.kill()
            sys.exit(1)
        print(f"{tool}: ok")
        return body


def compare(left, right, op="=="):
    return {"compare": {"left": left, "op": op, "right": right}}
