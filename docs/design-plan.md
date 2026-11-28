# Content Designer: the agreed plan

Agreed with the product owner on 2026-11-24, through several reviews: the scope, VsDock as the guide, the
DynamicFields/MudTail/Storage integration, four views, the newsroom workflow and Elsa. This page is the consolidated
result; it is the reference for every phase. On 2026-11-25 it was aligned with the newsroom design
(Organization, Shifts v2, NewsRoom, live sync): see [section 9](#9-aligned-with-the-newsroom-design-2026-11-25), which
overrides the sections above wherever they differ.

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
| **Organization** | The unit tree (Org → Sector → Department → Section). Desk, Multimedia and SEO are Sections. Each Section owns a **list of post categories** |
| **Shifts** | Shifts of a unit, the rota and attendance; the rota becomes unit-scoped grants from check-in to check-out |
| **NewsRoom** | Proposals, moderation, the **PostProject** (Approved → InProduction → ReadyForDesk → Scheduled → Published), workflow bindings, routing rules. It adds the studio's Workflow status and Elsa Studio panes |
| **Elsa** | The **pipeline**: a main workflow per post kind and a sub-workflow per content kind, routing, the post's assignment to a Section (and its category check), timers, escalations, system transitions, notifications. Elsa Studio for designing and testing workflows |
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
  - Workflow status (added by NewsRoom through the dock-panel contract; see section 9).
- **Bottom:** Problems, Activity (the post's change feed).
- **Elsa Studio:** its own app (MudBlazor) shown in an iframe pane, for Admin and Workflow Designer only; added by
  NewsRoom, not by this module.
- **Hot reload** (phase 6a, done): the studio listens on Content's live topic for the post (`content:post:{id}`) through
  the platform's live channel; on each message it reads the post's change feed (`?after=`), which also catches up after
  a reconnect or a gap. It reloads only the changed content and keeps the arrangement and selection. It polls the feed
  every 4 s while the page is visible when there is no live channel, while it is not connected, or when the topic is
  refused or revoked.
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

Done:
0. **Scaffold**: `platform-module-ca` template, CI, a playground that loads DynamicFields, Content, Storage and Shifts.
1. **Content**: Post/Document + shared Contents with stages, Desk review, change feed, migration of existing posts.
2. **Shifts**: department sections. **DynamicFields**: property groups, node ids, JSON-LD builder, `Lookup` kind,
   Asset `Accept`, pickers by capability.
3. **Platform**: `mb-module-panel`, the dock-panel contribution contract, `Provides` on ClientDialog; UI.MudTail packs
   (`designer`, `monaco`, `editorial`).
4. **ContentDesigner core**: PostDesign, templates, design rules, API; the Rendering RCL with server HTML/AMP.
5. **Studio v1** ([studio-v1-plan.md](studio-v1-plan.md)): open a post, the contents tray with stages, four views,
   polling hot reload, JSON-LD, Problems, document templates.
- **5.1 Fixes**: the stale Problems report, field titles in the contents tray, MudDock panel icons; the Platform
  `ClientModuleCatalog` move retry (Platform #44, in preview.7).
7. **Studio v2** ([studio-v2-plan.md](studio-v2-plan.md); moved ahead of phase 6): drag and drop, the block template
   library, "save block as template", AMP checks per block, focus mode, adding existing content from the library.
- **6a. Live** (on Platform preview.7): hot reload on the platform's live channel, with polling as the fallback (section
  6). The studio's side is done; Content does not offer the post's topic yet, so the studio still polls (section 10).

- **6b. Content model** (Content C1-C3, DynamicFields' URL prefixes, Organization's contracts): done.
  - `Kind` is the root DynamicFields field key (data migration `PostKindsAsFieldKeys`); the kind picker lists the root
    post fields with their URL prefixes.
  - The tray shows each content's unit, work-item state and assignee, filtered by state and unit; the studio shows the
    post's unit and categories read only.
  - `contentdesigner.arrange` and `.publish` are checked on the post's unit and the units above it (press and category
    scopes still count), with the "your shift ended" state, local keeping of unsaved work and the save retried. A post
    in no unit is the moderator's (`newsroom.moderate`) and the Desk's (`content.accept` or `content.publish`).
  - Problems also lists `content.validation-failed` (feed, live and 422 `problems`), joined per node.
  - Not done: document templates scoped by category (optional).

Next. These phases depend on the newsroom design and wait for the newsroom build's waves 4-6 ("P2".."P5" are that
design's phases; wave 4 is Shifts v2 and Content's newsroom core, wave 5 NewsRoom and Elsa, wave 6 the client and going
live). Wave 4 needs the Organization module released first, and each wave starts with its own plan for approval.
- **6c. Newsroom panes** (waits for wave 5: NewsRoom P4 and Elsa P5): NewsRoom adds the Workflow status and Elsa Studio
  panes to the studio through the dock-panel contract; this module owns no Elsa logic. The studio's Publish follows the
  PostProject's state (section 9).
