import requests

response = requests.post(
    "https://localhost:5001/api/auth/send",
    json={"phone": "+523321890176"},
    headers={"Origin": "http://localhost:3000"},
    verify=False  # Skip SSL verification for testing
)

print(response.status_code, response.text)