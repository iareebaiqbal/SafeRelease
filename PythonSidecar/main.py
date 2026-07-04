from fastapi import FastAPI, UploadFile, File, Form
import uvicorn
import os
from document_parser import parse_document
from context_builder import build_context
from llm_client import analyze_risk
from image_processor import process_image

app = FastAPI(title="SafeRelease Python Sidecar")

@app.post("/api/parse")
async def parse_and_build_context(file: UploadFile = File(...)):
    # Save uploaded file to temp path
    file_path = f"temp_{file.filename}"
    with open(file_path, "wb") as f:
        f.write(await file.read())
        
    file_ext = os.path.splitext(file.filename)[1].lower()
    
    # Process Images
    if file_ext in ['.png', '.jpg', '.jpeg', '.bmp']:
        extracted_text = process_image(file_path)
        markdown_content = f"Extracted Text from Image:\n\n{extracted_text}"
        if os.path.exists(file_path):
            os.remove(file_path)
    # Process Documents
    else:
        markdown_content = parse_document(file_path)
    
    # Build context using ContextForge structure
    context = build_context(markdown_content)
    
    # Call IBM Watsonx / Groq to analyze risk
    analysis = analyze_risk(context)
    
    return {"status": "success", "context": context, "analysis": analysis}

if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=8000)
