using Flirty.Samples.Web;

// Web-Sample (Minimal-API + Chat-UI): hostet die Flirty-Endpunkte und liefert eine statische Chat-UI
// (wwwroot), die diese Endpunkte konsumiert. Die eigentliche Komposition liegt in WebSampleApp, damit
// Program.cs und die Integrationstests denselben Aufbau teilen.
var builder = WebApplication.CreateBuilder(args);

WebSampleApp.ConfigureServices(builder);

var app = builder.Build();

WebSampleApp.MapEndpoints(app);

app.Run();
