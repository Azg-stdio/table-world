"""What each local TTS mod needs from Table World, to play it fully (parity target, 2026-10-23).

Usage:
  python -I tools/tts-analysis/mod_requirements.py ["<Tabletop Simulator folder>"] --json out.json --md out.md

Reads the Workshop saves already on this machine (never downloads, never copies assets). For every mod it lists:
save-level features (table, sky, lighting, grid, hands, turns, music, notebook, decals, tags), every object type and
per-object feature flag, every Lua event handler and every Lua API call (namespaced, object methods and globals), and
every XmlUI tag. The union over all mods is the parity checklist for "these mods play fully".
"""
import collections
import json
import os
import re
import sys

DEFAULT = os.path.expanduser(r"~/Documents/My Games/Tabletop Simulator")

NAMESPACES = {"UI", "Wait", "Turns", "Player", "Global", "Physics", "Notes", "JSON", "Time", "Lighting", "Tables",
              "Grid", "Hands", "Info", "Color", "Vector", "MusicPlayer", "Backgrounds", "WebRequest", "Timer",
              "Components", "Decals", "Tags"}
# Global functions of the TTS API (https://api.tabletopsimulator.com/base/), the ones worth counting.
GLOBALS = {"getObjectFromGUID", "getAllObjects", "getObjects", "getObjectsWithTag", "getObjectsWithAnyTags",
           "getObjectsWithAllTags", "spawnObject", "spawnObjectData", "spawnObjectJSON", "destroyObject",
           "broadcastToAll", "broadcastToColor", "printToAll", "printToColor", "print", "log", "logStyle",
           "startLuaCoroutine", "group", "addHotkey", "addContextMenuItem", "clearContextMenu", "getSeatedPlayers",
           "flipTable", "copy", "paste", "setLookingForPlayers", "getNotebookTabs", "addNotebookTab",
           "editNotebookTab", "removeNotebookTab", "stringColorToRGB", "sendExternalMessage", "showHotkeyConfig",
           "getSnapPoints", "setSnapPoints", "getDecals", "setDecals", "getVectorLines", "setVectorLines"}
LUA_KEYWORDS = {"if", "for", "while", "function", "return", "and", "or", "not", "local", "then", "end", "elseif",
                "else", "repeat", "until", "do", "in", "nil", "true", "false", "type", "pairs", "ipairs", "tostring",
                "tonumber", "string", "table", "math", "select", "unpack", "next", "pcall", "error", "assert",
                "setmetatable", "getmetatable", "rawget", "rawset", "require", "coroutine", "os", "load"}
OBJECT_FLAGS = ("Locked", "Grid", "Snap", "Autoraise", "Sticky", "Tooltip", "GridProjection", "HideWhenFaceDown",
                "Hands", "IgnoreFoW", "MeasureMovement", "DragSelectable")


def walk(objects):
    for obj in objects or []:
        yield obj
        yield from walk(obj.get("ContainedObjects"))
        for state in (obj.get("States") or {}).values():
            yield state
            yield from walk(state.get("ContainedObjects"))


def scripts_of(save):
    yield save.get("LuaScript") or ""
    for obj in walk(save.get("ObjectStates")):
        yield obj.get("LuaScript") or ""


def xml_of(save):
    yield save.get("XmlUI") or ""
    for obj in walk(save.get("ObjectStates")):
        yield obj.get("XmlUI") or ""


def strip_lua_comments_and_strings(code):
    code = re.sub(r"--\[(=*)\[.*?\]\1\]", " ", code, flags=re.S)
    code = re.sub(r"--[^\n]*", " ", code)
    code = re.sub(r"\[(=*)\[.*?\]\1\]", '""', code, flags=re.S)
    code = re.sub(r'"(?:\\.|[^"\\])*"', '""', code)
    code = re.sub(r"'(?:\\.|[^'\\])*'", "''", code)
    return code


