using System.Text.Json;

namespace IsTargetSleeping;

public sealed record WeatherHour(DateTime Time, double Temperature, int Code)
{
    /// La hora en curso se llama «Ahora»; el resto, su hora.
    public bool IsNow { get; init; }
    /// Sin datos de amanecer y anochecer, de noche = de 19 h a 6 h: con cielo despejado el icono enseña la luna.
    public bool IsNight => Time.Hour is < 6 or >= 19;
    public string TimeLabel => IsNow ? L10n.tr("Ahora") : Time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    public string TemperatureLabel => $"{Temperature:0}°";
}
public sealed record WeatherForecast(string City, double Temperature, int Code, IReadOnlyList<WeatherHour> Hours)
{
    public IReadOnlyList<WeatherHour> NextHours => Hours.Skip(1).Take(3).ToList();
    public bool IsNight => Hours.FirstOrDefault()?.IsNight ?? false;

    public static WeatherForecast? Parse(string city, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("current", out var current) || !root.TryGetProperty("hourly", out var hourly)) return null;
        var currentTime = current.GetProperty("time").GetDateTime();
        var times = hourly.GetProperty("time").EnumerateArray().Select(x => x.GetDateTime()).ToArray();
        var temps = hourly.GetProperty("temperature_2m").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        var codes = hourly.GetProperty("weather_code").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var hours = new List<WeatherHour>
        {
            new(currentTime, current.GetProperty("temperature_2m").GetDouble(), current.GetProperty("weather_code").GetInt32()) { IsNow = true },
        };
        for (int i = 0, count = Math.Min(times.Length, Math.Min(temps.Length, codes.Length)); i < count && hours.Count < 8; i++)
            if (times[i] > currentTime) hours.Add(new WeatherHour(times[i], temps[i], codes[i]));
        return new WeatherForecast(city, current.GetProperty("temperature_2m").GetDouble(), current.GetProperty("weather_code").GetInt32(), hours);
    }
}
