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
SOURCE = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / 'host/RemoteCodexFeed.cs'
SCRIPT = SOURCE.read_text(encoding='utf-8-sig').split('internal const string Script="""', 1)[1].split('\n""";', 1)[0]
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
    usage={'type':'event_msg','timestamp':'2026-10-28T08:51:40Z','payload':{'type':'token_count','rate_limits':{'limit_id':'codex','primary':{'used_percent':42,'window_minutes':10080,'resets_at':1793774145},'credits':{'balance':'private'}}}}
    record.write_text(record.read_text()+json.dumps(usage)+'\n',encoding='utf-8')
    weekly=f.run(offset=delta['offset'], **saved)['records'][0]
    check(weekly['timestamp']==usage['timestamp'] and weekly['payload']['rate_limits']['primary']['used_percent']==42 and 'credits' not in weekly['payload']['rate_limits'], 'Remote weekly usage retains timestamp and percentage without forwarding credit details')
    check(not f.run(answers=[['Generic answer']])['available'], 'Short generic answer cannot identify a conversation')
    check(not f.run(answers=[['x' * 200]])['available'], 'Long unrelated output cannot identify a conversation')
    check(not f.run(anchors=['A different complete question'])['available'], 'Answer evidence cannot override a conflicting visible question')
    check(f.run(anchors=[QUESTION], answers=[])['available'], 'Existing exact question matching remains supported')
    clipped = QUESTION[:-2] + '\u2026'
    check(f.run(anchors=[clipped])['available'], 'Clipped question and ordered answer in its original turn identify the record')
    check(f.run(anchors=[QUESTION[:-2] + '...'])['available'], 'Three-dot clipping requires the same original turn evidence')
    check(not f.run(anchors=[clipped], answers=[])['available'], 'A distinctive prefix alone cannot identify the record')
    check(not f.run(anchors=['A conflicting prefix that is sufficiently long to be distinctive\u2026'])['available'], 'Answer cannot override a different clipped question')
    check(not f.run(anchors=[clipped, 'A different complete question'])['available'], 'Clipped recovery cannot override another complete visible question')
    check(not f.run(anchors=[QUESTION[:15] + '\u2026'])['available'], 'Short clipped questions fail closed')
    check(not f.run(anchors=[clipped], answers=[[NORMAL(x) for x in reversed(PARTS)]])['available'], 'Clipped recovery requires ordered answer evidence')
    later = Fixture(base / 'later')
    later.record('later-fixture', [message('user', QUESTION), message('user', 'An unrelated later question'), message('assistant', ANSWER)])
    check(not later.run(anchors=[clipped])['available'], 'Answer after a different user question cannot prove the clipped turn')
    earlier = Fixture(base / 'earlier')
    earlier.record('earlier-fixture', [message('assistant', ANSWER), message('user', QUESTION)])
    check(not earlier.run(anchors=[clipped])['available'], 'Answer before the clipped question cannot prove its turn')
    duplicate = Fixture(base / 'prefix-duplicate')
    for name in ('one', 'two'):
        duplicate.record(name, [message('user', QUESTION), message('assistant', ANSWER)])
    check(not duplicate.run(anchors=[clipped])['available'], 'Two originals with the same prefix and answer remain ambiguous')
    literal = Fixture(base / 'literal-ellipsis')
    literal.record('literal-fixture', [message('user', clipped)])
    check(literal.run(anchors=[clipped], answers=[])['available'], 'A literal complete question ending in ellipsis still matches exactly')
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
    check(not scoped.run()['available'], 'An open record in a different working directory is rejected')
    resumed = Fixture(base / 'resumed-editor')
    original = resumed.record('resumed-editor-fixture', [message('user', QUESTION), message('assistant', ANSWER)], source='vscode')
    result = resumed.run(anchors=[QUESTION], answers=[])
    check(result['available'] and len(result['records']) == 3, 'A desktop/editor-origin conversation resumed in the SSH TUI returns the full original')
    check(resumed.run()['available'], 'A resumed editor-origin record can match substantive answer-only evidence')
    saved = {key: result[key] for key in ('path', 'tui', 'stamp')}
    check(resumed.run(answers=[], validateOnly=True, **saved)['available'], 'Resumed editor-origin request/attachment binding validates without changing identity')
    check(not resumed.run(answers=[])['available'], 'Editor origin and matching cwd alone do not identify an unbound conversation')
    check(not resumed.run(anchors=['An unrelated current terminal question'])['available'], 'An editor record cannot override conflicting terminal evidence')
    check(not resumed.run(connection='192.0.2.99 1 192.0.2.20 22')['available'], 'Another SSH connection still cannot access resumed editor history')
    original.write_text(original.read_text() + json.dumps(message('user', 'A resumed conversation reply')) + '\n', encoding='utf-8')
    delta = resumed.run(offset=result['offset'], **saved)
    check(delta['available'] and len(delta['records']) == 1, 'Resumed editor-origin history remains incremental after the initial read')
    resumed.record('cli-duplicate', [message('user', QUESTION), message('assistant', ANSWER)])
    check(not resumed.run()['available'], 'Matching CLI and editor-origin records remain ambiguous')
    other_editor = Fixture(base / 'other-editor-workspace')
    other_editor.record('other-editor', [message('assistant', ANSWER)], cwd='another-workspace', source='vscode')
    check(not other_editor.run()['available'], 'Editor origin does not bypass the working-directory boundary')
    auxiliary = Fixture(base / 'auxiliary')
    for index, source in enumerate(({'subagent': {'other': 'guardian'}}, 'exec', 'unknown', None)):
        auxiliary.record('auxiliary-' + str(index), [message('user', QUESTION), message('assistant', ANSWER)], source=source)
    check(not auxiliary.run()['available'], 'Subagent, exec and unknown-origin records never replace the user conversation')
    reply = Fixture(base / 'resumed-request-reply')
    question = 'Which synthetic report format should be used while preserving all verification details?'
    answer = 'Portable document plus editable presentation'
    envelope = '<send_user_message_question_reply>\n' + json.dumps([{'question':question,'answer':answer,'questionItemId':'synthetic-request'}]) + '\n</send_user_message_question_reply>'
    reply.record('request-reply', [message('user', envelope), message('assistant', ANSWER)], source='vscode')
    rendered = '> '+question+'\n\n'+answer
    result = reply.run(anchors=[rendered], answers=[])
    check(result['available'], 'A request reply rendered as question plus answer identifies its unchanged original envelope')
    check(result['records'][1]['payload']['content'][0]['text'] == envelope, 'Request reply matching never rewrites the original payload')
    clipped_reply = rendered[:len(question)+12] + '\u2026'
    check(reply.run(anchors=[clipped_reply])['available'], 'A clipped request reply plus its ordered answer identifies the resumed editor-origin conversation')
    check(not reply.run(anchors=[clipped_reply], answers=[])['available'], 'A clipped rendered request reply still requires original answer evidence')
    check(not reply.run(anchors=[rendered.replace(answer,'A different selection')])['available'], 'A different request reply selection is not accepted')
    malformed = Fixture(base / 'malformed-request-reply')
    malformed.record('malformed', [message('user', '<send_user_message_question_reply>{}</send_user_message_question_reply>'), message('assistant', ANSWER)])
    check(not malformed.run(anchors=[rendered])['available'], 'Malformed request envelopes cannot create question-answer identity evidence')
