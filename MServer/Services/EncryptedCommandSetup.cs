using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MServer.Services;

namespace MServer.Services
{
    public class EncryptedCommandSetup
    {
        private readonly EncryptedCommandService _encryptedCommandService;

        public EncryptedCommandSetup(EncryptedCommandService encryptedCommandService)
        {
            _encryptedCommandService = encryptedCommandService;
        }

        public async Task SetupGoogleAuthenticatorCommand()
        {
            var googleAuthCommand = new EncryptedCommandDefinition
            {
                CommandId = "google-authenticator-setup",
                Description = "Secure Google Authenticator setup with encrypted secret handling",
                Executor = "/bin/bash",
                ScriptContent = @"#!/bin/bash
set -euo pipefail

# Parameters: {0} = account_name
ACCOUNT_NAME=""{0}""
TEMP_DIR=""/tmp/gauth-secure-$RANDOM-$$""

# Validate inputs
if [ -z ""$ACCOUNT_NAME"" ]; then
    echo ""ERROR:Account name required"" >&2
    exit 1
fi

# Create secure temporary directory
mkdir -p ""$TEMP_DIR""
chmod 700 ""$TEMP_DIR""

# Cleanup function
cleanup() {
    rm -rf ""$TEMP_DIR"" 2>/dev/null || true
}
trap cleanup EXIT

# Validate google-authenticator exists and is executable
if ! [ -x ""/usr/bin/google-authenticator"" ]; then
    echo ""ERROR:google-authenticator not found or not executable"" >&2
    exit 1
fi

# Change to secure directory
cd ""$TEMP_DIR""
export HOME=""$TEMP_DIR""

# Execute google-authenticator with controlled input
# Responses: time-based=y, update_file=n, disallow_reuse=y, increase_window=n, rate_limit=y, skip_verification=-1
OUTPUT=$(echo -e ""y\nn\ny\nn\ny\n-1"" | /usr/bin/google-authenticator -t -d -f -r 3 -R 30 2>&1)
EXIT_CODE=$?

if [ $EXIT_CODE -ne 0 ]; then
    echo ""ERROR:google-authenticator failed with exit code $EXIT_CODE"" >&2
    exit $EXIT_CODE
fi

# Parse output securely
SECRET=""""
QR_URL=""""

while IFS= read -r line; do
    if [[ ""$line"" =~ ^Your\ new\ secret\ key\ is:\ (.+)$ ]]; then
        SECRET=""${BASH_REMATCH[1]}""
    elif [[ ""$line"" =~ ^https://www\.google\.com/chart.* ]]; then
        QR_URL=""$line""
    fi
done <<< ""$OUTPUT""

# Validate we got the required data
if [ -z ""$SECRET"" ]; then
    echo ""ERROR:Could not extract secret from google-authenticator output"" >&2
    exit 1
fi

if [ -z ""$QR_URL"" ]; then
    echo ""ERROR:Could not extract QR URL from google-authenticator output"" >&2
    exit 1
fi

# Output structured data for C# service to parse
echo ""SECRET:$SECRET""
echo ""QR_URL:$QR_URL""
echo ""ACCOUNT:$ACCOUNT_NAME""
echo ""SUCCESS:true""

# Clear sensitive variables
SECRET=""$(printf '%*s' ${#SECRET} '' | tr ' ' '0')""
unset SECRET
",
                WorkingDirectory = "/tmp",
                EnvironmentVariables = new Dictionary<string, string>
                {
                    ["PATH"] = "/usr/local/bin:/usr/bin:/bin",
                    ["HOME"] = "/tmp",
                    ["SHELL"] = "/bin/bash"
                },
                RequiredParameters = new[] { "account_name" }
            };

            await _encryptedCommandService.CreateEncryptedCommand("google-auth-setup", googleAuthCommand);
        }

        public async Task SetupTotpCodeGenerationCommand()
        {
            var totpGenerateCommand = new EncryptedCommandDefinition
            {
                CommandId = "totp-generate-code",
                Description = "Generate TOTP code from encrypted secret",
                Executor = "/bin/bash",
                ScriptContent = @"#!/bin/bash
set -euo pipefail

# Parameters: {0} = encrypted_secret_base64
ENCRYPTED_SECRET=""{0}""

# This would normally decrypt the secret and generate TOTP
# For security, we'll delegate this to the C# TOTP service instead
echo ""DELEGATE_TO_CSHARP:$ENCRYPTED_SECRET""
",
                WorkingDirectory = "/tmp",
                RequiredParameters = new[] { "encrypted_secret" }
            };

            await _encryptedCommandService.CreateEncryptedCommand("totp-generate", totpGenerateCommand);
        }

        public async Task SetupSystemAuthenticationCommand()
        {
            var systemAuthCommand = new EncryptedCommandDefinition
            {
                CommandId = "system-auth-setup",
                Description = "Setup PAM integration for system authentication",
                Executor = "/bin/bash",
                ScriptContent = @"#!/bin/bash
set -euo pipefail

# Parameters: {0} = username, {1} = encrypted_secret
USERNAME=""{0}""
ENCRYPTED_SECRET=""{1}""

# Validate root privileges
if [ ""$EUID"" -ne 0 ]; then
    echo ""ERROR:Root privileges required for PAM setup"" >&2
    exit 1
fi

# Validate username
if ! id ""$USERNAME"" &>/dev/null; then
    echo ""ERROR:User $USERNAME does not exist"" >&2
    exit 1
fi

# Create PAM configuration
PAM_CONFIG=""/etc/pam.d/totp-auth-$USERNAME""
cat > ""$PAM_CONFIG"" << 'EOF'
# TOTP Authentication for specific user
auth required pam_exec.so expose_authtok /usr/local/bin/totp-verify
account required pam_permit.so
EOF

# Create verification script
VERIFY_SCRIPT=""/usr/local/bin/totp-verify""
cat > ""$VERIFY_SCRIPT"" << 'EOF'
#!/bin/bash
# TOTP verification script
# This script is called by PAM for authentication

# Read the TOTP code from PAM
read -r TOTP_CODE

# Here you would verify the TOTP code against the encrypted secret
# For now, we output the verification request
echo ""VERIFY:$TOTP_CODE:$USERNAME"" >> /var/log/totp-auth.log
exit 0
EOF

chmod +x ""$VERIFY_SCRIPT""

echo ""SUCCESS:PAM configuration created for user $USERNAME""
echo ""CONFIG_FILE:$PAM_CONFIG""
echo ""VERIFY_SCRIPT:$VERIFY_SCRIPT""
",
                WorkingDirectory = "/tmp",
                EnvironmentVariables = new Dictionary<string, string>
                {
                    ["PATH"] = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"
                },
                RequiredParameters = new[] { "username", "encrypted_secret" }
            };

            await _encryptedCommandService.CreateEncryptedCommand("system-auth-setup", systemAuthCommand);
        }
    }
}