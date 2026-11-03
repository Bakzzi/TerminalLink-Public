"""Exercise validation from the actual remote script without an SSH connection."""
import ast
from pathlib import Path
import re

root = Path(__file__).resolve().parents[2]
text = (root / "host/RemoteImages.cs").read_text(encoding="utf-8")
script = text.split('internal const string FileScript="""', 1)[1].split('""";', 1)[0]
tree = ast.parse(script)
validation = ast.Module(body=[n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == "photos"], type_ignores=[])
scope = {"re": re}
exec(compile(validation, "actual-remote-validation", "exec"), scope)
def accepts(items):
    scope["r"] = {"photos": items}
    try:
        scope["photos"]()
        return True
    except ValueError:
        return False
def item(name, size, image=True):
    return dict(name=name, size=size, sha256="A" * 64, image=image)
assert accepts([item("0.png", 4), item("1.file.pdf", 33554432, False)])
assert accepts([item("0.file.txt", 0, False)])
assert accepts([dict(name="0.png", size=1, sha256="A" * 64)])
assert not accepts([item("../0.file.pdf", 1, False)])
assert not accepts([item("0.file.pdf", 33554433, False)])
assert not accepts([item("0.png", 0)])
assert not accepts([item("0.file.pdf\n", 1, False)])
assert not accepts([item("0.file.pdf", 1, False)] * 5)
print("PASS 8 actual remote-script validation cases; no remote filesystem or live transfer performed.")
