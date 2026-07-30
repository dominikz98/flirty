using Flirty.Designer;

var builder = WebApplication.CreateBuilder(args);

// The entire wiring lives in DesignerApp, so the Playwright E2E (#46) can host the same setup in-process
// (the pattern of Flirty.Samples.Web/WebSampleApp).
DesignerApp.ConfigureServices(builder);

var app = builder.Build();
DesignerApp.Configure(app);

app.Run();
