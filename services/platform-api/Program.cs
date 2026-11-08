using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("TravelOS");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContext<TravelOsDbContext>(options => options.UseNpgsql(connectionString));
}

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "travelos-platform-api",
    databaseConfigured = !string.IsNullOrWhiteSpace(connectionString)
}));

app.MapGet("/", () => Results.Ok(new
{
    name = "TravelOS Platform API",
    version = "0.1.0",
    stage = "foundation"
}));

app.Run();

public partial class Program { }
