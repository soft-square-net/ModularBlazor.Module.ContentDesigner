# Phase 7: Studio v2 (plan, approved 2026-11-25)

Branch `feat/studio-v2`, from `origin/main` cd840c7 (Studio v1, PR #3, merged). SDK stays on `1.0.0-preview.6`; the
studio depends on UI.MudTail only. The design plan's §9 decisions hold: theme tokens only, one content tree (views
reorder, hide and restyle it), no Elsa or routing here.

## 0. Findings from reading the code

| # | Finding | Consequence |
|---|---|---|
| F1 | **MudCanvas already does drag and drop.** It emits `drop { kind: 'move' \| 'new', id, key, parentId, index, position, targetId }` for blocks in the canvas and for *palette items anywhere on the page* (`[data-block-new="<key>"][draggable="true"]`). The dock panels are in the same document, so the tray and the Toolbox can be palettes. v1 creates the canvas with `autoDraggable: false` and ignores `drop` | No Platform change. v2 turns `autoDraggable` on, renders palette items with `data-block-new`, and handles `drop` |
| F2 | The canvas has **no root block**: `DesignBody`'s page element carries no `data-block-id`, so MudCanvas cannot resolve a drop *between top-level sections* | `RenderMapping.Decorate` marks the page element `data-block-id="page"`, `data-container="true"` (studio only; the server's HTML is unchanged) |
| F3 | Block templates: the **server is done** (CRUD, `save-block-as-template`, `BlockTemplateId` on a placement, rendering and design rules). `save-block-as-template` reads the **stored draft**, not the studio's unsaved views. The studio loads the templates (for rendering) but has no UI for them | Add an optional `design` (unsaved views) to the save request, like check/preview. UI in Explorer, Toolbox and Properties |
| F4 | AMP: the check report already has the AMP page's issues (`Codes.Amp`, path `""`) and `DesignRules` flags fields without an AMP renderer. The page-level issues **name no block**, so clicking one in Problems does nothing. The CSS size only appears in Output, with no warning before the 75 KB limit | Attribute tag-level AMP issues to the part that produced them; add a CSS budget; link Output's issues to the canvas |
| F5 | **No layout fills the `blank` role.** `LayoutRoles.Blank` exists, but only `default`, `admin` and `public` are registered. `[UseLayout("blank")]` falls back to the default layout (with its chrome). The layout is chosen per page *type* | Focus mode is a full-screen toggle on the same studio page: a fixed CSS frame that works under any layout. The `blank` role can be used later, if a layout ever fills it (a Platform follow-up) |
| F6 | Content has the library (`GET /contents?fieldKey=&stage=&sectionId=&skip=&take=`, `ContentSummary`), one content (`GET /contents/{id}`, with values) and **attach** (`POST /posts/{id}/contents { version, contentId, index?, required }`). The studio talks only to `ContentDesignerClient` | Three thin proxy endpoints in ContentDesigner (section 2) |
| F7 | The 5.1 fixes (Problems staleness, panel icons, the `FieldVersionInfo` title) are in a parallel session and touch `ProblemsPanel`, `Studio.razor` and `StudioSession` | Not duplicated here. I rebase on 5.1 if it merges first and resolve conflicts then |

## 1. Scope, pane by pane

