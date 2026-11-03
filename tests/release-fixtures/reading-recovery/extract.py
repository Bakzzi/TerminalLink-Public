from pathlib import Path
import sys, shutil, subprocess

root, out = map(Path, sys.argv[1:3])
out.mkdir(parents=True, exist_ok=True)
path = 'android/src/com/terminallink/remote/ReadingView.java'
source = (subprocess.check_output(['git', '-C', str(root), 'show', sys.argv[3]+':'+path]).decode('utf-8')
          if len(sys.argv) > 3 else (root/path).read_text(encoding='utf-8'))
fields = source[source.index(' String terminalText='):source.index(' boolean readScheduled;')]
update = source[source.index(' void update(JSONObject frame)'):source.index(' void refreshSent()')]
failed = source[source.index(' void failed('):source.index(' void update(')] if ' void failed(' in source else ''
support = (root/'tests/release-fixtures/reading-recovery/ReadingJvm.java').read_text(encoding='utf-8')
(out/'ReadingJvm.java').write_text(support.replace('/* PRODUCTION_FIELDS */', fields).replace('/* PRODUCTION_METHODS */', failed+update), encoding='utf-8')
shutil.copyfile(root/'android/src/com/terminallink/remote/ConversationStatus.java', out/'ConversationStatus.java')
