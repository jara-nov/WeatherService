using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<OpenWeatherAdapter>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var api = app.MapGroup("/api");
api.MapGet("weatherforecast", async (OpenWeatherAdapter openWeatherAdapter, ILogger<OpenWeatherAdapter> logger, CancellationToken cancellationToken) =>
{
    try
    {
        OpenWeatherAdapter.WeatherData[] weather = await openWeatherAdapter.GetWeatherAsync(cancellationToken);
        string weatherJson = JsonSerializer.Serialize(weather, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"service will return nonEmpty data: {weather != null && weather.Length > 0}");
        return Results.Ok(weather);
    }
    catch (WeatherUnavailableException ex)
    {
        logger.LogError(ex, "Weather data unavailable.");
        return Results.Problem("Weather data is currently unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.WithName("GetWeatherForecast");

app.MapDefaultEndpoints();

app.UseFileServer();

app.Run();