def build_context(parsed_content: str, media_type: str = "document") -> str:
    """
    IBM-aligned ContextForge-style prompt builder.

    Assembles the extracted content together with enterprise brand compliance
    rules and governance guidelines into a structured LLM prompt payload,
    following IBM Granite chat formatting conventions.

    Args:
        parsed_content: Extracted text/transcript from the document, image,
                        audio, or video being evaluated.
        media_type:     One of 'document', 'image', 'voice', 'video'.
                        Adjusts the prompt framing accordingly.
    """

    media_descriptions = {
        "document": "marketing or compliance document",
        "image":    "image (extracted on-screen text)",
        "voice":    "audio recording (transcribed speech)",
        "video":    "video file (on-screen text + audio transcript combined)",
    }
    media_label = media_descriptions.get(media_type, "content")

    compliance_rules = """
ENTERPRISE BRAND & COMPLIANCE GOVERNANCE RULES:

1. TRADEMARK & BRAND USAGE
   - Any mention of IBM, Apple, Google, Microsoft, Samsung, Nike, Sony must be flagged
   - Unauthorised use of competitor brands is a HIGH risk
   - Slogans or distinctive phrases associated with any brand require permission

2. FINANCIAL COMPLIANCE
   - Claims of "guaranteed returns", "zero risk", "risk-free investment" violate FCA/SEC regulations
   - Any financial promise without disclaimer is HIGH risk

3. DATA PRIVACY — GDPR / CCPA
   - Collection, processing, or sharing of personal data without explicit user consent = HIGH risk
   - Mentions of biometric data, location tracking, or behavioural profiling must be flagged
   - Retention policies must be stated if data collection is mentioned

4. CHILD SAFETY — COPPA
   - Any content targeting or collecting data from users under 13 is CRITICAL risk
   - Terms like "under 13", "children", "minors" in a data context must be flagged immediately

5. INTELLECTUAL PROPERTY
   - Phrases like "no license", "without formal licensing", "replicates", "copies" = IP risk
   - Music, imagery, or software described without attribution = potential DMCA violation

6. CONFIDENTIALITY
   - Words like "confidential", "internal only", "proprietary" in public-facing content = HIGH risk

7. SENTIMENT & TONE
   - Strongly negative sentiment toward a named entity = reputational risk
   - Defamatory or misleading claims about competitors are HIGH risk
"""

    prompt = f"""<|system|>
You are an enterprise content risk scanner for IBM SafeRelease.
Your task is to evaluate the provided {media_label} against the compliance rules below.
You MUST respond with ONLY a valid JSON object — no markdown, no explanation, no preamble.
<|end|>

<|user|>
{compliance_rules}

CONTENT TO EVALUATE ({media_label.upper()}):
==================================================
{parsed_content}
==================================================

Respond with a JSON object containing exactly these four keys:
- "risk_score": integer from 0 (no risk) to 100 (critical risk)
- "status": one of "Low Risk", "Medium Risk", "High Risk", "Critical Risk"
- "issues": array of strings, each describing a specific violation found (empty array if none)
- "recommendation": string with actionable guidance for the content team
<|end|>

<|assistant|>
"""

    return prompt
