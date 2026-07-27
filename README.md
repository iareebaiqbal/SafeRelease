# 🛡️ SafeRelease

**The Ultimate AI-Powered Content Risk Engine for Enterprise**

SafeRelease is an autonomous, multi-modal risk analysis platform designed to scan text, images, audio, video, and documents for brand risk, copyright violations, compliance issues, and safety concerns *before* public release. 

Built exclusively on **IBM Cloud**, SafeRelease features a highly resilient, enterprise-grade architecture. It utilizes a C# ASP.NET Core API backend powered by classic IBM Watson services, alongside a Python FastAPI sidecar powered by IBM's next-generation **watsonx.ai** Granite models. 

**The SafeRelease Guarantee:** Every single IBM service is backed by an automated, open-source fallback layer. If a cloud service is unreachable, the system automatically degrades gracefully to local, open-source AI models, ensuring the platform *never* goes down.

---

## 🏗️ The Architecture

SafeRelease is built using a modern, containerized microservices architecture hosted on **IBM Code Engine**, with persistent storage backed by **PostgreSQL** (local via Docker Compose, or [Neon.tech](https://neon.tech) free tier for cloud deployments).

```
Browser (index.html)
        │
        ▼
┌─────────────────────────────────────────┐
│  C# ASP.NET API  (port 5258)            │
│  ┌─────────────────────────────────┐    │
│  │ RiskEngineService               │◄───┼── Watson NLU (sentiment + emotion + entities)
│  │ SpeechToTextService             │◄───┼── Watson Speech to Text
│  │ TextToSpeechService             │◄───┼── Watson Text to Speech
│  │ ImageDetectionService           │◄───┼── watsonx.ai Granite Vision 3.2
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

---

## 🧠 IBM Services Ecosystem

SafeRelease maximizes the IBM Cloud ecosystem to deliver unparalleled content analysis. We integrate **ten distinct IBM technologies** across Watson AI, watsonx.ai Generative AI, and IBM Cloud infrastructure — every service used is on an active, free-tier plan.

### 🔹 Watsonx.ai Generative Models (The Sidecar)
1. **Granite Vision 3.2 2B:** Analyzes images and video frames to detect NSFW content, violence, PII, and unauthorized brand logos.
2. **Granite 13B Chat v2:** The primary LLM risk analyzer that parses extracted text against strict corporate compliance rules.
3. **Granite Guardian 3 8B:** Acts as a second-opinion classifier to flag social bias, profanity, and sexual content.

### 🔹 Classic Watson Services (The C# API)
4. **Watson Natural Language Understanding (NLU):** Analyzes sentiment and extracts entities from text. Free tier: 30,000 items/month.
5. **Watson NLU Emotion:** Zero-extra-API-call tone detection (Anger, Disgust, Fear) — included in the NLU Lite plan at no extra cost.
6. **Watson Speech to Text (STT):** Deep-listens to audio files and extracted video audio tracks, transcribing spoken words for risk analysis. Free tier: 500 min/month.
7. **Watson Text to Speech (TTS):** Synthesizes auditory risk reports, allowing the platform to speak its findings out loud. Free tier: 10,000 chars/month.
8. **Watson Assistant:** Integrated as a Web Chat widget to explain complex scan results in plain English to non-technical end-users. Free tier: 1,000 MAU/month.

### 🔹 IBM Cloud Infrastructure
9. **IBM Code Engine:** Serverless, scale-to-zero container hosting for the entire Dockerized stack. Free tier: 100,000 vCPU-seconds + 200,000 GB-seconds/month.
10. **IBM Container Registry:** Private Docker image registry used to store and deploy the SafeRelease container images. Free tier: 0.5 GB storage.

> **Note on PostgreSQL:** IBM Cloud Databases for PostgreSQL has no free tier. For local development, PostgreSQL runs as a Docker Compose service. For free cloud hosting, use [Neon.tech](https://neon.tech) (free 512 MB).

---

## 🛡️ The "Unbreakable" Fallback Matrix

SafeRelease was built to survive network outages, expired API keys, and rate limits without ever dropping a user request.

| Media Type | Primary IBM Engine | First Fallback (Open Source) | Second Fallback (Third-Party) |
|------------|--------------------|------------------------------|-------------------------------|
| **Images** | Granite Vision (watsonx.ai) | EasyOCR (Local Python) | Google Gemini API (C#) |
| **Audio**  | Watson Speech to Text | OpenAI Whisper (Local) | N/A |
| **Text**   | Granite 13B (watsonx.ai) | Llama 3 (via Groq API) | Basic Filename heuristics |

---

## 🚀 Setup & Installation (Local Development)

SafeRelease is fully dockerized. You do not need `.NET`, Python, or `FFmpeg` installed on your host machine to run this project.

### 1. Prerequisites
- Docker Desktop (or Docker Engine)
- Docker Compose

### 2. Environment Configuration
Clone the repository and copy the example environment file:
```bash
git clone https://github.com/iareebaiqbal/SafeRelease.git
cd SafeRelease
cp .env.example .env
```
Open `.env` and fill in your IBM Cloud API keys. All required IBM services offer a perpetual Free/Lite tier.

### 3. Build and Run
Start the entire stack (PostgreSQL, Python Sidecar, and C# API) with a single command:
```bash
docker-compose up --build -d
```

### 4. Access the Platform
Once the containers are running, simply open your browser and navigate to:
**[http://localhost:5258](http://localhost:5258)**

---

## ☁️ Deployment — IBM Cloud Code Engine

Deploy the full stack to IBM Cloud using Code Engine (serverless containers) and IBM Container Registry.

### Prerequisites
- [IBM Cloud CLI](https://cloud.ibm.com/docs/cli) installed and logged in: `ibmcloud login --sso`
- Container Registry and Code Engine plugins: `ibmcloud plugin install container-registry code-engine`

### 1. Tag and Push Images to IBM Container Registry

```bash
# Log in to IBM Container Registry
ibmcloud cr login

# Create a namespace (one-time)
ibmcloud cr namespace-add saferelease

# Build and push the C# API image
docker build -t us.icr.io/saferelease/saferelease-api:latest .
docker push us.icr.io/saferelease/saferelease-api:latest

# Build and push the Python sidecar image
docker build -t us.icr.io/saferelease/python-sidecar:latest ./PythonSidecar
docker push us.icr.io/saferelease/python-sidecar:latest
```

### 2. Create a Code Engine Project

```bash
ibmcloud ce project create --name saferelease
ibmcloud ce project select --name saferelease
```

### 3. Create a Registry Secret (so Code Engine can pull your images)

```bash
ibmcloud ce secret create --format registry \
  --name icr-secret \
  --server us.icr.io \
  --username iamapikey \
  --password $(ibmcloud iam oauth-tokens --output json | jq -r '.iam_token' | cut -d' ' -f2)
```

### 4. Deploy the Python Sidecar

```bash
ibmcloud ce application create \
  --name python-sidecar \
  --image us.icr.io/saferelease/python-sidecar:latest \
  --registry-secret icr-secret \
  --port 8000 \
  --min-scale 0 --max-scale 2 \
  --env GROQ_API_KEY=your_groq_api_key \
  --env IBM_CLOUD_APIKEY=your_ibm_cloud_apikey \
  --env IBM_PROJECT_ID=your_project_id \
  --env WATSON_STT_API_KEY=your_stt_api_key \
  --env WATSON_STT_URL=https://api.us-south.speech-to-text.watson.cloud.ibm.com
```

### 5. Deploy the C# API

Get the sidecar URL first: `ibmcloud ce application get --name python-sidecar --output url`

```bash
ibmcloud ce application create \
  --name saferelease-api \
  --image us.icr.io/saferelease/saferelease-api:latest \
  --registry-secret icr-secret \
  --port 5258 \
  --min-scale 0 --max-scale 2 \
  --env DB_CONNECTION_STRING="postgresql://user:pass@ep-xxx.neon.tech/saferelease?sslmode=require" \
  --env PYTHON_SIDECAR_URL=https://<your-sidecar-url>/api/parse \
  --env WATSON_API_KEY=your_nlu_api_key \
  --env WATSON_URL=https://api.us-south.natural-language-understanding.watson.cloud.ibm.com \
  --env IBM_CLOUD_APIKEY=your_ibm_cloud_apikey \
  --env IBM_PROJECT_ID=your_project_id \
  --env STT_API_KEY=your_stt_api_key \
  --env STT_URL=https://api.us-south.speech-to-text.watson.cloud.ibm.com \
  --env TTS_API_KEY=your_tts_api_key \
  --env TTS_URL=https://api.us-south.text-to-speech.watson.cloud.ibm.com \
  --env WATSON_ASSISTANT_INTEGRATION_ID=your_integration_id \
  --env WATSON_ASSISTANT_REGION=us-south \
  --env WATSON_ASSISTANT_SERVICE_INSTANCE_ID=your_instance_id
```

### 6. Get the Public URL

```bash
ibmcloud ce application get --name saferelease-api --output url
```

Open the returned URL in your browser — the full SafeRelease dashboard will be live.

> **Cost note:** Code Engine's free monthly allowance (100k vCPU-seconds, 200k GB-seconds) is sufficient for moderate demo traffic. Scale-to-zero (`--min-scale 0`) ensures no charges when idle.

---

## 📚 How to Use SafeRelease

1. **Upload Content:** Drag and drop any `.txt`, `.jpg`, `.mp4`, or `.wav` file into the SafeRelease dashboard.
2. **Automated Extraction:** SafeRelease will automatically extract frames from video, transcribe audio, and read text from images using OS-level FFmpeg bindings.
3. **Deep Analysis:** The content is routed through the IBM Watsonx and classic Watson APIs simultaneously to gauge sentiment, brand risk, and visual safety.
4. **Review Report:** Within seconds, you will receive a comprehensive Risk Report scoring the content from 0 (Low Risk) to 100 (Critical Risk), along with specific, actionable recommendations.

---

Engineered from the ground up by **Mohammed Ayaan Adil Ahmed** & **Areeba Iqbal** for the 2026 IBM AI Builder's Challenge. 

*(Assisted by IBM Bob for autonomous bug resolution and system hardening).*
