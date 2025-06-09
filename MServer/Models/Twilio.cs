using System.ComponentModel.DataAnnotations;

namespace MServer.Models
{
    public class TwilioRequest
    {
        [Required]
        [RegularExpression(@"^\+\d{10,15}$",
        ErrorMessage = "Phone number must be in E.164 format")]
        public string Phone { get; set; }

        [RegularExpression(@"^\d{6}$",
        ErrorMessage = "Code must be exactly 6 digits")]
        public string Code { get; set; }

        [MaxLength(500)]
        public string Token { get; set; }
    }

    public class LoginProviderReq
    {
        [Required]
        [MaxLength(50)]
        public string Provider { get; set; }
    }
}