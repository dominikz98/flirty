using Flirty.Designer.Components;
using Flirty.Designer.Services;
using Flirty.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Flirty-Engine OHNE fest verdrahteten Provider (parameterloses AddFlirty): der FlirtyDbContext wird
// stattdessen pro aktivem Connection-Profil über die Designer-Factory erzeugt (Multi-DB, Issue #37).
builder.Services.AddFlirty();

// Connection-Profil-Verwaltung: Store (persistiert als JSON im ContentRoot), aktives Profil (pro Circuit),
// Factory (IDbContextFactory gegen das aktive Profil) und die Test-/Migrate-Operationen.
builder.Services.AddSingleton<IConnectionProfileStore>(sp =>
{
    var environment = sp.GetRequiredService<IWebHostEnvironment>();
    var filePath = Path.Combine(environment.ContentRootPath, "connection-profiles.json");
    return new JsonConnectionProfileStore(filePath);
});
builder.Services.AddSingleton<ConnectionProfileOperations>();
builder.Services.AddScoped<ActiveConnectionProfile>();
builder.Services.AddScoped<IDbContextFactory<FlirtyDbContext>, FlirtyDesignerDbContextFactory>();
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<FlirtyDbContext>>().CreateDbContext());

// Admin-CRUD (#38): führt die Mediator-Commands/Queries je Operation in einem frischen DI-Scope aus,
// damit der FlirtyDbContext nicht über den ganzen Circuit lebt und Profilwechsel sofort greifen.
builder.Services.AddScoped<FlirtyAdminGateway>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
