"""Isolated installed-version protocol probe. Never loads live auth/config/threads."""
import argparse, base64, hashlib, http.server, json, os, queue, socket, struct, subprocess, threading, time, sys, uuid
from pathlib import Path
from conpty import Terminal

SCRIPT=Path(__file__).resolve().parent
REPO=SCRIPT.parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--codex',type=Path,required=True,help='Installed codex.exe; no live profile is opened')
parser.add_argument('--output',type=Path,default=REPO/'build/request-protocol-audit')
args=parser.parse_args()
BIN=args.codex.resolve(strict=True)
BASE=args.output.resolve()
if not BASE.is_relative_to((REPO/'build').resolve()):raise SystemExit('Output must stay in this repository build directory')
BASE.mkdir(parents=True,exist_ok=True)
ROOT=BASE/('isolated-'+uuid.uuid4().hex); ROOT.mkdir()
version=subprocess.check_output([str(BIN),'--version'],creationflags=subprocess.CREATE_NO_WINDOW,text=True).strip()
if version!='codex-cli 0.163.0':raise SystemExit('This fixture is pinned to codex-cli 0.163.0; review schema before changing the pin')
HOME=ROOT/'home'; HOME.mkdir(exist_ok=True)
WORK=ROOT/'workspace'; WORK.mkdir(exist_ok=True)
events=[]; model_calls=[]; cancellation_question_sent=[False]
class Provider(http.server.BaseHTTPRequestHandler):
 def log_message(self,*args):pass
 def do_GET(self):self.send_error(404)
 def do_POST(self):
  body=json.loads(self.rfile.read(int(self.headers['Content-Length'])))
  model_calls.append({'path':self.path,'tools':[t.get('name',t.get('type')) for t in body.get('tools',[])]})
  self.send_response(200);self.send_header('Content-Type','text/event-stream');self.end_headers()
  def emit(value):self.wfile.write(('data: '+json.dumps(value)+'\n\n').encode()); self.wfile.flush()
  emit({'type':'response.created','response':{'id':'response_probe','object':'response','status':'in_progress','output':[]}})
  cancellation='Cancellation fixture.' in json.dumps(body)
  if len(model_calls)==1 or cancellation and not cancellation_question_sent[0]:
   if cancellation:cancellation_question_sent[0]=True
   qs=[{'id':f'q{i}','header':f'Probe {i}','question':f'Isolated question {i}?','options':[{'label':'Yes','description':'Continue'},{'label':'No','description':'Stop'}]} for i in (1,2)]
   item={'type':'function_call','id':'fc_probe','call_id':'call_probe','name':'request_user_input','arguments':json.dumps({'questions':qs})}
  else:item={'type':'message','id':'msg_done','role':'assistant','content':[{'type':'output_text','text':'Isolated probe finished.'}]}
  emit({'type':'response.output_item.added','output_index':0,'item':item})
  emit({'type':'response.output_item.done','output_index':0,'item':item})
  emit({'type':'response.completed','response':{'id':'response_probe','status':'completed','output':[item],'usage':{'input_tokens':1,'output_tokens':1,'total_tokens':2}}})
provider=http.server.ThreadingHTTPServer(('127.0.0.1',0),Provider)
threading.Thread(target=provider.serve_forever,daemon=True).start()
with socket.socket() as reserve:reserve.bind(('127.0.0.1',0)); port=reserve.getsockname()[1]
token=base64.urlsafe_b64encode(os.urandom(32)).decode(); tokenfile=ROOT/'ws-token';tokenfile.write_text(token)
(HOME/'config.toml').write_text(f'''model = "gpt-5.1-codex"
model_provider = "probe"
model_reasoning_effort = "low"
[model_providers.probe]
name = "Isolated loopback fixture"
base_url = "http://127.0.0.1:{provider.server_port}/v1"
wire_api = "responses"
requires_openai_auth = false
request_max_retries = 0
stream_max_retries = 0
[analytics]
enabled = false
[features]
shell_tool = false
''',encoding='utf-8')
env=os.environ.copy();env['CODEX_HOME']=str(HOME)
env['TERM']='xterm-256color'
for k in list(env):
 if k.startswith(('OPENAI_','AZURE_OPENAI_')):env.pop(k)
