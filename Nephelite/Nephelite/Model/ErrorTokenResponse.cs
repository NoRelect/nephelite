namespace Nephelite.Model;

public class ErrorTokenResponse
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = default!;

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}