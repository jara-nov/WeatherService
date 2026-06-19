using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

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
api.MapGet("weatherforecast", () =>
{
    using var OpenWeatherAdapter = new OpenWeatherAdapter("50.680754", "13.875412");
    OpenWeatherAdapter.WeatherData[] weather = OpenWeatherAdapter.GetWeather();
    string weatherJson = JsonSerializer.Serialize(weather, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine($"service will return data: {weatherJson}");
    return weather;
})
.WithName("GetWeatherForecast");

app.MapDefaultEndpoints();

app.UseFileServer();

app.Run();