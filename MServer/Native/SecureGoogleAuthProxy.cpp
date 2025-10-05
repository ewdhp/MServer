#include <iostream>
#include <string>
#include <vector>
#include <cstdlib>
#include <unistd.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <fcntl.h>
#include <openssl/evp.h>
#include <openssl/aes.h>
#include <openssl/rand.h>
#include <pwd.h>
#include <grp.h>

class SecureGoogleAuthProxy {
private:
    static constexpr const char* ALLOWED_EXECUTABLE = "/usr/bin/google-authenticator";
    static constexpr const char* SERVICE_USER = "totp-service";
    static constexpr int MAX_OUTPUT_SIZE = 4096;
    
    std::string encryptionKey;
    
public:
    SecureGoogleAuthProxy() {
        // Initialize encryption key from secure source
        loadEncryptionKey();
    }
    
    // Main execution function - only callable by authorized processes
    int executeSecureGoogleAuth(const std::string& accountName, std::string& qrCodeUrl) {
        // 1. Validate caller permissions
        if (!validateCaller()) {
            std::cerr << "Unauthorized access attempt" << std::endl;
            return -1;
        }
        
        // 2. Validate executable path
        if (access(ALLOWED_EXECUTABLE, X_OK) != 0) {
            std::cerr << "google-authenticator not accessible" << std::endl;
            return -1;
        }
        
        // 3. Create secure execution environment
        int pipefd[2];
        if (pipe(pipefd) == -1) {
            perror("pipe");
            return -1;
        }
        
        pid_t pid = fork();
        if (pid == -1) {
            perror("fork");
            close(pipefd[0]);
            close(pipefd[1]);
            return -1;
        }
        
        if (pid == 0) {
            // Child process - execute google-authenticator
            close(pipefd[0]); // Close read end
            
            // Redirect output to pipe
            dup2(pipefd[1], STDOUT_FILENO);
            dup2(pipefd[1], STDERR_FILENO);
            close(pipefd[1]);
            
            // Drop privileges if running as root
            dropPrivileges();
            
            // Execute with restricted environment
            char* const args[] = {
                const_cast<char*>("google-authenticator"),
                const_cast<char*>("-t"), // time-based
                const_cast<char*>("-d"), // disallow reuse
                const_cast<char*>("-f"), // force
                const_cast<char*>("-r"), const_cast<char*>("3"), // rate limit
                const_cast<char*>("-R"), const_cast<char*>("30"), // window
                nullptr
            };
            
            execv(ALLOWED_EXECUTABLE, args);
            perror("execv failed");
            _exit(1);
        } else {
            // Parent process - capture and process output
            close(pipefd[1]); // Close write end
            
            char buffer[MAX_OUTPUT_SIZE];
            ssize_t bytesRead = read(pipefd[0], buffer, sizeof(buffer) - 1);
            close(pipefd[0]);
            
            int status;
            waitpid(pid, &status, 0);
            
            if (bytesRead > 0) {
                buffer[bytesRead] = '\0';
                return processOutput(std::string(buffer), accountName, qrCodeUrl);
            }
            
            return -1;
        }
    }
    
private:
    bool validateCaller() {
        // Check if caller is authorized (implement your logic)
        uid_t uid = getuid();
        gid_t gid = getgid();
        
        // Only allow specific users/processes
        struct passwd* pw = getpwuid(uid);
        if (!pw) return false;
        
        // Add your authorization logic here
        // For example, check if process is from your C# server
        return true; // Placeholder
    }
    
    void dropPrivileges() {
        struct passwd* pw = getpwnam(SERVICE_USER);
        if (pw) {
            if (setgid(pw->pw_gid) != 0) perror("setgid");
            if (setuid(pw->pw_uid) != 0) perror("setuid");
        }
    }
    
