# OneNoteMcp — project notes

Implementation notes for whoever works on this codebase. User-facing docs live in [README.md](README.md);
this file exists so knowledge isn't lost or repeated. Only non-obvious things belong here.

## Output style rationale

Everything the server writes follows one fixed, non-configurable layout (see README's "Markdown support").

- Blank line above a heading = `spaceBefore` on the heading's `one:OE` paragraph, not an empty paragraph. A real empty paragraph would reappear on every read, multiply on every rewrite, and give a place to click and type — contaminating the block.
- Must sit on the paragraph, not the QuickStyleDef: OneNote multiplies a QuickStyleDef's `spaceBefore` by 36 on load (11pt → 396, most of a page of white space). The paragraph value is taken as written.
- OneNote has no indent attribute — nesting is the only mechanism. A top-level list (and, for the same reason, a top-level table needing its left border moved in) is wrapped in an empty carrier paragraph; the parser unwraps it on read so round-trips are stable. Nesting is applied once per block so deep lists don't drift.
- Blank lines around lists/tables/code (trailing and surrounding) are real empty paragraphs, not spacing — those blocks are visually self-evident, so there's no risk of the blank line being mistaken for user-typed structure.
- Rules are written per-block, so two can land on the same seam (e.g. a table's trailing gap + a following code block's leading gap). They share one blank line — a rule only adds its own if the block doesn't already end with one.

## Implementation notes

Established by testing against a real OneNote install (Microsoft 365, OneNote 16.0.19127).

### COM binding uses direct vtable calls, not `dynamic`

| Approach | Failure |
| --- | --- |
| C# `dynamic` | Runtime binder calls `IDispatch::GetTypeInfo` first; OneNote returns `E_FAIL` → `0x80004005` on every access |
| `Type.InvokeMember` | `TYPE_E_LIBNOTREGISTERED` (`0x8002801D`) |
| `[ComImport]` + `InterfaceIsIDispatch` | Marshals via `IDispatch::Invoke`, which OneNote implements on its own type library — same failure |

Root cause: Office registers the OneNote type library only under `…\TypeLib\{0EA692EE-…}\1.1\0\Win32` — no `Win64` entry in either registry view, so 64-bit OneNote can't load its own type library (x86 doesn't help; the failure is inside the OneNote process).

`OneNoteInterop.cs` declares interfaces with `ComInterfaceType.InterfaceIsDual` → direct vtable calls, no type library needed. **Members must stay in exact vtable order**; unused ones are bare no-arg placeholder slots. Layout came from the type library embedded in `ONENOTE.EXE` resource 3.

### OneNote is strict about the XML it accepts

`UpdatePageContent` → `hrInvalidXML` (`0x80042001`) for:
- `one:Bullet` with a `font` attribute
- `one:Number` without `numberFormat`, or with `startAt`/`fontSize`
- `numberFormat` given as a name (`arabic`) instead of a literal mask (`##.`, `##)`)

Element order matters too: `one:Page` children go TagDef* → QuickStyleDef* → Title → body; inside `one:OE`: List → Tag → content → OEChildren.

### Author attribution has to go on the paragraph — this is the whole safety model

- `UpdatePageContent` honours a supplied `author`/`lastModifiedBy` — OneNote doesn't force the local Office identity, which is what lets the server label its own work.
- Must be set on `one:OE` (the paragraph); set on `one:Outline` it's silently discarded and replaced with the local user name. OneNote then propagates the paragraph's author *up* to the outline, which is where it persists and is read back from.
- Human-typed paragraphs always carry their own `author`; paragraphs written via `UpdatePageContent` carry none.
- That asymmetry makes contamination detectable: if someone types into an agent-created block, the outline still shows the agent's author, but the new paragraph has none. `AiBlocks.IsOwnedBy` requires both the outline being the agent's *and* no paragraph belonging to anyone else.
- Targeting: replace by the `one:Outline` objectID (body swapped in place). Targeting a `one:OE` instead makes OneNote create a **new** block and leave the original — silent duplication.
- A partial update carries no page context, so a block written into an existing page can't assume `quickStyleIndex` 0 means "p" or `TagDef` 0 means "To Do" — those indices are page-specific. `PageSchema` reuses a definition when the page already has one by that name, otherwise allocates an unused index.

### Every definition a block uses must travel with it

