namespace IsTargetSleeping;

/// Una ciudad que se puede elegir para el clima, con su país y sus coordenadas: no hay que escribir nada ni buscarla en ninguna parte
/// (solo se envían latitud y longitud a Open-Meteo). `Id` es lo que se guarda en los ajustes.
public sealed record WeatherPlace(string Id, string Es, string En, double Lat, double Lon)
{
    public string Name(string language) => language == "es" ? Es : En;
    /// El país, en minúsculas ISO («santiago-cl» → «cl»).
    public string Country => Id[(Id.LastIndexOf('-') + 1)..];
    /// Solo la ciudad, sin el país («Santiago, Chile» → «Santiago»): el país ya sale en el otro selector.
    public string City(string language) { var full = Name(language); var i = full.IndexOf(", ", StringComparison.Ordinal); return i < 0 ? full : full[..i]; }
}

public sealed record WeatherCountry(string Code, string Es, string En)
{
    public string Name(string language) => language == "es" ? Es : En;
}

public static class WeatherPlaces
{
    /// shortcut: lista fija de ciudades grandes; una ciudad que no esté se podía escribir antes y sigue funcionando si ya estaba guardada (se busca por nombre).
    public static readonly IReadOnlyList<WeatherPlace> All =
    [
        new("santiago-cl", "Santiago, Chile", "Santiago, Chile", -33.45, -70.67),
        new("valparaiso-cl", "Valparaíso, Chile", "Valparaíso, Chile", -33.05, -71.62),
        new("concepcion-cl", "Concepción, Chile", "Concepción, Chile", -36.83, -73.05),
        new("antofagasta-cl", "Antofagasta, Chile", "Antofagasta, Chile", -23.65, -70.40),
        new("temuco-cl", "Temuco, Chile", "Temuco, Chile", -38.74, -72.60),
        new("buenosaires-ar", "Buenos Aires, Argentina", "Buenos Aires, Argentina", -34.61, -58.38),
        new("cordoba-ar", "Córdoba, Argentina", "Córdoba, Argentina", -31.42, -64.18),
        new("rosario-ar", "Rosario, Argentina", "Rosario, Argentina", -32.95, -60.64),
        new("mendoza-ar", "Mendoza, Argentina", "Mendoza, Argentina", -32.89, -68.84),
        new("montevideo-uy", "Montevideo, Uruguay", "Montevideo, Uruguay", -34.90, -56.19),
        new("asuncion-py", "Asunción, Paraguay", "Asunción, Paraguay", -25.26, -57.58),
        new("lapaz-bo", "La Paz, Bolivia", "La Paz, Bolivia", -16.50, -68.15),
        new("santacruz-bo", "Santa Cruz de la Sierra, Bolivia", "Santa Cruz de la Sierra, Bolivia", -17.78, -63.18),
        new("lima-pe", "Lima, Perú", "Lima, Peru", -12.05, -77.04),
        new("cusco-pe", "Cusco, Perú", "Cusco, Peru", -13.53, -71.97),
        new("arequipa-pe", "Arequipa, Perú", "Arequipa, Peru", -16.41, -71.54),
        new("quito-ec", "Quito, Ecuador", "Quito, Ecuador", -0.18, -78.47),
        new("guayaquil-ec", "Guayaquil, Ecuador", "Guayaquil, Ecuador", -2.17, -79.92),
        new("bogota-co", "Bogotá, Colombia", "Bogotá, Colombia", 4.71, -74.07),
        new("medellin-co", "Medellín, Colombia", "Medellín, Colombia", 6.25, -75.56),
        new("cali-co", "Cali, Colombia", "Cali, Colombia", 3.45, -76.53),
        new("cartagena-co", "Cartagena, Colombia", "Cartagena, Colombia", 10.39, -75.48),
        new("caracas-ve", "Caracas, Venezuela", "Caracas, Venezuela", 10.48, -66.90),
        new("saopaulo-br", "São Paulo, Brasil", "São Paulo, Brazil", -23.55, -46.63),
        new("rio-br", "Río de Janeiro, Brasil", "Rio de Janeiro, Brazil", -22.91, -43.17),
        new("brasilia-br", "Brasilia, Brasil", "Brasília, Brazil", -15.79, -47.88),
        new("cdmx-mx", "Ciudad de México, México", "Mexico City, Mexico", 19.43, -99.13),
        new("guadalajara-mx", "Guadalajara, México", "Guadalajara, Mexico", 20.66, -103.35),
        new("monterrey-mx", "Monterrey, México", "Monterrey, Mexico", 25.69, -100.32),
        new("cancun-mx", "Cancún, México", "Cancún, Mexico", 21.16, -86.85),
        new("guatemala-gt", "Ciudad de Guatemala, Guatemala", "Guatemala City, Guatemala", 14.63, -90.51),
        new("sansalvador-sv", "San Salvador, El Salvador", "San Salvador, El Salvador", 13.69, -89.19),
        new("tegucigalpa-hn", "Tegucigalpa, Honduras", "Tegucigalpa, Honduras", 14.07, -87.19),
        new("managua-ni", "Managua, Nicaragua", "Managua, Nicaragua", 12.11, -86.24),
        new("sanjose-cr", "San José, Costa Rica", "San José, Costa Rica", 9.93, -84.08),
        new("panama-pa", "Ciudad de Panamá, Panamá", "Panama City, Panama", 8.98, -79.52),
        new("lahabana-cu", "La Habana, Cuba", "Havana, Cuba", 23.11, -82.37),
        new("santodomingo-do", "Santo Domingo, República Dominicana", "Santo Domingo, Dominican Republic", 18.49, -69.93),
        new("sanjuan-pr", "San Juan, Puerto Rico", "San Juan, Puerto Rico", 18.47, -66.11),
        new("madrid-es", "Madrid, España", "Madrid, Spain", 40.42, -3.70),
        new("barcelona-es", "Barcelona, España", "Barcelona, Spain", 41.39, 2.17),
        new("valencia-es", "Valencia, España", "Valencia, Spain", 39.47, -0.38),
        new("sevilla-es", "Sevilla, España", "Seville, Spain", 37.39, -5.98),
        new("bilbao-es", "Bilbao, España", "Bilbao, Spain", 43.26, -2.93),
        new("laspalmas-es", "Las Palmas de Gran Canaria, España", "Las Palmas de Gran Canaria, Spain", 28.12, -15.43),
        new("lisboa-pt", "Lisboa, Portugal", "Lisbon, Portugal", 38.72, -9.14),
        new("paris-fr", "París, Francia", "Paris, France", 48.86, 2.35),
        new("londres-gb", "Londres, Reino Unido", "London, United Kingdom", 51.51, -0.13),
        new("berlin-de", "Berlín, Alemania", "Berlin, Germany", 52.52, 13.40),
        new("roma-it", "Roma, Italia", "Rome, Italy", 41.90, 12.50),
        new("amsterdam-nl", "Ámsterdam, Países Bajos", "Amsterdam, Netherlands", 52.37, 4.90),
        new("nuevayork-us", "Nueva York, Estados Unidos", "New York, United States", 40.71, -74.01),
        new("losangeles-us", "Los Ángeles, Estados Unidos", "Los Angeles, United States", 34.05, -118.24),
        new("miami-us", "Miami, Estados Unidos", "Miami, United States", 25.76, -80.19),
        new("chicago-us", "Chicago, Estados Unidos", "Chicago, United States", 41.88, -87.63),
        new("toronto-ca", "Toronto, Canadá", "Toronto, Canada", 43.65, -79.38),
        new("tokio-jp", "Tokio, Japón", "Tokyo, Japan", 35.68, 139.69),
        new("seul-kr", "Seúl, Corea del Sur", "Seoul, South Korea", 37.57, 126.98),
        new("pekin-cn", "Pekín, China", "Beijing, China", 39.90, 116.41),
        new("delhi-in", "Delhi, India", "Delhi, India", 28.61, 77.21),
        new("sidney-au", "Sídney, Australia", "Sydney, Australia", -33.87, 151.21),
        new("elcairo-eg", "El Cairo, Egipto", "Cairo, Egypt", 30.04, 31.24),
        new("ciudaddelcabo-za", "Ciudad del Cabo, Sudáfrica", "Cape Town, South Africa", -33.92, 18.42),
    ];

