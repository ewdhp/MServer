using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Security.Claims;

namespace MServer.Services
{
    public interface IUCService
    {
        Guid GetUserId();
    }

    public class UserContextService : IUCService
    {
        private readonly IHttpContextAccessor _hca;

        public UserContextService(IHttpContextAccessor hca)
        {
            _hca = hca ?? throw new
            ArgumentNullException
            (nameof(hca));
        }

        public Guid GetUserId()
        {
            var userIdClaim = _hca
            .HttpContext?.User?
            .Claims.FirstOrDefault
            (c => c.Type == "Id")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) ||
                !Guid.TryParse(userIdClaim, out var userId))
            {
                // Log the issue for debugging
                Console.WriteLine
                ("User ID claim is missing or invalid.");
                throw new UnauthorizedAccessException
                ("Invalid or missing User ID claim.");
            }

            Console.WriteLine
            ($"Retrieved User ID: {userId}");
            return userId;
        }
    }
}