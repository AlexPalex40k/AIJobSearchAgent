using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIJobSearchAgent.Benchmarks;

public static class BenchmarkJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions(writeIndented: true);
    public static JsonSerializerOptions CanonicalSerializerOptions { get; } = CreateSerializerOptions(writeIndented: false);

    private static JsonSerializerOptions CreateSerializerOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = writeIndented,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
