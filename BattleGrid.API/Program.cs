using BattleGrid.API.Authorization;
using BattleGrid.API.BackgroundServices;
using BattleGrid.API.Extensions;
using BattleGrid.Contracts;
using BattleGrid.API.Hubs;
using BattleGrid.API.Matchmaking;
using BattleGrid.API.Services;
using BattleGrid.Application.Helpers;
using BattleGrid.Application.Interfaces;
using BattleGrid.Application.Services;
using BattleGrid.Domain;
using BattleGrid.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Add CORS policy for deployment (extend via Cors:AllowedOrigins in config / Render env vars).
var configuredCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var corsOrigins = configuredCorsOrigins
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (corsOrigins.Length == 0)
{
    corsOrigins =
    [
        "http://localhost:4744",                // http api localhost
        "http://127.0.0.1:4744",                
        "https://localhost:4743",               // https api locahost
        "https://127.0.0.1:4743",
        "http://localhost:4746",                // http web localhost
        "http://127.0.0.1:4746",
        "https://localhost:4745",               // https web localhost
        "https://127.0.0.1:4745",
        "http://api:8080",                      // api docker (default port)
        "https://api:8080",
        "http://web:8080",                      // web docker (default port)
        "https://web:8080",
        "http://api:10000",                      // api docker
        "https://api:10000",
        "http://web:10000",                      // web docker
        "https://web:10000",
        "http://battlegrid-api.onrender.com",   // api deployed (test)
        "https://battlegrid-api.onrender.com",
        "http://battlegrid-web.onrender.com",   // web deployed (test)
        "https://battlegrid-web.onrender.com",
        "http://battlegridapi.onrender.com",   // api deployed (production branch)
        "https://battlegridapi.onrender.com",
        "http://battlegridweb.onrender.com",   // web deployed (production branch)
        "https://battlegridweb.onrender.com",
        "https://battlegrid-api-kwxz.onrender.com", // api deployed (development branch)
        "http://battlegrid-api-kwxz.onrender.com",
        "https://battlegrid-web-lzql.onrender.com", // web deployed (development branch)
        "http://battlegrid-web-lzql.onrender.com"
    ];
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorFrontend", policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

builder.Services.AddDbContext<BattleGridDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("BattleGridDB");
    if (string.IsNullOrWhiteSpace(cs))
    {
        throw new InvalidOperationException("Connection string 'BattleGridDB' is missing.");
    }
    options.UseNpgsql(cs)
           //.EnableSensitiveDataLogging()
           //.LogTo(Console.WriteLine)
           ;
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var issuer = builder.Configuration["Jwt:Issuer"];
        var audience = builder.Configuration["Jwt:Audience"];
        var signingKey = builder.Configuration["Jwt:SigningKey"];

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException("Jwt:SigningKey is missing.");
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(issuer),
            ValidIssuer = issuer,
            ValidateAudience = !string.IsNullOrWhiteSpace(audience),
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(15),
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    (path.StartsWithSegments("/queue") || path.StartsWithSegments("/game")))
                {
                    context.Token = accessToken;
                    return Task.CompletedTask;
                }

                if (string.IsNullOrEmpty(context.Token) &&
                    context.Request.Cookies.TryGetValue(AuthCookieNames.AccessToken, out var cookieToken))
                {
                    context.Token = cookieToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.Requirements.Add(new AdminRequirement()));
});
builder.Services.AddScoped<IAuthorizationHandler, AdminAuthorizationHandler>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<MatchmakingCoordinator>();
builder.Services.AddHostedService<MatchmakingBackgroundService>();
builder.Services.AddHostedService<StaleMatchCleanupBackgroundService>();
builder.Services.AddHostedService<ExpiredBanCleanupBackgroundService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "BattleGrid API", Version = "v1" });

    var securityScheme = new OpenApiSecurityScheme
    {
        BearerFormat = "JWT",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
    };

    options.AddSecurityDefinition("Bearer", securityScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
                }
            },
            new List<string>()
        }
    });
});

builder.Services.AddScoped<IJwtHelper, JwtHelper>();
builder.Services.AddScoped<INormalizationHelper, NormalizationHelper>();

builder.Services.AddScoped<IAuthServices, AuthServices>();              // Some but not all Session AND User table related services such as; register, login, logout, and verify password
builder.Services.AddScoped<IAdminServices, AdminServices>();            // Admin related services such as; banning players, and ending/starting seasons
builder.Services.AddScoped<IUserServices, UserServices>();              // Rest of the User table related services. Also, include PlayerStat table related services here
builder.Services.AddScoped<IShipTypeServices, ShipTypeServices>();
builder.Services.AddScoped<IPlayerStatSeasonService, PlayerStatSeasonService>();
builder.Services.Configure<RatingFormulaSettings>(builder.Configuration.GetSection(RatingFormulaSettings.SectionName));
builder.Services.AddScoped<IMatchServices, MatchServices>();
builder.Services.AddScoped<IShipPlacementServices, ShipPlacementServices>();
builder.Services.AddSingleton<InMemoryMatchGameService>();
builder.Services.AddScoped<IReplayServices, ReplayServices>();
builder.Services.AddScoped<IBanListServices, BanListServices>();
builder.Services.AddScoped<ILeaderboardService, LeaderboardService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// This is commented out in case the hosting provider handles SSL termination at he proxy level
// Leaving enabled can sometimes cause infinite redirect loops in free Docker containers
//aaa app.UseHttpsRedirection();

app.UseForwardedHeaders();

// Enable CORS
app.UseCors("AllowBlazorFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(120)
});

app.MapControllers();

// Hub URLs for queue and game
app.MapHub<QueueHub>("/queue");
app.MapHub<GameHub>("/game/{matchId:int}");

// Standart response from API base URL. Will be used to detect API status in frontend
app.MapGet("/", () => "BattleGrid API is running.");

app.MapGet("/health", async (BattleGridDbContext db, CancellationToken cancellationToken) =>
{
    var dbOk = false;
    try
    {
        dbOk = await db.Database.CanConnectAsync(cancellationToken);
    }
    catch
    {
        dbOk = false;
    }

    return Results.Json(new BattleGrid.Contracts.ResponseDtos.HealthResponseDto
    {
        Status = "ok",
        Database = dbOk ? "connected" : "unreachable"
    });
});

app.Run();

// Used for integration tests, do NOT delete!
public partial class Program { }