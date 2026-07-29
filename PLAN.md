# snapdex — Plan

## Scope

A Windows desktop application that indexes local photo libraries and provides
fast search over:

- **Metadata:** filename, path/folder, EXIF (camera, lens, ISO, aperture,
  shutter, focal length), capture date, and GPS coordinates.
- **Content (optional, local AI):** natural-language and similarity search over
  image embeddings computed by a small local vision model.

Core value must work **without any AI** and **without any network access**.
AI is a strictly optional enhancement layered on top.

## Architecture / tech approach

- **Platform:** .NET 8, WPF (Windows 10/11 first). Single-window app with a
  search bar, results grid (virtualized thumbnails), and a settings pane.
- **Indexing:**
  - Recursive folder scan; supported formats: JPEG, PNG, HEIC, TIFF, WebP, RAW
    (metadata only for RAW initially).
  - EXIF/metadata extraction via a library such as `MetadataExtractor`.
  - Thumbnail generation and caching to a local thumbnail store.
  - **SQLite** index with FTS5 for text fields; a separate table for embeddings.
  - Incremental updates via `FileSystemWatcher` with debounce; startup
    reconciliation pass for changes made while the app was closed.
- **Search:**
  - A small query-language parser supporting `key:value`, ranges (`iso:>3200`,
    `date:A..B`), quoted phrases, and a `~ "text"` prefix for visual queries.
  - Metadata results ranked by recency/relevance; visual results ranked by
    cosine similarity of embeddings.
- **Local-AI layer:**
  - Client for an OpenAI-compatible `localhost` endpoint (Ollama / llama.cpp).
  - Per-image embedding computed once and cached in SQLite; query text embedded
    on demand.
  - Health check + graceful fallback to metadata-only when no endpoint present.
- **Testing:** xUnit for the indexer, query parser, and metadata extraction;
  fixture image set with known EXIF.

## Milestones

1. **M1 — Indexer core:** folder scan, EXIF extraction, SQLite schema + writes.
2. **M2 — Metadata search:** query parser + WPF results grid with thumbnails.
3. **M3 — Incremental updates:** `FileSystemWatcher`, reconciliation on startup.
4. **M4 — Local-AI visual search:** embedding client, caching, similarity rank,
   fallback.
5. **M5 — Windows packaging:** portable build + MSIX installer, first release.

## Non-goals

- No photo editing, tagging-as-a-service, or organization/rename automation.
- No cloud sync, upload, sharing, or account system (that's AuraPix's domain).
- No mobile or cross-platform builds in the initial phase (Windows-first).
- No large/GPU-mandatory models required; AI must remain optional and tiny.

## Packaging target for Windows

- Primary: portable, self-contained x64 build (`dotnet publish`), single-folder.
- Installer: **MSIX** package for Windows 10/11 (Start-menu entry, clean
  uninstall). Code-signing to be added when a certificate is available.
