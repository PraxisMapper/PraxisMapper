using System;
namespace PraxisMapper.Classes
{
    public record AuthData(string accountId, string intPassword, string authToken, DateTime expiration, bool isGdprRequest);
    public record AuthDataResponse(Guid authToken, int expiration);
}
