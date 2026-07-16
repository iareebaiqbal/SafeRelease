import os
import base64
from ibm_watsonx_ai.foundation_models import ModelInference
from ibm_watsonx_ai import Credentials

def process_image(file_path: str) -> str:
    """
    Extracts text from an image.
    Primary:  IBM Granite Vision via watsonx.ai (ibm/granite-vision-3-2-2b)
    Fallback: EasyOCR (local, no API key needed)
    """

    ibm_api_key   = os.getenv("IBM_CLOUD_APIKEY")
    ibm_project_id = os.getenv("IBM_PROJECT_ID")

    # ── Primary: IBM Granite Vision ──────────────────────────────────────────
    if ibm_api_key and ibm_project_id:
        try:
            with open(file_path, "rb") as img_file:
                image_b64 = base64.b64encode(img_file.read()).decode("utf-8")

            credentials = Credentials(
                url="https://us-south.ml.cloud.ibm.com",
                api_key=ibm_api_key
            )
            model = ModelInference(
                model_id="ibm/granite-vision-3-2-2b",
                credentials=credentials,
                project_id=ibm_project_id,
                params={"max_new_tokens": 1024}
            )
            # Granite Vision: send base64 image with extraction prompt
            prompt = (
                "Extract all visible text from this image exactly as it appears. "
                "Return only the extracted text, no commentary."
            )
            response = model.generate(
                prompt=prompt,
                params={"image_data": [image_b64]}
            )
            extracted = response.get("results", [{}])[0].get("generated_text", "").strip()
            if extracted:
                print("IBM Granite Vision OCR: success")
                return extracted
            # If empty response, fall through
            print("IBM Granite Vision returned empty text. Falling back to EasyOCR.")
        except Exception as e:
            print(f"IBM Granite Vision failed: {e}. Falling back to EasyOCR.")

    # ── Fallback: EasyOCR (local) ────────────────────────────────────────────
    try:
        import easyocr
        reader = easyocr.Reader(['en'], gpu=False)
        result = reader.readtext(file_path, detail=0)
        extracted_text = " ".join(result)
        print("EasyOCR fallback: success")
        return extracted_text if extracted_text else "No text detected in image."
    except Exception as e:
        return f"Error extracting text from image: {str(e)}"