| Pane | Area | v2 change |
|---|---|---|
| **Designer** (canvas) | centre | **Drag and drop.** Move a block or a region inside its region or into another region (and between top-level sections). Drop palette items: a content from the tray, a region kind, `items`, a block template (onto a block of the same field), a library content. A drop that the rules refuse is refused before anything changes (toast with the reason, like v1's edits). One drop = one undo step |
| **Explorer** (new) | left | A tree. **Document templates** of the post's kind (moved from the Templates pane: apply, save as, default). **Block templates**, grouped by block field → view: rename, delete, edit the inner part order and hidden parts, "apply to the selection". Templates of fields the post does not use are listed greyed |
| **Toolbox** (new) | left | Palette for the **current view**: region kinds (Section, Row, Column), `items`, the unplaced post values and contents, and the block templates of this view for the fields the post uses. Every item is draggable onto the canvas, and also has a click action ("add to the selected region") for keyboard users |
| **Contents** (tray) | left | Items become draggable onto the canvas. **"Add existing content…"** opens the library picker (section 1.4) |
| **Structure** | left | Unchanged; it shows the same edits as the canvas |
| **Properties** | right | For a selected content: **Block template** (select among the templates of its field and the current view, or none; "Detach" copies the template's tokens onto the placement), **"Save as block template…"** (name, this view or all four views, keep its values) |
| **Templates** | right | Removed: its document-template actions move into the Explorer. The dock's saved layout (`v1` key) drops the panel without error; the default layout changes to `v2` |
| **Problems** | bottom | AMP issues are linked to the part they come from (click → AMP view, part selected). A CSS budget line ("AMP CSS 41,200 / 75,000 bytes"). A filter by view (All · Mobile · Tablet · Desktop · AMP) |
| **Output** | centre | The AMP tab lists issues with their part (click → select in the canvas) and a CSS budget bar (warning at 80 %). It re-renders after edits (debounced 1 s) while the tab is showing |
| **JSON-LD**, **Activity** | right, bottom | Unchanged |
| **Studio toolbar** | — | **Focus** toggle (shortcut `Ctrl+Shift+F`), "Exit focus" in focus mode |

### 1.1 Drag and drop: what a drop does

A `DropPlanner` (pure, unit-tested) turns MudCanvas's `drop` payload into one `DesignEditor` edit:

| Dragged | Onto | Edit |
|---|---|---|
| a placed part (`content:`, `prop:`, `items`) | a position in a region | `MoveTo(part, region, index)`. Same region: reorder in **this view only** (as v1's Alt+↑/↓). Another region: the part's region changes in every web view with its own design (one region tree, as v1's "Place in"), and its order is set in this view; in AMP it changes only AMP |
| a region | a position in a region, or at the top (`page`) | `MoveRegionTo(region, parent, index)`. Reparenting follows `DesignRules`' nesting and depth (≤ 3), and is a structural edit (all web views with their own design) |
| palette `part:{part}` (tray, Toolbox) | a region | `Place` at that index (v1 placed at the end only) |
| palette `region:{Kind}` | a position | `AddRegion(kind, parent)` at that index, then select it |
| palette `template:{id}` | a block of the template's field | `SetBlockTemplate(part, id)` in this view. Anywhere else: refused ("A Video template goes on a video block") |
| palette `library:{contentId}` | a region | Attach the content to the post through the API (section 1.4), reload the post, then `Place` it there |

The canvas shows the editor's result at once (the page is rebuilt from the views). New edits in `DesignEditor`: `MoveTo`, `MoveRegionTo`, `PlaceAt`, `AddRegionAt`, `SetBlockTemplate`, `DetachBlockTemplate`; the inheritance rules stay as in v1 (an edit detaches an inheriting view first).

### 1.2 Block templates

- **Save** (Properties, a selected content): name; **view**: this view or all four (one template per view, named "Name · Tablet"); **keep its values** (the content's node is stored, as the API already does). Sent with the **unsaved** views, so what is saved is what the canvas shows. Needs `contentdesigner.templates` (the button is disabled with a tooltip otherwise).
- **Use**: the Properties select, the Toolbox drop, or Explorer's "apply to the selection". The placement keeps its own tokens on top of the template's (the existing render rule).
- **Manage** (Explorer): rename, delete (with the version; a design that still names it gets the existing "template does not exist" problem), and the **inner parts** editor: the block field's value keys in order, each with a hide toggle. The keys come from a new `blockFields` map in the workspace.
- A template **with values** is applied as styling only in v2; creating a new content from it is Content's job (out of scope).

### 1.3 AMP checks

- `AmpChecker.CheckFragment(html)`: the tag-level rules (forbidden tags, inline styles, event handlers, layouts, https sources, extensions used). `DesignWorkbench` renders each placed block of the AMP view alone and attributes its issues to that placement (`/amp/placements/{n}`, code `amp`). The page-level rules (doctype, head, boilerplate, runtime, CSS) stay on the page; an issue already attributed to a block is not reported twice.
- **CSS budget**: `amp-css-budget` warning from 80 % of 75 KB, error over (the existing `css-size`).
- `DesignReport` gains `ampCssBytes` (int?, additive), and `PreviewDto.AmpIssues` gains `part` (string?, additive), so Problems and Output link to the canvas.
- The official validator stays opt-in in the tests (needs Node).

### 1.4 Add existing content

- **Library picker** (a MudTail dialog from the tray's "Add existing content…" button): filters by block field (the kind's block fields first), stage (default: accepted) and section; paged (20). Already attached and archived contents are left out. Selecting one shows a preview rendered by its block renderer (`GET library/{id}`).
- **Add**: required or not (default: not required, since a shared content usually belongs to another post's plan; the user can tick it), and where: "unplaced" (lands in `items`) or into the selected region. Sends the **post's version** (409 → "The post changed; reload" toast, then a reload).
- Drag a picker row onto the canvas (`library:{id}`) to attach and place in one gesture.
- Content decides who may attach (its own permission check through the gateway); ContentDesigner requires `contentdesigner.arrange` too.

### 1.5 Focus mode

Approved change: a **toggle on the same page**, no second route.
- The toolbar's **Focus** button (and `Ctrl+Shift+F`) puts the studio in a fixed full-viewport frame (`position: fixed; inset: 0`, theme tokens only) over the layout's chrome. **Esc** (when no text is being edited and no dialog is open) or "Exit focus" leaves it.
- The session never leaves the page, so unsaved edits, undo, the selection and the poller are untouched; the dock is re-measured after the switch.
- The URL keeps an optional `?focus=1` (opening the link starts in focus mode; toggling replaces the query without navigation).
- The `blank` layout role is not used until a layout fills it (F5).

## 2. API changes (UseCases + Web + tests, then OpenAPI re-export and the client regenerated)

| Route | Permission | Change |
|---|---|---|
| `GET /library?fieldKey=&stage=&sectionId=&skip=&take=` | `read` | New. `ListLibraryQuery` → `{ items: ContentSummary-like rows, more }`, proxy of Content's `GET /contents` |
| `GET /library/{contentId}` | `read` | New. One content with its values as a `RenderBlock`, for the preview |
| `POST /posts/{postId}/contents` | `arrange` | New. `AttachContentCommand(postId, postVersion, contentId, index?, required)` → Content's attach; returns the new workspace (one round trip for the studio) |
| `POST /posts/{postId}/design/views/{view}/save-block-as-template` | `templates` | Request gains `design: DesignViews?` (the unsaved views; null = the stored draft, as today) |
| `GET /posts/{postId}/workspace` | `read` | Response gains `blockFields: { fieldKey → [{ key, label }] }` (additive) |
| `POST /posts/{postId}/design/check` | `read` | `DesignReport` gains `ampCssBytes`; AMP problems carry a placement path |
| `POST /posts/{postId}/design/preview` | `read` | `AmpIssue` gains `part` |

`IContentApi` gains `ListContentsAsync`, `GetContentAsync` and `AttachContentAsync` (HTTP through the gateway, as now). All changes are additive; the published-render endpoints are unchanged.

## 3. Client code (`src/Module.ContentDesigner`)

```
Pages/Studio.razor              focus toggle (button, Ctrl+Shift+F, Esc, ?focus=1), Explorer/Toolbox in the default layout
Studio/DesignEditor.cs          MoveTo, MoveRegionTo, PlaceAt, AddRegionAt, SetBlockTemplate, DetachBlockTemplate
Studio/DropPlanner.cs           new: drop payload + palette key → one edit (or the reason it is refused)
Studio/Palette.cs               new: palette keys (part:, region:, template:, library:) and their markup attributes
Studio/StudioSession.cs         block templates (list, save, apply, edit, delete), library (search, attach), AMP output state
Studio/RenderMapping.cs         the page element as the root block
Panels/ExplorerPanel.razor      new (left)
Panels/ToolboxPanel.razor       new (left)
Panels/LibraryDialog.razor      new: the "add existing content" picker
Panels/ContentsPanel.razor      draggable rows, "Add existing content…"
Panels/PropertiesPanel.razor    block template section, "Save as block template…"
Panels/ProblemsPanel.razor      view filter, AMP budget, AMP links (on top of 5.1's changes)
Panels/OutputPanel.razor        AMP issue links, budget bar, debounced refresh
Panels/TemplatesPanel.razor     removed (folded into the Explorer)
wwwroot/contentdesigner.css     palette items, focus frame, budget bar (theme tokens only)
```

## 4. Tests

- **Module.ContentDesigner.Tests** (xUnit, no bUnit, as v1):
  - `DesignEditor`: `MoveTo` (same region, across regions in one tree, AMP alone, inheriting views detach), `MoveRegionTo` (depth, into itself refused, to the top), `PlaceAt`, `AddRegionAt`, `SetBlockTemplate` (wrong field refused) and `DetachBlockTemplate`;
  - `DropPlanner`: every row of the table in 1.1, including the refusals;
  - `Palette`: key round trips.
- **UnitTests** (server):
  - the library, library-item and attach handlers (NSubstitute `IContentApi`: not found, Content unavailable → 503, 409 passed through, the mapping);
  - `SaveBlockAsTemplate` with unsaved views;
  - `AmpChecker.CheckFragment` and the per-block attribution (a fallback block with `<img>` is reported on its placement, once); the budget warning;
  - the workspace's `blockFields`.
- **IntegrationTests**: unchanged (no schema change: block templates already exist).

## 5. Verification (playground, as acme-admin, post 1)

1. Drag a paragraph to another position in its region, then into another section; check Mobile/Tablet/Desktop share the region and only this view's order changed; undo restores both.
2. Drag a section above another (the top level); drag a row into a column (refused beyond depth 3).
3. Toolbox: drop a new Row into a section; drop an unplaced content into it.
4. Properties → "Save as block template…" on the image, Tablet only, then all views; it shows in the Explorer and the Toolbox; drop it on another image; Detach; edit its inner parts; delete it.
5. AMP: make a block break AMP (a field with no AMP renderer, shown in AMP); Check: the problem names the block and selects it; Output's AMP tab shows the same link and the CSS bar.
6. Focus: toggle with unsaved edits; the edits, the undo and the layout survive; Esc returns; `?focus=1` opens in focus mode.
7. Add existing content: create a content in Content (another post), add it from the picker, unplaced, then drag another one straight onto the canvas; the tray, the canvas and Activity show them; Save; Check.

I'll report what I could not exercise in the browser.

## 6. Out of scope (later)

- A real layout for the `blank` role (Platform; Layouts.Default could register it), and a separate focus route. The CSS frame covers it until then.
- Creating a new content from a block template's stored values, and creating contents at all (Content's job).
- Editing content values in Properties (`ContentEditorForm`), and detaching a content from the post (Content's UI).
- Reordering the post's **reading order** (Content's `PUT contents/order`): the views only reorder their layout.
- Block templates for kinds other than the post's own, and template sharing across tenants.
- The newsroom items (6a live hub, 6b content model, 6c Workflow/Elsa panes), SignalR push, live presence.
- The official AMP validator in CI.

## 7. Order of work

1. Server: library, attach, save-block with unsaved views, `blockFields`, AMP attribution and budget; tests; OpenAPI and the client.
2. `DesignEditor`, `DropPlanner`, `Palette`, with tests.
3. Canvas drag and drop, Toolbox, tray drag.
4. Explorer (moves the Templates pane), Properties' block templates.
5. Problems/Output AMP, focus mode, the library dialog.
6. Playground walkthrough (section 5), README and design plan (phase 7 status), commit, push, PR against main (not merged).

## 8. Verified in the playground (acme-admin, post 1)

- Drag and drop: a Toolbox part into an empty section; a placed block across regions; a block out of "items" (a
  paragraph, the quote, the video) reordered within main; a new Section dropped before main (a top-level section).
- The depth rule: Row → Column → Row nest, and the next Column is refused ("Regions nest at most 4 deep").
- Block templates: save from the selection for all four views (with a token), apply from the Toolbox, detach (the
  token stays), rename, save inner parts, delete with confirm; a paragraph template dropped on a quote is refused.
- AMP: the Problems view filter and counts, the CSS budget meter in Problems and Output ("AMP rules pass").
- Focus mode: the button, Ctrl+Shift+F, Esc, `?focus=1` on load; unsaved edits (Save●) survive each toggle.
- The library picker: filters, preview, "Add to the post" into the selected region (not required), drag a row onto
  the canvas (attached, then placed before the headline), the unsaved edits kept; then Save.

Found and fixed while verifying: an embed's iframe took the pointer (a video could not be selected or dragged), and Esc
did nothing after a click on plain content (the key went to the body).

Not verified in the browser: an AMP issue linked to its block (the playground's posts break no AMP rule; the unit tests
cover the attribution); the 409 when the post moved on during an attach; the budget warning near 75,000 bytes.

Known limits: a block shown through "items" is placed where "items" is when it is dragged or given a template, so it
can move ahead of the other "items" contents; the Toolbox and Explorer tabs share the left pane with Contents and
Structure and are clipped in a narrow pane until it is widened (MudDock's tab strip).
