using Flirty.Samples.Web;

// Web sample (minimal API + chat UI): hosts the Flirty endpoints and serves a static chat UI (wwwroot)
// that consumes them. The actual composition lives in WebSampleApp, so that Program.cs and the
// integration tests share the same setup.
var builder = WebApplication.CreateBuilder(args);

WebSampleApp.ConfigureServices(builder);

var app = builder.Build();

WebSampleApp.MapEndpoints(app);

app.Run();
