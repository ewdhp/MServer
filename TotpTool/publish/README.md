# 🔐 Secure TOTP Tool - Google Authenticator Proxy

This tool provides a **secure proxy for Google Authenticator** that encrypts secrets while showing only QR codes for phone scanning.

## 🚀 Features

### ✅ **Core TOTP Functions**
- Generate new TOTP secrets
- Import existing secrets from services
- Generate current TOTP codes
- Verify TOTP codes
- AES-256 encryption for all secrets
- Compatible with Google Authenticator

### ✅ **Google Authenticator Proxy**
- **Secure wrapper** for `google-authenticator` command
- **Hides secret keys** from terminal output
- **Shows only QR codes** for phone scanning
- **Automatically encrypts** and stores secrets
- **No plain text secrets** exposed

### ✅ **Security Features**
- AES-256 encryption with PBKDF2 key derivation
- 100,000 iteration PBKDF2 for key stretching
- Secure file permissions on Linux/macOS
- Master encryption key protection
- No hardcoded secrets or credentials

## 📋 Usage Examples

### **Basic TOTP Operations**
```bash
# Generate new secret
./totp generate myserver

# Import existing secret from service
./totp import google-account "MFRGG2DFMZTWQ2LK"

# Get current code
./totp get myserver

# Verify a code
./totp verify myserver 123456

# List all accounts
./totp list
```

### **🔐 Google Authenticator Secure Proxy**
```bash
# Method 1: Use the bash proxy script (RECOMMENDED)
./google-auth-proxy.sh

# Method 2: Use the C# proxy (advanced)
./totp proxy-gauth -t -d -f

# Method 3: Direct integration (for system setup)
sudo ./totp setup-pam username
```

### **Import from Services**
```bash
# From QR code URI
./totp import-qr "otpauth://totp/Google%3Auser@gmail.com?secret=MFRGG2DFMZTWQ2LK&issuer=Google"

# From manual secret key
./totp import github-main "MFRGG2DFMZTWQ2LK"

# From AWS, Azure, etc.
./totp import aws-root "GEZDGNBVGY3TQOJQ"
```

## 🛡️ **Security Workflow**

### **Setting Up System Authentication**
1. **Run the secure proxy:**
   ```bash
   ./google-auth-proxy.sh
   ```

2. **Enter account name when prompted:**
   ```
   📝 Enter account name for this TOTP: system-ssh
   ```

3. **Scan QR code with phone:**
   - The tool shows only the QR code URL
   - Secret key is automatically encrypted and hidden
   - Phone can scan the QR code normally

4. **Use encrypted storage:**
   ```bash
   ./totp get system-ssh  # Get codes from encrypted storage
   ```

### **What Happens Behind the Scenes:**
1. ✅ `google-authenticator` generates secret + QR code
2. ✅ Proxy **intercepts** the output
3. ✅ **Extracts** the secret key silently
4. ✅ **Encrypts** the secret with AES-256
5. ✅ **Stores** encrypted secret in local config
6. ✅ **Shows** only the QR code to user
7. ✅ **Hides** the plain text secret completely

## 📱 **Phone Setup**

### **Scanning QR Code:**
1. Open Google Authenticator app
2. Tap "Add a code" → "Scan QR code"
3. Scan the QR code URL shown by the proxy
4. Account appears in your phone app
5. **Both your phone AND encrypted tool** can generate codes

### **Manual Entry (if QR scan fails):**
If you need the secret for manual entry:
```bash
./totp export account-name  # ⚠️ Use carefully!
```

## 🔒 **Security Benefits**

### **vs. Standard Google Authenticator:**
| Feature | Standard | Secure Proxy |
|---------|----------|--------------|
| Secret Storage | Google Cloud | AES-256 Encrypted |
| Network Exposure | Possible | None |
| Backup Control | Limited | Full Control |
| Secret Visibility | Plain Text | Hidden/Encrypted |
| Offline Operation | Depends | Always |
| Vendor Lock-in | Yes | No |

### **vs. Exporting Your Secrets:**
❌ **DON'T DO:** Generate secret → Export to Google
✅ **DO THIS:** Google gives secret → Import to proxy

## 🚨 **Important Security Notes**

### **Safe Operations:**
- ✅ Using the proxy script
- ✅ Importing secrets from services
- ✅ Scanning QR codes with phone
- ✅ Getting codes from encrypted storage

### **Be Careful With:**
- ⚠️ `totp export` command - only use when necessary
- ⚠️ Manual secret sharing - always verify recipient
- ⚠️ Backup files - ensure they're encrypted

### **Never Do:**
- ❌ Share plain text secrets over insecure channels
- ❌ Store secrets in unencrypted files
- ❌ Screenshot secrets or QR codes
- ❌ Email secrets without encryption

## 🔧 **System Integration**

### **PAM Integration (Advanced):**
```bash
# Setup system authentication
sudo ./totp setup-pam username

# Test the integration  
sudo -u username /usr/local/bin/totp-gauth --verify
```

### **SSH Integration Example:**
```bash
# 1. Setup TOTP for user
./google-auth-proxy.sh
# Enter: ssh-user-john

# 2. Configure SSH to require TOTP
# Add to /etc/pam.d/sshd:
# auth required pam_exec.so expose_authtok /path/to/totp verify ssh-user-john

# 3. User logs in with password + TOTP code
```

## 📂 **File Locations**

### **Encrypted Storage:**
- `totp_accounts.json` - Encrypted TOTP secrets
- `~/.totp_master.key` - Master encryption key

### **Executables:**
- `totp` - Main executable (self-contained)
- `google-auth-proxy.sh` - Bash proxy script

### **Security:**
- Files are created with restrictive permissions (600)
- Master key is stored separately from accounts
- All secrets encrypted before storage

## 🔄 **Migration & Backup**

### **Backup Encrypted Data:**
```bash
# Backup encrypted accounts (safe to store)
cp totp_accounts.json backup/totp_accounts.backup.json

# Backup master key (store separately)
cp ~/.totp_master.key secure-location/
```

### **Restore from Backup:**
```bash
# Restore accounts
cp backup/totp_accounts.backup.json totp_accounts.json

# Restore master key
cp secure-location/.totp_master.key ~/.totp_master.key
```

### **Migration to New System:**
```bash
# Copy both files to new system
scp totp_accounts.json newserver:~/
scp ~/.totp_master.key newserver:~/.totp_master.key

# Test on new system
./totp list
./totp get account-name
```

This tool gives you **complete control** over your TOTP secrets while maintaining **full compatibility** with Google Authenticator and other standard TOTP apps!