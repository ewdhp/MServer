import requests
from requests.auth import HTTPBasicAuth

account_sid = "AC23f88289374bd1212027f88ec0bf0c27"
auth_token = "2269e69c15c86407bbdec1a486657b0a"

url = "https://verify.twilio.com/v2/Services/VAc6245af6c94f63ff1903cb8024c918ad/VerificationCheck"
data = {"To": "+523321890176", "Code": "751018"}

response = requests.post(url, data=data, auth=HTTPBasicAuth(account_sid, auth_token))

print(response.json())  # Check response
