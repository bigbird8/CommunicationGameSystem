using System.Text.Json;

namespace CommunicationGame.Shared.Messages;

public static class TcpMessageSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(TcpMessage message)
    {
        return JsonSerializer.Serialize(message, Options);
    }

    public static TcpMessage? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<TcpMessage>(json, Options);
    }

    public static string ToLine(TcpMessage message)
    {
        return Serialize(message) + "\n";
    }
}
