from pathlib import Path
import io,json,sys,zipfile
root=Path(__file__).resolve().parents[1]
items=json.loads((root/'android/build/resolved-artifacts.json').read_text(encoding='utf-8'))
blocks=['Android resolved runtime dependencies (no local paths)\n']
for item in items:
 blocks.append('\n## '+item['coordinate']+'\n')
 def collect(z):
  for entry in z.infolist():
   name=Path(entry.filename).name.upper()
   if any(name.startswith(x) for x in ['LICENSE','NOTICE','COPYING']) and entry.file_size<1024*1024:
    blocks.append(z.read(entry).decode('utf-8',errors='replace'))
   elif entry.filename=='classes.jar':
    with zipfile.ZipFile(io.BytesIO(z.read(entry))) as nested:collect(nested)
 with zipfile.ZipFile(item['path']) as archive:collect(archive)
(root/'docs/licenses/Android-dependencies.txt').write_text('\n'.join(blocks),encoding='utf-8')
print('Collected notices for',len(items),'Android runtime artifacts')
