def build_context(parsed_markdown: str) -> str:
    """
    Simulates the IBM ContextForge context building process.
    Assembles the parsed markdown with brand guidelines and compliance rules
    to create an optimized prompt for the Risk Engine LLM.
    """
    
    brand_guidelines = """
    BRAND GUIDELINES:
    1. Do not guarantee returns or claim "zero risk".
    2. Protect IBM, Apple, and Google trademarks.
    3. No unauthorized competitor bashing.
    4. Data privacy rules (GDPR, COPPA) must be strictly followed.
    """
    
    context = f"""
    You are an enterprise content risk scanner.
    Evaluate the following content against the provided Brand Guidelines.
    
    {brand_guidelines}
    
    CONTENT TO EVALUATE:
    ====================
    {parsed_markdown}
    ====================
    
    Provide your analysis as a JSON object containing:
    - risk_score (0-100)
    - status ("Low Risk", "Medium Risk", "High Risk")
    - issues (list of strings)
    - recommendation (string)
    """
    
    return context
