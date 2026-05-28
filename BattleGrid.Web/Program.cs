using BattleGrid.Web.Services;
using BattleGrid.Web.Components;
using BattleGrid.Web.Components.Layout;
using BattleGrid.Web.Components.Pages;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddRazorPages(options =>
{
    options.RootDirectory = "/Components/Pages";
});
builder.Services.AddServerSideBlazor();

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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.MapStaticAssets();
app.UseStaticFiles();
app.UseRouting();
app.MapControllers();
app.MapBlazorHub();

app.MapFallbackToPage("/_Host");

app.Run();