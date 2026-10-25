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
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<OrderSyncDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("OrderSync")));

builder.Services.AddSingleton<MockOrderStore>();
builder.Services.AddHttpClient<MockShopClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["MockShop:BaseUrl"] ?? "http://localhost:5168/"));
builder.Services.AddSingleton<OrderChangeNotifier>();
builder.Services.AddScoped<OrderSyncService>();

builder.Services.Configure<MoneybirdOptions>(builder.Configuration.GetSection(MoneybirdOptions.SectionName));
builder.Services.AddHttpClient<IMoneybirdClient, MoneybirdClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<OrderInvoicingService>();
builder.Services.AddHostedService<OrderSyncWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<OrderSyncDbContext>().Database.Migrate();
}

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
app.MapMockShop();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
