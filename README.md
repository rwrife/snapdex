# snapdex

**Local photo indexer & semantic search for Windows.** Point snapdex at your
photo folders and get instant search across filename, EXIF metadata, and dates —
plus optional local-AI visual search ("beach at sunset", "my dog in the snow")
powered by tiny models running entirely on your machine. Offline & privacy-first:
your photos never leave your computer.

![status: bootstrapping](https://img.shields.io/badge/status-bootstrapping-orange)

---

## Overview

snapdex builds a fast local index of your image library and lets you find photos
in milliseconds. It works in two modes:

- **Metadata mode (no AI required):** Search by filename, folder, camera model,
  lens, ISO/aperture/shutter, GPS location, and capture date/date-range. Backed
  by a SQLite index with incremental updates via `FileSystemWatcher`.
- **Visual mode (optional local AI):** Compute image embeddings with a small
  local vision model (CLIP / MiniCPM-V class) and search by natural-language
  description or by visual similarity to an example photo. Runs against an
  Ollama or llama.cpp OpenAI-compatible endpoint on `localhost` — no cloud, no
  API keys, graceful fallback to metadata mode when unavailable.

## Motivation

Windows has no good built-in way to find "that photo from the hike last autumn."
File Explorer search is slow, ignores EXIF, and can't understand image content.
Cloud photo services can do semantic search — but at the cost of uploading your
entire library. snapdex gives you the same "search by what's in the picture"
experience **fully offline**, on hardware you control.

## Use cases

- Find every photo taken with a specific camera/lens or within an aperture range.
- Locate all shots from a date range or GPS area (e.g., a trip).
- Natural-language visual search: "whiteboard", "receipt", "red car", "sunset".
- Find near-duplicates and visually similar images to clean up a library.
- Quickly triage a freshly imported SD card without uploading anything anywhere.

## How to use (Windows-first quickstart)

> Requires Windows 10/11.

1. Install snapdex using either:
   - **Portable build** (no installer): extract the `snapdex-portable-win-x64` artifact,
     then run `Snapdex.App.exe`.
   - **MSIX installer**: install the `.msix` package and launch **snapdex** from the
     Start menu.
2. If you are building from source, run:
   ```powershell
   git clone https://github.com/rwrife/snapdex.git
   cd snapdex
   dotnet build -c Release
   ```
3. Launch snapdex and add one or more photo folders (e.g., `C:\Users\you\Pictures`).
4. Let it index — progress is shown live; the index is incremental thereafter.
5. Search from the box at the top. Examples below.

## Windows packaging / release artifacts

### Portable self-contained folder

From repo root on Windows:

```powershell
./scripts/windows/build-portable.ps1 -Configuration Release -RuntimeIdentifier win-x64 -SelfContained
```

This produces a runnable folder at `artifacts/portable/win-x64/`.

Equivalent raw command:

```powershell
dotnet publish src/Snapdex.App/Snapdex.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/portable/win-x64
```

### MSIX installer package

From repo root on Windows:

```powershell
./scripts/windows/build-msix.ps1 -Configuration Release -Platform x64
```

This uses the Desktop Bridge project at `packaging/Snapdex.Package/` and writes
an MSIX package under `artifacts/msix/`.

Local sideload install (Developer Mode):

```powershell
Add-AppxPackage -Path .\artifacts\msix\<package>.msix -AllowUnsigned
```

### Uninstall/data retention behavior

Uninstalling the MSIX package removes the installed app binaries. snapdex stores
its local index/cache/settings under:

- `%LocalAppData%\snapdex\snapdex.db`
- `%LocalAppData%\snapdex\thumb-cache\`
- `%LocalAppData%\snapdex\local-ai-settings.json`

If you want a full cleanup, delete `%LocalAppData%\snapdex\` after uninstall.

## Example workflow / commands

Metadata queries (available with no AI):

```
camera:"Canon EOS R6" iso:>3200
date:2025-10-01..2025-10-31
folder:Trips\Iceland
lens:"24-70" f:<2.8
```

Visual queries (require local-AI mode enabled):

```
~ "golden retriever in the snow"
~ "handwritten notes on a whiteboard"
similar:C:\Users\you\Pictures\IMG_2043.jpg
```

## Local-AI integration

snapdex is designed to work great **without** AI, and better **with** it — all
locally.

- **Endpoint:** any OpenAI-compatible server on `localhost`, e.g.
  [Ollama](https://ollama.com) or [llama.cpp](https://github.com/ggerganov/llama.cpp).
  Configure this in the app's **Local AI** settings row (`Endpoint`, `Model`) and
  use **Check AI** to verify health before running visual queries.
- **Suggested tiny models:**
  - CLIP-family embedders available through your local stack
  - `nomic-embed-text` for lightweight text embedding (Ollama)
  - MiniCPM-V family variants for local vision workflows (llama.cpp / Ollama)
- **How it's used:** snapdex caches per-image embeddings in SQLite and reuses
  them until the source image changes, then recomputes. Visual queries (`~ "..."`
  and `similar:<path>`) are ranked by cosine similarity.
- **Privacy:** No network calls leave `localhost`. If no endpoint is configured
  or reachable, snapdex falls back to metadata-only search without blocking.

## Current status / milestones

- [ ] **M1 — Indexer core:** recursive scan, EXIF extraction, SQLite schema.
- [ ] **M2 — Metadata search:** query parser + results grid with thumbnails.
- [ ] **M3 — Incremental updates:** `FileSystemWatcher`, re-index on change.
- [ ] **M4 — Local-AI visual search:** embeddings via Ollama/llama.cpp, similarity.
- [ ] **M5 — Windows packaging:** portable build + MSIX installer.

See [PLAN.md](./PLAN.md) for scope, architecture, and non-goals.

---

*Part of the auto-tool-lab Windows utilities series. Offline. Privacy-first.*
