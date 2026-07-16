import os
import sys
import json
import urllib.request
import base64

# Load .env
env_path = os.path.join(os.path.dirname(__file__), '.env')
if os.path.exists(env_path):
    with open(env_path, 'r') as f:
        for line in f:
            line = line.strip()
            if line and not line.startswith('#') and '=' in line:
                key, val = line.split('=', 1)
                os.environ[key] = val.strip('"\'')

print("=== Testing Watson NLU (C# Backend dependency) ===")
watson_api_key = os.getenv('WATSON_API_KEY')
watson_url = os.getenv('WATSON_URL')

if not watson_api_key or not watson_url:
    print("WARNING: WATSON_API_KEY or WATSON_URL is missing in .env")
else:
    try:
        endpoint = f"{watson_url}/v1/analyze?version=2022-04-07"
        payload = json.dumps({
            "text": "This is a test.",
            "features": {"sentiment": {}}
        }).encode('utf-8')
        
        req = urllib.request.Request(endpoint, data=payload, method="POST")
        req.add_header('Content-Type', 'application/json')
        
        auth_string = f"apikey:{watson_api_key}"
        auth_bytes = auth_string.encode('ascii')
        base64_bytes = base64.b64encode(auth_bytes).decode('ascii')
        req.add_header('Authorization', f'Basic {base64_bytes}')
        
        with urllib.request.urlopen(req) as response:
            if response.status == 200:
                print("SUCCESS: Watson NLU keys are valid!")
            else:
                print(f"FAILED: Watson NLU returned status {response.status}")
    except Exception as e:
        print(f"FAILED: Watson NLU Error: {e}")

print("\n=== Testing Watsonx.ai Granite (Python Sidecar) ===")
ibm_cloud_apikey = os.getenv('IBM_CLOUD_APIKEY')
ibm_project_id = os.getenv('IBM_PROJECT_ID')

if not ibm_cloud_apikey or not ibm_project_id:
    print("WARNING: IBM_CLOUD_APIKEY or IBM_PROJECT_ID is missing in .env")
else:
    try:
        # We'll test this via the actual SDK by importing the module
        sys.path.append(os.path.join(os.path.dirname(__file__), 'PythonSidecar'))
        from llm_client import analyze_risk
        
        res = analyze_risk("Evaluate this simple string.")
        
        if "System error" not in str(res) and "API keys missing" not in str(res):
            print("SUCCESS: Watsonx.ai Granite keys are valid and the model responded!")
            print(f"Sample response: {json.dumps(res, indent=2)[:200]}...")
        else:
            print(f"FAILED: Fallback occurred or error: {res}")
    except Exception as e:
        print(f"FAILED: Watsonx.ai Error: {e}")
