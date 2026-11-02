using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System;
using System.Text.Json;

public class OpenWeatherAdapter:IDisposable
{
	string API_URL = "https://api.openweathermap.org/data/2.5/weather";
    string FORECAST_URL = "https://api.openweathermap.org/data/2.5/forecast";
    AttributeDictionary attributes;
    private readonly string? ApiKey = Environment.GetEnvironmentVariable("OPEN_WEATHER_API_KEY");

    public OpenWeatherAdapter(string latitude, string longitude)
    {
        attributes = [
            new KeyValuePair<string, string?>("lat", latitude), 
            new KeyValuePair<string, string?>("lon", longitude),
            new KeyValuePair<string, string?>("appid", ApiKey)
        ];
    }

    public WeatherData[] GetWeather()
    {
        using var client = new HttpClient();
        string attributesString = string.Join("&", attributes.Select(a => $"{a.Key}={a.Value}"));

        string currentUrl = $"{API_URL}?{attributesString}";
        var currentResponse = client.GetAsync(currentUrl).Result;
        var currentWeather = MapWeather(currentResponse.Content.ReadAsStringAsync().Result);

        string forecastUrl = $"{FORECAST_URL}?{attributesString}";
        var forecastResponse = client.GetAsync(forecastUrl).Result;
        string forecastJson = forecastResponse.Content.ReadAsStringAsync().Result;
        var forecastDays = MapForecast(forecastJson, currentWeather.datum);
        var (forecastMin, forecastMax) = OpenWeatherMapper.GetCurrentDayMinMax(forecastJson, currentWeather.datum);
        if (!double.IsNaN(forecastMin)) currentWeather.temp_min = forecastMin;
        if (!double.IsNaN(forecastMax)) currentWeather.temp_max = forecastMax;

        var result = new WeatherData[1 + forecastDays.Length];
        result[0] = currentWeather;
        Array.Copy(forecastDays, 0, result, 1, forecastDays.Length);
        return result;
    }

    public WeatherData MapWeather(string openWeatherJson)
    {
        try
        {
            return OpenWeatherMapper.Map(openWeatherJson);
        }
        catch (InvalidOperationException)
        {
            return new WeatherData
            {
                feels_like = double.NaN,
                temp_min = double.NaN,
                temp_max = double.NaN,
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
        public double feels_like { get; set; }
        public double temp_min { get; set; }
        public double temp_max { get; set; }
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

        public static (double min, double max) GetCurrentDayMinMax(string forecastJson, long currentDatum)
        {
            var source = JsonSerializer.Deserialize<ForecastResponse>(forecastJson);
            if (source.list == null || source.list.Length == 0)
            {
                return (double.NaN, double.NaN);
            }

            DateTime currentDate = DateTimeOffset.FromUnixTimeSeconds(currentDatum).UtcDateTime.Date;
            var todayEntries = source.list
                .Where(e => DateTimeOffset.FromUnixTimeSeconds(e.dt).UtcDateTime.Date == currentDate)
                .ToList();

            if (todayEntries.Count == 0)
            {
                return (double.NaN, double.NaN);
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

                string? iconString = noonEntry.weather?[0].icon;
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

        private static double KelvinToCelsius(double kelvin)
        {
            return Math.Round(kelvin - 273.15, 2);
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
            public double feels_like { get; set; }
            public double temp_min { get; set; }
            public double temp_max { get; set; }
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
                public double feels_like { get; set; }
                public double temp_min { get; set; }
                public double temp_max { get; set; }
            }

            public struct Weather
            {
                public string? icon { get; set; }
            }
        }
    }

    public void Dispose()
    {
        if(attributes != null)
        {
            attributes = null;
            this.Dispose();
        }
    }
}
