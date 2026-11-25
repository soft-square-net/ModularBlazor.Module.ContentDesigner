# Content Designer: the agreed plan

Agreed with the product owner on 2026-11-24, through several reviews: the scope, VsDock as the guide, the
DynamicFields/MudTail/Storage integration, four views, the newsroom workflow and Elsa. This page is the consolidated
result; it is the reference for every phase.

## 1. What it is

The designer opens a **post** (a document with a URL) of a known **kind**: a DynamicFields root field such as
`post.news` (schema.org `NewsArticle`), `post.article` or `post.blog`.
- It shows the **contents** attached to the post (paragraphs, headings, images, video, galleries, quotes, ...). Each
  content is worked on by its section and reviewed by the Desk.
- The designer, editor or SEO analyst **arranges** them in **four views: Mobile, Tablet, Desktop and AMP**.
- They **check** each view against the kind's schema.
- They can **save** a block's design as a block template for the current view, or the whole design as a document
  template.
- While someone designs, changes made by the sections **hot-load** into the open post.
- **JSON-LD** is filled automatically from the kind and each content's schema.org mapping.

Content types are designed in DynamicFields; the studio hosts its builder. Home pages, section fronts and data-bound
blocks come later.

## 2. Who owns what

| Module | Owns |
|---|---|
| **Content** | **Post/Document**: URL (slug per culture), kind, root values (headline, ...), the ordered references to contents, each pinned to an accepted version; publishing. **Content**: one paragraph/image/video/..., a shared library (a content can be used by several posts), its section and assignee, its stage, versions, Desk review (accept, reject with a note, edit as Desk). The change feed |
| **Shifts** | Sections, including **department sections** that serve all categories (Multimedia, SEO): a flag on Section |
| **Elsa** | The **pipeline**: stages, allowed actions per role and stage, routing of contents to sections (images → Multimedia, SEO values → SEO, text → the category's section), timers, escalations, system transitions, notifications. Elsa Studio for designing and testing workflows |
| **DynamicFields** | Kinds and block fields, validation, translation overlays, the schema.org mapping and **JSON-LD** |
| **Storage** | Assets (pinned by id and version, public URL, width/height) and the file picker |
| **ContentDesigner** | **PostDesign** (the four-view arrangement of a post), **DocumentTemplate** and **BlockTemplate**, the studio, rendering (WASM and server HTML/AMP), the design checks |

### Elsa vs the domain

Elsa owns the pipeline. Content keeps the stored state, the audit trail and the invariants:
- permission and version checks;
- version pinning, shared-content references;
- publish only with the required contents accepted;
- no delete while a content is referenced;
- tenant isolation.

The pipeline definition is published to Content as data, so Content validates a user action without calling Elsa.
Elsa reacts to the change feed and performs system transitions through Content's API. A stage is a configurable key,
and a user action is `Act(contentId, action, expectedVersion, note?)`.

## 3. ContentDesigner domain

```
PostDesign       one per post: views { mobile, tablet, desktop, amp } -> ViewDesign;
                 basedOn a DocumentTemplate; draft + published (pins the content versions it was checked against)
ViewDesign       regions (section/row/column: this module's own, never DynamicFields fields)
                 + placements: part -> { region, order, hidden, span, style tokens, blockTemplate? }
                 part = prop:{key} (a root value) | content:{id} | items (the remaining contents, in reading order)
DocumentTemplate per kind (root field + version), four ViewDesigns (a view may inherit another), default per kind
BlockTemplate    per block field (block.video ...) and view: inner part order, style tokens, hidden parts, optional content
```

- **One content tree, four designs.** Views reorder, hide, span and restyle the same contents through
  per-breakpoint tokens. They never re-parent them. The reading order (used by JSON-LD and AMP) belongs to the post.
- **Output:**
  - Mobile, Tablet and Desktop merge into one responsive page (base / `md` / `lg`; container queries in the canvas).
  - AMP is a separate page at `/amp/{publicId}/{slug}`, linked with `rel=amphtml`.
- **Opening a post with no design** applies the kind's default DocumentTemplate. Contents attached later land in
  `items` and show as unplaced.

## 4. Checks (Problems panel, per view)

1. Content: DynamicFields validation of the assembled document.
2. Design vs field version:
   - placed parts exist;
   - required schema.org values are visible in every view;
   - tokens are valid;
   - version drift is flagged.
3. Accessibility: visual vs reading order, heading levels.
4. AMP:
   - AMP renderers only;
   - CSS ≤ 75 KB;
   - no custom JS;
   - the AMP validator passes.
5. JSON-LD essentials (headline, image, datePublished, ...), the same in all views.

## 5. Rendering

- Block renderers are Razor components in a `ContentDesigner.Rendering` RCL, with AMP variants (`amp-img`,
  `amp-video`, `amp-carousel`, `amp-iframe`).
  - The studio canvas and the WASM site render them live.
  - The server renders them to HTML with .NET's `HtmlRenderer`, for AMP and for SEO prerendering.
- **v1 renderers** are built in, one per DynamicFields starter block, plus a fallback. Other modules can add renderers
  later (the `BlockRenderer` contract).
- **Data:** the server fetches the post from Content's public API through the gateway (HTTP, never in-process).
  Output is cached by post version, design version and culture.
- **Styling:** theme tokens only (DynamicFields style tokens and brand colours, start/end alignment). There are no
  free classes and no Tailwind CDN.

## 6. The studio

- **Shell:**
  - built on UI.MudTail: MudDock workspace, a VsDock-style layout;
  - opens inside the app layout, with a focus-mode toggle to full screen (the `blank` role);
  - Monaco is a self-hosted MudTail plugin, loaded on first use.
- **Left:**
  - Explorer: kinds → document templates; the block template library.
  - Toolbox: block templates for the current view + the blocks the kind allows.
  - Structure.
  - Contents tray: every content with its section, assignee and stage; "add existing content".
- **Centre:**
  - Designer: MudCanvas, the port of VsDock's canvas, with Mobile · Tablet · Desktop · AMP tabs, zoom, LTR/RTL and
    culture.
  - JSON (Monaco).
  - Output: the responsive HTML, AMP HTML and JSON-LD.
- **Right:**
  - Properties: placement and tokens for the view, "copy to view"; content values via `ContentEditorForm` or the
    field's EditorDialog, for the content's owner only.
  - JSON-LD.
  - Workflow status (contributed by the Elsa client module).
- **Bottom:** Problems, Activity (the post's change feed).
- **Elsa Studio:** its own app (MudBlazor) shown in an iframe pane, for Admin and Workflow Designer only.
- **Hot reload:** the studio polls the post's change feed every 3–5 s while the post is open. It reloads only the
  changed content and keeps the arrangement and selection. Push (SignalR) replaces the polling later behind the same
  client interface.
- **Pickers by capability:** assets from Storage (`picker:asset`), concepts, terms. `PropertyKind.Lookup` brings any
  module's lookup.

## 7. Repo layout (target)

```
src/WebApi.Modules.ContentDesigner.Core/            PostDesign, DocumentTemplate, BlockTemplate, ViewDesign, design rules
src/WebApi.Modules.ContentDesigner.UseCases/        commands and queries
src/WebApi.Modules.ContentDesigner.Infrastructure/  DbContext (schema contentdesigner), migrations ×4, Content HTTP client, output cache
src/WebApi.Modules.ContentDesigner.Web/             entry: endpoints, public render endpoints (html, amp)
src/ContentDesigner.Rendering/                      RCL: block renderers + AMP variants, responsive merge (server and client)
src/WebApi.Modules.ContentDesigner.Client/          NSwag client (hidden client module)
src/Module.ContentDesigner/                         the studio
src/Module.ContentDesigner.Site/                    public renderer for the WASM site
```

Permissions: `contentdesigner.read`, `contentdesigner.arrange`, `contentdesigner.templates`, `contentdesigner.publish`.

## 8. Phases

0. **Scaffold** (this commit):
   - `platform-module-ca` template, SDK preview.3;
   - CI (`PACKAGES_TOKEN` secret with `read:packages`);
   - a playground that loads DynamicFields, Content, Storage and Shifts beside it when they are built.
1. **Content**: Post/Document + shared Contents with stages, Desk review, change feed, migration of existing posts.
2. **Shifts**: department sections. **DynamicFields**: property groups, node ids, JSON-LD builder, `Lookup` kind,
   Asset `Accept`, pickers by capability.
3. **Platform**:
   - `mb-module-panel`, the dock-panel contribution contract, `Provides` on ClientDialog;
   - UI.MudTail packs (`designer`, `monaco`, `editorial`).
4. **ContentDesigner core**: PostDesign, templates, design rules, API; the Rendering RCL with server HTML/AMP. The Note
   example is removed here.
5. **Studio v1** (done; see [studio-v1-plan.md](studio-v1-plan.md)):
   - open a post;
   - the contents tray with stages;
   - arranging in four views;
   - polling hot reload, JSON-LD, Problems;
   - document templates.
6. **Elsa**:
   - newsroom activities, pipeline publishing to Content, routing;
   - the Workflow status panel;
   - Elsa Studio in an iframe pane;
   - test tenant and dry run.
7. **Studio v2** (done, see [studio-v2-plan.md](studio-v2-plan.md)): drag and drop, the block template library,
   "save block as template", AMP checks per block, focus mode, adding existing content from the library.
8. **Going live**: Site module, AMP and canonical endpoints, the publish rule; then push updates.
9. Later: home and section fronts, data-bound blocks.

## 9. Known follow-ups

- ModuleTemplate: `tests/Directory.Build.props` pins its own platform version (preview.2), apart from `global.json`.
  Bumping one without the other causes NU1605 downgrade errors. This repo bumps both to preview.3.
- The playground does not run Elsa yet: it needs its own database, a service client secret and a workflow pool.
- Platform: `ClientModuleCatalog` installs a client module with `Directory.Move(staging, target)` and no retry. On
  Windows a file still held open (antivirus, the indexer) makes it throw "Access to the path ... is denied", which
  aborts every client module's seeding, so the shell does not start. Retry the move briefly.
- MudDock takes a panel icon as markup, and a dock panel names a Lucide icon, so the studio shows its panes without icons.
- `FieldVersionInfo` could carry the field's title, so the contents tray shows "Heading" from the field, not from its key.
