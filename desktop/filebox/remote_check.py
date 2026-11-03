# Runs only via explicit --remote-check; all helper tests use a fresh private temporary root.
import hashlib, importlib.util, json, os, tempfile, time
from pathlib import Path

source = Path.home() / '.local/share/terminallink-filebox-v1/publish.py'
spec = importlib.util.spec_from_file_location('filebox_test', source)
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
passed = 0
def check(value):
    global passed
    if not value: raise AssertionError('remote helper check failed')
    passed += 1
def rejects(call):
    try: call()
    except (OSError, ValueError): check(True); return
    raise AssertionError('unsafe helper operation accepted')

with tempfile.TemporaryDirectory(prefix='terminallink-filebox-test-') as temp:
    m.ROOT = Path(temp) / 'box'; m.ROOT.mkdir(mode=0o700)
    original = Path(temp) / 'sample 한글.txt'; original.write_bytes(b'explicit snapshot\n')
    rid = m.publish([str(original)])
    request = m.read_json(m.request_path(rid) / 'manifest.json')
    check(request['files'][0]['sha256'] == hashlib.sha256(original.read_bytes()).hexdigest().upper())
    check(m.status(rid, 0)['status'] == 'pending')
    check(original.read_bytes() == b'explicit snapshot\n')
    check(m.name_for_pc('CON.txt') == '_CON.txt')
    check(m.name_for_pc('a:b?.txt') == 'a_b_.txt')
    rejects(lambda: m.request_path('../bad'))
    rejects(lambda: m.publish([temp]))
    linked = Path(temp) / 'linked.txt'; linked.symlink_to(original)
    rejects(lambda: m.publish([str(linked)]))
    pipe = Path(temp) / 'pipe'; os.mkfifo(pipe)
    rejects(lambda: m.publish([str(pipe)]))
    empty = Path(temp) / 'empty'; empty.touch(); check(m.publish([str(empty)]) != rid)
    cap = m.MAX_TOTAL; m.MAX_TOTAL = 1
    rejects(lambda: m.publish([str(original)])); m.MAX_TOTAL = cap
    check(not list(m.root().glob('.stage-*')))
    item = dict(request['files'][0], id='a'*32, created=int(time.time()*1000)); item.pop('index'); item['expires'] = item['created'] + 86400000
    m.atomic_json(m.request_path(rid) / 'receipt.json', dict(ok=True, request=rid, files=[item]))
    check(m.status(rid, 0)['ok'])
    check(not (m.request_path(rid) / '0.bin').exists())
    check(original.exists())
    # Receipt contents must match the request, not just say success.
    item['sha256'] = '0'*64
    m.atomic_json(m.request_path(rid) / 'receipt.json', dict(ok=True, request=rid, files=[item]))
    rejects(lambda: m.status(rid, 0))
print(json.dumps({'passed': passed, 'failed': 0}))
