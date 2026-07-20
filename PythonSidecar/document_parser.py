import os
from docling.document_converter import DocumentConverter

def parse_document(file_path: str) -> str:
    """
    Parses a document (PDF, DOCX, etc.) using Docling and returns Markdown.
    """
    try:
        converter = DocumentConverter()
        result = converter.convert(file_path)
        markdown_text = result.document.export_to_markdown()
        
        # Clean up temp file
        if os.path.exists(file_path):
            os.remove(file_path)
            
        return markdown_text
    except Exception as e:
        if os.path.exists(file_path):
            os.remove(file_path)
        return f"Error parsing document: {str(e)}"
