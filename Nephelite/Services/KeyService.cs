namespace Nephelite.Services;

public class KeyService
{
    private readonly RsaSecurityKey _privateKey;
    private readonly JsonWebKey _publicKey;
    private readonly ChaCha20Poly1305 _sessionEncryptionKey;
    private readonly SymmetricSecurityKey _accessTokenEncryptionKey;
    private readonly SymmetricSecurityKey _authorizationCodeEncryptionKey;
    
    public KeyService()
    {
        var key = RSA.Create(4096);
        _privateKey = new RsaSecurityKey(key);
        _privateKey.KeyId = Convert.ToBase64String(_privateKey.ComputeJwkThumbprint());
        _publicKey = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(key.ExportParameters(false))
        {
            KeyId = _privateKey.KeyId
        });
        
        _sessionEncryptionKey = new ChaCha20Poly1305(RandomNumberGenerator.GetBytes(32));
        _accessTokenEncryptionKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        _authorizationCodeEncryptionKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
    }

    public JsonWebKeySet GetPublicJsonWebKeySet()
    {
        var keys = new JsonWebKeySet();
        keys.Keys.Add(_publicKey);
        return keys;
    }

    public SigningCredentials GetSigningCredentials()
    {
        return new SigningCredentials(_privateKey, "RS256");
    }
    
    public EncryptingCredentials GetAccessTokenEncryptingCredentials()
    {
        return new EncryptingCredentials(_accessTokenEncryptionKey, SecurityAlgorithms.Aes256KW,
            SecurityAlgorithms.Aes256CbcHmacSha512);
    }
    
    public EncryptingCredentials GetAuthorizationCodeEncryptingCredentials()
    {
        return new EncryptingCredentials(_authorizationCodeEncryptionKey, SecurityAlgorithms.Aes256KW,
            SecurityAlgorithms.Aes256CbcHmacSha512);
    }

    public string EncryptSession(string data)
    {
        var plain = Encoding.UTF8.GetBytes(data);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        _sessionEncryptionKey.Encrypt(nonce, plain, cipher, tag);
        return Convert.ToBase64String(nonce) + "." + Convert.ToBase64String(cipher) + "." + Convert.ToBase64String(tag);
    }

    public string DecryptSession(string data)
    {
        var parts = data.Split(".").Select(Convert.FromBase64String).ToArray();
        var nonce = parts[0];
        var cipher = parts[1];
        var tag = parts[2];
        var plain = new byte[cipher.Length];
        _sessionEncryptionKey.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}