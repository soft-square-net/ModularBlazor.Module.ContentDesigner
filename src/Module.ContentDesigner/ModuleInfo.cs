using soft.square.net.ModularBlazor.Abstractions;

// Default settings of this client module: read by the WebApi's client-modules core module when the file is uploaded.
// Open to every signed-in user: what each one may do is decided by the API (contentdesigner.* permissions in the user's
// tenant), and the page shows its answer. The API is tenant-bound, so sign in to a tenant to use it.
[assembly: ClientModule(Name = "ContentDesigner", Layout = "default", RoutePrefixes = new[] { "contentdesigner" },
    Dependencies = new[] { "WebApi.Modules.ContentDesigner.Client" }, Styles = new[] { "contentdesigner.css" },
    NavTitle = "ContentDesigner", NavHref = "contentdesigner", NavOrder = 50)]
