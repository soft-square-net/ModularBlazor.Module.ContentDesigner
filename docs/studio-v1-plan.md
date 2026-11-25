# Phase 5: Studio v1 (plan, approved and built)

Branch `feat/studio-v1`, from `origin/main` 3aa9dc1 (phase 4 merged). It replaces the read-only `Pages/Designs.razor`.

## 0. Prerequisites found while reading

| # | Finding | Consequence |
|---|---|---|
| P1 | The repo is on the **MudTail V2** contract: SDK `1.0.0-preview.5`, the runtime is the `UI.MudTail` client module (`MudTailRuntime`, `moduleHost.get('UI.MudTail')`). The studio depends on UI.MudTail only, never on Layouts.Default | Everything below uses V2 APIs: `create('dock' \| 'canvas' \| 'monaco')`, `load(area)` and `loadPack('editorial')` |
| P2 | `[DockPanel]`, `IStudioPanels`, `mb-module-panel`, `ClientDialog.Provides` and `FindProvider` are on Platform **main** (PR #42), along with the `designer`, `monaco` and `editorial` packs (PR #43). They are **not in preview.5** (checked: preview.5's Abstractions dll has no `IStudioPanels`) | The studio needs **SDK `1.0.0-preview.6`**: a Platform release (tag `v1.0.0-preview.6` from main, which CI publishes). Bump `global.json` (and `tests/Directory.Build.props` if it pins a version) |
| P3 | The playground builds the platform from `../Platform`. That checkout is on `main` at f376325, before #42/#43, with uncommitted MudTail files that are not mine | It has to be at e531d30 or later for the host's `mb-module-panel` and the UI.MudTail packs. **The user should pull it** (I won't touch its uncommitted files) |
| P4 | The sibling checkouts are on feature branches: Content `feat/shared-contents`, DynamicFields `master` at 4cb5c50 (before PR #8). The playground loads their Debug builds | For the live check, Content needs a build of `origin/main` (has `/posts/{id}/events`) and DynamicFields one of PR #8 (node ids, `/content/jsonld`). I would build them in **their own temporary worktrees** and point the playground at them with `--Playground:<Name>=<path>`, leaving their checkouts alone |
| P5 | The studio has to read the post (document, contents, stages), but it talks only to `ContentDesignerClient` | New server endpoints (section 2) |

## 1. Shape of the studio

One routed page, `/contentdesigner/studio/{postId:int}`. `/contentdesigner` stays as the landing page: the designs list, "Open post…" by id, and the templates. The page is a **MudDock** workspace (`create('dock')`, `storageKey` per user), with the VsDock-style layout of plan section 6:

| Area | Panel (v1) | Notes |
|---|---|---|
| Centre (document) | **Designer**: MudCanvas (`create('canvas', { adopt: true })`) with the Mobile · Tablet · Desktop · AMP tabs, zoom, LTR/RTL and culture | The blocks are rendered **live** by `ContentDesigner.Rendering` (`DesignBody`) |
| Centre (document) | **Output**: responsive HTML, AMP HTML (from `POST design/preview`, read-only Monaco) | |
| Left | **Contents** tray: every content with its field, section, assignee and stage badge (draft, submitted, accepted, …), placed/unplaced per view, click to select | "Add existing content" is phase 7 / Content's UI |
| Left | **Structure**: the view's region tree and placements (add/remove region, move a placement to a region, order up/down) | Phase 7 adds drag and drop on the canvas |
| Right | **Properties** of the selection for the current view: region (kind, parent), placement (region, order, span, hidden), the editorial **tokens** as selects (only the values `StyleTokens` allows; for a content, also its field's styles), **"Inherit from…"/"Detach"** for the view, and **"Copy to view"** | Theme tokens only: no free classes, no CSS |
| Right | **JSON-LD** (read-only Monaco, from the check report) | |
| Right | **Templates**: apply a document template (all views or the current one), save as a template, make a kind's default | |
| Bottom | **Problems** (from `POST design/check`, grouped per view, click to select the part/region named in `Path`) | |
| Bottom | **Activity**: the post's change feed | |
| Any | Panels other modules contribute (`IStudioPanels.For("contentdesigner", ["content.post"])`), e.g. phase 6's Workflow status | Mounted the same way |

**Mounting the panels (V2 contract).** A lazily loaded module cannot register JS root components. So each of the studio's own panels is a `[assembly: DockPanel(..., ForStudio = "contentdesigner", ForContext = ["content.post"])]` contribution of this module. MudDock mounts it through the host's `mb-module-panel` with the contribution id as its `PanelKey`, exactly like a panel contributed from outside. The panels share one **StudioSession**: a module-static registry keyed by post id, found through the `ContributionContext` that the page sets in `IPageContext("content.post", postId)`. It holds the post, the draft views, the selection, the current view, the report and the poller, raises `Changed`, and is disposed with the page. The Designer canvas uses `adopt: true` inside its panel, so its blocks are ordinary Blazor markup.

**Editing model.** Edits change the session's in-memory `DesignViews`, and the canvas re-renders at once. **Save** sends `PUT design` with the version it read (409 → "reload and merge" toast). The page shows an unsaved-changes dot. Check and preview send the **unsaved** views (`CheckRequest.views`/`PreviewRequest.views`, already in the API). Publish and unpublish are buttons on the toolbar (`contentdesigner.publish`). The page asks the API (`design`, 403s) for what the user may do and disables what they can't.

**Inheritance between views.** A view that inherits shows its source's design read-only, with a banner: "Tablet inherits Mobile · Detach". Detach copies the resolved design into the view. "Inherit from…" drops the view's own design (with a confirmation). Mobile, Tablet and Desktop share one region tree: the structure panel greys out reparenting in a web view whose region differs (the rule `DesignRules` checks).

**Hot reload.** A `ChangeFeedPoller` (an `IPostChanges` interface so SignalR can replace it later) calls `GET posts/{id}/events?after=` every 4 s while the page is visible (`document.visibilityState`). On content/post events it refetches the workspace's **post part** only. The design, the selection and the unsaved edits stay. New contents show as unplaced (they fall into `items`). The changed block flashes, and the Activity panel lists the event. Problems re-check after a reload (debounced).

## 2. Server additions (UseCases + Web + tests, then OpenAPI re-export)

| Route | Permission | Use case |
|---|---|---|
| `GET /posts/{postId}/workspace?culture=` | `read` | `GetWorkspaceQuery` → `{ post: { id, publicId, title, kind, kindVersion, version, permalink }, contents: [PostContentInfo + section/assignee if Content gives them], document: RenderDocument (the workbench's `Document(...)`, with JSON-LD left out), kindValues, fieldStyles: { field → { token → values } }, design: PostDesignDto? }`: one call to open a post. `design` is null when there is none yet; the studio then calls `POST design` (Open) |
| `GET /posts/{postId}/events?after=` | `read` | `GetPostEventsQuery` → `FeedPage` (proxy of `IContentApi.GetPostEventsAsync`, which exists) |

- `DesignReport` gains `JsonLd` (string?, additive), so `POST design/check` feeds the JSON-LD panel and the Problems panel in one call. The `DesignWorkbench.CheckAsync` signature returns it.
- Unit tests for both handlers (NSubstitute ports: not found, Content unavailable → 503, the mapping) and for the report's JSON-LD.
- Re-export `src/openapi/contentdesigner.json` with Platform's `Export-ModuleOpenApi.ps1`; the build regenerates `ContentDesignerClient`.

## 3. Client code (`src/Module.ContentDesigner`)

```
Pages/Designs.razor            landing: designs list + Open post + templates (kept, trimmed; links into the studio)
Pages/Studio.razor             /contentdesigner/studio/{postId}: toolbar, MudDock host, session lifetime, IPageContext
Studio/StudioSession.cs        state + commands (select, set token, move, hide, span, order, inherit/detach, copy to view, undo stack of 50)
Studio/StudioSessions.cs       static registry by post id (cleared on dispose; nothing in a host-wide cache)
Studio/DesignEditor.cs         pure edits on DesignViews (unit-tested): the same rules as Core's ViewDesign.Checked, so a bad edit is refused before Save
Studio/RenderMapping.cs        generated DTOs → Rendering's model (RenderDesign with inheritance resolved, RenderDocument by JSON round-trip)
Studio/ChangeFeedPoller.cs     IPostChanges, polling implementation
Panels/*.razor                 Designer, Output, Contents, Structure, Properties, JsonLd, Templates, Problems, Activity ([DockPanel] each)
wwwroot/contentdesigner.css    studio chrome only (canvas and tokens come from the packs)
```

- **Rendering in the browser.** `ContentDesigner.Rendering.dll` ships as a **private assembly** of the client module. The client-modules inspector accepts a zip/folder of assemblies: the one with `[ClientModule]` is the module, the rest are private. The playground's `ClientModules:Local` points at the output folder, and `install` zips the two dlls. Server and studio render the same components.
- **Monaco** (`create('monaco')`, read-only, JSON/HTML) for JSON-LD and Output; a textarea until it loads.
- **Tests:** a new `tests/Module.ContentDesigner.Tests` (xUnit) covering DesignEditor, RenderMapping and the session's undo, inheritance and copy-to-view. No bUnit in v1.
- README and design plan: status → phase 5, the studio section, the two new routes, the playground notes.

## 4. Order of work

1. Server: the workspace and events endpoints, the report's JSON-LD, tests, OpenAPI, the client regenerated.
2. **Early live check** (as the brief asks): playground with Content and DynamicFields at their merged mains, as `acme-admin@modular.local`. Create a post in Content, then hit `workspace`, `events`, `check` and `preview` through Scalar. This verifies the phase-4 calls to Content and DynamicFields before any UI is built on them.
3. SDK preview.6 bump (needs P2), the session, the editor and the mapping with tests.
4. Studio page, dock, Designer canvas, Properties and Structure.
5. Contents, Problems, JSON-LD, Templates, Output and Activity, hot reload.
6. Playground walkthrough: open, arrange in four views, inherit/detach, check, save, apply/save template, default, hot reload (edit a content in Content's page in another tab).
7. Docs, then a commit on `feat/studio-v1`. Push and PR only after you OK them.

## 5. Out of v1 (stays in later phases)

Drag and drop on the canvas, the block template library and "save block as template" (phase 7). Content value editing in Properties via `ContentEditorForm` (phase 7, needs DynamicFields' client form as a dialog). Workflow status and Elsa (phase 6). SignalR push.
