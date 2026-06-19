using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

app.Use(async (context, next) =>
{
    var stopwatch = Stopwatch.StartNew();

    try
    {
        await next();
        stopwatch.Stop();

        app.Logger.LogInformation(
            "HTTP {Method} {Path}{QueryString} responded {StatusCode} in {ElapsedMilliseconds} ms",
            context.Request.Method,
            context.Request.Path,
            context.Request.QueryString,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
        stopwatch.Stop();

        app.Logger.LogError(
            ex,
            "HTTP {Method} {Path}{QueryString} failed after {ElapsedMilliseconds} ms",
            context.Request.Method,
            context.Request.Path,
            context.Request.QueryString,
            stopwatch.ElapsedMilliseconds);

        throw;
    }
});

app.Logger.LogInformation("WeatherService started in {Environment}", app.Environment.EnvironmentName);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var api = app.MapGroup("/api");
api.MapGet("weatherforecast", (ILogger<Program> logger) =>
{
    using var OpenWeatherAdapter = new OpenWeatherAdapter("50.680754", "13.875412");
    var weather = OpenWeatherAdapter.GetWeather();

    logger.LogInformation("Weather request completed");
    return weather;
})
.WithName("GetWeatherForecast");

app.MapDefaultEndpoints();

app.UseFileServer();

app.Run();