"""Lua API coverage of the local mods (M5, T6.4).

Reads the per-mod requirement report (mod_requirements.py --json) and says, per mod and overall, which share of the Lua
calls the mods make our ScriptHost already provides (docs/lua-api.md), weighted by how often each is called, and lists the
most used missing ones. Keep IMPLEMENTED in step with core/Scripting.

    python -I tools/tts-analysis/lua_coverage.py docs/data/mod-requirements-2026-10-23.json [--md out.md]
"""
import argparse
import collections
import json

IMPLEMENTED = {
    # globals
    "getObjectFromGUID", "getObjects", "getAllObjects", "getObjectsWithTag", "broadcastToAll", "printToAll",
    "broadcastToColor", "printToColor", "print",
    # namespaced
    "JSON.encode", "JSON.decode", "Wait.time", "Wait.frames", "Wait.condition", "Wait.stop", "Wait.stopAll",
    "Global.call", "Global.Call", "Global.getVar", "Global.setVar",
    # object methods
    "getGUID", "getName", "setName", "getDescription", "setDescription", "getGMNotes", "setGMNotes", "getTags", "hasTag",
    "getPosition", "setPosition", "setPositionSmooth", "getRotation", "setRotation", "setRotationSmooth", "flip", "shuffle",
    "randomize", "roll", "lock", "unlock", "setLock", "getLock", "is_face_down", "getValue", "setValue", "getQuantity",
    "getObjects", "takeObject", "putObject", "destruct", "call", "getVar", "setVar", "getLuaScript",
    "setLuaScript", "getTable", "setTable", "getColorTint", "setColorTint", "getScale", "setScale", "getBounds",
    "getBoundsNormalized", "getZones", "positionToWorld", "positionToLocal", "playLoopingEffect", "playTriggerEffect",
    "setVelocity", "getVelocity", "addForce", "addTorque", "highlightOn", "highlightOff", "startLuaCoroutine",
    "destroyObject", "getSeatedPlayers", "onSave", "onObjectPickUp", "onPickUp",
    "isSmoothMoving", "reload", "setState", "getStates", "getCustomObject", "log", "JSON.encode_pretty", "Global.getTable",
    "Global.setTable", "getNotebookTabs", "addNotebookTab", "editNotebookTab", "removeNotebookTab",
    "onObjectEnterScriptingZone", "onObjectLeaveScriptingZone",
    "createButton", "editButton", "removeButton", "clearButtons", "getButtons",
    "UI.setAttribute", "UI.setAttributes", "UI.getAttribute", "UI.getAttributes", "UI.setValue", "UI.getValue", "UI.show",
    "UI.hide", "UI.setXml", "UI.getXml", "UI.setXmlTable", "UI.getXmlTable", "UI.setCustomAssets", "UI.getCustomAssets",
    "onClick", "onValueChanged", "onEndEdit", "getHandObjects", "lookAt", "Player.getPlayers", "Player.getAvailableColors",
    "setInvisibleTo", "getInvisibleTo", "deal", "isDestroyed", "clone", "Timer.create", "Timer.destroy", "getStateId", "addTag",
    "removeTag", "setTags", "getObjectsWithAllTags", "getObjectsWithAnyTags",
    "Physics.cast", "Physics.getGravity", "onCollisionEnter", "onCollisionExit", "onObjectCollisionEnter",
    "onObjectCollisionExit", "registerCollisions", "unregisterCollisions",
    "spawnObject", "onObjectEnterContainer", "onObjectLeaveContainer", "onChat",
    "createInput", "editInput", "removeInput", "clearInputs", "getInputs",
    "setCustomObject", "getComponent", "MusicPlayer.getPlaylist",
    # Vector and Color methods (core/Scripting/LuaPrelude.cs)
    "copy", "get", "set", "setAt", "add", "sub", "scale", "clamp", "sqrMagnitude", "magnitude", "normalize", "normalized",
    "sqrDistance", "distance", "dot", "cross", "angle", "lerp", "inverse", "equals", "string", "heading", "rotateOver",
    "between", "fromString", "fromHex", "toHex", "toString",
    # events
    "onLoad", "onObjectDrop", "onDrop", "onObjectEnterZone", "onObjectLeaveZone", "onObjectFlip", "onFlip",
    "onObjectRandomize", "onRandomize", "onPlayerTurn",
    "getData", "spawnObjectData", "reset", "layout", "onUpdate", "onObjectSpawn", "onObjectDestroy",
    "onScriptingButtonDown", "onScriptingButtonUp", "onNumberTyped", "getHoldingObjects", "onPlayerConnect",
    "setPage", "getPage",
}

# Lua built-ins and string/table/math library names the analyzer may count as methods; they are provided by MoonSharp.
LUA_BUILTINS = {
    "insert", "remove", "concat", "sort", "unpack", "pack", "format", "sub", "gsub", "find", "match", "gmatch", "len",
    "lower", "upper", "rep", "byte", "char", "reverse", "floor", "ceil", "abs", "max", "min", "random", "sqrt", "huge",
    "pi", "fmod", "modf", "tostring", "tonumber", "pairs", "ipairs", "next", "type", "select", "error", "assert", "pcall",
    "xpcall", "setmetatable", "getmetatable", "rawget", "rawset", "time", "clock", "date",
}


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("report")
    parser.add_argument("--md")
    args = parser.parse_args()
    mods = json.load(open(args.report, encoding="utf-8"))
    total = collections.Counter()
    lines = ["# Lua API coverage (T6.4)", "", f"Source: `{args.report}`. Calls weighted by how often the mods' scripts make them.", ""]
    lines += ["| Mod | Calls | Covered | Share |", "|---|---|---|---|"]
    for mod in mods:
        calls = collections.Counter()
        for key in ("lua_globals", "lua_methods", "lua_namespaced", "lua_events"):
            for name, count in (mod.get(key) or {}).items():
                if name not in LUA_BUILTINS:
                    calls[name] += count
        covered = sum(c for n, c in calls.items() if n in IMPLEMENTED)
        whole = sum(calls.values())
        total.update(calls)
        share = covered / whole if whole else 1.0
        lines.append(f"| {mod['name']} | {whole} | {covered} | {share:.0%} |")
        print(f"{mod['name'][:40]:40} {covered:6}/{whole:<6} {share:.0%}")
    covered = sum(c for n, c in total.items() if n in IMPLEMENTED)
    whole = sum(total.values())
    print(f"{'ALL':40} {covered:6}/{whole:<6} {covered / whole if whole else 1:.0%}")
    lines.append(f"| **All** | {whole} | {covered} | **{covered / whole if whole else 1:.0%}** |")
    missing = [(n, c) for n, c in total.most_common() if n not in IMPLEMENTED]
    lines += ["", "## Most used missing", "", "| Call | Uses |", "|---|---|"]
    lines += [f"| `{n}` | {c} |" for n, c in missing[:40]]
    print("missing:", ", ".join(f"{n} {c}" for n, c in missing[:25]))
    if args.md:
        open(args.md, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")


if __name__ == "__main__":
    main()
