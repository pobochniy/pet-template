namespace LA.Atheneum.Dto.Auth;

public class VkTokenDto
{
    public string access_token { get; set; }
    public int expires_in { get; set; }
    public long user_id { get; set; }
}