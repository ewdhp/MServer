using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MServer.Services
{
    public class EncryptedCommandService
    {
        private readonly string _encryptedCommandsPath;
        private readonly string _masterKey;

        public EncryptedCommandService()
        {
            _encryptedCommandsPath = Path.Combine(AppContext.BaseDirectory, "encrypted_commands");
            _masterKey = LoadOrGenerateMasterKey();
            
            // Create encrypted commands directory
            Directory.CreateDirectory(_encryptedCommandsPath);
        }

        public async Task<string> ExecuteEncryptedCommand(string commandId, params string[] parameters)
        {
            try
            {
                // 1. Load and decrypt the command
                var encryptedCommand = await LoadEncryptedCommand(commandId);
                if (encryptedCommand == null)
                {
                    throw new InvalidOperationException($"Command {commandId} not found");
                }

                var decryptedCommand = DecryptCommand(encryptedCommand);
                
                // 2. Execute the decrypted command securely
                var result = await ExecuteDecryptedCommand(decryptedCommand, parameters);
                
                // 3. Clear decrypted command from memory immediately
                ClearFromMemory(decryptedCommand);
                
                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to execute encrypted command: {ex.Message}");
            }
        }

        public async Task<bool> CreateEncryptedCommand(string commandId, EncryptedCommandDefinition definition)
        {
            try
            {
                var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions { WriteIndented = false });
                var encryptedData = EncryptData(json);
                
                var filePath = Path.Combine(_encryptedCommandsPath, $"{commandId}.enc");
                await File.WriteAllBytesAsync(filePath, encryptedData);
                
                // Set restrictive permissions
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<byte[]?> LoadEncryptedCommand(string commandId)
        {
            var filePath = Path.Combine(_encryptedCommandsPath, $"{commandId}.enc");
            
            if (!File.Exists(filePath))
            {
                return null;
            }
            
            return await File.ReadAllBytesAsync(filePath);
        }

        private EncryptedCommandDefinition DecryptCommand(byte[] encryptedData)
        {
            var decryptedJson = DecryptData(encryptedData);
            var definition = JsonSerializer.Deserialize<EncryptedCommandDefinition>(decryptedJson);
            
            if (definition == null)
            {
                throw new InvalidOperationException("Failed to deserialize command definition");
            }
            
            return definition;
        }

        private async Task<string> ExecuteDecryptedCommand(EncryptedCommandDefinition command, string[] parameters)
        {
            // Create temporary script file
            var tempScriptPath = Path.Combine(Path.GetTempPath(), $"cmd_{Guid.NewGuid():N}.sh");
            
            try
            {
                // Replace parameters in the command
                var processedScript = ProcessCommandTemplate(command.ScriptContent, parameters);
                
                // Write to temporary file
                await File.WriteAllTextAsync(tempScriptPath, processedScript);
                
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    File.SetUnixFileMode(tempScriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }

                // Execute the script
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = command.Executor, // "/bin/bash", "/usr/bin/python3", etc.
                        Arguments = $"\"{tempScriptPath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = command.WorkingDirectory ?? Path.GetTempPath()
                    }
                };

                // Set environment variables if specified
                foreach (var env in command.EnvironmentVariables)
                {
                    process.StartInfo.Environment[env.Key] = env.Value;
                }

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Command failed with exit code {process.ExitCode}: {error}");
                }

                return output;
            }
            finally
            {
                // Secure cleanup of temporary file
                if (File.Exists(tempScriptPath))
                {
                    try
                    {
                        // Overwrite with random data before deletion
                        var fileSize = new FileInfo(tempScriptPath).Length;
                        var randomData = new byte[fileSize];
                        using (var rng = RandomNumberGenerator.Create())
                        {
                            rng.GetBytes(randomData);
                        }
                        await File.WriteAllBytesAsync(tempScriptPath, randomData);
                        File.Delete(tempScriptPath);
                    }
                    catch { /* Ignore cleanup errors */ }
                }
            }
        }

        private string ProcessCommandTemplate(string template, string[] parameters)
        {
            var result = template;
            
            // Replace parameter placeholders like {0}, {1}, etc.
            for (int i = 0; i < parameters.Length; i++)
            {
                // Escape parameters to prevent injection
                var escapedParam = EscapeShellParameter(parameters[i]);
                result = result.Replace($"{{{i}}}", escapedParam);
            }
            
            return result;
        }

        private string EscapeShellParameter(string parameter)
        {
            // Escape shell special characters to prevent injection
            return "'" + parameter.Replace("'", "'\"'\"'") + "'";
        }

        private void ClearFromMemory(EncryptedCommandDefinition command)
        {
            // Clear sensitive data from memory (safe version)
            if (command.ScriptContent != null)
            {
                // Create new string filled with zeros to overwrite memory
                command.ScriptContent = new string('\0', command.ScriptContent.Length);
            }
        }

        private byte[] EncryptData(string plaintext)
        {
            using var aes = Aes.Create();
            var key = DeriveKey(_masterKey);
            aes.Key = key;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var encryptedBytes = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

            var result = new byte[aes.IV.Length + encryptedBytes.Length];
            Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
            Array.Copy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

            return result;
        }

        private string DecryptData(byte[] encryptedData)
        {
            using var aes = Aes.Create();
            var key = DeriveKey(_masterKey);
            aes.Key = key;

            var iv = new byte[16];
            var ciphertext = new byte[encryptedData.Length - 16];
            Array.Copy(encryptedData, 0, iv, 0, 16);
            Array.Copy(encryptedData, 16, ciphertext, 0, ciphertext.Length);

            aes.IV = iv;
            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

            return Encoding.UTF8.GetString(decryptedBytes);
        }

        private byte[] DeriveKey(string password)
        {
            var salt = Encoding.UTF8.GetBytes("EncryptedCommandService.Salt.2024");
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
            return pbkdf2.GetBytes(32);
        }

        private string LoadOrGenerateMasterKey()
        {
            var keyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cmd_master.key");
            
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
            
            return masterKey;
        }
    }

    public class EncryptedCommandDefinition
    {
        public string CommandId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Executor { get; set; } = "/bin/bash"; // or /usr/bin/python3, etc.
        public string ScriptContent { get; set; } = string.Empty;
        public string? WorkingDirectory { get; set; }
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
        public string[] RequiredParameters { get; set; } = Array.Empty<string>();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}