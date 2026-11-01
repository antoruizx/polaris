using System.Text.Json.Serialization;
using Polaris.Api.Background;
using Polaris.Api.Endpoints;
using Polaris.Api.Infrastructure;
using Polaris.Api.Realtime;
using Polaris.Application;
using Polaris.Application.Abstractions;
using Polaris.Application.Common;
using Polaris.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var polaris = builder.Configuration.GetSection("Polaris").Get<PolarisOptions>() ?? new PolarisOptions();
var connectionString = builder.Configuration.GetConnectionString("Polaris")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:Polaris en appsettings.json.");

builder.Services.AddSingleton(polaris);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPolarisApplication();
builder.Services.AddPolarisInfrastructure(connectionString);

builder.Services.AddSignalR();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRNotifier>();
builder.Services.AddHostedService<HousekeepingService>();

builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

builder.Services.AddCors(o => o.AddPolicy("web", p => p
    .WithOrigins(polaris.WebOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

await app.Services.InitializePolarisDatabaseAsync();

app.UseExceptionHandler();
app.UseCors("web");
app.UseMiddleware<TrafficMiddleware>();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();

app.MapLogisticsEndpoints();
app.MapParkingEndpoints();
app.MapDeviceEndpoints();
app.MapHub<PolarisHub>("/hubs/polaris");

app.Run();

public partial class Program;
