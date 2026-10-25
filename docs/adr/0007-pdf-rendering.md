# ADR 0007 — PDFs render with PDFium, through our own small wrapper

Status: **accepted** (2026-10-25, M7 / T8.8; Miguel chose option 2 below).

## Context

TTS's `Custom_PDF` puts rulebooks and scenario books on the table: 15 of them across the local mods (8 in Gloomhaven,
6 in Sinnoh, 1 in Riftbound). Unity cannot render PDFs. TTS itself ships `Paroxe.PDFRenderer.dll`, a paid Asset Store
wrapper, over a native `pdfrenderer.dll`. That DLL is a PDFium build: it exports 362 `FPDF_*` functions and its debug
info is named `pdfium.dll`.

Options:
1. Buy Paroxe PDF Renderer, as TTS did: paid, closed source, and bought on the Asset Store.
2. Use PDFium directly through a small P/Invoke wrapper of our own. Prebuilt binaries come from
   [bblanchon/pdfium-binaries](https://github.com/bblanchon/pdfium-binaries): MIT packaging, PDFium itself BSD-3, built
   for Windows, macOS and Linux.

Nothing is ever taken from the TTS install (CLAUDE.md §7).

## Decision

Option 2.
- **Binary:** `unity/Assets/Plugins/PDFium/x86_64/pdfium.dll`, release `chromium/8086` (PDFium 157.0.8086). This was
  the newest release at least two weeks old.
- **Build flags:** no V8 (no JavaScript inside PDFs) and no XFA forms.
- **Licences:** `LICENSE.txt` and the `Licenses/` folder next to the DLL.
- **Wrapper:** `TableWorld.View.Documents.PdfDocument` covers load from memory, page count, page size and rendering
  to a BGRA texture (about 15 calls).
- **Threading:** PDFium is not thread-safe, so pages render on the main thread. A 67 MB, 52-page rulebook opens in
  7 ms and renders a 1536-pixel page in about 170 ms.
- **Model:** a `Pdf` table object (asset of type `pdf`, `page`, `pageOffset`, `size`) and a `SetPageCommand`. Turning
  the page is shared; each machine renders the page itself.
  - The reader window pages through a PDF for one player only.
  - Scripts use TTS's `obj.Book.getPage` / `setPage`.

## Consequences

- This is the first native plugin. macOS and Linux builds need the matching `pdfium` binaries from the same release.
  If the library is missing, a PDF shows plain paper and logs why; nothing crashes.
- Page turns on a big PDF hitch for about 0.2 s while the page renders. Rendering on a worker thread (one PDFium
  user at a time) is the fix if players notice.
- Password-protected PDFs are opened without a password. The import notes say so, and such a PDF shows blank.
- Updating PDFium means replacing the DLL and its licence files together, and checking the release notes.
