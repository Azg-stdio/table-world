"""Print the UnityFS header (format, engine version, compression) of every AssetBundle in a folder.

Usage: python -I tools/tts-analysis/bundle_headers.py "<TTS>/Mods/Assetbundles"
"""
import sys, os, struct, collections
d = sys.argv[1]
rows = []
for fn in sorted(os.listdir(d)):
    p = os.path.join(d, fn)
    if not os.path.isfile(p): continue
    with open(p, 'rb') as f:
        head = f.read(256)
    try:
        sig_end = head.index(b'\0')
        sig = head[:sig_end].decode('ascii', 'replace')
        pos = sig_end + 1
        fmt_ver = struct.unpack('>I', head[pos:pos+4])[0]; pos += 4
        e = head.index(b'\0', pos); web_ver = head[pos:e].decode('ascii','replace'); pos = e+1
        e = head.index(b'\0', pos); eng_ver = head[pos:e].decode('ascii','replace'); pos = e+1
        size = struct.unpack('>Q', head[pos:pos+8])[0]; pos += 8
        comp_sz, uncomp_sz, flags = struct.unpack('>III', head[pos:pos+12])
        comp = {0:'none',1:'lzma',2:'lz4',3:'lz4hc'}.get(flags & 0x3F, f'?{flags&0x3F}')
        rows.append((sig, fmt_ver, web_ver, eng_ver, comp, os.path.getsize(p), fn[:40]))
    except Exception as ex:
        rows.append(('ERR', 0, '', str(ex), '', os.path.getsize(p), fn[:40]))
print(f"{'sig':8} {'fmt':>3} {'web':>6} {'engine':14} {'blocksComp':10} {'bytes':>9} file")
for r in rows: print(f"{r[0]:8} {r[1]:>3} {r[2]:>6} {r[3]:14} {r[4]:10} {r[5]:>9} {r[6]}")
print("\n=== Summary by engine version ===")
for k, v in collections.Counter(r[3] for r in rows).most_common(): print(f"{v:3}  {k}")
print("\n=== Summary by compression ===")
for k, v in collections.Counter(r[4] for r in rows).most_common(): print(f"{v:3}  {k}")
