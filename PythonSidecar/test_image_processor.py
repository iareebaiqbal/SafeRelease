import pytest
from unittest.mock import patch, MagicMock
from image_processor import process_image

@patch('image_processor.os.getenv')
@patch('image_processor.easyocr')
def test_process_image_easyocr_success(mock_easyocr, mock_getenv):
    mock_getenv.return_value = None # Force fallback to easyocr
    
    mock_reader_instance = MagicMock()
    mock_easyocr.Reader.return_value = mock_reader_instance
    mock_reader_instance.readtext.return_value = ["Hello", "World"]
    
    result = process_image("dummy.jpg")
    
    assert result == "Hello World"
    mock_easyocr.Reader.assert_called_once_with(['en'], gpu=False)
    mock_reader_instance.readtext.assert_called_once_with("dummy.jpg", detail=0)

@patch('image_processor.os.getenv')
def test_process_image_error_handling(mock_getenv):
    mock_getenv.return_value = None # Force fallback to easyocr
    
    # We won't patch easyocr here, but since it's not installed in the test environment (unless pip installed), 
    # it might throw ModuleNotFoundError or similar, which should be caught.
    # Alternatively, we patch it to explicitly throw an exception
    with patch('image_processor.easyocr') as mock_easyocr:
        mock_easyocr.Reader.side_effect = Exception("Failed to load models")
        result = process_image("dummy.jpg")
        
        assert "Error extracting text from image" in result
        assert "Failed to load models" in result
