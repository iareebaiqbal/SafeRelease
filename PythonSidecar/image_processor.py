import os

def process_image(file_path: str) -> str:
    """
    Extracts text from an image using IBM Visual Recognition.
    Falls back to EasyOCR if IBM keys are missing or API fails.
    """
    
    ibm_api_key = os.getenv("IBM_CLOUD_APIKEY")
    
    # Attempt IBM Visual Recognition First
    if ibm_api_key:
        try:
            # Placeholder for IBM Watson Visual Recognition OCR call
            # from ibm_watson import VisualRecognitionV3
            # visual_recognition = VisualRecognitionV3('2018-03-19', authenticator=...)
            # with open(file_path, 'rb') as images_file:
            #     text_results = visual_recognition.recognize_text(images_file).get_result()
            # return extract_text(text_results)
            pass
        except Exception as e:
            print(f"IBM Visual Recognition failed: {e}. Falling back to EasyOCR.")
            
    # Fallback to EasyOCR
    try:
        import easyocr
        # Initialize reader (will download weights on first run)
        reader = easyocr.Reader(['en'], gpu=False) 
        result = reader.readtext(file_path, detail=0)
        extracted_text = " ".join(result)
        return extracted_text
    except Exception as e:
        return f"Error extracting text from image: {str(e)}"
