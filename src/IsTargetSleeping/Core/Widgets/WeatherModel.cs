using System.Text.Json;

namespace IsTargetSleeping;

public sealed record WeatherHour(DateTime Time, double Temperature, int Code)
{
    public string TimeLabel => Time.ToString("t", System.Globalization.CultureInfo.CurrentCulture);
    public string TemperatureLabel => $"{Temperature:0}°";
}
public sealed record WeatherForecast(string City, double Temperature, int Code, IReadOnlyList<WeatherHour> Hours)
{
    public static WeatherForecast? Parse(string city, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("current", out var current) || !root.TryGetProperty("hourly", out var hourly)) return null;
        var currentTime = current.GetProperty("time").GetDateTime();
        var times = hourly.GetProperty("time").EnumerateArray().Select(x => x.GetDateTime()).ToArray();
        var temps = hourly.GetProperty("temperature_2m").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        var codes = hourly.GetProperty("weather_code").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var hours = times.Select((t, i) => (t, i)).Where(x => x.t >= currentTime).Take(8)
            .Select(x => new WeatherHour(x.t, temps[x.i], codes[x.i])).ToList();
        return new WeatherForecast(city, current.GetProperty("temperature_2m").GetDouble(), current.GetProperty("weather_code").GetInt32(), hours);
    }
}
