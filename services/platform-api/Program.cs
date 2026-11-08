using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("TravelOS");

builder.Services.AddHealthChecks();

if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContext<TravelOsDbContext>(options =>
        options.UseNpgsql(connectionString));

    builder.Services.AddHealthChecks()
        .AddNpgSql(connectionString, name: "postgresql");
}

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapGet("/", () => Results.Ok(new
{
    name = "TravelOS Platform API",
    version = "0.2.0",
    stage = "database-foundation"
}));

app.Run();

public partial class Program { }