stderr=open(ROOT/'server-stderr.txt','wb')
p=subprocess.Popen([str(BIN),'app-server','--listen',f'ws://127.0.0.1:{port}','--ws-auth','capability-token','--ws-token-file',str(tokenfile)],cwd=WORK,env=env,stdin=subprocess.DEVNULL,stdout=subprocess.DEVNULL,stderr=stderr,creationflags=subprocess.CREATE_NO_WINDOW)
class Client:
 def __init__(self,name):
  self.name=name;self.q=queue.Queue();self.responses={}; self.n=0
  self.sock=socket.create_connection(('127.0.0.1',port),timeout=3);self.sock.settimeout(None)
  self.f=self.sock.makefile('rb');key=base64.b64encode(os.urandom(16)).decode()
  self.sock.sendall(f'GET / HTTP/1.1\r\nHost: localhost\r\nAuthorization: Bearer {token}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n'.encode())
  headers=b''
  while not headers.endswith(b'\r\n\r\n'):headers+=self.f.read(1)
  assert headers.startswith(b'HTTP/1.1 101'), headers[:64]
  threading.Thread(target=self.read,daemon=True).start()
  self.call('initialize',{'clientInfo':{'name':name,'version':'0.0.0'},'capabilities':{'experimentalApi':True}})
  self.send({'method':'initialized'})
 def send(self,obj):
  data=json.dumps(obj).encode();mask=os.urandom(4);n=len(data)
  header=bytes([0x81,0x80|n]) if n<126 else bytes([0x81,0xfe])+struct.pack('!H',n)
  self.sock.sendall(header+mask+bytes(v^mask[i%4] for i,v in enumerate(data)))
 def read(self):
  try:
   while True:
    h=self.f.read(2)
    if len(h)<2:return
    n=h[1]&127
    if n==126:n=struct.unpack('!H',self.f.read(2))[0]
    if n==127:n=struct.unpack('!Q',self.f.read(8))[0]
    data=self.f.read(n)
    if h[0]&15==8:return
    if h[0]&15!=1:continue
    msg=json.loads(data)
    events.append({'client':self.name,**msg})
    if 'id'in msg and 'method' not in msg:self.responses[msg['id']]=msg
    else:self.q.put(msg)
  except (OSError,ValueError):pass
 def call(self,method,params,timeout=12):
  self.n+=1;n=self.n;self.send({'id':n,'method':method,'params':params})
  deadline=time.monotonic()+timeout
  while n not in self.responses and time.monotonic()<deadline:time.sleep(.03)
  if n not in self.responses:raise TimeoutError(method)
  answer=self.responses.pop(n)
  if 'error'in answer:raise RuntimeError(method+': '+json.dumps(answer['error']))
  return answer['result']
 def request(self,timeout=12,thread=None):
  deadline=time.monotonic()+timeout
  while time.monotonic()<deadline:
   try:msg=self.q.get(timeout=.2)
   except queue.Empty:continue
   if 'id'in msg and 'method'in msg and (thread is None or msg.get('params',{}).get('threadId')==thread):return msg
  return None
 def close(self):
  try:self.sock.shutdown(socket.SHUT_RDWR)
  except OSError:pass
  self.sock.close();self.f.close()
