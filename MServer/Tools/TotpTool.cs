using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MServer.Tools
{
    public class TotpTool
    {
        private readonly TotpService _totpService;
        private readonly string _configFile = "totp_config.json";

        public TotpTool()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Security:EncryptionKey"] = Environment.GetEnvironmentVariable("TOTP_ENCRYPTION_KEY") ?? 
                                                GenerateRandomKey()
                })
                .Build();

            _totpService = new TotpService(configuration);
        }

        public void Run(string[] args)
        {
            if (args.Length == 0)
            {
                ShowHelp();
                return;
            }

            switch (args[0].ToLower())
            {
                case "generate":
                    GenerateNewSecret(args.Length > 1 ? args[1] : "default");
                    break;
                case "code":
                    GenerateCode(args.Length > 1 ? args[1] : "default");
                    break;
                case "verify":
                    if (args.Length >= 3)
                        VerifyCode(args[1], args[2]);
                    else
                        Console.WriteLine("Usage: verify <account> <code>");
                    break;
                case "qr":
                    GenerateQrUri(args.Length > 1 ? args[1] : "default");
                    break;
                case "list":
                    ListAccounts();
                    break;
                default:
                    ShowHelp();
                    break;
            }
        }

        private void GenerateNewSecret(string accountName)
        {
            var encryptedSecret = _totpService.GenerateNewSecret();
            SaveAccount(accountName, encryptedSecret);
            
            Console.WriteLine($"✅ Generated new TOTP secret for account: {accountName}");
            Console.WriteLine($"🔐 Encrypted secret stored securely");
            
            // Generate first code to verify
            var code = _totpService.GenerateCode(encryptedSecret);
            Console.WriteLine($"📱 First code: {code}");
            
            // Show QR URI
            var qrUri = _totpService.GetProvisioningUri(encryptedSecret, accountName);
            Console.WriteLine($"📲 QR URI: {qrUri}");
        }

        private void GenerateCode(string accountName)
        {
            var config = LoadConfig();
            if (!config.Accounts.ContainsKey(accountName))
            {
                Console.WriteLine($"❌ Account '{accountName}' not found. Use 'generate {accountName}' first.");
                return;
            }

            var encryptedSecret = config.Accounts[accountName];
            var code = _totpService.GenerateCode(encryptedSecret);
            Console.WriteLine($"📱 TOTP Code for {accountName}: {code}");
        }

        private void VerifyCode(string accountName, string code)
        {
            var config = LoadConfig();
            if (!config.Accounts.ContainsKey(accountName))
            {
                Console.WriteLine($"❌ Account '{accountName}' not found.");
                return;
            }

            var encryptedSecret = config.Accounts[accountName];
            var isValid = _totpService.VerifyCode(encryptedSecret, code);
            Console.WriteLine($"{(isValid ? "✅ Valid" : "❌ Invalid")} code for {accountName}");
        }

        private void GenerateQrUri(string accountName)
        {
            var config = LoadConfig();
            if (!config.Accounts.ContainsKey(accountName))
            {
                Console.WriteLine($"❌ Account '{accountName}' not found.");
                return;
            }

            var encryptedSecret = config.Accounts[accountName];
            var qrUri = _totpService.GetProvisioningUri(encryptedSecret, accountName);
            Console.WriteLine($"📲 QR URI for {accountName}:");
            Console.WriteLine(qrUri);
        }

        private void ListAccounts()
        {
            var config = LoadConfig();
            Console.WriteLine("📋 Configured TOTP accounts:");
            foreach (var account in config.Accounts.Keys)
            {
                Console.WriteLine($"  • {account}");
            }
        }

        private void SaveAccount(string accountName, string encryptedSecret)
        {
            var config = LoadConfig();
            config.Accounts[accountName] = encryptedSecret;
            
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFile, json);
        }

        private TotpConfig LoadConfig()
        {
            if (!File.Exists(_configFile))
            {
                return new TotpConfig { Accounts = new Dictionary<string, string>() };
            }

            var json = File.ReadAllText(_configFile);
            return JsonSerializer.Deserialize<TotpConfig>(json) ?? new TotpConfig { Accounts = new Dictionary<string, string>() };
        }

        private string GenerateRandomKey()
        {
            var keyBytes = new byte[32];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(keyBytes);
            return Convert.ToBase64String(keyBytes);
        }

        private void ShowHelp()
        {
            Console.WriteLine("🔐 TOTP Tool - Secure Time-based One-Time Password Generator");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  generate <account>     - Generate new TOTP secret for account");
            Console.WriteLine("  code <account>         - Generate current TOTP code");
            Console.WriteLine("  verify <account> <code> - Verify a TOTP code");
            Console.WriteLine("  qr <account>           - Show QR code URI for Google Authenticator");
            Console.WriteLine("  list                   - List all configured accounts");
            Console.WriteLine();
            Console.WriteLine("Environment Variables:");
            Console.WriteLine("  TOTP_ENCRYPTION_KEY    - Master encryption key (optional)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  ./totp generate myserver");
            Console.WriteLine("  ./totp code myserver");
            Console.WriteLine("  ./totp verify myserver 123456");
        }
    }

    public class TotpConfig
    {
        public Dictionary<string, string> Accounts { get; set; } = new();
    }

    // Program class removed to avoid multiple entry points
    // This tool is now integrated as a service
}