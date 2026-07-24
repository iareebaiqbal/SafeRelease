# SafeRelease

> **AI-powered content risk scanner** — scan text, images, audio, video, and documents for brand risk, copyright violations, compliance issues, and safety concerns before public release.

Built on **IBM Cloud** with a dual-architecture: a .NET 10 C# API backed by IBM Watson services, and a Python FastAPI sidecar powered by IBM watsonx.ai Granite models. Every IBM service has an open-source fallback so the system never goes completely dark.

---

## IBM Services Integrated

| # | Service | Plan | Free Quota | Role in SafeRelease |
|---|---------|------|-----------|---------------------|
| 1 | **Watson Natural Language Understanding** | Lite | 30,000 NLU items/mo | Primary text risk engine — sentiment, entities, keywords |
| 2 | **Watson NLU Emotion** | Lite | Included in NLU quota | Anger / disgust / fear tone detection on scanned text (zero extra API calls) |
| 3 | **Watson Speech to Text** | Lite | 500 minutes/mo | Transcribes audio and video audio tracks for risk analysis |
| 4 | **Watson Text to Speech** | Lite | 10,000 characters/mo | Synthesises audio output from text content |
| 5 | **Watson Language Translator** | Lite | 1,000,000 characters/mo | Auto-detects non-English content and translates to English before scanning |
| 6 | **Watson Assistant** | Lite | 1,000 MAU/mo | Web Chat widget — explains scan results in plain English to non-technical users |
| 7 | **watsonx.ai — Granite 13B Chat v2** | Trial | ~25,000 tokens/mo | Primary LLM risk analyser in Python sidecar (via ContextForge prompt) |
| 8 | **watsonx.ai — Granite Vision 3.2 2B** | Trial | Shared token budget | Image content analysis — detects NSFW, violence, brand logos, PII in images |
| 9 | **watsonx.ai — Granite Guardian 3 8B** | Trial | Shared token budget | Second-opinion harm classifier — social bias, profanity, violence, sexual content |
| 10 | **IBM Cloud Object Storage** | Lite | 25 GB storage + 25 GB egress/mo | Audit trail — archives original scanned files tied to each scan ID |
| 11 | **IBM Code Engine** | Free | 100,000 vCPU-s + 200,000 GB-s/mo | Hosts both containers (C# API + Python sidecar) |
| 12 | **IBM Container Registry** | Free | 0.5 GB image storage | Stores Docker images for deployment |

> All IBM services use the **Lite / free tier**. No credit card required to start.

---

## Architecture

```
Browser (index.html)
        │
        ▼
┌─────────────────────────────────────────┐
│  C# ASP.NET API  (port 5258)            │
│  ┌─────────────────────────────────┐    │
│  │ RiskEngineService               │◄───┼── Watson NLU (sentiment + emotion + entities)
│  │  + TranslatorService            │◄───┼── Watson Language Translator
│  │ SpeechToTextService             │◄───┼── Watson Speech to Text
│  │ TextToSpeechService             │◄───┼── Watson Text to Speech
│  │ ImageDetectionService           │◄───┼── watsonx.ai Granite Vision 3.2
│  │ CosService                      │◄───┼── IBM Cloud Object Storage
│  └─────────────────────────────────┘    │
│               │ (sidecar call)           │
└───────────────┼─────────────────────────┘
                ▼
┌─────────────────────────────────────────┐
│  Python FastAPI Sidecar  (port 8000)    │
│  ┌─────────────────────────────────┐    │
│  │ llm_client.py                   │◄───┼── watsonx.ai Granite 13B Chat
│  │  + Granite Guardian 3           │◄───┼── watsonx.ai Granite Guardian 3
│  │ image_processor.py              │◄───┼── watsonx.ai Granite Vision (EasyOCR fallback)
│  │ voice_processor.py              │◄───┼── Watson STT (Whisper fallback)
│  │ video_processor.py              │◄───┼── Granite Vision + Watson STT
│  │ document_parser.py              │    │   Docling
│  └─────────────────────────────────┘    │
└─────────────────────────────────────────┘
                ▼
        PostgreSQL (persistent scan history)
```

**Fallback chain:** Every IBM service call is wrapped in a try/catch. If IBM is unavailable, the system falls back to open-source alternatives (EasyOCR, Whisper, Groq/Llama3) so scans always complete.

---

## Quick Start (local)

### Prerequisites
- Docker + Docker Compose
- `.env` file (copy `.env.example` and fill in your IBM API keys)

```bash
# 1. Clone and configure
cp .env.example .env
# Edit .env with your IBM API keys

# 2. Start all services (PostgreSQL + Python sidecar + C# API)
docker-compose up --build

# 3. Open the UI
open http://localhost:5258
```

### IBM Cloud setup (free)
1. Create a free account at [cloud.ibm.com](https://cloud.ibm.com) — no credit card required
2. Provision each service from the catalog (all Lite plan):
   - Natural Language Understanding
   - Speech to Text
   - Text to Speech
   - Language Translator
   - Object Storage (enable HMAC credentials)
   - Watson Assistant (optional)
3. Open [watsonx.ai](https://dataplatform.cloud.ibm.com/wx/home) → create a project → copy the Project ID
4. Fill all values into your `.env` file

---

## Environment Variables

See [`.env.example`](.env.example) for the full annotated list. Key variables:

| Variable | Service | Required |
|----------|---------|----------|
| `DB_CONNECTION_STRING` | PostgreSQL | ✅ |
| `IBM_CLOUD_APIKEY` | All IBM services | ✅ |
| `IBM_PROJECT_ID` | watsonx.ai Granite models | ✅ |
| `WATSON_API_KEY` + `WATSON_URL` | Watson NLU + Emotion | ✅ |
| `STT_API_KEY` + `STT_URL` | Watson Speech to Text | ✅ |
| `TTS_API_KEY` + `TTS_URL` | Watson Text to Speech | ✅ |
| `WATSON_STT_API_KEY` + `WATSON_STT_URL` | Watson STT (Python sidecar) | ✅ |
| `TRANSLATOR_API_KEY` + `TRANSLATOR_URL` | Watson Language Translator | Optional |
| `COS_ENDPOINT` + `COS_BUCKET` + `COS_ACCESS_KEY_ID` + `COS_SECRET_ACCESS_KEY` | IBM Cloud Object Storage | Optional |
| `WATSON_ASSISTANT_INTEGRATION_ID` + `WATSON_ASSISTANT_REGION` + `WATSON_ASSISTANT_SERVICE_INSTANCE_ID` | Watson Assistant | Optional |
| `GROQ_API_KEY` | Groq (LLM fallback) | Optional |
| `GEMINI_API_KEY` + `GEMINI_URL` | Google Gemini Vision (image fallback) | Optional |

---

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/scan/scan` | Scan plain text |
| `POST` | `/api/scan/scan-media` | Scan image, video, or audio file |
| `POST` | `/api/scan/scan-file` | Scan document (PDF, DOCX, etc.) via sidecar |
| `GET`  | `/api/scan/report/{id}` | Retrieve a stored scan result by ID |

---

## Scan Response Shape

```json
{
  "id": 42,
  "riskScore": 65,
  "status": "High Risk",
  "issues": [
    "Trademark reference: IBM — verify usage rights",
    "Watson NLU Emotion: High anger/hostility tone detected (score: 0.83)",
    "Watson NLU: Negative sentiment detected in content",
    "Social bias / discrimination detected (Granite Guardian)"
  ],
  "recommendation": "Do not publish. Serious violations detected. Consult legal team.",
  "auditFileUrl": "https://s3.us-south.cloud-object-storage.appdomain.cloud/saferelease-scans/scans/42/file.jpg"
}
```

---

## Project Structure

```
SafeRelease/
├── Controllers/
│   └── ScanController.cs          # REST endpoints + fallback architecture
├── Services/
│   ├── RiskEngineService.cs        # Watson NLU + Emotion + keyword rules
│   ├── TranslatorService.cs        # Watson Language Translator (NEW)
│   ├── CosService.cs               # IBM Cloud Object Storage (NEW)
│   ├── Services/
│   │   ├── ImageDetectionService.cs  # Granite Vision (thread-safe)
│   │   ├── SpeechToTextService.cs    # Watson STT
│   │   └── TextToSpeechService.cs    # Watson TTS
├── Models/
│   ├── ScanRequest.cs
│   ├── ScanResponse.cs
│   └── ScanResult.cs
├── Data/
│   └── AppDbContext.cs             # EF Core + PostgreSQL
├── PythonSidecar/
│   ├── main.py                     # FastAPI entry point (safe temp files)
│   ├── llm_client.py               # Granite 13B + Guardian 3 + Groq fallback
│   ├── image_processor.py          # Granite Vision → EasyOCR
│   ├── voice_processor.py          # Watson STT → Whisper
│   ├── video_processor.py          # Frame OCR + audio transcription
│   ├── document_parser.py          # Docling
│   └── context_builder.py          # IBM Granite prompt builder
├── wwwroot/
│   └── index.html                  # Frontend UI + Watson Assistant widget
├── Dockerfile                      # .NET 9 + ffmpeg
├── PythonSidecar/Dockerfile        # Python 3.11 + ffmpeg
├── docker-compose.yml              # All 3 services + postgres volume
└── .env.example                    # All environment variables documented
```

---

## Version

`6.1` — All 9 known bugs fixed. 4 new IBM services integrated.
