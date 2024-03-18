namespace Nephelite.Controllers;

[ApiController]
[Route("/token")]
public class TokenController : ControllerBase
{
    private readonly KeyService _keyService;
    private readonly KubernetesService _kubernetesService;
    private readonly NepheliteConfiguration _nepheliteConfiguration;
    private readonly ILogger<TokenController> _logger;

    public TokenController(
        KeyService keyService,
        KubernetesService kubernetesService,
        IOptions<NepheliteConfiguration> nepheliteConfiguration,
        ILogger<TokenController> logger)
    {
        _keyService = keyService;
        _kubernetesService = kubernetesService;
        _nepheliteConfiguration = nepheliteConfiguration.Value;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromForm] TokenRequest request, CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        HttpContext.Response.Headers.Pragma = "no-cache";

        if (request.GrantType != "authorization_code")
        {
            HttpContext.Response.StatusCode = 400;
            _logger.LogWarning("Token request used an unsupported grant type: {GrantType}", request.GrantType);
            return new JsonResult(new ErrorTokenResponse
            {
                Error = "unsupported_grant_type",
                ErrorDescription = "Unsupported grant_type"
            });
        }

        var authorization = HttpContext.Request.Headers.Authorization.FirstOrDefault();
        if (!TryGetClientBasicAuthentication(authorization, out var clientId, out var clientSecret))
        {
            // Gather client credentials either from basic auth or from form data
            clientId = request.ClientId;
            clientSecret = request.ClientSecret;
        }

        var clients = await _kubernetesService.GetClients(cancellationToken);
        var client = clients.FirstOrDefault(c => c.ClientId == clientId && c.ClientSecret == clientSecret);
        if (client == null)
        {
            HttpContext.Response.StatusCode = 401;
            HttpContext.Response.Headers.WWWAuthenticate = "Basic realm=Nephelite, charset=\"UTF-8\"";
            _logger.LogWarning("Token request supplied invalid client credentials");
            return new JsonResult(new ErrorTokenResponse
            {
                Error = "invalid_client",
                ErrorDescription = "Invalid client credentials"
            });
        }

        if (request.Code == null)
        {
            HttpContext.Response.StatusCode = 400;
            _logger.LogWarning("Token request is missing the code parameter");
            return new JsonResult(new ErrorTokenResponse
            {
                Error = "invalid_grant",
                ErrorDescription = "Missing code parameter"
            });
        }

        var jwtHandler = new JsonWebTokenHandler();
        var idpUrl = $"https://{_nepheliteConfiguration.Host}";
        var validationResult = await jwtHandler.ValidateTokenAsync(request.Code, new TokenValidationParameters
        {
            ValidIssuer = idpUrl,
            ValidAudience = idpUrl,
            IssuerSigningKey = _keyService.GetSigningCredentials().Key,
            TokenDecryptionKey = _keyService.GetAuthorizationCodeEncryptingCredentials().Key,
            RequireAudience = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.Zero
        });
        
        if (!validationResult.IsValid)
        {
            HttpContext.Response.StatusCode = 400;
            _logger.LogWarning("Token request contained invalid code parameter: {Exception}", validationResult.Exception);
            return new JsonResult(new ErrorTokenResponse
            {
                Error = "invalid_grant",
                ErrorDescription = "Invalid code parameter"
            });
        }

        var claims = validationResult.Claims;
        var sessionInformation = JsonSerializer.Deserialize<AuthorizationSessionInformation>((string)claims["session_info"])!;

        if (request.RedirectUri != null && (sessionInformation.AuthorizationRequest.RedirectUri != request.RedirectUri ||
            !client.RedirectUris.Contains(request.RedirectUri)))
        {
            HttpContext.Response.StatusCode = 400;
            _logger.LogWarning("Token request contained invalid redirect uri: {RedirectUri}", request.RedirectUri);
            return new JsonResult(new ErrorTokenResponse
            {
                Error = "invalid_request",
                ErrorDescription = "Invalid redirect uri"
            });
        }

        var tokenLifetime = client.TokenLifetime ?? _nepheliteConfiguration.DefaultTokenLifetime; 
        return new JsonResult(new SucessfulTokenResponse
        {
            TokenType = "Bearer",
            AccessToken = (string)claims["access_token"],
            IdToken = (string)claims["id_token"],
            ExpiresIn = (int)sessionInformation.RequestStart.Add(tokenLifetime).Subtract(DateTime.UtcNow).TotalSeconds,
            RefreshToken = null
        });
    }

    private static bool TryGetClientBasicAuthentication(string? value, out string username, out string password)
    {
        username = "";
        password = "";
        if (string.IsNullOrEmpty(value) || !value.StartsWith("basic", StringComparison.OrdinalIgnoreCase))
            return false;
        var base64EncodedData = value.Split(" ").LastOrDefault();
        if (base64EncodedData == null)
            return false;
        var decodedData = Encoding.UTF8.GetString(Convert.FromBase64String(base64EncodedData));
        var splitIndex = decodedData.IndexOf(':');
        if (splitIndex == -1)
            return false;
        username = decodedData[..splitIndex];
        password = decodedData[(splitIndex+1)..];
        return true;
    }
}