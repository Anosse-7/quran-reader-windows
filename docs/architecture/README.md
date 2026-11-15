# Architecture

`QuranReader.Core` owns immutable ayah identities, stable source anchors, view-model state, cancellation, themes, layout policies and source-span validation. `QuranReader.Data` owns separate read-only content access and writable user preferences/bookmarks/checkpoints. `QuranReader.App` owns native WinUI rendering, RTL input, source-bound selection/Tafsir and provider lifetimes.

Content startup validates its hardcoded hash, schema, complete coverage and Tafsir mapping before enabling reading. Filesystem/SQLite/hash work runs off the dispatcher. User state stays under the current user's local application-data directory and never writes into content.

Adaptive composition uses native RichTextBlock/overflow measurement and canonical source boundaries. Two retained native banks and bounded metadata caches preserve stable forward/backward identities. Published linked graphs retire intact; native text providers/ranges retain their actual owners. Existing offset-cache and additional-bank experiments are not presented as fixes. The unresolved exact-RGB and memory issues are in STATUS.md.

The curated snapshot exports Release-equivalent source: DEBUG-only private harnesses are omitted, the private Release benchmark entry point is removed, and missing external content is conditional at build-copy time. Actual content/font validation, renderer, pagination and UIA ownership remain intact. Generic bounded opt-in navigation instrumentation is intentionally retained as a reviewed runtime component; no private evidence is shipped. No historical development Git objects are imported.
