using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Collections.Generic;

namespace MServer.Helpers
{
    public static class JwtTokenHelper
    {
        public static IDictionary<string, string>
        ExtractClaims(string token)
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);
            var claims = jwtToken.Claims
            .ToDictionary(c => c.Type, c => c.Value);
            return claims;
        }
    }
}