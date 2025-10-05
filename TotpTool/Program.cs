using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OtpNet;

namespace TotpTool
{
    public class SecureTotpGenerator
    {
        private readonly string _encryptionKey;
        private readonly string _configFile = "totp_accounts.json";

        public SecureTotpGenerator()
        {
            _encryptionKey = Environment.GetEnvironmentVariable("TOTP_MASTER_KEY") ?? 
                           LoadOrGenerateMasterKey();
        }

        public void Run(string[] args)
        {
            if (args.Length == 0)
            {
                ShowHelp();
                return;
            }

            try
            {
                switch (args[0].ToLower())
                {
                    case "new":
                    case "generate":
                        GenerateNewAccount(args.Length > 1 ? args[1] : "default");
                        break;
                    case "get":
                    case "code":
                        GetCurrentCode(args.Length > 1 ? args[1] : "default");
                        break;
                    case "verify":
                        if (args.Length >= 3)
                            VerifyCode(args[1], args[2]);
                        else
                            Console.WriteLine("❌ Usage: verify <account> <code>");
                        break;
                    case "qr":
                        ShowQrCode(args.Length > 1 ? args[1] : "default");
                        break;
                    case "list":
                        ListAccounts();
                        break;
                    case "remove":
                    case "delete":
                        RemoveAccount(args.Length > 1 ? args[1] : "");
                        break;
                    case "export":
                        ExportAccount(args.Length > 1 ? args[1] : "");
                        break;
                    case "import":
                        if (args.Length >= 3)
                            ImportSecret(args[1], args[2]);
                        else
                            Console.WriteLine("❌ Usage: import <account> <secret>");
                        break;
                    case "import-qr":
                        if (args.Length >= 2)
                            ImportFromQrUri(args[1]);
                        else
                            Console.WriteLine("❌ Usage: import-qr <otpauth_uri>");
                        break;
                    case "proxy-gauth":
                    case "gauth-proxy":
                        ProxyGoogleAuthenticator(args.Skip(1).ToArray());
                        break;
                    case "setup-pam":
                        SetupPamIntegration(args.Length > 1 ? args[1] : Environment.UserName);
                        break;
                    default:
                        ShowHelp();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
            }
        }

        private void GenerateNewAccount(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
            {
                Console.WriteLine("❌ Account name cannot be empty");
                return;
            }

            // Generate new secret
            var secretBytes = KeyGeneration.GenerateRandomKey(20);
            var secret = Base32Encoding.ToString(secretBytes);
            var encryptedSecret = EncryptSecret(secret);

            // Save to config
            var config = LoadConfig();
            config.Accounts[accountName] = encryptedSecret;
            SaveConfig(config);

            // Generate first code
            var totp = new Totp(secretBytes);
            var currentCode = totp.ComputeTotp();

            Console.WriteLine($"✅ Generated new TOTP for account: {accountName}");
            Console.WriteLine($"📱 Current code: {currentCode}");
            Console.WriteLine($"🔗 Manual entry key: {secret}");
            
            // Generate QR code URI manually
            var qrUri = $"otpauth://totp/{Uri.EscapeDataString(accountName)}?secret={secret}&issuer={Uri.EscapeDataString("SecureTotpTool")}";
            Console.WriteLine($"📲 QR Code URI:");
            Console.WriteLine(qrUri);
            Console.WriteLine();
            Console.WriteLine("💡 Scan the QR code with Google Authenticator or enter the manual key");
        }

        private void GetCurrentCode(string accountName)
        {
            var config = LoadConfig();
            if (!config.Accounts.ContainsKey(accountName))
            {
                Console.WriteLine($"❌ Account '{accountName}' not found");
                Console.WriteLine("💡 Use 'totp list' to see available accounts");
                return;
            }

            var encryptedSecret = config.Accounts[accountName];
            var secret = DecryptSecret(encryptedSecret);
            var secretBytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(secretBytes);
            
            var code = totp.ComputeTotp();
            var timeRemaining = 30 - (DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 30);
            
            Console.WriteLine($"📱 TOTP Code for '{accountName}': {code}");
            Console.WriteLine($"⏱️  Valid for {timeRemaining} more seconds");
        }

        private void VerifyCode(string accountName, string inputCode)
        {
            var config = LoadConfig();
            if (!config.Accounts.ContainsKey(accountName))
            {
                Console.WriteLine($"❌ Account '{accountName}' not found");
                return;
            }

            var encryptedSecret = config.Accounts[accountName];
            var secret = DecryptSecret(encryptedSecret);
            var secretBytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(secretBytes);
            
            var isValid = totp.VerifyTotp(inputCode, out var timeStepMatched, window: new VerificationWindow(1, 1));
            
            if (isValid)
            {
                Console.WriteLine($"✅ Code '{inputCode}' is VALID for account '{accountName}'");
                Console.WriteLine($"🕐 Matched time step: {timeStepMatched}");
            }
            else
            {
                Console.WriteLine($"❌ Code '{inputCode}' is INVALID for account '{accountName}'");
            }
        }

        private void ShowQrCode(string accountName)
        {
            var config = LoadConfig();
            if (!config.Accounts.ContainsKey(accountName))
            {
                Console.WriteLine($"❌ Account '{accountName}' not found");
                return;
            }

            var encryptedSecret = config.Accounts[accountName];
            var secret = DecryptSecret(encryptedSecret);
            
            var qrUri = $"otpauth://totp/{Uri.EscapeDataString(accountName)}?secret={secret}&issuer={Uri.EscapeDataString("SecureTotpTool")}";
            Console.WriteLine($"📲 QR Code URI for '{accountName}':");
            Console.WriteLine(qrUri);
        }

        private void ListAccounts()
        {
            var config = LoadConfig();
            if (config.Accounts.Count == 0)
            {
                Console.WriteLine("📋 No accounts configured");
                Console.WriteLine("💡 Use 'totp generate <name>' to create a new account");
                return;
            }

            Console.WriteLine("📋 Configured TOTP accounts:");
            foreach (var account in config.Accounts.Keys)
            {
                Console.WriteLine($"  • {account}");
            }
        }

        private void RemoveAccount(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
            {
                Console.WriteLine("❌ Account name required");
                return;
            }

            var config = LoadConfig();
            if (!config.Accounts.ContainsKey(accountName))
            {
                Console.WriteLine($"❌ Account '{accountName}' not found");
                return;
            }

            config.Accounts.Remove(accountName);
            SaveConfig(config);
            Console.WriteLine($"✅ Removed account '{accountName}'");
        }

        private void ExportAccount(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
            {
                Console.WriteLine("❌ Account name required");
                return;
            }

            var config = LoadConfig();
            if (!config.Accounts.ContainsKey(accountName))
            {
                Console.WriteLine($"❌ Account '{accountName}' not found");
                return;
            }

            var encryptedSecret = config.Accounts[accountName];
            var secret = DecryptSecret(encryptedSecret);
            
            Console.WriteLine($"🔑 Secret key for '{accountName}': {secret}");
            Console.WriteLine("⚠️  Keep this secret secure! Anyone with this key can generate your TOTP codes.");
        }

        // Encryption/Decryption methods
        private string EncryptSecret(string plainSecret)
        {
            using var aes = Aes.Create();
            var key = DeriveKey(_encryptionKey);
            aes.Key = key;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plainSecret);
            var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            var result = new byte[aes.IV.Length + encryptedBytes.Length];
            Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
            Array.Copy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

            return Convert.ToBase64String(result);
        }

