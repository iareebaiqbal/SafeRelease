import os
import tempfile
from ibm_watson import SpeechToTextV1
from ibm_cloud_sdk_core.authenticators import IAMAuthenticator

def process_voice(file_path: str, content_type: str = None) -> str:
    """
    Transcribes an audio file to text.
    Primary:  IBM Watson Speech to Text
    Fallback: OpenAI Whisper (local, no API key needed)
    """

    stt_api_key = os.getenv("WATSON_STT_API_KEY")
    stt_url     = os.getenv("WATSON_STT_URL")

    # Detect MIME type from extension if not provided
    ext = os.path.splitext(file_path)[1].lower()
    mime_map = {
        ".wav":  "audio/wav",
        ".mp3":  "audio/mp3",
        ".m4a":  "audio/mp4",
        ".ogg":  "audio/ogg",
        ".webm": "audio/webm",
        ".flac": "audio/flac",
    }
    audio_mime = content_type or mime_map.get(ext, "audio/wav")

    # ── Primary: IBM Watson Speech to Text ───────────────────────────────────
    if stt_api_key and stt_url:
        try:
            authenticator = IAMAuthenticator(stt_api_key)
            stt = SpeechToTextV1(authenticator=authenticator)
            stt.set_service_url(stt_url)

            with open(file_path, "rb") as audio_file:
                result = stt.recognize(
                    audio=audio_file,
                    content_type=audio_mime,
                    model="en-US_Multimedia",          # IBM next-gen model
                    smart_formatting=True,
                ).get_result()

            transcripts = [
                alt["transcript"]
                for r in result.get("results", [])
                for alt in r.get("alternatives", [])
            ]
            transcript = " ".join(transcripts).strip()
            if transcript:
                print("IBM Watson STT: transcription success")
                return transcript
            print("IBM Watson STT returned empty transcript. Falling back to Whisper.")
        except Exception as e:
            print(f"IBM Watson Speech to Text failed: {e}. Falling back to Whisper.")

    # ── Fallback: OpenAI Whisper (local) ─────────────────────────────────────
    try:
        import whisper
        model = whisper.load_model("base")
        result = model.transcribe(file_path)
        transcript = result.get("text", "").strip()
        print("Whisper fallback: transcription success")
        return transcript if transcript else "No speech detected in audio."
    except Exception as e:
        return f"Error transcribing audio: {str(e)}"
