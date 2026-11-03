#!/usr/bin/env python3
"""TerminalLink filebox managed v1: explicit immutable snapshots, verified PC receipts."""
import argparse
import hashlib
import json
import os
import re
import stat
import sys
import time
import uuid
from pathlib import Path

MAX_FILE = 512 * 1024 * 1024
MAX_TOTAL = 2 * 1024 * 1024 * 1024
ROOT = Path.home() / '.local/share/terminallink-filebox-v1'


def private(path, directory=True):
    s = path.lstat()
    if s.st_uid != os.getuid() or (not stat.S_ISDIR(s.st_mode) if directory else not stat.S_ISREG(s.st_mode)) or stat.S_IMODE(s.st_mode) != (0o700 if directory else 0o600):
        raise ValueError('Unsafe filebox path')
    if not directory and s.st_nlink != 1:
        raise ValueError('Linked queue file')


def root():
    private(ROOT)
    queue = ROOT / 'outbox'
    queue.mkdir(mode=0o700, exist_ok=True)
    private(queue)
    return queue


def request_path(value):
    if not re.fullmatch('[a-f0-9]{32}', value):
        raise ValueError('Invalid request ID')
    p = root() / value
    private(p)
    return p


def read_json(path):
    private(path, False)
    with os.fdopen(os.open(path, os.O_RDONLY | os.O_NOFOLLOW), 'rb') as f:
        b = f.read(65537)
    if len(b) > 65536:
        raise ValueError('Oversized manifest')
    return json.loads(b)


def atomic_json(path, value):
    tmp = path.with_name('.tmp-' + uuid.uuid4().hex)
    fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    try:
        with os.fdopen(fd, 'w', encoding='utf-8') as f:
            json.dump(value, f, ensure_ascii=False)
            f.flush()
            os.fsync(f.fileno())
        os.replace(tmp, path)
    finally:
        if tmp.exists():
            tmp.unlink()


def name_for_pc(name):
    name = re.sub(r'[<>:"/\\|?*\x00-\x1f]', '_', name).rstrip(' .')
    if not name or name in ('.', '..'):
        raise ValueError('Invalid filename')
    if re.fullmatch(r'(?i)(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?', name):
        name = '_' + name
    if len(name.encode('utf-16-le')) // 2 > 200:
        raise ValueError('Filename exceeds 200 characters')
    return name


def cleanup():
    # Only our generated flat request directories; never follow links or touch originals.
    for p in list(root().iterdir()):
        if not re.fullmatch('[a-f0-9]{32}', p.name):
            continue
        try:
            private(p)
            m = read_json(p / 'manifest.json')
            if m.get('id') != p.name or time.time() * 1000 - m['created'] < 86400000:
                continue
            paths = list(p.iterdir())
            if any(not re.fullmatch(r'(manifest|receipt|cancel)\.json|[0-9]{1,2}\.bin', f.name) for f in paths):
                continue
            for f in paths:
                private(f, False)
            for f in paths:
                f.unlink()
            p.rmdir()
        except (OSError, ValueError, KeyError, TypeError):
            continue