Reusing an index isn't enough — replacing a block removes its paragraphs before the incoming ones attach, and **OneNote prunes any `QuickStyleDef`/`TagDef` nothing references at that moment**. A definition only the replaced block used is already gone when the new paragraphs arrive, so their indices resolve to 0 (flat body text, headings lost). Appending hides the bug since nothing gets pruned.

Fix: every fragment carries a definition for each index it uses. For an index the page already had, that's the page's own definition copied verbatim — keeps the index alive without restyling. OneNote may clone such a copy to a fresh index and re-point the incoming paragraphs there; this settles after the first write and doesn't accumulate.

### `0x80042030` means the page is open for editing

While a page is checked out in the OneNote window, `UpdatePageContent` fails with `0x80042030` — but `GetPageContent`, `GetHierarchy`, `CreateNewPage` keep working, so it looks like an XML bug when it isn't. Clears on its own once editing stops; the service translates the HRESULT into a message saying so.

### OneNote emits HTML, not XHTML

Run formatting inside `one:T` comes back as `<span style='font-weight:bold' lang=en-US>` — note the unquoted attribute. `InlineHtmlReader` normalises unquoted attributes, void tags and `&nbsp;` before parsing, falling back to tag-stripping if all else fails.

### Subpages are a position, not a relationship

No parent/child link exists in OneNote's data model. "Subpage-ness" is the `pageLevel` attribute (0/1/2 — hard-capped at three visual levels) on that page's hierarchy-XML `one:Page` element, combined with position: a page reads as a subpage of the nearest earlier page in the section with a lower `pageLevel`. Placing a page after the right sibling at the right level *is* making it that level's subpage — there's nothing else to reparent.

### `UpdateHierarchy`'s stub was wrong to trust

Unlike other never-called `IApplication` no-op slots, `UpdateHierarchy` (vtable slot 2) sat as a bare `void UpdateHierarchy()` until this feature needed it. Its real IDL takes exactly `bstrChangesXmlIn` and `xsSchema` — no `force`, no timestamp, no output parameter, unlike sibling `UpdatePageContent`. It's the only way to set `pageLevel`/page order directly — `CreateNewPage` always appends an unindented top-level page with no positioning parameter.

### Creating a subpage is two calls, not one

`create_page` still does `CreateNewPage` + `UpdatePageContent` first, so title/content go through the proven path. Only after that succeeds — with a real page id and an accepted title — does a *second*, separate `UpdateHierarchy` call reposition it under `parentPageId`. (Folding both into one call would mean handing OneNote a nameless page and trusting it to sync `name` from content before anything could read it back — untested and unnecessary, since the caller already has id and title.)

That second call sends the section's complete, explicit, ordered list of `one:Page` children, never a fragment (the docs warn a partial hierarchy string is ambiguous). Pre-existing pages are verbatim clones of what `GetHierarchy` returned (same ID/name/pageLevel/etc.); only the new page's element is hand-built, with `pageLevel` = parent's + 1. Existing pages' list *positions* may shift when a subpage lands mid-section (same visible effect as dragging a tab, not a bug) but their own attributes travel through untouched.

### File attachments are content, not a COM operation

