"""Checks the repository's Markdown (M8 T9.2).

Every relative link must point at a file or folder that exists, and a link with an anchor (#heading) at a heading
of that file. Mermaid blocks are parsed with the Mermaid CLI (`mmdc`) when it is installed, else skipped.
Exit code 0 when everything holds, 1 with a list of problems otherwise.

    python -I tools/docs/check_docs.py [root]
"""
import os
import re
import shutil
import subprocess
import sys
import tempfile

SKIP_DIRS = {".git", "Library", "Temp", "Logs", "obj", "bin", "node_modules", "Builds", "UserSettings", ".vs", ".idea"}
LINK = re.compile(r"(?<!!)\[[^\]]*\]\(([^)\s]+)(?:\s+\"[^\"]*\")?\)|!\[[^\]]*\]\(([^)\s]+)\)")
HEADING = re.compile(r"^(#{1,6})\s+(.*?)\s*#*\s*$")
FENCE = re.compile(r"^(```|~~~)")


def markdown_files(root):
    for folder, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS and not d.startswith(".") or d == ".github"]
        for name in files:
            if name.lower().endswith(".md"):
                yield os.path.join(folder, name)


def slug(text):
    """GitHub's anchor for a heading: lower case, punctuation dropped, spaces to hyphens."""
    text = re.sub(r"`([^`]*)`", r"\1", text)
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", text)
    text = text.strip().lower()
    text = re.sub(r"[^\w\- ]", "", text, flags=re.UNICODE)
    return text.replace(" ", "-")


def outside_fences(lines):
    fenced = False
    for number, line in enumerate(lines, 1):
        if FENCE.match(line.strip()):
            fenced = not fenced
            continue
        if not fenced:
            yield number, line


def anchors(path, cache={}):
    if path not in cache:
        found, seen = set(), {}
        with open(path, encoding="utf-8") as f:
            for _, line in outside_fences(f.read().splitlines()):
                m = HEADING.match(line)
                if m:
                    base = slug(m.group(2))
                    count = seen.get(base, 0)
                    found.add(base if count == 0 else f"{base}-{count}")
                    seen[base] = count + 1
        cache[path] = found
    return cache[path]


def check_links(path, root, problems):
    with open(path, encoding="utf-8") as f:
        text = f.read()
    for number, line in outside_fences(text.splitlines()):
        line = re.sub(r"`[^`]*`", "", line)
        for m in LINK.finditer(line):
            target = m.group(1) or m.group(2)
            if re.match(r"^[a-z][a-z0-9+.-]*:", target, re.I):
                continue
            file_part, _, anchor = target.partition("#")
            file_part = file_part.split("?")[0]
            resolved = path if not file_part else os.path.normpath(os.path.join(os.path.dirname(path), file_part))
            where = f"{os.path.relpath(path, root)}:{number}"
            if not os.path.exists(resolved):
                problems.append(f"{where}: link to missing '{target}'")
                continue
            if anchor and resolved.lower().endswith(".md") and anchor.lower() not in anchors(resolved):
                problems.append(f"{where}: no heading '#{anchor}' in {os.path.relpath(resolved, root)}")


def check_mermaid(path, root, problems, mmdc):
    with open(path, encoding="utf-8") as f:
        blocks = re.findall(r"```mermaid\n(.*?)```", f.read(), re.S)
    for i, block in enumerate(blocks, 1):
        with tempfile.TemporaryDirectory() as folder:
            source = os.path.join(folder, "d.mmd")
            with open(source, "w", encoding="utf-8") as f:
                f.write(block)
            # CI runners need the headless browser without its sandbox.
            browser = os.path.join(folder, "puppeteer.json")
            with open(browser, "w", encoding="utf-8") as f:
                f.write('{"args": ["--no-sandbox"]}')
            run = subprocess.run([mmdc, "-p", browser, "-i", source, "-o", os.path.join(folder, "d.svg")], capture_output=True, text=True)
            if run.returncode != 0:
                problems.append(f"{os.path.relpath(path, root)}: Mermaid block {i} does not parse: {run.stderr.strip()[:200]}")


def main():
    root = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "..", ".."))
    problems = []
    mmdc = os.environ.get("MMDC") or shutil.which("mmdc")
    files = sorted(markdown_files(root))
    for path in files:
        check_links(path, root, problems)
        if mmdc:
            check_mermaid(path, root, problems, mmdc)
    for problem in problems:
        print(problem)
    print(f"{len(files)} Markdown files, {len(problems)} problems" + ("" if mmdc else " (Mermaid not checked: mmdc not installed)"))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
