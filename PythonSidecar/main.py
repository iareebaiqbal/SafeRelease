from fastapi import FastAPI, UploadFile, File, Form
import uvicorn
import os
import tempfile
from document_parser import parse_document
from context_builder import build_context
from llm_client import analyze_risk
from image_processor import process_image
from voice_processor import process_voice
from video_processor import process_video

app = FastAPI(title="SafeRelease Python Sidecar")

IMAGE_EXTS = {'.png', '.jpg', '.jpeg', '.bmp', '.gif', '.webp', '.tiff'}
VIDEO_EXTS = {'.mp4', '.mov', '.avi', '.webm', '.mkv', '.flv'}
AUDIO_EXTS = {'.wav', '.mp3', '.m4a', '.ogg', '.flac', '.aac'}

@app.post("/api/parse")
async def parse_and_analyze(
    file: UploadFile = File(...),
    contentType: str = Form(default=None)   # 'image', 'video', 'voice', or None
):
    """
    Single entry point for all media types.
    Routes to the correct processor based on contentType field or file extension.
    IBM services are always tried first; open-source libraries are fallbacks.
    """

    # FIXED: use tempfile.NamedTemporaryFile so each request gets a unique path
    # in the OS temp directory — prevents path traversal and concurrent-upload races.
    file_ext = os.path.splitext(file.filename)[1].lower()
    tmp = tempfile.NamedTemporaryFile(delete=False, suffix=file_ext)
    file_path = tmp.name
    tmp.close()

    with open(file_path, "wb") as f:
        f.write(await file.read())

    # Determine media_type — prefer explicit contentType from frontend
    if contentType in ("video",):
        media_type = "video"
    elif contentType in ("voice", "audio"):
        media_type = "voice"
    elif contentType in ("image",):
        media_type = "image"
    elif file_ext in VIDEO_EXTS:
        media_type = "video"
    elif file_ext in AUDIO_EXTS:
        media_type = "voice"
    elif file_ext in IMAGE_EXTS:
        media_type = "image"
    else:
        media_type = "document"

    try:
        # ── Route to the correct processor ────────────────────────────────────
        if media_type == "image":
            extracted_text = process_image(file_path)             # IBM Granite Vision → EasyOCR
            content_for_context = f"Extracted Text from Image:\n\n{extracted_text}"

        elif media_type == "voice":
            transcript = process_voice(file_path)                 # IBM Watson STT → Whisper
            content_for_context = f"Audio Transcript:\n\n{transcript}"

        elif media_type == "video":
            combined_text = process_video(file_path)              # IBM Granite Vision + IBM STT → fallbacks
            content_for_context = combined_text

        else:  # document
            content_for_context = parse_document(file_path)       # Docling
            file_path = None  # parse_document handles cleanup

        # ── Build IBM Granite-formatted context prompt ─────────────────────────
        context = build_context(content_for_context, media_type=media_type)

        # ── Analyse risk with IBM Watsonx.ai Granite → Groq fallback ──────────
        analysis = analyze_risk(context)

    finally:
        if file_path and os.path.exists(file_path):
            os.remove(file_path)

    return {
        "status": "success",
        "media_type": media_type,
        "context": context,
        "analysis": analysis
    }


if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=8000)
