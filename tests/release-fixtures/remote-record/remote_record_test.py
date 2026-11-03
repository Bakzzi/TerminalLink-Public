"""Run the production remote reader against synthetic /proc and original records.
No SSH connection, user history, or live process is used by these tests.
"""
import contextlib
import io
import json
import os
from pathlib import Path
import sys
import tempfile
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[3]
SCRIPT = (ROOT / 'host/RemoteCodexFeed.cs').read_text(encoding='utf-8-sig').split('internal const string Script="""', 1)[1].split('\n""";', 1)[0]
REAL_PATH = Path
CONNECTION = '192.0.2.10 12345 192.0.2.20 22'
QUESTION = 'An explicit synthetic question for the first remote session.'
PARTS = [
    'The monitoring service preserves device identities across reconnects.',
    'The final report keeps verification results separate from live changes.',
]
NORMAL = lambda text: ''.join(text.split())
ANSWER = '**' + PARTS[0] + '**\n\n' + PARTS[1]


def message(role, text, phase='final'):
    return {'type': 'response_item', 'payload': {'type': 'message', 'role': role, 'phase': phase,
            'content': [{'type': 'input_text' if role == 'user' else 'output_text', 'text': text}]}}


class Fixture:
    def __init__(self, folder):
        self.root = folder
        self.proc = folder / 'proc'
        self.cwd = str(folder / 'workspace')
        self.links = {}
        for pid, command in [(100, ['codex']), (200, ['codex', 'app-server'])]:
            p = self.proc / str(pid)
            (p / 'fd').mkdir(parents=True)
            (p / 'comm').write_text('codex\n')
            (p / 'cmdline').write_bytes(('\0'.join(command) + '\0').encode())
            (p / 'environ').write_bytes(('SSH_CONNECTION=' + CONNECTION + '\0').encode())
            (p / 'stat').write_text(str(pid) + ' (codex) ' + ' '.join(['1'] * 19 + [str(pid * 100)]))
            self.links[str(p / 'cwd')] = self.cwd
        self.paths = []

    def record(self, name, items, cwd=None, source='cli'):
        p = self.root / '.codex' / 'sessions' / ('rollout-' + name + '.jsonl')
        p.parent.mkdir(parents=True, exist_ok=True)
        data = [{'type': 'session_meta', 'payload': {'id': name, 'source': source, 'cwd': cwd or self.cwd}}] + items
        p.write_text(''.join(json.dumps(x) + '\n' for x in data), encoding='utf-8')
        fd = self.proc / '200' / 'fd' / str(len(self.paths) + 3)
        fd.touch()
        self.links[str(fd)] = str(p).replace('\\', '/')
        self.paths.append(p)
        return p

    def run(self, **values):
        request = {'connection': CONNECTION, 'anchors': [], 'answers': [[NORMAL(x) for x in PARTS]]}
        request.update(values)
        output = io.StringIO()
        def mapped_path(*args, **kwargs):
            return self.proc if args == ('/proc',) else REAL_PATH(*args, **kwargs)
        with patch('pathlib.Path', side_effect=mapped_path), patch('os.readlink', side_effect=lambda p: self.links[str(p)]), \
             patch('os.getuid', return_value=self.proc.stat().st_uid, create=True), patch.object(sys, 'argv', ['reader', json.dumps(request)]), contextlib.redirect_stdout(output):
            try:
                exec(SCRIPT, {})
            except SystemExit:
                pass
        return json.loads(output.getvalue())


def check(condition, title):
    if not condition:
        raise AssertionError(title)
    print('PASS ' + title)


with tempfile.TemporaryDirectory(prefix='terminallink-remote-fixture-') as temp:
    base = Path(temp)
    f = Fixture(base / 'normal')
    record = f.record('main-fixture', [message('user', QUESTION), message('assistant', ANSWER)])
    result = f.run()
    check(result['available'] and len(result['records']) == 3, 'Answer-only viewport binds a unique owned original and returns original messages')
    saved = {key: result[key] for key in ('path', 'tui', 'stamp')}
    check(f.run(answers=[], **saved)['available'], 'Same verified TUI and sole open original survive a viewport containing no identity text')
    check(f.run(answers=[], validateOnly=True, **saved)['available'], 'Cold-process binding validation keeps the exact record identity')
    record.write_text(record.read_text() + json.dumps(message('user', 'A later reply')) + '\n', encoding='utf-8')
    delta = f.run(offset=result['offset'], **saved)
    check(delta['available'] and len(delta['records']) == 1, 'Verified remote record remains append-only after answer recovery')
    check(not f.run(answers=[['Generic answer']])['available'], 'Short generic answer cannot identify a conversation')
    check(not f.run(answers=[['x' * 200]])['available'], 'Long unrelated output cannot identify a conversation')
    check(not f.run(anchors=['A different complete question'])['available'], 'Answer evidence cannot override a conflicting visible question')
    check(f.run(anchors=[QUESTION], answers=[])['available'], 'Existing exact question matching remains supported')
    denied = f.run(connection='192.0.2.99 1 192.0.2.20 22')
    check(not denied['available'] and 'records' not in denied, 'Another SSH connection cannot disclose records')
    f.record('duplicate-fixture', [message('user', 'Different question'), message('assistant', ANSWER)])
    denied = f.run()
    check(not denied['available'] and 'records' not in denied, 'Matching answer in two open originals is rejected as ambiguous')
    split = Fixture(base / 'split')
    split.record('split-fixture', [message('assistant', PARTS[0]), message('assistant', PARTS[1])])
    check(not split.run()['available'], 'Evidence cannot be assembled from separate assistant messages')
    tool = Fixture(base / 'tool')
    tool.record('tool-fixture', [{'type': 'response_item', 'payload': {'type': 'function_call_output', 'output': ANSWER}}])
    check(not tool.run()['available'], 'Tool payload never supplies answer evidence')
    scoped = Fixture(base / 'scoped')
    scoped.record('other-workspace', [message('assistant', ANSWER)], cwd='different-workspace')
    scoped.record('app-owned', [message('assistant', ANSWER)], source='vscode')
    check(not scoped.run()['available'], 'Working-directory and CLI ownership boundaries remain enforced')
