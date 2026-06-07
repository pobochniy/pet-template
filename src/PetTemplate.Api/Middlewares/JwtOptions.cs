namespace PetTemplate.Api.Middlewares;

public class JwtOptions
{
    public string Issuer { get; set; }
    public string Audience { get; set; }
    public string SigningKey { get; set; }
    public int ExpirationSeconds { get; set; }
}