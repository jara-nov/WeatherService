using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System;

public class OpenWeatherAdapter:IDisposable
{
	string API_URL = "api.openweathermap.org/data/2.5/forecast";
    AttributeDictionary attributes;
    private readonly string? ApiKey = Environment.GetEnvironmentVariable("OPEN_WEATHER_API_KEY");

    public OpenWeatherAdapter(string latitude, string longitude)
    {
        attributes = new AttributeDictionary() {
            new KeyValuePair<string, string?>("lat", latitude), 
            new KeyValuePair<string, string?>("lon", longitude),
            new KeyValuePair<string, string?>("appid", ApiKey)
        };
    }

    public string GetWeather()
    {
        // Call the OpenWeather API using the attributes and return the weather data as a string.
        using (var client = new HttpClient())
        {
            string attributesString = string.Join("&", attributes.Select(a => $"{a.Key}={a.Value}"));
            string requestUrl = $"{API_URL}?{attributesString}";
            var response = client.GetAsync(requestUrl).Result;
            return response.Content.ReadAsStringAsync().Result;
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
