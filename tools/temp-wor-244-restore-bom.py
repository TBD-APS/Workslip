from pathlib import Path

path = Path("src/FE/src/App.css")
content = path.read_bytes()
bom = b"\xef\xbb\xbf"
if not content.startswith(bom):
    path.write_bytes(bom + content)