    public static readonly IReadOnlyList<WeatherCountry> Countries =
    [
        new("cl", "Chile", "Chile"), new("ar", "Argentina", "Argentina"), new("uy", "Uruguay", "Uruguay"), new("py", "Paraguay", "Paraguay"),
        new("bo", "Bolivia", "Bolivia"), new("pe", "Perú", "Peru"), new("ec", "Ecuador", "Ecuador"), new("co", "Colombia", "Colombia"),
        new("ve", "Venezuela", "Venezuela"), new("br", "Brasil", "Brazil"), new("mx", "México", "Mexico"), new("gt", "Guatemala", "Guatemala"),
        new("sv", "El Salvador", "El Salvador"), new("hn", "Honduras", "Honduras"), new("ni", "Nicaragua", "Nicaragua"), new("cr", "Costa Rica", "Costa Rica"),
        new("pa", "Panamá", "Panama"), new("cu", "Cuba", "Cuba"), new("do", "República Dominicana", "Dominican Republic"), new("pr", "Puerto Rico", "Puerto Rico"),
        new("es", "España", "Spain"), new("pt", "Portugal", "Portugal"), new("fr", "Francia", "France"), new("gb", "Reino Unido", "United Kingdom"),
        new("de", "Alemania", "Germany"), new("it", "Italia", "Italy"), new("nl", "Países Bajos", "Netherlands"), new("us", "Estados Unidos", "United States"),
        new("ca", "Canadá", "Canada"), new("jp", "Japón", "Japan"), new("kr", "Corea del Sur", "South Korea"), new("cn", "China", "China"),
        new("in", "India", "India"), new("au", "Australia", "Australia"), new("eg", "Egipto", "Egypt"), new("za", "Sudáfrica", "South Africa"),
    ];

    public static IEnumerable<WeatherPlace> InCountry(string? code) => All.Where(p => string.Equals(p.Country, code, StringComparison.OrdinalIgnoreCase));

    /// Lo guardado en los ajustes: un id de la lista o, de cuando se escribía a mano, el nombre de una ciudad («Santiago», «madrid, españa»).
    public static WeatherPlace? Resolve(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        if (Find(stored) is { } byId) return byId;
        var text = stored.Trim();
        return All.FirstOrDefault(p => string.Equals(p.Es, text, StringComparison.OrdinalIgnoreCase) || string.Equals(p.En, text, StringComparison.OrdinalIgnoreCase))
            ?? All.FirstOrDefault(p => string.Equals(p.City("es"), text, StringComparison.OrdinalIgnoreCase) || string.Equals(p.City("en"), text, StringComparison.OrdinalIgnoreCase));
    }

    public static WeatherPlace? Find(string? id) => All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
