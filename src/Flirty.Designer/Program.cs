using Flirty.Designer;

var builder = WebApplication.CreateBuilder(args);

// Die gesamte Verdrahtung liegt in DesignerApp, damit die Playwright-E2E (#46) denselben Aufbau
// in-Prozess hosten kann (Muster wie Flirty.Samples.Web/WebSampleApp).
DesignerApp.ConfigureServices(builder);

var app = builder.Build();
DesignerApp.Configure(app);

app.Run();
