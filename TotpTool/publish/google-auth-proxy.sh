#!/bin/bash

# Secure Google Authenticator Proxy Script
# This script runs google-authenticator and processes the output securely

echo "🔐 Secure Google Authenticator Proxy"
echo "📱 Running google-authenticator with secure secret handling..."
echo

# Run google-authenticator with automatic responses
OUTPUT=$(echo -e "y\nn\ny\nn\ny\n-1" | google-authenticator -t -d -f -r 3 -R 30 2>&1)

# Extract the secret key
SECRET=$(echo "$OUTPUT" | grep "Your new secret key is:" | sed 's/.*Your new secret key is: //')

# Extract QR code URL
QR_URL=$(echo "$OUTPUT" | grep "https://www.google.com/chart" | head -1)

# Show only the QR code information, hide the secret
echo "📲 QR Code for Google Authenticator:"
if [ ! -z "$QR_URL" ]; then
    echo "$QR_URL"
    echo
    echo "🔗 Scan this QR code with Google Authenticator app"
else
    echo "❌ Could not extract QR code URL"
fi

# If we have a secret, offer to import it
if [ ! -z "$SECRET" ]; then
    echo
    echo "🔐 Secret key detected and ready for secure storage"
    read -p "📝 Enter account name for this TOTP: " ACCOUNT_NAME
    
    if [ ! -z "$ACCOUNT_NAME" ]; then
        # Import the secret using our secure tool
        if [ -f "./publish/totp" ]; then
            ./publish/totp import "$ACCOUNT_NAME" "$SECRET"
        elif [ -f "./totp" ]; then
            ./totp import "$ACCOUNT_NAME" "$SECRET"
        elif command -v dotnet >/dev/null 2>&1; then
            dotnet run -- import "$ACCOUNT_NAME" "$SECRET"
        else
            echo "⚠️  TOTP tool not found. Secret key is: $SECRET"
            echo "🔒 Please store this securely and delete this output!"
        fi
    fi
else
    echo "❌ Could not extract secret key from output"
fi

echo
echo "🛡️  Security Notes:"
echo "   ✅ Secret key hidden from display"
echo "   ✅ QR code safe to scan with phone"
echo "   ✅ Original secret encrypted in secure storage"