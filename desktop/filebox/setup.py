# TerminalLink filebox managed v1: invoked only by explicit --setup-remote.
import base64, hashlib, json, os, stat, sys
from pathlib import Path

payload = json.loads(base64.b64decode(sys.argv[1]))
home = Path.home()

def directory(path, private=False):
    if path == home:
        return
    directory(path.parent)
    if not path.exists():
        path.mkdir(mode=0o700)
    s = path.lstat()
    if not stat.S_ISDIR(s.st_mode) or s.st_uid != os.getuid() or s.st_mode & 0o022:
        raise ValueError('Unsafe installation directory: ' + str(path))
    if private and stat.S_IMODE(s.st_mode) == 0o2700:
        # A setgid parent can add this bit even to mkdir(0700). Tighten only the
        # new module's owned private directory; never change any shared parent.
        os.chmod(path, 0o700, follow_symlinks=False)
        s = path.lstat()
    if private and stat.S_IMODE(s.st_mode) != 0o700:
        raise ValueError('Filebox directory must be private: ' + str(path) + ' mode=' + oct(stat.S_IMODE(s.st_mode)))

def write(path, text):
    data = text.encode('utf-8')
    if path.exists() or path.is_symlink():
        s = path.lstat()
        if not stat.S_ISREG(s.st_mode) or s.st_uid != os.getuid() or s.st_nlink != 1:
            raise ValueError('Unsafe existing install file')
        if b'TerminalLink filebox managed v1' not in path.read_bytes():
            raise ValueError('Unmanaged file exists; not overwritten')
    tmp = path.with_name(path.name + '.install-' + str(os.getpid()))
    fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, 'wb') as f:
        f.write(data); f.flush(); os.fsync(f.fileno())
    os.replace(tmp, path)
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()

base = home / '.local/share/terminallink-filebox-v1'
directory(base, True)
directory(base / 'outbox', True)
skill = home / '.codex/skills/terminallink-filebox'
directory(skill, True)
hashes = {'helper': write(base / 'publish.py', payload['helper']), 'skill': write(skill / 'SKILL.md', payload['skill'])}
print(json.dumps({'ok': True, 'hashes': hashes}))
