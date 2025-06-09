using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MServer.Services;
using MServer.Data;
using MServer.Models;
using System;
using System.Linq;


namespace MServer.Controllers
{
    [ApiController]
    [Route("api/testauth")]
    public class AuthController : ControllerBase
    {
        private readonly UDbContext _context;
        private readonly ILogger<AuthController> _logger;
        private readonly IJwtTokenService _jwtTkService;
        private readonly FingerprintService _fpService;

        public AuthController
        (
            UDbContext context,
            ILogger<AuthController> logger,
            IJwtTokenService jwtTkService,
            FingerprintService fpService)
        {
            _context = context;
            _logger = logger;
            _jwtTkService = jwtTkService;
            _fpService = fpService;
        }

        [HttpPost("send")]
        public IActionResult SendSMS([FromBody] TwilioRequest request)
        {
            if (string.IsNullOrEmpty(request.Phone) ||
            !request.Phone.StartsWith("+"))
            {
                _logger.LogWarning("Invalid phone format");
                return BadRequest
                (new { message = "Phone number must be in E.164 format" });
            }

            _logger.LogInformation
            ("Code request sent for phone: {Phone}",
            request.Phone);

            return Ok(new { message = "Code request sent" });
        }

        [HttpPost("verify")]
        public IActionResult VerifySMS([FromBody] TwilioRequest request)
        {
            _logger.LogInformation("Simulating verification");

            if (!ModelState.IsValid)
            {
                return BadRequest
                (new { message = "Invalid request model" });
            }

            if (request.Code != "123456")
            {
                return BadRequest
                (new { message = "Invalid code" });
            }

            try
            {
                var existingUser = _context.Users.FirstOrDefault
                    (u => u.Phone == request.Phone);
                var fingerprint = _fpService
                    .GenerateFingerprint(HttpContext);

                if (existingUser != null)
                {
                    var newToken = _jwtTkService.GenerateJwtToken
                    (existingUser.Phone, fingerprint);

                    return Ok(new
                    {
                        message = "Login successful (new token generated)",
                        token = newToken,
                        loginProviders = existingUser.LoginProviders
                    });
                }

                var newUser = new User
                {
                    Phone = request.Phone,
                    Name = "Usuario",
                    RegDate = DateTime.UtcNow
                };

                _context.Users.Add(newUser);
                _context.SaveChanges();

                var newUserToken = _jwtTkService
                .GenerateJwtToken(newUser.Phone, fingerprint);

                _logger.LogInformation
                ("Signup successful for user: {Phone}",
                request.Phone);

                return Ok(new
                {
                    message = "Signup successful",
                    token = newUserToken,
                    loginProviders = newUser.LoginProviders
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during verification");
                return StatusCode
                (StatusCodes.Status500InternalServerError,
                new { message = "Internal server error" });
            }
        }

        [HttpGet("login-providers")]
        public IActionResult GetLoginProviders()
        {
            var phone = HttpContext.User.Claims.FirstOrDefault
            (c => c.Type == "PhoneNumber")?.Value;
            if (string.IsNullOrEmpty(phone))
            {
                _logger.LogWarning
                ("Phone number not found in token");
                return Unauthorized
                (new { message = "Unauthorized access" });
            }

            var user = _context.Users
            .FirstOrDefault(u => u.Phone == phone);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            return Ok(new { loginProviders = user.LoginProviders });
        }

        [HttpPost("add-login-provider")]
        public IActionResult AddLoginProvider
        ([FromBody] LoginProviderReq request)
        {
            var phone = HttpContext.User.Claims
            .FirstOrDefault(c => c.Type == "PhoneNumber")?.Value;
            if (string.IsNullOrEmpty(phone))
            {
                _logger.LogWarning
                ("Phone number not found");
                return Unauthorized
                (new { message = "Unauthorized access" });
            }

            var user = _context.Users
            .FirstOrDefault(u => u.Phone == phone);
            if (user == null)
            {
                return NotFound
                (new { message = "User not found" });
            }

            if (user.LoginProviders.Contains(request.Provider))
            {
                return BadRequest
                (new { message = "Provider already exists" });
            }

            user.LoginProviders.Add(request.Provider);
            _context.SaveChanges();

            _logger.LogInformation("Added login provider");

            return Ok(new
            {
                message = "Login provider added",
                loginProviders = user.LoginProviders
            });
        }
    }
}