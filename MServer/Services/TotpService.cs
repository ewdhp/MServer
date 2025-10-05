using System;
using System.Security.Cryptography;
using System.Text;
using OtpNet;
using Microsoft.Extensions.Configuration;

namespace MServer.Services
{
    public class TotpService
    {
        private readonly IConfiguration _configuration;
        private readonly string _encryptionKey;

        public TotpService(IConfiguration configuration)
        {
            _configuration = configuration;
            _encryptionKey = _configuration["Security:EncryptionKey"] ?? GenerateEncryptionKey();
        }

        /// <summary>
        /// Generates a new TOTP secret and returns it encrypted
        /// </summary>
        public string GenerateNewSecret()
        {
            var secretBytes = KeyGeneration.GenerateRandomKey(20); // 160-bit key
            var secret = Base32Encoding.ToString(secretBytes);
            return EncryptSecret(secret);
        }

        /// <summary>
        /// Generates TOTP code from encrypted secret
        /// </summary>
        public string GenerateCode(string encryptedSecret)
        {
            try
            {
                var secret = DecryptSecret(encryptedSecret);
                var secretBytes = Base32Encoding.ToBytes(secret);
                var totp = new Totp(secretBytes);
                return totp.ComputeTotp();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to generate TOTP code", ex);
            }
        }

        /// <summary>
        /// Verifies a TOTP code against encrypted secret
        /// </summary>
        public bool VerifyCode(string encryptedSecret, string code, int windowSteps = 1)
        {
            try
            {
                var secret = DecryptSecret(encryptedSecret);
                var secretBytes = Base32Encoding.ToBytes(secret);
                var totp = new Totp(secretBytes);
                return totp.VerifyTotp(code, out _, new VerificationWindow(windowSteps, windowSteps));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the provisioning URI for QR code generation
        /// </summary>
        public string GetProvisioningUri(string encryptedSecret, string accountName, string issuer = "MServer")
        {
            var secret = DecryptSecret(encryptedSecret);
            return $"otpauth://totp/{Uri.EscapeDataString(accountName)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}";
        }

        /// <summary>
        /// Encrypts the TOTP secret using AES
        /// </summary>
        private string EncryptSecret(string plainSecret)
        {
            using var aes = Aes.Create();
            var key = DeriveKey(_encryptionKey);
            aes.Key = key;
            aes.GenerateIV();

            var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plainSecret);
            var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            // Combine IV and encrypted data
            var result = new byte[aes.IV.Length + encryptedBytes.Length];
            Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
            Array.Copy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

            return Convert.ToBase64String(result);
        }

        /// <summary>
        /// Decrypts the TOTP secret using AES
        /// </summary>
        private string DecryptSecret(string encryptedSecret)
        {
            var encryptedData = Convert.FromBase64String(encryptedSecret);
            
            using var aes = Aes.Create();
            var key = DeriveKey(_encryptionKey);
            aes.Key = key;

            // Extract IV and encrypted bytes
            var iv = new byte[aes.IV.Length];
            var encryptedBytes = new byte[encryptedData.Length - iv.Length];
            Array.Copy(encryptedData, 0, iv, 0, iv.Length);
            Array.Copy(encryptedData, iv.Length, encryptedBytes, 0, encryptedBytes.Length);

            aes.IV = iv;
            var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

            return Encoding.UTF8.GetString(decryptedBytes);
        }

        /// <summary>
        /// Derives a 256-bit key from the master key using PBKDF2
        /// </summary>
        private byte[] DeriveKey(string masterKey)
        {
            var salt = Encoding.UTF8.GetBytes("MServer.TOTP.Salt.2024"); // Use a consistent salt
            using var pbkdf2 = new Rfc2898DeriveBytes(masterKey, salt, 10000, HashAlgorithmName.SHA256);
            return pbkdf2.GetBytes(32); // 256-bit key
        }

        /// <summary>
        /// Generates a random encryption key if none is configured
        /// </summary>
        private string GenerateEncryptionKey()
        {
            var keyBytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(keyBytes);
            return Convert.ToBase64String(keyBytes);
        }
    }
}