def publish(files):
    import fcntl
    # Serialize queue-size checks and snapshots across concurrently working Codex tabs.
    lock = os.open(ROOT / 'publish.lock', os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    try:
        fcntl.flock(lock, fcntl.LOCK_EX)
        return publish_locked(files)
    finally:
        os.close(lock)


def publish_locked(files):
    if not 1 <= len(files) <= 32:
        raise ValueError('Select 1-32 files')
    cleanup()
    queue = root()
    # Include queued snapshots in the limit; prevents unlimited spool growth while PC is offline.
    used = count = 0
    for p in queue.iterdir():
        if not re.fullmatch('[a-f0-9]{32}', p.name):
            continue
        private(p)
        for f in p.iterdir():
            private(f, False)
            used += f.stat().st_size
        count += 1
    if count >= 32:
        raise ValueError('Queue is full; check previous request statuses')
    rid = uuid.uuid4().hex
    stage = queue / ('.stage-' + rid)
    stage.mkdir(mode=0o700)
    entries = []
    try:
        for i, path in enumerate(files):
            path = Path(path).expanduser().absolute()
            fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
            with os.fdopen(fd, 'rb') as source:
                before = os.fstat(source.fileno())
                if not stat.S_ISREG(before.st_mode) or before.st_size > MAX_FILE:
                    raise ValueError('Only regular files up to 512 MiB are supported')
                name = name_for_pc(path.name)
                h = hashlib.sha256()
                size = 0
                out = os.open(stage / (str(i) + '.bin'), os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
                with os.fdopen(out, 'wb') as dest:
                    while True:
                        data = source.read(192 * 1024)
                        if not data:
                            break
                        size += len(data)
                        if size > MAX_FILE or used + size + sum(x['size'] for x in entries) > MAX_TOTAL:
                            raise ValueError('Filebox snapshot quota exceeded')
                        dest.write(data)
                        h.update(data)
                    dest.flush()
                    os.fsync(dest.fileno())
                after = os.fstat(source.fileno())
                if size != before.st_size or (before.st_mtime_ns, before.st_ctime_ns) != (after.st_mtime_ns, after.st_ctime_ns):
                    raise ValueError('Source changed while preparing upload; retry when writing finishes')
                entries.append(dict(index=i, name=name, size=size, sha256=h.hexdigest().upper()))
        atomic_json(stage / 'manifest.json', dict(version=1, id=rid, created=int(time.time() * 1000), files=entries))
        os.rename(stage, queue / rid)
        return rid
    finally:
        if stage.exists():
            # Only files created by this invocation, inside its exclusive staging directory.
            for p in stage.iterdir():
                p.unlink()
            stage.rmdir()


def status(rid, wait):
    p = request_path(rid)
    manifest = read_json(p / 'manifest.json')
    end = time.monotonic() + wait
    while True:
        if (p / 'receipt.json').exists():
            receipt = read_json(p / 'receipt.json')
            if receipt.get('request') != rid or receipt.get('ok') is not True:
                raise ValueError('PC receipt is invalid')
            files = receipt.get('files', [])
            if len(files) != len(manifest['files']):
                raise ValueError('Incomplete PC receipt')
            for original, actual in zip(manifest['files'], files):
                if any(original[k] != actual.get(k) for k in ('name', 'size', 'sha256')) or not re.fullmatch('[a-f0-9]{32}', actual.get('id', '')) or actual.get('expires', 0) != actual.get('created', 0) + 86400000:
                    raise ValueError('PC receipt does not match the uploaded files')
            # The PC has verified its durable copies. Free only queue snapshot blobs.
            for i in range(len(files)):
                blob = p / (str(i) + '.bin')
                if blob.exists():
                    private(blob, False)
                    blob.unlink()
            return receipt
        if (p / 'cancel.json').exists():
            return dict(ok=False, status='cancelled', request=rid)
        if time.time() * 1000 - manifest['created'] >= 86400000:
            return dict(ok=False, status='expired', request=rid)
        if time.monotonic() >= end:
            return dict(ok=False, status='pending', request=rid, message='PC publication is not confirmed. Check the same request with --status; do not submit it again.')
        time.sleep(1)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('files', nargs='*')
    parser.add_argument('--status')
    parser.add_argument('--cancel')
    parser.add_argument('--wait', type=int, default=120)
    args = parser.parse_args()
    if not 0 <= args.wait <= 600 or sum(bool(x) for x in (args.files, args.status, args.cancel)) != 1:
        parser.error('Choose files, --status or --cancel; wait must be 0-600')
    if args.cancel:
        p = request_path(args.cancel)
        atomic_json(p / 'cancel.json', dict(request=args.cancel))
        result = dict(ok=False, status='cancelled', request=args.cancel)
    else:
        rid = args.status or publish(args.files)
        result = status(rid, args.wait)
    print(json.dumps(result, ensure_ascii=False))
    return 0 if result.get('ok') else 2


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (OSError, ValueError, KeyError, TypeError) as e:
        print(json.dumps(dict(ok=False, status='error', message=str(e)), ensure_ascii=False))
        sys.exit(1)