clients=[];tui=None;report={'isolated':True,'liveThreadsTouched':False,'version':'0.163.0'}
try:
 for i in range(60):
  try:
   with socket.create_connection(('127.0.0.1',port),timeout=.2):break
  except OSError:time.sleep(.1)
 a=Client('probe_primary');clients.append(a)
 thread=a.call('thread/start',{'cwd':str(WORK),'model':'gpt-5.1-codex','modelProvider':'probe','sandbox':'read-only','approvalPolicy':'never','baseInstructions':'You are an isolated protocol test.','ephemeral':False})['thread']['id']
 turn=a.call('turn/start',{'threadId':thread,'input':[{'type':'text','text':'Ask the two fixture questions.'}],'collaborationMode':{'mode':'plan','settings':{'model':'gpt-5.1-codex','reasoning_effort':'low','developer_instructions':None}}})
 qa=a.request()
 env['TERMINALLINK_PROBE_TOKEN']=token
 tui=Terminal([sys.executable,str(SCRIPT/'tui_entry.py'),str(BIN),'--remote',f'ws://127.0.0.1:{port}','--remote-auth-token-env','TERMINALLINK_PROBE_TOKEN','--no-alt-screen','resume',thread],WORK,env)
 time.sleep(2)
 report['nativeTuiShowsPendingQuestion']=b'Isolated question 1?' in b''.join(tui.chunks)
 b=Client('probe_secondary');clients.append(b)
 b.call('thread/resume',{'threadId':thread})
 report['secondClientJoined']=True
 qb=b.request(timeout=4)
 report['requestToPrimary']=qa.get('method') if qa else None
 report['requestToSecondary']=qb.get('method') if qb else None
 report['questionCount']=len((qb or qa or {}).get('params',{}).get('questions',[]))
 c=Client('probe_late');clients.append(c);c.call('thread/resume',{'threadId':thread});qc=c.request(timeout=4)
 report['lateRequestReplay']=qc.get('method') if qc else None
 if qb:
  b.close();clients.remove(b)
  b=Client('probe_reconnected');clients.append(b);b.call('thread/resume',{'threadId':thread});replayed=b.request(timeout=4)
  report['reconnectSameLogicalRequest']=replayed is not None and all(replayed['params'][k]==qb['params'][k] for k in ('threadId','turnId','itemId'))
  qb=replayed
  b.send({'id':qb['id'],'result':{'answers':{'q1':{'answers':['Yes']},'q2':{'answers':['No']}}}})
  time.sleep(2)
 report['modelCallsAfterReply']=len(model_calls)
 report['resolvedClients']=sorted(set(e['client'] for e in events if e.get('method')=='serverRequest/resolved'))
 report['tuiAliveAfterExternalAnswer']=tui.alive()
 tui.write(b'LOCAL_DRAFT_PROBE');time.sleep(1)
 report['tuiDraftVisibleAfterExternalAnswer']=b'LOCAL_DRAFT_PROBE' in b''.join(tui.chunks)
 tui.write(b'\r')
 for i in range(40):
  if len(model_calls)>=3:break
  time.sleep(.1)
 report['nativeTuiCanSubmitAfterExternalAnswer']=len(model_calls)>=3
 (ROOT/'tui-output.txt').write_bytes(b''.join(tui.chunks))
 record=next((HOME/'sessions').rglob('*'+thread+'.jsonl'))
 records=[json.loads(line) for line in record.read_text(encoding='utf-8').splitlines()]
 outputs=[r['payload'] for r in records if r.get('type')=='response_item' and r['payload'].get('type')=='function_call_output' and r['payload'].get('call_id')=='call_probe']
 report['recordConfirmsExactAnswers']=len(outputs)==1 and json.loads(outputs[0]['output'])=={'answers':{'q1':{'answers':['Yes']},'q2':{'answers':['No']}}}
 cancelled=a.call('thread/start',{'cwd':str(WORK),'modelProvider':'probe','sandbox':'read-only','approvalPolicy':'never','baseInstructions':'Isolated cancellation fixture.'})['thread']['id']
 cancel_turn=a.call('turn/start',{'threadId':cancelled,'input':[{'type':'text','text':'Cancellation fixture.'}],'collaborationMode':{'mode':'plan','settings':{'model':'gpt-5.1-codex','reasoning_effort':'low','developer_instructions':None}}})
 cancel_request=a.request(thread=cancelled)
 report['cancelRequestObserved']=cancel_request is not None
 if cancel_request:
  a.call('turn/interrupt',{'threadId':cancelled,'turnId':cancel_turn['turn']['id']})
  time.sleep(.5)
 report['cancelAlsoEmitsResolved']=any(e.get('method')=='serverRequest/resolved' and e['params']['threadId']==cancelled for e in events)
 report['cliSha256']=hashlib.sha256(BIN.read_bytes()).hexdigest()
 report['processesStoppedByFixture']='Only its isolated app-server and ConPTY child'

except Exception as e:report['failure']=str(e)
finally:
 if tui:tui.close()
 for c in clients:c.close()
 p.terminate()
 try:p.wait(4)
 except subprocess.TimeoutExpired:p.kill();p.wait(4)
 stderr.close();provider.shutdown();provider.server_close();tokenfile.unlink(missing_ok=True)
 (ROOT/'events.json').write_text(json.dumps(events,indent=2),encoding='utf-8')
 (ROOT/'model-calls.json').write_text(json.dumps(model_calls,indent=2),encoding='utf-8')
 (BASE/'isolated-result.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
 print(json.dumps(report))

required=['nativeTuiShowsPendingQuestion','secondClientJoined','reconnectSameLogicalRequest','tuiAliveAfterExternalAnswer','tuiDraftVisibleAfterExternalAnswer','nativeTuiCanSubmitAfterExternalAnswer','recordConfirmsExactAnswers','cancelAlsoEmitsResolved']
if report.get('failure') or any(report.get(k) is not True for k in required) or report.get('questionCount')!=2 or report.get('lateRequestReplay')!='item/tool/requestUserInput':raise SystemExit(1)