def analyze(path):
    save = json.load(open(path, encoding="utf-8"))
    objects = list(walk(save.get("ObjectStates")))
    types = collections.Counter(o.get("Name", "?") for o in objects)
    flags = collections.Counter()
    for o in objects:
        for f in OBJECT_FLAGS:
            v = o.get(f)
            default = f in ("Grid", "Snap", "Autoraise", "Sticky", "Tooltip", "Hands", "DragSelectable")
            if isinstance(v, bool) and v != default:
                flags[f"{f}={v}"] += 1
        if o.get("States"):
            flags["States"] += 1
        if o.get("AttachedSnapPoints"):
            flags["AttachedSnapPoints"] += 1
        if o.get("AttachedDecals"):
            flags["AttachedDecals"] += 1
        if o.get("JointFixed") or o.get("JointHinge") or o.get("JointSpring"):
            flags["Joints"] += 1
        if o.get("Tags"):
            flags["Tags"] += 1
        if o.get("GMNotes"):
            flags["GMNotes"] += 1
        if o.get("LuaScriptState"):
            flags["LuaScriptState"] += 1
        if o.get("Value"):
            flags["Value"] += 1
        if o.get("RotationValues"):
            flags["RotationValues"] += 1
        if o.get("CustomDeck") and len(o["CustomDeck"]) > 1:
            flags["MultiSheetDeck"] += 1
        if o.get("SidewaysCard"):
            flags["SidewaysCard"] += 1
        if o.get("Counter"):
            flags["CounterValue"] += 1
        if o.get("Text"):
            flags["3DText"] += 1
        if o.get("CustomPDF"):
            flags["PDF"] += 1
        # FogColor is also the owner colour of every HandTrigger; only a FogOfWarTrigger is a fog-of-war zone.
        if o.get("Name") == "FogOfWarTrigger":
            flags["FogOfWar"] += 1

    lua = "\n".join(strip_lua_comments_and_strings(s) for s in scripts_of(save))
    events = collections.Counter(re.findall(r"\bfunction\s+(on[A-Z]\w*)\s*\(", lua))
    namespaced = collections.Counter(f"{a}.{b}" for a, b in re.findall(r"\b([A-Z]\w*)\.(\w+)\s*\(", lua) if a in NAMESPACES)
    # TTS accepts both obj:method() and obj.method() on objects, players and colours.
    lua_libraries = {"string", "table", "math", "os", "coroutine", "io", "debug", "utf8", "bit32", "self_"}
    methods = collections.Counter()
    for prefix, name in re.findall(r"(\w+|[\)\]])\s*[.:]\s*(\w+)\s*\(", lua):
        if prefix in NAMESPACES or prefix in lua_libraries or name in LUA_KEYWORDS:
            continue
        methods[name] += 1
    calls = collections.Counter(n for n in re.findall(r"(?<![\.:\w])(\w+)\s*\(", lua) if n in GLOBALS)
    xml_tags = collections.Counter()
    for x in xml_of(save):
        xml_tags.update(t for t in re.findall(r"<\s*([A-Za-z]\w*)", x))

    urls = collections.Counter()
    for m in re.finditer(r"https?://[^\"'\s]+", json.dumps(save)):
        ext = os.path.splitext(m.group(0).split("?")[0])[1].lower() or "(none)"
        urls[ext if len(ext) <= 6 else "(other)"] += 1

    lighting = save.get("Lighting") or {}
    return {
        "file": os.path.basename(path),
        "name": save.get("SaveName"),
        "save": {
            "Table": save.get("Table"), "TableURL": bool(save.get("TableURL")), "Sky": save.get("Sky"),
            "SkyURL": bool(save.get("SkyURL")), "Gravity": save.get("Gravity"), "PlayArea": save.get("PlayArea"),
            "LightIntensity": lighting.get("LightIntensity"), "AmbientIntensity": lighting.get("AmbientIntensity"),
            "GridLines": (save.get("Grid") or {}).get("Lines"), "GridSnapping": (save.get("Grid") or {}).get("Snapping"),
            "Hands": save.get("Hands"), "TurnsEnabled": (save.get("Turns") or {}).get("Enable"),
            "MusicPlaylist": len(((save.get("MusicPlayer") or {}).get("AudioLibrary")) or []),
            "NotebookTabsWithText": sum(1 for t in (save.get("TabStates") or {}).values() if t.get("body")),
            "DecalPallet": len(save.get("DecalPallet") or []), "Decals": len(save.get("Decals") or []),
            "VectorLines": len(save.get("VectorLines") or []), "ComponentTags": len(((save.get("ComponentTags") or {}).get("labels")) or []),
            "SnapPoints": len(save.get("SnapPoints") or []), "CustomUIAssets": len(save.get("CustomUIAssets") or []),
            "GlobalLuaChars": len(save.get("LuaScript") or ""), "GlobalXmlChars": len(save.get("XmlUI") or ""),
        },
        "objects": len(objects),
        "types": dict(types.most_common()),
        "flags": dict(flags.most_common()),
        "lua_events": dict(events.most_common()),
        "lua_namespaced": dict(namespaced.most_common()),
        "lua_methods": dict(methods.most_common()),
        "lua_globals": dict(calls.most_common()),
        "xml_tags": dict(xml_tags.most_common()),
        "url_types": dict(urls.most_common()),
    }


