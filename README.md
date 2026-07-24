# 🛡️ SafeRelease

**The Ultimate AI-Powered Content Risk Engine for Enterprise**

SafeRelease is an autonomous, multi-modal risk analysis platform designed to scan text, images, audio, video, and documents for brand risk, copyright violations, compliance issues, and safety concerns *before* public release. 

Built exclusively on **IBM Cloud**, SafeRelease features a highly resilient, enterprise-grade architecture. It utilizes a C# ASP.NET Core API backend powered by classic IBM Watson services, alongside a Python FastAPI sidecar powered by IBM's next-generation **watsonx.ai** Granite models. 

**The SafeRelease Guarantee:** Every single IBM service is backed by an automated, open-source fallback layer. If a cloud service is unreachable, the system automatically degrades gracefully to local, open-source AI models, ensuring the platform *never* goes down.

---

## 🏗️ The Architecture

SafeRelease is built using a modern, containerized microservices architecture hosted on **IBM Code Engine**, with persistent storage backed by **IBM Cloud Databases for PostgreSQL**.

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

---

## 🧠 IBM Services Ecosystem

SafeRelease maximizes the IBM Cloud ecosystem to deliver unparalleled content analysis. We integrate **twelve distinct IBM technologies** to analyze every possible vector of a media file and host the platform.

### 🔹 Watsonx.ai Generative Models (The Sidecar)
1. **Granite Vision 3.2 2B:** Analyzes images and video frames to detect NSFW content, violence, PII, and unauthorized brand logos.
2. **Granite 13B Chat v2:** The primary LLM risk analyzer that parses extracted text against strict corporate compliance rules.
3. **Granite Guardian 3 8B:** Acts as a second-opinion classifier to flag social bias, profanity, and sexual content.

### 🔹 Classic Watson Services (The C# API)
4. **Watson Natural Language Understanding (NLU):** Analyzes sentiment and extracts entities from text.
5. **Watson NLU Emotion:** Zero-extra-API-call tone detection (Anger, Disgust, Fear) applied directly to scanned text.
6. **Watson Speech to Text (STT):** Deep-listens to audio files and extracted video audio tracks, transcribing spoken words for risk analysis.
7. **Watson Text to Speech (TTS):** Synthesizes auditory risk reports, allowing the platform to speak its findings out loud.
8. **Watson Language Translator:** Automatically detects non-English content and translates it to English *before* scanning to ensure global compliance.
9. **Watson Assistant:** Integrated as a Web Chat widget to explain complex scan results in plain English to non-technical end-users.

### 🔹 IBM Cloud Infrastructure
10. **IBM Cloud Databases for PostgreSQL:** Provides persistent, relational storage for historical scan reports and analytics.
11. **IBM Cloud Object Storage (COS):** Provides an immutable audit trail by securely archiving the original media files tied to each scan ID.
12. **IBM Code Engine & Container Registry:** Serverless, scale-to-zero container hosting for the entire Dockerized stack, with the images hosted directly on IBM Cloud.

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
Open `.env` and fill in your IBM Cloud API keys. (All required IBM services offer a perpetual Free/Lite tier).

### 3. Build and Run
Start the entire stack (PostgreSQL, Python Sidecar, and C# API) with a single command:
```bash
docker-compose up --build -d
```

### 4. Access the Platform
Once the containers are running, simply open your browser and navigate to:
**[http://localhost:5258](http://localhost:5258)**

---

## 📚 How to Use SafeRelease

1. **Upload Content:** Drag and drop any `.txt`, `.jpg`, `.mp4`, or `.wav` file into the SafeRelease dashboard.
2. **Automated Extraction:** SafeRelease will automatically extract frames from video, transcribe audio, and read text from images using OS-level FFmpeg bindings.
3. **Deep Analysis:** The content is routed through the IBM Watsonx and classic Watson APIs simultaneously to gauge sentiment, brand risk, and visual safety.
4. **Review Report:** Within seconds, you will receive a comprehensive Risk Report scoring the content from 0 (Low Risk) to 100 (Critical Risk), along with specific, actionable recommendations.

---

Engineered from the ground up by **Mohammed Ayaan Adil Ahmed** & **Areeba Iqbal** for the 2026 IBM AI Builder's Challenge. 

*(Assisted by IBM Bob for autonomous bug resolution and system hardening).*
