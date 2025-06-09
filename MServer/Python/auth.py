import requests

# Base URL of the API
BASE_URL = "https://localhost:5001/api/testauth/verify"

# Disable SSL warnings for self-signed certificates (for testing purposes only)
import urllib3
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)


def signup_login(phone, code):
    """
    Perform a login request.

    :param phone: Phone number in E.164 format (e.g., +1234567890)
    :param code: Verification code (e.g., 123456)
    """
    payload = {
        "Phone": phone,
        "Code": code
    }

    print("\nPerforming Login or signup...")
    try:
        response = requests.post(BASE_URL, json=payload, verify=False)
        print(f"Status Code: {response.status_code}")
        print("Response JSON:")
        print(response.json())
    except requests.exceptions.RequestException as e:
        print(f"An error occurred during login: {e}")

# Test cases
if __name__ == "__main__":
    # Test phone number and verification code
    phone = "+1234567890"
    code = "123456"
    signup_login(phone, code)
    signup_login(phone, code)