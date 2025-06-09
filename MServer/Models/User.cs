using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace MServer.Models
{
    public class User
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column(TypeName = "uuid")]
        public Guid Id { get; set; }

        [ForeignKey("FaceId")]
        public string FaceId { get; set; }

        [Required(ErrorMessage = "Phone is required.")]
        [RegularExpression(@"^\d{10}$",
        ErrorMessage = "Invalid phone number format." +
        "It should be a 10-digit number.")]
        public string Phone { get; set; }

        [Required(ErrorMessage = "Name is required.")]
        [StringLength(30, MinimumLength = 5,
        ErrorMessage = "Name must be between 5 and 30 characters.")]
        public string Name { get; set; }

        [Required(ErrorMessage = "Reg date is required.")]
        public DateTime RegDate { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }

        // New property to track login providers
        public List<string> LoginProviders { get; set; } = [];

        public User()
        {
            Phone = string.Empty;
            Name = string.Empty;
            RegDate = DateTime.UtcNow;
        }

        public User(string phone, string name)
        {
            Phone = phone;
            Name = name;
            RegDate = DateTime.UtcNow;
        }
    }
}