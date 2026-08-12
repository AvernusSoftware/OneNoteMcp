# OneNoteMcp — project notes

Implementation notes for whoever works on this codebase next (human or Claude). User-facing docs — tools, setup, configuration — live in [README.md](README.md); this file exists so that knowledge isn't lost or repeated. Only add non-obvious things here, not a description of what the code does.

## Output style rationale

Everything the server writes follows one fixed, non-configurable layout (see README's "Markdown support" and the style table it links here from). The reasons behind specific choices:

The blank line above a heading is paragraph spacing (`spaceBefore` on the heading's `one:OE`), not
an empty paragraph. That distinction matters: a real empty paragraph would come back as a stray
blank line on every read, multiply each time the block was rewritten, and give you somewhere to
click and type — which would contaminate the block and lock it against further edits.

It has to sit on the paragraph rather than on the quick style, because OneNote multiplies a
`QuickStyleDef`'s `spaceBefore` by 36 as it takes the definition in: 11 points of leading comes
back as 396, most of a page of white space above every heading. On the paragraph the value is taken
as written.

List indentation cannot be done the same way, because OneNote has no indent attribute: nesting is
the only mechanism it has. A top-level list is therefore wrapped in an empty carrier paragraph and
hung off it. The parser unwraps that carrier on the way back, so Markdown that goes in comes out
identical rather than drifting a level deeper on every edit. Nesting is applied once per block, so
a list three deep does not walk off the page. A top-level table is wrapped in the same carrier, for
the same structural reason — it is the only way to move its left border in from the page margin.

The trailing blank line after a top-level list, the blank lines around a table, and the blank lines
around code are all real empty paragraphs rather than paragraph spacing — each sits between two
blocks that are visually distinct on their own, so there is no risk of an empty-looking paragraph
being mistaken for structure the user typed, the way there would be above a heading.

These per-block rules are written from each block's own point of view, so two of them can land on
the same seam — a table's trailing gap and a following code block's leading gap, for instance. When
that happens they share a single blank line rather than stacking into two: each rule only adds its
blank line if the block doesn't already end with one.

## Implementation notes

These are the non-obvious things, all established by testing against a real OneNote install
(Microsoft 365, OneNote 16.0.19127). They are the reason the code looks the way it does.

### COM binding uses direct vtable calls, not `dynamic`

Three natural approaches all fail:

| Approach | Failure |
| --- | --- |
| C# `dynamic` | The runtime binder calls `IDispatch::GetTypeInfo` *before* dispatching anything. OneNote returns `E_FAIL`, so every member access throws `0x80004005`. |
| `Type.InvokeMember` | Resolves through the registered type library: `TYPE_E_LIBNOTREGISTERED` (`0x8002801D`). |
| `[ComImport]` + `InterfaceIsIDispatch` | Marshals via `IDispatch::Invoke`, which OneNote implements on top of its own type library — same failure. |

Root cause of the last two: Office registers the OneNote type library **only** under
`…\TypeLib\{0EA692EE-…}\1.1\0\Win32`. There is no `Win64` entry in either registry view, so 64-bit
OneNote cannot load its own type library. Building the client as x86 does not help — the failure is
inside the OneNote process.

`OneNoteInterop.cs` therefore declares the interfaces with `ComInterfaceType.InterfaceIsDual`,
which produces direct vtable calls: no `GetTypeInfo`, no `IDispatch::Invoke`, no type library.
**Every member must stay declared in exact vtable order**; unused members are bare no-argument
slots that only hold their position. The layout came from the type library embedded in
`ONENOTE.EXE` resource 3.

### OneNote is strict about the XML it accepts

`UpdatePageContent` answers `hrInvalidXML` (`0x80042001`) for any of these:

- `one:Bullet` with a `font` attribute
- `one:Number` **without** `numberFormat`
- `one:Number` with `startAt` or `fontSize`
- `numberFormat` given as a name (`arabic`) rather than a literal mask (`##.`, `##)`)

Element order matters too: `one:Page` children go TagDef* → QuickStyleDef* → Title → body, and
inside `one:OE` it is List → Tag → content → OEChildren.

### Author attribution has to go on the paragraph, not the block

This is what the whole safety model rests on, and OneNote's behaviour here is not documented:

- `UpdatePageContent` **honours** an `author` / `lastModifiedBy` you supply. OneNote does not force
  the local Office identity, which is what lets the server label its own work.
- The attributes must be set on `one:OE` (the paragraph). Set on `one:Outline` they are **silently
  discarded** and replaced with the local Office user name. OneNote then propagates the paragraph's
  author *up* to the containing outline, which is where it persists and where it is read back from.
- Paragraphs a person types always come back carrying their own `author`. Paragraphs written
  through `UpdatePageContent` come back with no `author` attribute at all.

That last asymmetry is what makes contamination detectable: someone can click into a block the
agent created and type, and the outline keeps the agent's author — only the new paragraph gives it
away. `AiBlocks.IsOwnedBy` therefore requires both that the outline is the agent's *and* that no
paragraph inside it belongs to anyone else.

Targeting matters too. To replace a block, supply the `objectID` of its `one:Outline`; the body is
swapped in place. Supplying the `objectID` of a `one:OE` instead makes OneNote create a **new**
block and leave the original behind, silently duplicating the content.

Because a partial update carries no page context, a block written into an existing page must not
assume `quickStyleIndex` 0 means "p" or `TagDef` 0 means "To Do" — those indices belong to the
target page and may mean something else. `PageSchema` reuses a definition when the page already has
one by that name, and otherwise allocates an index the page is not using.

### Every definition a block uses must travel with it

Reusing an index is not the same as being able to leave the definition out. Replacing a block
removes its paragraphs before the incoming ones are attached, and **OneNote prunes any
`QuickStyleDef` or `TagDef` that nothing references at that moment** — so a definition only the
replaced block was using is already gone when the new paragraphs arrive. The indices they carry
then point at nothing, and OneNote quietly resolves every one of them to index 0: the block comes
back styled as flat body text, its headings gone. Appending a block hides the bug, because a new
block prunes nothing.

Every fragment therefore carries a definition for each index its content references. For an index
the page already had, that is the page's own definition copied verbatim, so carrying it keeps the
index alive without restyling anything. OneNote may answer such a copy by cloning it to a fresh
index and re-pointing the incoming paragraphs there; the duplicate settles after the first write
and does not accumulate.

### `0x80042030` means the page is open for editing

While a page is checked out in the OneNote window, `UpdatePageContent` fails with `0x80042030` —
but `GetPageContent`, `GetHierarchy` and `CreateNewPage` keep working, so it looks like the XML is
at fault when it is not. It clears on its own once the page is no longer being edited. The service
translates the HRESULT into a message that says so.

### OneNote emits HTML, not XHTML

Run formatting inside `one:T` comes back as `<span style='font-weight:bold' lang=en-US>` — note the
unquoted attribute value. `InlineHtmlReader` normalises unquoted attributes, void tags and
`&nbsp;` before parsing, and falls back to tag-stripping if all else fails.

### Subpages are a position, not a relationship

OneNote's data model has no parent/child link between pages. A page's "subpage-ness" is the
`pageLevel` attribute (0/1/2 - OneNote hard-caps at three visual levels) on that page's hierarchy-XML
`one:Page` element, combined with its position: a page reads as a subpage of whichever earlier page
in the same section has a lower `pageLevel`. There is nothing else to move or reparent - place a
page after the right sibling at the right level and it *is* that level's subpage, structurally.

### `UpdateHierarchy`'s stub was wrong to trust

Every other never-called member in `IApplication` is a placeholder-safe no-op slot - but
`UpdateHierarchy` sat at vtable slot 2 as a bare `void UpdateHierarchy()` until this feature needed to
actually call it. Microsoft's docs give its real IDL as exactly two parameters - `bstrChangesXmlIn`
and `xsSchema` - no `force`, no `dateExpectedLastModified`, no output parameter, unlike its
page-content sibling `UpdatePageContent`. It is what lets a caller set `pageLevel` and page order
directly, which is the only way this codebase creates a subpage: `CreateNewPage` always appends an
unindented top-level page and has no positioning parameter at all.

### Creating a subpage is two calls, not one

`create_page` still creates the new page exactly as before - `CreateNewPage` + `UpdatePageContent` -
so its title and content go through the one, proven path. Only once that succeeds, with a real page
id and a title OneNote has already accepted, does a *second*, separate `UpdateHierarchy` call
reposition it under `parentPageId`. Folding both into one `UpdateHierarchy` call would mean handing
OneNote a nameless page and trusting it to sync the hierarchy `name` from content before anything
could read it back - untested, and unnecessary, since the caller already knows the id and title from
the first call.

That second call sends the section's complete, explicit, ordered list of `one:Page` children, never a
fragment - the docs warn that a partial hierarchy string leaves OneNote to guess at an ambiguous
operation. Every pre-existing page in that list is a verbatim clone of what `GetHierarchy` just
returned for it - same `ID`, `name`, `pageLevel`, anything else it carried. Only the new page's
element is hand-built, with `pageLevel` set to its parent's own level + 1. The existing pages'
*positions* in the list do shift when a subpage lands in the middle of a section - that is the same
visible effect as dragging a page's tab in the OneNote UI, not a bug - but every attribute they
already had travels through untouched, so a write that was never meant to touch them cannot perturb
their own subpage grouping.

### Threading

Every COM call runs on one dedicated STA thread (`StaThreadRunner`) — never a pool, because the
OneNote RCW is apartment-affine. Work items complete via a `TaskCompletionSource` created with
`RunContinuationsAsynchronously`; without that, awaiting continuations resume *on* the STA thread
and starve the queue.

The idle thread runs no message pump. That is safe here because the server registers no COM event
sinks, so OneNote never calls into it, and COM pumps messages itself during each outgoing call.

### Trimming and AOT are disabled

`Directory.Build.props` sets `PublishTrimmed=false` and `PublishAot=false`. Both strip or disable
the built-in COM interop this server depends on.

### Resolving a pasted page link

`OneNoteLinkParser` recognises two link shapes, both of which OneNote and SharePoint hand out for
"copy link to page": a `onenote:` URI carrying `section-id`/`page-id` query parameters, and a
SharePoint doc URL whose `wd=target(...)` parameter encodes the section path, page title and both
ids together. It scans the input for the first shape it recognises rather than requiring a clean,
standalone URL, since a pasted link often arrives mid-sentence in a chat message.

Not every field comes through with equal confidence, so `OneNoteLinkResolver` treats the embedded
ids as strong evidence and the decoded title/section name as the fallback: it matches by exact
title first, narrows by section when the title alone is ambiguous, and only falls back to a
best-effort substring match of the GUID's hex digits inside OneNote's own compound hierarchy id
when no title match is available.

### Other behaviours worth knowing

- `get_current_page` prefers `Windows.CurrentWindow.CurrentPageId`, but OneNote throws when it has
  no active window (for example when COM started it in the background). It then falls back to the
  `isCurrentlyViewed` attribute in the hierarchy, and if there is still nothing, returns a `note`
  explaining how to proceed rather than an error.
- `search_notes` passes `fIncludeUnindexedPages: true`, so pages Windows Search has not indexed yet
  (including ones just created) are still found, and `fDisplay: false`, so your OneNote UI is never
  disturbed by a query.
- Quick Notes appears as a notebook named "Quick Notes"; OneNote models it as `one:UnfiledNotes`.
- The recycle bin section group is filtered out of the hierarchy.

## Coding conventions

- Don't artificially wrap a line of code that already fits on one line (e.g. a ternary `?:`
  expression). Let it stay on one line even if a formatter or habit would normally split it.
- Use explicit types, not `var` — anywhere, unless `var` is truly unavoidable (e.g. an anonymous
  type).
- One file, one class. If a file would define several classes, split them into separate files.
- A method's parameters go on a single line, however many there are — never wrapped one per line.

## Testing conventions

C# test projects in this repo use NUnit, not xUnit.
