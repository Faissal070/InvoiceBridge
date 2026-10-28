using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using OrderSync.Clients;
using OrderSync.Components;
using OrderSync.Data;
using OrderSync.MockShop;
using OrderSync.Options;
using OrderSync.Services;
using OrderSync.Workers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddLocalization(opts => opts.ResourcesPath = "Resources");
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<OrderSyncDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("OrderSync")));

builder.Services.AddSingleton<MockOrderStore>();
builder.Services.AddHttpClient<MockShopClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["MockShop:BaseUrl"] ?? "http://localhost:5168/"));
builder.Services.AddSingleton<OrderChangeNotifier>();
builder.Services.AddScoped<OrderSyncService>();

builder.Services.AddMemoryCache();
builder.Services.Configure<MoneybirdOptions>(builder.Configuration.GetSection(MoneybirdOptions.SectionName));
builder.Services.AddHttpClient<IMoneybirdClient, MoneybirdClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SandboxSimulationService>();
builder.Services.AddScoped<OrderInvoicingService>();
builder.Services.AddHostedService<OrderSyncWorker>();

builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.AddSingleton<AdminAuthService>();

builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.SectionName));
builder.Services.AddSingleton<DemoRateLimiter>();
builder.Services.AddScoped<DemoOrderService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<VisitorContext>();
builder.Services.AddScoped<CircuitHandler, VisitorCircuitHandler>();

// Behind a reverse proxy the visitor's IP (for the demo limits) comes from X-Forwarded-For.
// Only loopback proxies are trusted by default; add the host's proxy to KnownProxies if needed.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<OrderSyncDbContext>().Database.Migrate();
}

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("nl")
    .AddSupportedCultures("nl", "en")
    .AddSupportedUICultures("nl", "en")
    .SetDefaultCulture("nl"));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapGet("/set-culture/{lang}", (string lang, HttpContext ctx) =>
{
    var culture = lang == "en" ? "en" : "nl";
    ctx.Response.Cookies.Append("lang", culture, new CookieOptions { MaxAge = TimeSpan.FromDays(365), IsEssential = true });
    ctx.Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
        new CookieOptions { MaxAge = TimeSpan.FromDays(365), IsEssential = true });
    var referer = ctx.Request.Headers.Referer.ToString();
    return Results.Redirect(string.IsNullOrEmpty(referer) ? "/" : referer);
});

app.MapPost("/admin/login", (AdminAuthService auth, HttpContext ctx, [Microsoft.AspNetCore.Mvc.FromForm] string code) =>
{
    var adminOptions = ctx.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<AdminOptions>>().Value;
    if (string.IsNullOrEmpty(adminOptions.AdminCode) || code != adminOptions.AdminCode)
        return Results.Redirect("/admin?error=1");

    ctx.Response.Cookies.Append("admin_token", AdminAuthService.ComputeToken(adminOptions.AdminCode), new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = ctx.Request.IsHttps,
        MaxAge = TimeSpan.FromDays(30)
    });
    return Results.Redirect("/demo");
}).DisableAntiforgery();

app.MapStaticAssets();
app.MapMockShop();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