- **8. Going live** (waits for wave 6): Site module, AMP and canonical endpoints from each kind's static URL (with
  redirects when a kind's URL changes), then push updates.
- **Later**: home and section fronts, data-bound blocks, live presence ("who else is editing").

## 9. Aligned with the newsroom design (2026-11-25)

The newsroom design (Organization, Shifts v2, NewsRoom, Elsa, live sync; agreed as v5) changes this plan:
- **No Elsa or routing in this module.** The newsroom activities, routing and publishing pipeline belong to NewsRoom and
  Elsa. The studio's Workflow status and Elsa Studio panes are dock panels that NewsRoom adds (phase 6c).
  - Pro: no duplicated workflow logic; the dock-panel contract gets its first outside user.
  - Con: those panes wait for NewsRoom P4 and Elsa P5.
- **Design published ≠ post published.** The studio's Publish publishes the design only, and is enabled only while the
  post's PostProject is InProduction, ReadyForDesk or Published, the last for design fixes (without NewsRoom installed,
  it behaves as today). The post is published by the Desk through NewsRoom (Scheduled → Published), guarded by Content's `validate`.
  - Pro: one owner per state; a designer cannot publish past the Desk.
  - Con: a soft dependency on NewsRoom's project state.
- **Unit-scoped permissions.** The studio checks `contentdesigner.arrange` (and `.publish`) on `unit:{the post's unit}`,
  covered by grants on its ancestors. Shift grants exist only from check-in to check-out, so the studio shows a clear
  "your shift ended" state and keeps unsaved work locally until the user can save again. A post with no unit yet is
  editable by the moderator and the Desk only. The playground's dev grants become unit-scoped.
- **The category rule.** A Section owns a list of post categories, stored on its Organization unit (category ids; the
  categories stay in Content). A post can be assigned to a unit only when at least one of its categories belongs to
  that unit. The check is an Elsa activity (`EnsurePostInUnitCategories`), run when the moderator assigns the Section
  and again whenever the post's categories or unit change; on failure the workflow returns the post to the moderator.
  Only the workflow (its service identity) can set a post's unit, so the Content API offers no way around the rule.
  The same data lets routing suggest the Section whose categories match.
  - Pro: the rule is visible and editable per tenant in Elsa Studio; exceptions (per kind or tenant) are workflow
    changes, not code.
  - Con: when no workflow is bound for a kind, nothing assigns a unit; NewsRoom must treat that as a setup error.
- **Department sections** (the Shifts flag merged in Shifts #5) are replaced by Organization units; Shifts v2 migrates
  the Section rows into the org tree.

## 10. Known follow-ups

- ModuleTemplate: `tests/Directory.Build.props` pins its own platform version, apart from `global.json`. Bumping one
  without the other causes NU1605 downgrade errors. This repo pins both to preview.7.
- The playground does not run Elsa yet: it needs its own database, a service client secret and a workflow pool (needed
  before wave 5).
- The live topic `content:post:{id}` is Content's; whoever its permissions leave out is polled instead.
- The studio draws the icons of its own dock panels only (`StudioIcons`): a pane another module adds, such as NewsRoom's
  in phase 6c, shows no icon unless the studio carries the Lucide icon it names.
- No layout fills the platform's `blank` role, so focus mode is a full-screen frame on the studio page.
- Studio v2's known limits and what was not verified in the browser: [studio-v2-plan.md](studio-v2-plan.md), section 8.
