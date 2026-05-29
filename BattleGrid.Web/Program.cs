using BattleGrid.Web.Services;
using BattleGrid.Web.Components;
using BattleGrid.Web.Components.Layout;
using BattleGrid.Web.Components.Pages;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddControllers();
builder.Services.AddRazorPages(options =>
{
    options.RootDirectory = "/Components/Pages";
});

builder.Services.AddServerSideBlazor()
    .AddCircuitOptions(options =>
    {
        options.DetailedErrors = builder.Environment.IsDevelopment();
    })
    .AddHubOptions(options =>
    {
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        options.HandshakeTimeout = TimeSpan.FromSeconds(30);
    });

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:4744";
if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiBaseUri))
{
    throw new InvalidOperationException($"ApiBaseUrl is not a valid absolute URI: '{apiBaseUrl}'");
}

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = apiBaseUri,
    Timeout = TimeSpan.FromSeconds(15)
});

builder.Services.AddScoped<AuthStateService>();
builder.Services.AddScoped<ApiService>();
builder.Services.AddScoped<QueueMatchmakingService>();
builder.Services.AddScoped<ResumableMatchService>();
builder.Services.AddScoped<ActiveMatchSession>();

var app = builder.Build();

// Render terminates TLS at the edge; required for wss:// Blazor circuits and Secure cookies.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(120)
});

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();