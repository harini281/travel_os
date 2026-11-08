using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TravelOS.PlatformApi.Auth;
using TravelOS.PlatformApi.Data;
using TravelOS.PlatformApi.Domain;
using TravelOS.PlatformApi.Itineraries;
using TravelOS.PlatformApi.Trips;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("TravelOS");

var healthChecks = builder.Services.AddHealthChecks();

if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContext<TravelOsDbContext>(options =>
        options.UseNpgsql(connectionString));
    healthChecks.AddNpgSql(connectionString, name: "postgresql");
}

builder.Services
    .AddIdentityCore<User>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<TravelOsDbContext>()
    .AddSignInManager();

var issuer = builder.Configuration["Jwt:Issuer"] ?? "TravelOS";
var audience = builder.Configuration["Jwt:Audience"] ?? "TravelOS.Client";
var signingKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Configure Jwt:SigningKey with a secret of at least 32 bytes.");
if (Encoding.UTF8.GetByteCount(signingKey) < 32)
    throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 bytes.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = ClaimTypes.Role
        };
    });

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173" };
builder.Services.AddCors(options =>
{
    options.AddPolicy("web", policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("ai-generation", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.FindFirst("sub")?.Value
                ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddHttpClient("ai-planner", client =>
{
    client.Timeout = TimeSpan.FromSeconds(70);
});

var app = builder.Build();

app.UseCors("web");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapTripEndpoints();
app.MapItineraryEndpoints();

app.MapGet("/", () => Results.Ok(new
{
    name = "TravelOS Platform API",
    version = "0.3.0",
    stage = "authentication"
}));

app.Run();

public partial class Program { }
