import os
import json
from groq import Groq

# Import IBM Watsonx SDK
from ibm_watsonx_ai.foundation_models import ModelInference
from ibm_watsonx_ai.metanames import GenTextParamsMetaNames as GenParams
from ibm_watsonx_ai import Credentials

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
            credentials = Credentials(
                url="https://us-south.ml.cloud.ibm.com", # Default US South endpoint
                api_key=ibm_api_key
            )
            
            parameters = {
                GenParams.DECODING_METHOD: "greedy",
                GenParams.MAX_NEW_TOKENS: 1024,
                GenParams.STOP_SEQUENCES: ["\n\n"]
            }
            
            model = ModelInference(
                model_id="ibm/granite-13b-chat-v2",
                credentials=credentials,
                project_id=ibm_project_id,
                params=parameters
            )
            
            response = model.generate_text(context)
            
            # Attempt to extract JSON from the response in case of conversational wrapper
            start = response.find("{")
            end = response.rfind("}") + 1
            if start != -1 and end != 0:
                json_str = response[start:end]
                return json.loads(json_str)
            else:
                return json.loads(response)
                
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
            model="llama-3.1-8b-instant",
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
