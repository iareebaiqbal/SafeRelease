import os
import json
from groq import Groq

# Import IBM Watsonx SDK
from ibm_watsonx_ai.foundation_models import ModelInference
from ibm_watsonx_ai.metanames import GenTextParamsMetaNames as GenParams
from ibm_watsonx_ai import Credentials


def _get_credentials() -> tuple[str | None, str | None]:
    return os.getenv("IBM_CLOUD_APIKEY"), os.getenv("IBM_PROJECT_ID")


def _granite_credentials(ibm_api_key: str, ibm_project_id: str) -> ModelInference:
    credentials = Credentials(
        url="https://us-south.ml.cloud.ibm.com",
        api_key=ibm_api_key
    )
    # FIXED: removed STOP_SEQUENCES=["\n\n"] — it truncated JSON responses that
    # contained blank lines inside the "recommendation" string field.
    parameters = {
        GenParams.DECODING_METHOD: "greedy",
        GenParams.MAX_NEW_TOKENS: 1024,
    }
    return ModelInference(
        model_id="ibm/granite-13b-chat-v2",
        credentials=credentials,
        project_id=ibm_project_id,
        params=parameters
    )


def _run_granite_guardian(content_text: str, ibm_api_key: str, ibm_project_id: str) -> list[str]:
    """
    NEW: IBM Granite Guardian 3 — dedicated harm classifier.
    Returns a list of harm flags detected (empty list = safe).
    Uses the same IBM_CLOUD_APIKEY + IBM_PROJECT_ID — no extra keys needed.
    """
    harm_categories = [
        ("social_bias",    "Social bias / discrimination detected (Granite Guardian)"),
        ("profanity",      "Profanity / offensive language detected (Granite Guardian)"),
        ("violence",       "Violence-related content detected (Granite Guardian)"),
        ("sexual_content", "Sexual content detected (Granite Guardian)"),
        ("unethical",      "Unethical behaviour reference detected (Granite Guardian)"),
    ]

    flags = []
    try:
        credentials = Credentials(
            url="https://us-south.ml.cloud.ibm.com",
            api_key=ibm_api_key
        )
        guardian = ModelInference(
            model_id="ibm/granite-guardian-3-8b",
            credentials=credentials,
            project_id=ibm_project_id,
            params={GenParams.DECODING_METHOD: "greedy", GenParams.MAX_NEW_TOKENS: 20}
        )

        for harm_key, harm_label in harm_categories:
            # Guardian prompt format: system message sets the harm category,
            # user message contains the content to evaluate.
            # Model responds "Yes" (unsafe) or "No" (safe).
            messages = [
                {
                    "role": "system",
                    "content": (
                        f"You are a safety classifier. Does the following content contain {harm_key.replace('_', ' ')}? "
                        "Answer only Yes or No."
                    )
                },
                {"role": "user", "content": content_text[:2000]}  # cap to avoid token overflow
            ]
            result = guardian.chat(messages=messages)
            answer = result.get("choices", [{}])[0].get("message", {}).get("content", "").strip().lower()
            if answer.startswith("yes"):
                flags.append(harm_label)

    except Exception as e:
        print(f"Granite Guardian check failed: {e}")

    return flags


def analyze_risk(context: str) -> dict:
    """
    Calls IBM watsonx.ai (Granite 13B) with the ContextForge payload.
    Also runs Granite Guardian 3 as a second-opinion harm classifier.
    Falls back to Groq (Llama3) if IBM keys are missing or API fails.
    """

    ibm_api_key, ibm_project_id = _get_credentials()

    # Attempt IBM Watsonx.ai First
    if ibm_api_key and ibm_project_id:
        try:
            model = _granite_credentials(ibm_api_key, ibm_project_id)
            response = model.generate_text(context)

            # Extract JSON from the response (strip any conversational wrapper)
            start = response.find("{")
            end = response.rfind("}") + 1
            if start != -1 and end != 0:
                json_str = response[start:end]
                result = json.loads(json_str)
            else:
                result = json.loads(response)

            # NEW: run Granite Guardian as a second-opinion harm classifier
            # Extract the plain content from the context prompt for Guardian
            content_start = context.find("CONTENT TO EVALUATE")
            plain_content = context[content_start:content_start + 3000] if content_start != -1 else context[:3000]
            guardian_flags = _run_granite_guardian(plain_content, ibm_api_key, ibm_project_id)
            if guardian_flags:
                result.setdefault("issues", [])
                result["issues"].extend(guardian_flags)
                # Bump score if Guardian found something Granite Chat missed
                result["risk_score"] = min(result.get("risk_score", 0) + 10 * len(guardian_flags), 100)

            return result

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
                {"role": "system", "content": "You are a JSON-only API that outputs risk analysis."},
                {"role": "user",   "content": context}
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
