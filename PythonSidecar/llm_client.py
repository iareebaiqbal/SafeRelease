import os
import json
from groq import Groq

# In a real scenario, you'd import IBM Watson SDKs here:
# from ibm_watson_machine_learning.foundation_models import Model
# from ibm_watson_machine_learning.metanames import GenTextParamsMetaNames as GenParams

def analyze_risk(context: str) -> dict:
    """
    Calls IBM watsonx.ai (Granite) with the ContextForge payload.
    Falls back to Groq (Llama3/Mixtral) if IBM keys are missing or API fails.
    """
    
    ibm_api_key = os.getenv("IBM_CLOUD_APIKEY")
    ibm_project_id = os.getenv("IBM_PROJECT_ID")
    
    # Attempt IBM Watsonx.ai First
    if ibm_api_key and ibm_project_id:
        try:
            # Placeholder for actual IBM Watsonx.ai call
            # model = Model("ibm/granite-13b-chat-v2", credentials={"apikey": ibm_api_key}, project_id=ibm_project_id, ...)
            # response = model.generate_text(context)
            # return parse_llm_response(response)
            pass
        except Exception as e:
            print(f"IBM Watsonx.ai failed: {e}. Falling back to Groq.")
            
    # Fallback to Groq
    groq_api_key = os.getenv("GROQ_API_KEY")
    if not groq_api_key:
        return _mock_response("API keys missing. Could not reach IBM Watson or Groq.")
        
    try:
        client = Groq(api_key=groq_api_key)
        chat_completion = client.chat.completions.create(
            messages=[
                {
                    "role": "system",
                    "content": "You are a JSON-only API that outputs risk analysis."
                },
                {
                    "role": "user",
                    "content": context,
                }
            ],
            model="llama3-8b-8192",
            response_format={"type": "json_object"}
        )
        
        response_text = chat_completion.choices[0].message.content
        return json.loads(response_text)
    except Exception as e:
        return _mock_response(f"Groq API failed: {str(e)}")

def _mock_response(error_message: str) -> dict:
    return {
        "risk_score": 100,
        "status": "High Risk",
        "issues": [error_message],
        "recommendation": "System error. Please configure API keys."
    }
