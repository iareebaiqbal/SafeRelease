import pytest
from unittest.mock import patch, MagicMock
from llm_client import analyze_risk, _mock_response
import os

@patch('llm_client.os.getenv')
@patch('llm_client.Groq')
def test_analyze_risk_groq_success(mock_groq_class, mock_getenv):
    # Setup mocks
    def getenv_side_effect(key):
        if key == "GROQ_API_KEY":
            return "test_groq_key"
        return None
    mock_getenv.side_effect = getenv_side_effect
    
    mock_client = MagicMock()
    mock_groq_class.return_value = mock_client
    
    mock_choice = MagicMock()
    mock_choice.message.content = '{"risk_score": 10, "status": "Low Risk", "issues": [], "recommendation": "All good"}'
    mock_client.chat.completions.create.return_value = MagicMock(choices=[mock_choice])
    
    result = analyze_risk("Check this text")
    
    assert result["risk_score"] == 10
    assert result["status"] == "Low Risk"
    assert len(result["issues"]) == 0

@patch('llm_client.os.getenv')
def test_analyze_risk_no_keys(mock_getenv):
    mock_getenv.return_value = None
    
    result = analyze_risk("Check this text")
    
    assert result["risk_score"] == 100
    assert result["status"] == "High Risk"
    assert "API keys missing" in result["issues"][0]
