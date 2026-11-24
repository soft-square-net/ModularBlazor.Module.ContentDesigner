using soft.square.net.ModularBlazor.Abstractions;

// The ContentDesigner API client as a hidden client module (catalog id "WebApi.Modules.ContentDesigner.Client"): no pages, no menu
// entry; it is loaded when a module that lists it in Dependencies is, and those modules resolve its types from it.
[assembly: ClientModule(Name = "ContentDesigner API client", Hidden = true)]
