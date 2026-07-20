import pytest
from context_builder import build_context

def test_build_context():
    # Arrange
    markdown_input = "# Sample Marketing Draft\nBuy our new product."
    
    # Act
    result = build_context(markdown_input)
    
    # Assert
    assert "BRAND GUIDELINES:" in result
    assert "CONTENT TO EVALUATE:" in result
    assert markdown_input in result
    assert "risk_score" in result
