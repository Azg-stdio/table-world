"""Analyze local Tabletop Simulator Workshop saves.

Usage:  python -I tools/tts-analysis/analyze_mods.py [tts_dir] [--json out.json]

Reads every Workshop/*.json save and reports: object types, Lua/XmlUI usage,
zones/snap points, asset URL hosts, AssetBundle dependence and the Lua API
functions most used. Read-only; never touches the Workshop.
"""
import collections, json, os, re, sys

DEFAULT = os.path.expanduser(r"~/Documents/My Games/Tabletop Simulator")
LUA_API = re.compile(r"\b((?:getObjectFromGUID|getObjects|getGMNotes|getName|getPosition|getRotation|setPosition|setRotation|setPositionSmooth|setRotationSmooth|broadcastToAll|printToAll|Wait\.time|Wait\.frames|Wait\.condition|getAllObjects|spawnObject|spawnObjectJSON|getLuaScript|setLuaScript|getVar|setVar|call|getDescription|setDescription|getGUID|flip|shuffle|takeObject|putObject|dealToAll|deal|getObjects|setLock|lock|getLock|getScale|setScale|destruct|highlightOn|highlightOff|createButton|editButton|clearButtons|createInput|getButtons|getSnapPoints|setSnapPoints|getZones|getTable|setTable|getBounds|getBoundsNormalized|getTransformForward|getTransformRight|getTransformUp|getValue|setValue|addTag|hasTag|getTags|setTags|getJSON|getData|setState|getStateId|getStates|UI\.setAttribute|UI\.getAttribute|UI\.setXml|UI\.show|UI\.hide|Player\.getPlayers|getSeatedPlayers|Turns\.\w+|Global\.\w+|Physics\.cast|JSON\.decode|JSON\.encode|getHandTransform|setHandTransform|Notes\.\w+|Lighting\.\w+|Timer\.\w+|startLuaCoroutine|coroutine\.yield|onLoad|onSave|onUpdate|onObjectDrop|onObjectPickUp|onObjectEnterScriptingZone|onObjectLeaveScriptingZone|onObjectEnterContainer|onObjectLeaveContainer|onPlayerTurn|onChat|onCollisionEnter|onObjectRandomize|onObjectSpawn|onObjectDestroy))\b")
URL_RE = re.compile(r'https?://[^\s"\'\\]+')

def walk(objs):
    for o in objs:
        yield o
        yield from walk(o.get("ContainedObjects", []) or [])
        for s in (o.get("States") or {}).values():
            yield s
            yield from walk(s.get("ContainedObjects", []) or [])

def host(u):
    m = re.match(r"https?://([^/]+)/", u)
    return m.group(1) if m else "?"

def analyze(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        d = json.load(f)
    objs = list(walk(d.get("ObjectStates", [])))
    types = collections.Counter(o.get("Name", "?") for o in objs)
    lua_global = len(d.get("LuaScript") or "")
    xml_global = len(d.get("XmlUI") or "")
    lua_objs = sum(1 for o in objs if o.get("LuaScript"))
    xml_objs = sum(1 for o in objs if o.get("XmlUI"))
    snaps = len(d.get("SnapPoints") or []) + sum(len(o.get("AttachedSnapPoints") or []) for o in objs)
    zones = sum(1 for o in objs if o.get("Name") in ("ScriptingTrigger", "HandTrigger", "FogOfWarTrigger", "RandomizeTrigger"))
    bundles = sum(1 for o in objs if "Assetbundle" in o.get("Name", ""))
    gm_notes = sum(1 for o in objs if o.get("GMNotes"))
    states = sum(1 for o in objs if o.get("States"))
    raw = json.dumps(d)
    urls = URL_RE.findall(raw)
    hosts = collections.Counter(host(u) for u in urls)
    api = collections.Counter()
    for src in [d.get("LuaScript") or ""] + [o.get("LuaScript") or "" for o in objs]:
        api.update(LUA_API.findall(src))
    return {
        "file": os.path.basename(path), "name": d.get("SaveName"), "version": d.get("VersionNumber"),
        "objects_total": len(objs), "objects_top_level": len(d.get("ObjectStates", [])),
        "types": dict(types.most_common()), "lua_global_chars": lua_global, "lua_objects": lua_objs,
        "xmlui_global_chars": xml_global, "xmlui_objects": xml_objs, "snap_points": snaps, "zones": zones,
        "assetbundle_objects": bundles, "gm_notes_objects": gm_notes, "multi_state_objects": states,
        "urls": len(urls), "url_hosts": dict(hosts.most_common()), "lua_api": dict(api.most_common(25)),
    }

def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    tts = args[0] if args else DEFAULT
    ws = os.path.join(tts, "Mods", "Workshop")
    results = [analyze(os.path.join(ws, f)) for f in sorted(os.listdir(ws)) if f.endswith(".json") and f[0].isdigit()]
    all_types, all_api, all_hosts = collections.Counter(), collections.Counter(), collections.Counter()
    print(f"{'mod':44} {'objs':>5} {'bundles':>7} {'luaG':>6} {'luaO':>5} {'xml':>4} {'snaps':>5} {'zones':>5} {'urls':>5}")
    for r in results:
        all_types.update(r["types"]); all_api.update(r["lua_api"]); all_hosts.update(r["url_hosts"])
        print(f"{(r['name'] or '')[:44]:44} {r['objects_total']:>5} {r['assetbundle_objects']:>7} {r['lua_global_chars']:>6} {r['lua_objects']:>5} {r['xmlui_global_chars']>0:>4} {r['snap_points']:>5} {r['zones']:>5} {r['urls']:>5}")
    print("\n=== Object types (all mods, incl. contained) ===")
    for k, v in all_types.most_common(40): print(f"{v:6}  {k}")
    print("\n=== URL hosts ===")
    tot = sum(all_hosts.values())
    for k, v in all_hosts.most_common(8): print(f"{v:6} {100*v/tot:5.1f}%  {k}")
    print("\n=== Lua API usage (occurrences) ===")
    for k, v in all_api.most_common(30): print(f"{v:6}  {k}")
    if "--json" in sys.argv:
        out = sys.argv[sys.argv.index("--json") + 1]
        with open(out, "w", encoding="utf-8") as f: json.dump(results, f, indent=1)
        print(f"\nwrote {out}")

if __name__ == "__main__":
    main()
