#!/bin/bash

# Create native helper for maximum security
cd /home/esdtyiti/github/ewdhp/MServer/MServer/Native

# Compile the C++ secure proxy
g++ -std=c++17 -O3 -s \
    -DNDEBUG \
    -fstack-protector-strong \
    -D_FORTIFY_SOURCE=2 \
    -Wl,-z,relro,-z,now \
    -o secure-gauth-proxy \
    SecureGoogleAuthProxy.cpp \
    -lssl -lcrypto

# Set secure permissions
sudo chown root:root secure-gauth-proxy
sudo chmod 4755 secure-gauth-proxy  # setuid for controlled privilege escalation

# Create service user
sudo useradd -r -s /bin/false totp-service

# Create secure directories
sudo mkdir -p /var/lib/totp-service
sudo mkdir -p /etc/totp-service
sudo chown totp-service:totp-service /var/lib/totp-service
sudo chmod 700 /var/lib/totp-service

# Generate master key
sudo openssl rand -base64 32 > /tmp/master.key
sudo mv /tmp/master.key /etc/totp-service/master.key
sudo chown root:totp-service /etc/totp-service/master.key
sudo chmod 640 /etc/totp-service/master.key

echo "✅ Native secure proxy compiled and configured"
echo "🔐 Use from C# via: ./secure-gauth-proxy <account_name>"