No COM method inserts a file (`MergeFiles` imports whole printout pages; `Publish`/`OpenPackage` are `.one`/`.onepkg` export/import). The only route is a `one:InsertedFile` element inside `one:OE`, written via `UpdatePageContent` like any content — `AttachmentXml` builds the same partial-page fragment `MarkdownToOneNoteXml.BuildOutlineXml` does; no new interop slot needed. `pathSource` is read by OneNote *at write time* and copied into the notebook, hence the absolute-path requirement (a relative path means nothing inside OneNote's process).

Because an attachment is just block content, the safety model is inherited whole: `AiBlocks.StampAuthor` walks `DescendantsAndSelf(One + "OE")`, so `IsOwnedBy`/`RequireOwnedBlock`/`delete_block` all work on an attachment block unchanged — which is why `attach_file` adds no delete tool of its own.

Three things were open until verified against a real install; all three held:
1. `UpdatePageContent` accepts `one:InsertedFile` in a partial write (bare `one:Outline`, no whole-page context) — no `hrInvalidXML`.
2. The author stamp survives the round trip and propagates to the outline, so `delete_block` needs no attachment-specific code.
3. Replacing by `objectID` rewrites the block in place (one copy, changed caption comes back changed) and **re-reads `pathSource` from disk** — confirmed by attaching a file, editing it on disk, re-attaching under the same name, and checking the result in OneNote. Can't be verified from inside the codebase (would need slot 10 `GetBinaryPageContent`); a same-file-twice test proves nothing, since both copies would be identical either way.

One read-side surprise: unlike `one:Image`, an inserted file has **no `objectID`** under `PageInfo.Basic`, so `RenderAttachment` emits a bare `[attachment: name]` rather than an `onenote-object:` link — fine, since edits key off the containing block's `ai-block` marker, not the file's id.

`one:InsertedFile` must stay in `OneNoteXmlParser.IsIndentCarrier`'s disqualifier list — an attachment-only OE has no text, so without this it reads as an empty indent carrier, gets unwrapped, and the attachment silently vanishes from the Markdown.

Gap in the inherited safety model: `update_block` rebuilds a block from Markdown, and Markdown only ever carries an attachment as the `[attachment: name]` label — round-tripping through it drops `one:InsertedFile` and turns the file into text, and since the block carries an `ai-block` marker it looks like fair game. `RequireNoAttachment` blocks this in `OneNoteComService` itself (not just the tool description), because the whole safety model is server-side and this failure is silent and unrecoverable.

### An unusable parent has to be rejected before the page is created

`attach_file` may create its target page, and subpage creation is two calls (`CreateNewPage` then `UpdateHierarchy`). If parent validation only happens between those two calls, a rejected `parentPageId` leaves a real, stray top-level page behind — no delete-page tool exists, and the next `attach_file` for the same title won't find the stray under the corrected parent, so it creates a second one, breaking the "ask twice, get one page" guarantee.

`ParentPage.RequireNestable` therefore runs in `AttachmentsPageLookup.FindPage` (before `CreateNewPage`) as well as `SubpagePlacement.PlaceUnderParent` (after). Checking only the latter looks natural but is the bug: a parent already at `pageLevel` 2 makes the lookup search for a nonexistent level-3 page, return `null` (ordinary "not found"), and send the caller on to create a page it can never place. (Residual case: `UpdateHierarchy` itself failing, e.g. a checked-out page — inherent to the two-call design, shared with `create_page`.)

### Threading

All COM calls run on one dedicated STA thread (`StaThreadRunner`) — never a pool, since the OneNote RCW is apartment-affine. Work completes via a `TaskCompletionSource` created with `RunContinuationsAsynchronously`; without that, awaits resume *on* the STA thread and starve the queue. The idle thread runs no message pump — safe because the server registers no COM event sinks, and COM pumps messages itself during outgoing calls.

### Trimming and AOT are disabled

`Directory.Build.props` sets `PublishTrimmed=false` and `PublishAot=false` — both strip or disable the COM interop this server depends on.

### Resolving a pasted page link

`OneNoteLinkParser` recognises two shapes: a `onenote:` URI with `section-id`/`page-id` query parameters, and a SharePoint doc URL whose `wd=target(...)` parameter encodes section path, page title and both ids. It scans for the first recognised shape rather than requiring a clean standalone URL, since pasted links often arrive mid-sentence.

`OneNoteLinkResolver` treats embedded ids as strong evidence and the decoded title/section as fallback: exact title match first, narrowed by section when ambiguous, then a best-effort substring match of the GUID's hex digits inside OneNote's compound hierarchy id if no title match exists.

### Other behaviours worth knowing

- `get_current_page` prefers `Windows.CurrentWindow.CurrentPageId`, but OneNote throws with no active window (e.g. COM-started in the background). Falls back to the hierarchy's `isCurrentlyViewed` attribute, then to a `note` explaining how to proceed rather than an error.
- `search_notes` passes `fIncludeUnindexedPages: true` (finds pages Windows Search hasn't indexed yet) and `fDisplay: false` (never disturbs the OneNote UI).
- Quick Notes is a notebook named "Quick Notes"; OneNote models it as `one:UnfiledNotes`.
- The recycle bin section group is filtered out of the hierarchy.

## Coding conventions

- Don't artificially wrap a line of code that already fits on one line (e.g. a ternary `?:`), even if a formatter or habit would normally split it.
- Use explicit types, not `var` — anywhere, unless truly unavoidable (e.g. an anonymous type).
- One file, one class. If a file would define several classes, split them into separate files.
- A method's parameters go on a single line, however many there are — never wrapped one per line.

## Testing conventions

C# test projects in this repo use NUnit.