    int processOutput(const std::string& output, const std::string& accountName, std::string& qrCodeUrl) {
        std::string secret;
        
        // Parse output to extract secret and QR URL
        size_t secretPos = output.find("Your new secret key is: ");
        if (secretPos != std::string::npos) {
            secretPos += 24; // Length of "Your new secret key is: "
            size_t endPos = output.find('\n', secretPos);
            if (endPos != std::string::npos) {
                secret = output.substr(secretPos, endPos - secretPos);
            }
        }
        
        size_t qrPos = output.find("https://www.google.com/chart");
        if (qrPos != std::string::npos) {
            size_t endPos = output.find('\n', qrPos);
            if (endPos != std::string::npos) {
                qrCodeUrl = output.substr(qrPos, endPos - qrPos);
            }
        }
        
        // Encrypt and store secret securely
        if (!secret.empty()) {
            std::string encryptedSecret = encryptSecret(secret);
            storeEncryptedSecret(accountName, encryptedSecret);
            
            // Clear secret from memory
            std::fill(secret.begin(), secret.end(), '\0');
        }
        
        return 0;
    }
    
    std::string encryptSecret(const std::string& plaintext) {
        // Implement AES encryption
        EVP_CIPHER_CTX* ctx = EVP_CIPHER_CTX_new();
        if (!ctx) return "";
        
        unsigned char iv[16];
        if (RAND_bytes(iv, sizeof(iv)) != 1) {
            EVP_CIPHER_CTX_free(ctx);
            return "";
        }
        
        if (EVP_EncryptInit_ex(ctx, EVP_aes_256_cbc(), nullptr, 
                              reinterpret_cast<const unsigned char*>(encryptionKey.c_str()), iv) != 1) {
            EVP_CIPHER_CTX_free(ctx);
            return "";
        }
        
        std::vector<unsigned char> ciphertext(plaintext.length() + AES_BLOCK_SIZE);
        int len;
        int ciphertext_len;
        
        if (EVP_EncryptUpdate(ctx, ciphertext.data(), &len, 
                             reinterpret_cast<const unsigned char*>(plaintext.c_str()), 
                             plaintext.length()) != 1) {
            EVP_CIPHER_CTX_free(ctx);
            return "";
        }
        ciphertext_len = len;
        
        if (EVP_EncryptFinal_ex(ctx, ciphertext.data() + len, &len) != 1) {
            EVP_CIPHER_CTX_free(ctx);
            return "";
        }
        ciphertext_len += len;
        
        EVP_CIPHER_CTX_free(ctx);
        
        // Combine IV + ciphertext and encode
        std::string result;
        result.append(reinterpret_cast<char*>(iv), sizeof(iv));
        result.append(reinterpret_cast<char*>(ciphertext.data()), ciphertext_len);
        
        return base64Encode(result);
    }
    
    void storeEncryptedSecret(const std::string& accountName, const std::string& encryptedSecret) {
        // Store in secure location (implement your storage)
        std::string filename = "/var/lib/totp-service/" + accountName + ".enc";
        
        FILE* file = fopen(filename.c_str(), "wb");
        if (file) {
            fwrite(encryptedSecret.c_str(), 1, encryptedSecret.length(), file);
            fclose(file);
            chmod(filename.c_str(), 0600); // Restrict permissions
        }
    }
    
    void loadEncryptionKey() {
        // Load master key from secure location
        const char* keyFile = "/etc/totp-service/master.key";
        FILE* file = fopen(keyFile, "rb");
        if (file) {
            char buffer[32];
            if (fread(buffer, 1, 32, file) == 32) {
                encryptionKey.assign(buffer, 32);
            }
            fclose(file);
        }
    }
    
    std::string base64Encode(const std::string& input) {
        // Implement base64 encoding
        // (Using OpenSSL's BIO functions or custom implementation)
        return input; // Placeholder
    }
};

// Service main function
int main(int argc, char* argv[]) {
    if (argc != 2) {
        std::cerr << "Usage: " << argv[0] << " <account_name>" << std::endl;
        return 1;
    }
    
    SecureGoogleAuthProxy proxy;
    std::string qrCodeUrl;
    
    int result = proxy.executeSecureGoogleAuth(argv[1], qrCodeUrl);
    
    if (result == 0 && !qrCodeUrl.empty()) {
        // Return only QR code URL, secret is encrypted and stored
        std::cout << "QR_CODE_URL:" << qrCodeUrl << std::endl;
        return 0;
    }
    
    return 1;
}