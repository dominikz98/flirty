using Flirty.Designer;

var builder = WebApplication.CreateBuilder(args);

// The entire wiring lies in DesignerApp, so that the Playwright E2E (#46) can host the same setup
// in-process (pattern like Flirty.Samples.Web/WebSampleApp).
DesignerApp.ConfigureServices(builder);

var app = builder.Build();
DesignerApp.Configure(app);

app.Run();
