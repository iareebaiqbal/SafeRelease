import os
import base64
import tempfile
from image_processor import process_image
from voice_processor import process_voice

def process_video(file_path: str) -> str:
    """
    Analyses a video file for content risk.

    Pipeline:
      Track 1 – Visual:  Extract frames every 5s → IBM Granite Vision OCR (EasyOCR fallback)
      Track 2 – Audio:   Extract audio → IBM Watson STT (Whisper fallback)

    Returns a single combined text blob for the LLM risk analyser.
    """

    frame_texts  = []
    audio_transcript = ""
    temp_audio_path  = None

    # ── Track 1: Frame extraction & OCR ──────────────────────────────────────
    try:
        import cv2

        cap = cv2.VideoCapture(file_path)
        fps = cap.get(cv2.CAP_PROP_FPS) or 25
        frame_interval = int(fps * 5)          # sample 1 frame every 5 seconds
        frame_index    = 0
        sampled        = 0
        max_frames     = 10                    # cap at 10 frames to avoid huge payloads

        while cap.isOpened() and sampled < max_frames:
            ret, frame = cap.read()
            if not ret:
                break
            if frame_index % frame_interval == 0:
                tmp = tempfile.NamedTemporaryFile(suffix=".jpg", delete=False)
                cv2.imwrite(tmp.name, frame)
                tmp.close()
                text = process_image(tmp.name)   # IBM Granite Vision → EasyOCR
                os.unlink(tmp.name)
                if text and "Error" not in text:
                    frame_texts.append(f"[Frame {sampled+1}] {text}")
                sampled += 1
            frame_index += 1

        cap.release()
        print(f"Video frame OCR: extracted text from {sampled} frames.")
    except Exception as e:
        print(f"Video frame extraction failed: {e}")

    # ── Track 2: Audio extraction & transcription ─────────────────────────────
    try:
        from moviepy import VideoFileClip

        clip = VideoFileClip(file_path)
        if clip.audio is not None:
            tmp_audio = tempfile.NamedTemporaryFile(suffix=".wav", delete=False)
            temp_audio_path = tmp_audio.name
            tmp_audio.close()
            clip.audio.write_audiofile(temp_audio_path, logger=None)
            clip.close()

            audio_transcript = process_voice(temp_audio_path, "audio/wav")  # IBM STT → Whisper
            print("Video audio transcription: success")
        else:
            clip.close()
            print("Video has no audio track.")
    except Exception as e:
        print(f"Video audio extraction failed: {e}")
    finally:
        if temp_audio_path and os.path.exists(temp_audio_path):
            os.unlink(temp_audio_path)

    # ── Combine results ───────────────────────────────────────────────────────
    parts = []
    if frame_texts:
        parts.append("=== ON-SCREEN TEXT (from video frames) ===\n" + "\n".join(frame_texts))
    if audio_transcript:
        parts.append("=== AUDIO TRANSCRIPT ===\n" + audio_transcript)

    if not parts:
        return "No text content could be extracted from the video."

    return "\n\n".join(parts)