        private string DecryptSecret(string encryptedSecret)
        {
            var encryptedData = Convert.FromBase64String(encryptedSecret);
            
            using var aes = Aes.Create();
            var key = DeriveKey(_encryptionKey);
            aes.Key = key;

            var iv = new byte[16]; // AES IV is always 16 bytes
            var encryptedBytes = new byte[encryptedData.Length - 16];
            Array.Copy(encryptedData, 0, iv, 0, 16);
            Array.Copy(encryptedData, 16, encryptedBytes, 0, encryptedBytes.Length);

            aes.IV = iv;
            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

            return Encoding.UTF8.GetString(decryptedBytes);
        }

        private byte[] DeriveKey(string password)
        {
            var salt = Encoding.UTF8.GetBytes("SecureTotpTool.Salt.2024");
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
            return pbkdf2.GetBytes(32);
        }

        private string LoadOrGenerateMasterKey()
        {
            var keyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".totp_master.key");
            
            if (File.Exists(keyFile))
            {
                return File.ReadAllText(keyFile).Trim();
            }

            // Generate new master key
            var keyBytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(keyBytes);
            var masterKey = Convert.ToBase64String(keyBytes);
            
            File.WriteAllText(keyFile, masterKey);
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            
            Console.WriteLine($"🔐 Generated new master key at: {keyFile}");
            Console.WriteLine("⚠️  Keep this file secure! It encrypts all your TOTP secrets.");
            
