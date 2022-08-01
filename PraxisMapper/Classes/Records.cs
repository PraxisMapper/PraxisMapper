using System;
namespace PraxisMapper.Classes
{
    public record AuthData(string accountId, string intPassword, string authToken, DateTime expiration);
    public record AuthDataResponse(Guid authToken, int expiration);
}
