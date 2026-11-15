# Deferred proposal intake: BM-1.0 Animated Physical Ribbon Bookmark

Status: **PLANNED; implementation requires separate authorization.** The original intake predated UI-1.2I.1 engineering closure. Its handoff now records engineering verification at Release source 30d90c0 for the tested scenarios; user visual approval and external release gates remain pending. Current cross-feature status and LAN-1.0 dependencies are maintained in the coordinated roadmap. This metadata update does not alter the supplied specification or authorize implementation.

Cross-feature status, state distinctions and shared KH-1.3/BM-1.0 dependencies live in [the coordinated roadmap](../../ROADMAP.md). The text below is the original specification, preserved verbatim; future instructions inside it apply only after the entry gate opens.

Original attachment SHA-256: ebc98185a7551d8b5b3331eef619c053db1541e8391d7fdbf3d7210e8ddc6c37

---

# CODEX ROADMAP ADDENDUM — BM-1.0
## Animated Physical Ribbon Bookmark and Exact Ayah Position

This is an additional approved product concept for the future development roadmap of the existing Windows-native Qur’an Reader.

Do not implement it immediately. Finish the currently active development phase first.

After current work is technically closed, incorporate this capability into the permanent product roadmap and requirements documentation.

## 1. Product goal

Create a beautiful animated bookmark inspired by the physical ribbon found between the pages of traditional books.

The bookmark must do more than identify a page.

It should visually indicate the exact ayah where the reader stopped.

## 2. Expected appearance

Implement a fabric-inspired ribbon bookmark that appears between the two facing Mushaf pages, with a portion visible along the book spine or page edge.

The ribbon should have:

- A refined traditional appearance.
- A configurable color, with deep red as the proposed default.
- Subtle depth and appropriate shading.
- An elegant pointed or notched ribbon end.
- Light, Dark and Sepia compatibility.
- Correct positioning in RTL book layouts.

A delicate secondary marker should extend from the ribbon toward the precise ayah number.

The endpoint should sit beneath or beside the ayah marker without covering Qur’anic text or diacritics.

The design must not introduce visual clutter.

## 3. Bookmark animation

When the user bookmarks an ayah:

1. The ribbon enters the book with a smooth, subtle movement.
2. It settles between the relevant pages.
3. A small extension travels toward the selected ayah marker.
4. The extension settles at the correct location.
5. The selected ayah receives restrained visual emphasis.

Use native Windows composition animation where appropriate.

Do not animate the entire Mushaf or rebuild scripture unnecessarily.

Respect reduced-motion preferences.

Do not allow overlapping animations or stale positioning after rapid navigation.

## 4. Exact placement

Use authoritative surah and ayah identity to determine the bookmark target.

Use actual rendered WinUI glyph and marker geometry to position its visual endpoint.

Do not estimate the ayah position using only text length or an arbitrary percentage of page height.

Support:

- Original Mushaf Mode.
- Adaptive Continuous Book Mode.
- Single-page layouts.
- Facing-page spreads.
- Different font sizes.
- Window resizing.
- RTL navigation.
- Theme changes.
- Long ayat spanning multiple lines or dynamic leaves.

If the target ayah is off-screen, ensure it can be brought into view. Do not draw a long line across unrelated scripture.

Recalculate the visual endpoint safely after text reflow.

## 5. Bookmark categories

Preserve and distinguish:

**Reading Position Ribbon**

The primary marker associated with the user's saved reading position.

**Saved Ayah Bookmarks**

User-created bookmarks for revisiting particular ayat.

Avoid covering the Mushaf with many simultaneous ribbons. Allow secondary bookmarks to appear as restrained edge indicators or through the bookmark library.

## 6. Integration with Khatma Planner

Integrate the Reading Position Ribbon with KH-1.3 Exact Resume.

When the user starts a planned reading session, restore the precise saved ayah and display the ribbon at the corresponding location.

Do not confuse the saved reading position with the confirmed Khatma completion checkpoint.

Bookmarking or viewing an ayah must not automatically mark it as completed.

## 7. Source integrity

Never modify authoritative Qur’anic text to place a bookmark.

All ribbon graphics, animations, highlights and endpoint markers must exist only in the presentation layer.

Never obscure sacred text, change diacritics or interfere with ayah selection.

## 8. Quality requirements

Create native tests covering:

- Correct bookmark-to-ayah association.
- Persistence across application restarts.
- Correct restoration after font resizing.
- Correct position after page recomposition.
- Accurate behavior across facing pages.
- No clipping or obscuring Arabic text.
- Animation cancellation and reduced-motion behavior.
- No interference with Tafsir or navigation.
- Correct behavior at beginning and end of the Qur’an.
- Preservation of existing bookmarks and reading anchors.

Provide real native visual evidence.

## 9. Architecture and delivery

Reuse existing bookmark persistence and reading-anchor services.

Do not introduce a second bookmark database.

Separate the canonical bookmark identity from the transient on-screen ribbon position.

Use the existing source of truth for ayah identities.

Create:

`docs/roadmap/bookmarks/BM-1.0-ANIMATED-RIBBON-BOOKMARK.md`

Document the proposed visual design, interaction sequence, rendering approach, accessibility requirements, acceptance tests and integration with KH-1.3.

Add the feature to the central roadmap with status PLANNED.

Do not mark implementation or user visual approval as complete.

## Final objective

Make bookmarking feel like placing a real ribbon inside a beautiful physical Mushaf, while giving the reader a precise visual reminder of the ayah where they stopped.

The result should be emotionally familiar, visually elegant, technically reliable and fully compatible with the Khatma system.

**Document the future goal after the active development phase closes. Do not start implementation without separate authorization.**