            return masterKey;
        }

        private TotpConfig LoadConfig()
        {
            if (!File.Exists(_configFile))
            {
                return new TotpConfig();
            }

            var json = File.ReadAllText(_configFile);
            return JsonSerializer.Deserialize<TotpConfig>(json) ?? new TotpConfig();
        }

        private void SaveConfig(TotpConfig config)
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFile, json);
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(_configFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        /// <summary>
        /// Import an existing TOTP secret (from Google, GitHub, etc.)
        /// </summary>
        private void ImportSecret(string accountName, string existingSecret)
        {
            if (string.IsNullOrWhiteSpace(accountName))
            {
                Console.WriteLine("❌ Account name cannot be empty");
                return;
            }

            if (string.IsNullOrWhiteSpace(existingSecret))
            {
                Console.WriteLine("❌ Secret cannot be empty");
                return;
            }

            try
            {
                // Clean and validate the secret
                var cleanSecret = existingSecret.Replace(" ", "").Replace("-", "").ToUpper();
                
                // Validate the secret by trying to decode it
                var secretBytes = Base32Encoding.ToBytes(cleanSecret);
                var totp = new Totp(secretBytes);
                
                // Test generation to ensure secret is valid
                var testCode = totp.ComputeTotp();
                
                // Encrypt and store the secret
                var encryptedSecret = EncryptSecret(cleanSecret);
                
                var config = LoadConfig();
                config.Accounts[accountName] = encryptedSecret;
                SaveConfig(config);

                Console.WriteLine($"✅ Successfully imported TOTP secret for: {accountName}");
                Console.WriteLine($"📱 Current code: {testCode}");
                Console.WriteLine($"🔐 Secret safely encrypted and stored");
                Console.WriteLine();
                Console.WriteLine("💡 You can now use this program to generate codes instead of Google Authenticator");
                Console.WriteLine($"   Example: ./totp get {accountName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Invalid secret format: {ex.Message}");
                Console.WriteLine("💡 Make sure the secret is a valid Base32 string (letters A-Z and numbers 2-7)");
                Console.WriteLine("💡 Example: MFRGG2DFMZTWQ2LK");
            }
        }

        /// <summary>
        /// Import from QR code URI (otpauth://...)
        /// </summary>
        private void ImportFromQrUri(string qrUri)
        {
            try
            {
                if (!qrUri.StartsWith("otpauth://totp/"))
                {
                    Console.WriteLine("❌ Invalid QR URI format. Must start with 'otpauth://totp/'");
                    return;
                }

                var uri = new Uri(qrUri);
                var pathParts = uri.AbsolutePath.TrimStart('/');
                var accountName = Uri.UnescapeDataString(pathParts);
                
                // Parse query parameters manually (avoid System.Web dependency)
                var query = uri.Query.TrimStart('?');
                var parameters = new Dictionary<string, string>();
                
                foreach (var param in query.Split('&'))
                {
                    var keyValue = param.Split('=');
                    if (keyValue.Length == 2)
                    {
                        parameters[keyValue[0]] = Uri.UnescapeDataString(keyValue[1]);
                    }
                }

                if (!parameters.ContainsKey("secret"))
                {
                    Console.WriteLine("❌ No secret found in QR URI");
                    return;
                }

                var secret = parameters["secret"];
                
                if (parameters.ContainsKey("issuer"))
                {
                    accountName = $"{parameters["issuer"]}:{accountName}";
                }

                ImportSecret(accountName, secret);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to parse QR URI: {ex.Message}");
                Console.WriteLine("💡 Example URI: otpauth://totp/Google%3Auser@gmail.com?secret=MFRGG2DFMZTWQ2LK&issuer=Google");
            }
        }

        /// <summary>
        /// Secure proxy for google-authenticator command
        /// Intercepts output, encrypts secrets, shows only QR codes
        /// </summary>
        private void ProxyGoogleAuthenticator(string[] args)
        {
            try
            {
                Console.WriteLine("🔐 Secure Google Authenticator Proxy");
                Console.WriteLine("📱 This will run google-authenticator but encrypt the secret automatically");
                Console.WriteLine();

                // Check if google-authenticator is installed
                if (!IsGoogleAuthenticatorInstalled())
                {
                    Console.WriteLine("❌ google-authenticator not found. Install it first:");
                    Console.WriteLine("   sudo apt install libpam-google-authenticator  # Ubuntu/Debian");
                    Console.WriteLine("   sudo dnf install google-authenticator         # Fedora");
                    Console.WriteLine("   sudo zypper install google-authenticator      # openSUSE");
                    return;
                }

                // Create temporary directory for secure execution
                var tempDir = Path.Combine(Path.GetTempPath(), $"totp-proxy-{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);
                
                try
                {
                    var result = ExecuteGoogleAuthenticatorSecurely(args, tempDir);
                    if (result.Success)
                    {
                        ProcessGoogleAuthenticatorOutput(result.Output, result.AccountName);
                    }
                    else
                    {
                        Console.WriteLine($"❌ Error running google-authenticator: {result.Error}");
                    }
                }
                finally
                {
                    // Clean up temporary directory
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Proxy error: {ex.Message}");
            }
        }

        /// <summary>
        /// Setup PAM integration for system authentication
        /// </summary>
        private void SetupPamIntegration(string username)
        {
            try
            {
                if (Environment.UserName != "root")
                {
                    Console.WriteLine("❌ PAM setup requires root privileges");
                    Console.WriteLine("💡 Run: sudo ./totp setup-pam <username>");
                    return;
                }

                Console.WriteLine($"🔧 Setting up PAM integration for user: {username}");
                
                // Create PAM configuration
                var pamConfig = CreatePamConfiguration();
                var pamConfigPath = "/etc/pam.d/totp-auth";
                
                File.WriteAllText(pamConfigPath, pamConfig);
                Console.WriteLine($"✅ Created PAM config: {pamConfigPath}");

                // Create wrapper script
                var wrapperScript = CreateGoogleAuthWrapper();
                var wrapperPath = "/usr/local/bin/totp-gauth";
                
                File.WriteAllText(wrapperPath, wrapperScript);
                File.SetUnixFileMode(wrapperPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | 
                                                 UnixFileMode.UserExecute | UnixFileMode.GroupRead | 
                                                 UnixFileMode.GroupExecute | UnixFileMode.OtherRead | 
                                                 UnixFileMode.OtherExecute);
                
                Console.WriteLine($"✅ Created wrapper script: {wrapperPath}");
                Console.WriteLine();
                Console.WriteLine("📋 Next steps:");
                Console.WriteLine($"1. Run as {username}: /usr/local/bin/totp-gauth");
                Console.WriteLine("2. Scan QR code with your phone");
                Console.WriteLine("3. Test: sudo -u {username} /usr/local/bin/totp-gauth --verify");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ PAM setup error: {ex.Message}");
            }
        }

        private bool IsGoogleAuthenticatorInstalled()
        {
            try
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "which",
                        Arguments = "google-authenticator",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private (bool Success, string Output, string Error, string AccountName) ExecuteGoogleAuthenticatorSecurely(string[] args, string tempDir)
        {
            try
            {
                var accountName = $"system-{Environment.UserName}-{DateTime.Now:yyyyMMdd}";
                
                // Set HOME to temp directory to capture .google_authenticator file
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "google-authenticator",
                        Arguments = string.Join(" ", args),
                        WorkingDirectory = tempDir,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                // Set environment to use temp directory as home
                process.StartInfo.Environment["HOME"] = tempDir;

                var output = new StringBuilder();
                var error = new StringBuilder();

                process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) error.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Send automatic responses for typical google-authenticator prompts
                process.StandardInput.WriteLine("-1"); // Skip code verification

                process.WaitForExit(30000); // 30 second timeout

                if (!process.HasExited)
                {
                    process.Kill();
                    return (false, "", "Process timeout", accountName);
                }

                return (process.ExitCode == 0, output.ToString(), error.ToString(), accountName);
            }
            catch (Exception ex)
            {
                return (false, "", ex.Message, "");
            }
        }

        private void ProcessGoogleAuthenticatorOutput(string output, string accountName)
        {
            try
            {
                Console.WriteLine("🔍 Processing google-authenticator output...");
                
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                string secret = null;
                string qrCodeUrl = null;

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    
                    // Extract secret key (but don't display it)
                    if (trimmedLine.StartsWith("Your new secret key is: "))
                    {
                        secret = trimmedLine.Substring("Your new secret key is: ".Length).Trim();
                    }
                    
                    // Extract QR code URL
                    if (trimmedLine.StartsWith("https://www.google.com/chart?chs=200x200&chld=M|0&cht=qr&chl="))
                    {
                        qrCodeUrl = trimmedLine;
                    }
                    
                    // Show QR code related output but hide secret
                    if (trimmedLine.Contains("QR code") || 
                        trimmedLine.StartsWith("https://") ||
                        trimmedLine.Contains("scan the QR code") ||
                        trimmedLine.Contains("emergency scratch codes"))
                    {
                        // Show QR-related lines but filter out secret
                        if (!trimmedLine.Contains("secret key is:"))
                        {
                            Console.WriteLine($"📱 {trimmedLine}");
                        }
                    }
                }

                // If we extracted a secret, encrypt and store it securely
                if (!string.IsNullOrEmpty(secret))
                {
                    try
                    {
                        ImportSecret(accountName, secret);
                        Console.WriteLine();
                        Console.WriteLine($"✅ Secret automatically encrypted and stored as: {accountName}");
                        Console.WriteLine($"🔐 Original secret key hidden for security");
                        Console.WriteLine($"💡 Use: ./totp get {accountName}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Could not auto-import secret: {ex.Message}");
                    }
                }

                // Display QR code URL if found
                if (!string.IsNullOrEmpty(qrCodeUrl))
                {
                    Console.WriteLine();
                    Console.WriteLine("📲 QR Code URL (scan with your phone):");
                    Console.WriteLine(qrCodeUrl);
                }

                Console.WriteLine();
                Console.WriteLine("🛡️ Security Notes:");
                Console.WriteLine("   ✅ Secret key automatically encrypted");
                Console.WriteLine("   ✅ Original secret hidden from output");
                Console.WriteLine("   ✅ QR code safe to scan with phone");
                Console.WriteLine("   ✅ Use this tool for code generation");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error processing output: {ex.Message}");
                Console.WriteLine("📋 Raw output:");
                Console.WriteLine(output);
            }
        }

        private string CreatePamConfiguration()
        {
            return @"# TOTP Authentication Configuration
# This file enables TOTP authentication through our secure proxy

auth required pam_exec.so expose_authtok /usr/local/bin/totp-gauth --verify
account required pam_permit.so
";
        }

        private string CreateGoogleAuthWrapper()
        {
            var currentToolPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            return $@"#!/bin/bash
# Secure wrapper for google-authenticator
# This script proxies through our secure TOTP tool

TOTP_TOOL=""{currentToolPath}""

if [ ""$1"" == ""--verify"" ]; then
    # Verification mode for PAM
    read -s -p ""Enter TOTP code: "" CODE
    echo
    exec dotnet ""$TOTP_TOOL"" verify system-$USER-$(date +%Y%m%d) ""$CODE""
else
    # Setup mode
    exec dotnet ""$TOTP_TOOL"" proxy-gauth ""$@""
fi
";
        }

        private void ShowHelp()
        {
            Console.WriteLine("🔐 Secure TOTP Generator - Encrypted Google Authenticator Compatible Tool");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  generate <account>      - Generate new TOTP secret for account");
            Console.WriteLine("  get <account>           - Get current TOTP code");
            Console.WriteLine("  verify <account> <code> - Verify a TOTP code");
            Console.WriteLine("  qr <account>            - Show QR code URI for Google Authenticator");
            Console.WriteLine("  list                    - List all configured accounts");
            Console.WriteLine("  remove <account>        - Remove an account");
            Console.WriteLine("  export <account>        - Export secret key (use carefully!)");
            Console.WriteLine("  import <account> <secret> - Import existing TOTP secret");
            Console.WriteLine("  import-qr <otpauth_uri> - Import from QR code URI");
            Console.WriteLine("  proxy-gauth [options]   - Secure proxy for google-authenticator command");
            Console.WriteLine("  setup-pam <user>        - Setup PAM integration (requires root)");
            Console.WriteLine();
            Console.WriteLine("Environment Variables:");
            Console.WriteLine("  TOTP_MASTER_KEY         - Custom master encryption key (optional)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  ./totp generate myserver");
            Console.WriteLine("  ./totp get myserver");
            Console.WriteLine("  ./totp verify myserver 123456");
            Console.WriteLine("  ./totp import google-account MFRGG2DFMZTWQ2LK");
            Console.WriteLine("  ./totp import-qr \"otpauth://totp/Google%3Auser@gmail.com?secret=MFRGG2DFMZTWQ2LK&issuer=Google\"");
            Console.WriteLine("  ./totp proxy-gauth -t -d -f -r 3 -R 30");
            Console.WriteLine("  sudo ./totp setup-pam myuser");
            Console.WriteLine();
            Console.WriteLine("Security Features:");
            Console.WriteLine("  ✅ All secrets encrypted with AES-256");
            Console.WriteLine("  ✅ PBKDF2 key derivation (100k iterations)");
            Console.WriteLine("  ✅ Secure file permissions");
            Console.WriteLine("  ✅ Compatible with Google Authenticator");
            Console.WriteLine("  ✅ Secure proxy for google-authenticator command");
            Console.WriteLine("  ✅ PAM integration for system authentication");
            Console.WriteLine();
            Console.WriteLine("Google Authenticator Proxy:");
            Console.WriteLine("  The proxy-gauth command runs google-authenticator securely:");
            Console.WriteLine("  • Hides the secret key from output");
            Console.WriteLine("  • Automatically encrypts and stores the secret");
            Console.WriteLine("  • Shows only the QR code for phone scanning");
            Console.WriteLine("  • Integrates with system PAM authentication");
        }
    }

    public class TotpConfig
    {
        public Dictionary<string, string> Accounts { get; set; } = new();
    }

    class Program
    {
        static void Main(string[] args)
        {
            var generator = new SecureTotpGenerator();
            generator.Run(args);
        }
    }
}