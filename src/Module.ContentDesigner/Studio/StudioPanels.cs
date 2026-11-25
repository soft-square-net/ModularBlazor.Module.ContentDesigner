using soft.square.net.Module.ContentDesigner.Panels;
using soft.square.net.ModularBlazor.Abstractions.Shell;
using soft.square.net.Module.ContentDesigner.Studio;

// The studio's own panes, declared like any other module's (the dock-panel contract): the studio lists every panel for
// "contentdesigner" (IStudioPanels) and mounts each through the host's mb-module-panel root component, so its own and
// other modules' panels (a workflow status, an Elsa Studio pane) are arranged, saved and restored the same way.
[assembly: DockPanel("designer", "Designer", typeof(DesignerPanel), ForStudio = StudioKeys.Studio, ForContext = new[] { StudioKeys.Post },
    Document = true, Icon = "layout-template", Order = 10)]
[assembly: DockPanel("output", "Output", typeof(OutputPanel), ForStudio = StudioKeys.Studio, ForContext = new[] { StudioKeys.Post },
    Document = true, Icon = "code", Order = 20)]
[assembly: DockPanel("contents", "Contents", typeof(ContentsPanel), ForStudio = StudioKeys.Studio, ForContext = new[] { StudioKeys.Post },
    Area = "left", Icon = "layers", Order = 10)]
[assembly: DockPanel("structure", "Structure", typeof(StructurePanel), ForStudio = StudioKeys.Studio, ForContext = new[] { StudioKeys.Post },
    Area = "left", Icon = "list-tree", Order = 20)]
[assembly: DockPanel("properties", "Properties", typeof(PropertiesPanel), ForStudio = StudioKeys.Studio, ForContext = new[] { StudioKeys.Post },
    Area = "right", Icon = "sliders-horizontal", Order = 10)]
[assembly: DockPanel("templates", "Templates", typeof(TemplatesPanel), ForStudio = StudioKeys.Studio, ForContext = new[] { StudioKeys.Post },
    Area = "right", Icon = "copy", Order = 20)]
[assembly: DockPanel("jsonld", "JSON-LD", typeof(JsonLdPanel), ForStudio = StudioKeys.Studio, ForContext = new[] { StudioKeys.Post },
    Area = "right", Icon = "braces", Order = 30)]
[assembly: DockPanel("problems", "Problems", typeof(ProblemsPanel), ForStudio = StudioKeys.Studio, ForContext = new[] { StudioKeys.Post },
    Area = "bottom", Icon = "circle-alert", Order = 10)]
[assembly: DockPanel("activity", "Activity", typeof(ActivityPanel), ForStudio = StudioKeys.Studio, ForContext = new[] { StudioKeys.Post },
    Area = "bottom", Icon = "activity", Order = 20)]
