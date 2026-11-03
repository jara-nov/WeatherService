using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.Text.Json;

public class WeatherUnavailableException(string message) : Exception(message);

public class OpenWeatherAdapter
{
	string API_URL = "https://api.openweathermap.org/data/2.5/weather";
    string FORECAST_URL = "https://api.openweathermap.org/data/2.5/forecast";
    private readonly string? ApiKey;
    private readonly AttributeDictionary attributes;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<OpenWeatherAdapter> logger;

    public OpenWeatherAdapter(IHttpClientFactory httpClientFactory, ILogger<OpenWeatherAdapter> logger, IConfiguration configuration)
    {
        this.httpClientFactory = httpClientFactory;
        this.logger = logger;
        ApiKey = configuration["OPEN_WEATHER_API_KEY"];
        string latitude = configuration["OpenWeather:Latitude"] ?? "50.680754";
        string longitude = configuration["OpenWeather:Longitude"] ?? "13.875412";
        attributes = [
            new KeyValuePair<string, string?>("lat", latitude),
            new KeyValuePair<string, string?>("lon", longitude),
            new KeyValuePair<string, string?>("appid", ApiKey)
        ];
    }

    public async Task<WeatherData[]> GetWeatherAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new WeatherUnavailableException("OPEN_WEATHER_API_KEY is not configured.");
        }

        var client = httpClientFactory.CreateClient();
        string attributesString = string.Join("&", attributes.Select(a => $"{a.Key}={Uri.EscapeDataString(a.Value ?? string.Empty)}"));

        var currentTask = FetchJsonAsync(client, "current weather", $"{API_URL}?{attributesString}", cancellationToken);
        var forecastTask = FetchJsonAsync(client, "forecast", $"{FORECAST_URL}?{attributesString}", cancellationToken);
        await Task.WhenAll(currentTask, forecastTask);

        string? currentJson = await currentTask;
        string? forecastJson = await forecastTask;

        WeatherData? current = currentJson == null ? null : TryMapWeather(currentJson);
        if (current == null && forecastJson == null)
        {
            throw new WeatherUnavailableException("OpenWeather returned no usable data.");
        }

        // Without current weather the forecast is still anchored to today's date.
        var currentWeather = current ?? new WeatherData
        {
            icon = "00",
            datum = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        WeatherData[] forecastDays = [];
        if (forecastJson != null)
        {
            try
            {
                forecastDays = MapForecast(forecastJson, currentWeather.datum);
                var (forecastMin, forecastMax) = OpenWeatherMapper.GetCurrentDayMinMax(forecastJson, currentWeather.datum);
                if (forecastMin.HasValue) currentWeather.temp_min = forecastMin;
                if (forecastMax.HasValue) currentWeather.temp_max = forecastMax;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
            {
                logger.LogWarning(ex, "OpenWeather forecast contains invalid data.");
                forecastDays = [];
            }
        }

        var result = new WeatherData[1 + forecastDays.Length];
        result[0] = currentWeather;
        Array.Copy(forecastDays, 0, result, 1, forecastDays.Length);
        return result;
    }

    // Returns null on HTTP error, timeout or network failure; only caller cancellation propagates.
    private async Task<string?> FetchJsonAsync(HttpClient client, string name, string url, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);
        try
        {
            using var response = await client.GetAsync(url, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OpenWeather {Name} request failed with status {StatusCode}.", name, (int)response.StatusCode);
                return null;
            }
            return await response.Content.ReadAsStringAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("OpenWeather {Name} request timed out after {Seconds}s.", name, RequestTimeout.TotalSeconds);
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("OpenWeather {Name} request failed: {Error}", name, ex.HttpRequestError);
            return null;
        }
    }

    private WeatherData? TryMapWeather(string json)
    {
        try
        {
            return OpenWeatherMapper.Map(json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(ex, "OpenWeather current weather contains invalid data.");
            return null;
        }
    }

    public WeatherData MapWeather(string openWeatherJson)
    {
        try
        {
            return OpenWeatherMapper.Map(openWeatherJson);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new WeatherData
            {
                feels_like = null,
                temp_min = null,
                temp_max = null,
                icon = "00",
                datum = 0
            };
        }
    }

    public WeatherData[] MapForecast(string forecastJson, long currentDatum)
    {
        return OpenWeatherMapper.MapForecast(forecastJson, currentDatum);
    }

    public struct WeatherData
    {
        public double? feels_like { get; set; }
        public double? temp_min { get; set; }
        public double? temp_max { get; set; }
        public string icon { get; set; }
        public long datum { get; set; }
    }

    private static class OpenWeatherMapper
    {
        public static WeatherData Map(string openWeatherJson)
        {
            var source = JsonSerializer.Deserialize<OpenWeatherResponse>(openWeatherJson);
            if (source.weather == null || source.weather.Length == 0)
            {
                throw new InvalidOperationException("OpenWeather response does not contain weather data.");
            }
            string? iconString = source.weather[0].icon;
            string iconSubstring = iconString != null && iconString.Length >= 2 ? iconString[..2] : "00"; // options ["01", "02", "03", "04", "09", "10", "11", "13", "50"];

            return new WeatherData
            {
                feels_like = KelvinToCelsius(source.main.feels_like),
                temp_min = KelvinToCelsius(source.main.temp_min),
                temp_max = KelvinToCelsius(source.main.temp_max),
                icon = iconSubstring,
                datum = (source.dt)
            };
        }

        public static (double? min, double? max) GetCurrentDayMinMax(string forecastJson, long currentDatum)
        {
            var source = JsonSerializer.Deserialize<ForecastResponse>(forecastJson);
            if (source.list == null || source.list.Length == 0)
            {
                return (null, null);
            }

            DateTime currentDate = DateTimeOffset.FromUnixTimeSeconds(currentDatum).UtcDateTime.Date;
            var todayEntries = source.list
                .Where(e => DateTimeOffset.FromUnixTimeSeconds(e.dt).UtcDateTime.Date == currentDate)
                .ToList();

            if (todayEntries.Count == 0)
            {
                return (null, null);
            }

            return (
                KelvinToCelsius(todayEntries.Min(e => e.main.temp_min)),
                KelvinToCelsius(todayEntries.Max(e => e.main.temp_max))
            );
        }

        public static WeatherData[] MapForecast(string forecastJson, long currentDatum)
        {
            var source = JsonSerializer.Deserialize<ForecastResponse>(forecastJson);
            if (source.list == null || source.list.Length == 0)
            {
                return [];
            }

            DateTime currentDate = DateTimeOffset.FromUnixTimeSeconds(currentDatum).UtcDateTime.Date;

            var dailyGroups = source.list
                .GroupBy(entry => DateTimeOffset.FromUnixTimeSeconds(entry.dt).UtcDateTime.Date)
                .Where(g => g.Key > currentDate)
                .OrderBy(g => g.Key);

            var result = new List<WeatherData>();
            foreach (var day in dailyGroups)
            {
                var entries = day.ToList();
                var noonEntry = entries.MinBy(e =>
                    Math.Abs((DateTimeOffset.FromUnixTimeSeconds(e.dt).UtcDateTime.TimeOfDay - TimeSpan.FromHours(12)).TotalSeconds));

                string? iconString = noonEntry.weather?.FirstOrDefault().icon;
                string iconSubstring = iconString != null && iconString.Length >= 2 ? iconString[..2] : "00";

                result.Add(new WeatherData
                {
                    feels_like = KelvinToCelsius(noonEntry.main.feels_like),
                    temp_min = KelvinToCelsius(entries.Min(e => e.main.temp_min)),
                    temp_max = KelvinToCelsius(entries.Max(e => e.main.temp_max)),
                    icon = iconSubstring,
                    datum = entries.Min(e => e.dt)
                });
            }
            return [.. result];
        }

        private static double? KelvinToCelsius(double? kelvin)
        {
            return kelvin.HasValue ? Math.Round(kelvin.Value - 273.15, 2) : null;
        }
    }

    private struct OpenWeatherResponse
    {
        public Main main { get; set; }
        public Weather[]? weather { get; set; }
        public long dt { get; set; }
        public int timezone { get; set; }

        public struct Main
        {
            public double? feels_like { get; set; }
            public double? temp_min { get; set; }
            public double? temp_max { get; set; }
        }

        public struct Weather
        {
            public string? icon { get; set; }
        }
    }

    private struct ForecastResponse
    {
        public ForecastEntry[]? list { get; set; }

        public struct ForecastEntry
        {
            public long dt { get; set; }
            public Main main { get; set; }
            public Weather[]? weather { get; set; }

            public struct Main
            {
                public double? feels_like { get; set; }
                public double? temp_min { get; set; }
                public double? temp_max { get; set; }
            }

            public struct Weather
            {
                public string? icon { get; set; }
            }
        }
    }
}
