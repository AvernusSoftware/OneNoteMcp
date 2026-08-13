# OneNoteMcp

A Model Context Protocol (MCP) server that gives an AI client — Claude Code, Cursor, or anything
else that speaks MCP over STDIO — access to your local **Microsoft OneNote** notebooks, converting
OneNote's XML into clean Markdown.

Built on .NET 10 and OneNote's out-of-process COM automation interface. No Office add-in, no
plugin, no PIA, and no network access: it talks to the OneNote desktop application already on your
machine. No Azure app registration or cloud configuration is needed — nothing to set up in the
Azure portal, no client ID or API permissions to grant.

## Contents

- [Tools](#tools)
- [Safety model](#safety-model)
- [Requirements](#requirements)
- [Build](#build)
- [Register with a client](#register-with-a-client)
- [Configuration](#configuration)
- [Markdown support](#markdown-support)
- [Style](#style)
- [Contributing](#contributing)
- [License](#license)

## Tools

| Tool | Parameters | Returns |
| --- | --- | --- |
| `get_hierarchy` | `scope` = `Notebooks` \| `Sections` \| `Pages` (default `Sections`), optional `startNodeId` | JSON tree of notebooks, section groups, sections and pages with ids |
| `get_current_page` | – | Id, title, section and notebook of the page open in the OneNote window |
| `get_page_content` | `pageId` | The page as Markdown, with YAML front matter (`id`, `title`, timestamps). Agent-written blocks are preceded by `<!-- ai-block: ID -->` |
| `get_page_by_link` | `link` | The page as Markdown, resolved directly from a pasted `onenote:` URI or SharePoint `Doc.aspx` link - no hierarchy walk or search needed first |
| `search_notes` | `query`, optional `maxResults` (default 20) | Matching pages with location and a text excerpt |
| `create_page` | `sectionId`, `title`, `contentMarkdown`, optional `parentPageId` | The new `page_id`, its `page_level` if nested, and a status |
| `append_to_page` | `pageId`, `contentMarkdown` | The new `block_id` and a status. Never touches existing content |
| `update_block` | `pageId`, `blockId`, `contentMarkdown` | Status. **Fails** unless the block is the agent's and untouched by you, and on a block holding an attachment |
| `delete_block` | `pageId`, `blockId` | Status. **Fails** unless the block is the agent's and untouched by you |
| `attach_file` | `filePath`, and either `pageId` or `sectionId` + `pageTitle`; optional `parentPageId`, `caption` | The `block_id` and a status, plus `page_status` (`created` \| `reused`) when the page was looked up by title |

## Requirements

- Windows
- **OneNote desktop** (the one that ships with Microsoft 365 / Office). The OneNote app from the
  Microsoft Store is a UWP app with no COM API and will not work.
- .NET 10 SDK to build (the pinned version is in `global.json`)

## Build

```powershell
dotnet build OneNoteMcp.sln -c Release
```

## Register with a client

Claude Code:

```powershell
claude mcp add onenote -- C:\OneNoteMcp\OneNoteMcp.Server\bin\Release\net10.0-windows\OneNoteMcp.Server.exe
```

Or by hand, in an MCP client config:

```json
{
  "mcpServers": {
    "onenote": {
      "command": "C:\\OneNoteMcp\\OneNoteMcp.Server\\bin\\Release\\net10.0-windows\\OneNoteMcp.Server.exe"
    }
  }
}
```

## Configuration

All configuration is available from the `appsettings.json` file.

```json
{
  "Agent": {
    "DisplayName": "OneNoteMcp Agent",
    "Initials": "AI_MCP"
  }
}
```

| Setting | Default | Meaning |
| --- | --- | --- |
| `Agent:DisplayName` | `OneNoteMcp Agent` | Written to OneNote's `author` / `lastModifiedBy`. Shown in OneNote's author highlighting, and the value `update_block` and `delete_block` match against |
| `Agent:Initials` | `AI_MCP` | Written to OneNote's `authorInitials` |

The server refuses to start if either value is empty: an empty `DisplayName` would match blocks that
carry no author, which is exactly the content that must
stay protected. Changing `DisplayName` later orphans blocks written under the old name — they stay
on the page and become read-only, which is the safe direction to fail.

## Safety model

**The server can only change text it wrote itself.** Anything you typed is out of reach.

Every block this server writes is stamped with a configurable author identity (`OneNoteMcp Agent`
by default). `update_block` and `delete_block` take a block id, re-read the page from OneNote, and
refuse unless that block carries the agent's author. The check runs on freshly read XML on every
single write, so a model cannot reach your content by inventing or reusing an id — the restriction
lives in the server, not in the tool descriptions the model is asked to respect.

**If you type into a block the agent created, that block becomes off-limits.** OneNote records
the author per paragraph, so your paragraph is visible even though the surrounding block is still
attributed to the agent. The server treats any foreign paragraph as contamination and locks the
whole block. Ask the agent to add a new block instead.

## Markdown support

Round-trips through `create_page` → `get_page_content`:

headings (`#`–`######`) · **bold** · *italic* · ~~strikethrough~~ · `inline code` · links ·
nested bullet lists · numbered lists · to-do checkboxes (`- [ ]` / `- [x]`, mapped to OneNote's
To Do tag) · tables · block quotes

Reading additionally handles underline (as `<u>`), non-to-do OneNote tags (surfaced as `` `#Tag` ``),
images (as `![alt](onenote-object:<id>)` placeholders) and ink (as a comment).

Two lossy spots, both inherent to OneNote's data model:

- **Fenced code blocks** become monospace lines in the Code quick style; the language tag is not
  preserved, because OneNote has no code-block element.
- **Thematic breaks** (`---`) become a line of em dashes.

## Style

Style is not configurable — a page assembled from several `append_to_page` calls has to look like one page,
and a per-call knob would guarantee it does not.

| | |
| --- | --- |
| Text | Segoe UI 11, in every style except code |
| Headings | Segoe UI, sized 16 / 14 / 12 / 11 by level, not bold, with a blank line above |
| Lists | bullets, numbers and to-dos all sit one level in and get a blank line after the top-level list; nesting can go arbitrarily deep without walking off the page |
| Tables | pushed in one level, like a list, so the border does not sit left of the surrounding text; a blank line above and below |
| Code | OneNote's Code style in Consolas 10, with a blank line above and below |

## License

[MIT](LICENSE)