def markdown(mods):
    out = ["# What the local TTS mods need (generated by `tools/tts-analysis/mod_requirements.py`)", ""]
    union = collections.defaultdict(collections.Counter)
    for m in mods:
        for key in ("types", "flags", "lua_events", "lua_namespaced", "lua_methods", "lua_globals", "xml_tags"):
            for k, v in m[key].items():
                union[key][k] += v
    out += ["## All mods together (the parity checklist)", ""]
    for key, title in (("types", "Object types"), ("flags", "Object features"), ("lua_events", "Lua events"),
                       ("lua_namespaced", "Lua namespaced calls"), ("lua_globals", "Lua global functions"),
                       ("lua_methods", "Lua object methods (and other `:` calls)"), ("xml_tags", "XmlUI tags")):
        items = union[key].most_common()
        out.append(f"### {title} ({len(items)})")
        out.append("")
        out.append(", ".join(f"`{k}` {v}" for k, v in items) or "none")
        out.append("")
    for m in mods:
        out += [f"## {m['name']} (`{m['file']}`, {m['objects']} objects)", ""]
        out.append("Save: " + ", ".join(f"{k} {v}" for k, v in m["save"].items() if v not in (None, False, 0, "")))
        out.append("")
        for key, title in (("types", "Types"), ("flags", "Features"), ("lua_events", "Events"),
                           ("lua_namespaced", "Namespaced"), ("lua_globals", "Globals"), ("xml_tags", "XmlUI"),
                           ("url_types", "Asset URLs")):
            out.append(f"- **{title}:** " + (", ".join(f"`{k}` {v}" for k, v in m[key].items()) or "none"))
        top = list(m["lua_methods"].items())[:60]
        out.append("- **Methods (top 60):** " + (", ".join(f"`{k}` {v}" for k, v in top) or "none"))
        out.append("")
    return "\n".join(out)


def main():
    args = [a for a in sys.argv[1:]]
    out_json = args[args.index("--json") + 1] if "--json" in args else None
    out_md = args[args.index("--md") + 1] if "--md" in args else None
    positional = [a for i, a in enumerate(args) if not a.startswith("--") and (i == 0 or not args[i - 1].startswith("--"))]
    root = positional[0] if positional else DEFAULT
    workshop = os.path.join(root, "Mods", "Workshop")
    mods = [analyze(os.path.join(workshop, f)) for f in sorted(os.listdir(workshop))
            if f.endswith(".json") and f != "WorkshopFileInfos.json"]
    if out_json:
        json.dump(mods, open(out_json, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    if out_md:
        open(out_md, "w", encoding="utf-8").write(markdown(mods))
    for m in mods:
        print(f"{m['name']}: {m['objects']} objects, {len(m['lua_events'])} events, "
              f"{len(m['lua_namespaced']) + len(m['lua_globals']) + len(m['lua_methods'])} distinct API names, {len(m['xml_tags'])} XmlUI tags")


if __name__ == "__main__":
    main()
