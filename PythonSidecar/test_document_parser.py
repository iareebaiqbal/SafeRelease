import pytest
import os
from unittest.mock import patch, MagicMock
from document_parser import parse_document

@patch('document_parser.DocumentConverter')
def test_parse_document_success(mock_converter_class):
    # Arrange
    mock_converter = mock_converter_class.return_value
    mock_result = MagicMock()
    mock_result.document.export_to_markdown.return_value = "# Test Document Markdown"
    mock_converter.convert.return_value = mock_result
    
    test_file = "dummy_file.pdf"
    with open(test_file, "w") as f:
        f.write("dummy content")
        
    # Act
    result = parse_document(test_file)
    
    # Assert
    assert result == "# Test Document Markdown"
    assert not os.path.exists(test_file)  # Ensures cleanup happened
