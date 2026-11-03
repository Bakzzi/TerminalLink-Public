using System.Net;
using System.Net.Sockets;
using QRCoder;
namespace TerminalLink;
internal sealed class MainForm : ConnectorForm {
 readonly Identity identity=null!;
 readonly CheckedListBox windows=new(){Dock=DockStyle.Fill,CheckOnClick=true,BackColor=ConnectorTheme.Surface,ForeColor=ConnectorTheme.Text,BorderStyle=BorderStyle.None,IntegralHeight=false};
 readonly TextBox endpoint=new(){Width=230};
 readonly NumericUpDown port=new(){Minimum=1024,Maximum=65535,Width=90,Value=49443};
 readonly Label state=new(){AutoSize=true,Text="공유할 창을 선택하고 연결 대기를 시작하세요.",ForeColor=ConnectorTheme.Accent};
 readonly Label peer=new(){AutoSize=true,ForeColor=ConnectorTheme.Muted};
 readonly TextBox logs=new(){Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.None,BackColor=ConnectorTheme.Surface,ForeColor=ConnectorTheme.Muted,BorderStyle=BorderStyle.None};
 readonly object sync=new();
 List<TerminalWindow> permitted=new();
 RemoteServer? server;
 readonly WindowShareSelection shares=new();readonly System.Windows.Forms.Timer discovery=new(){Interval=2000};
 readonly CheckBox automatic=new(){Text="새 터미널 창 자동 공유",AutoSize=true,FlatStyle=FlatStyle.Flat,ForeColor=ConnectorTheme.Muted,Margin=new Padding(0,4,0,8)};bool updatingWindows;
 string windowSignature="";
 bool updateRestored=true;
 readonly Button start=Button("연결 대기 시작");
 internal MainForm(string[]? launchArgs=null,bool preview=false){
  Text="TerminalLink · 연결 프로그램";Width=880;Height=810;MinimumSize=new Size(760,740);
  var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=4};
  root.RowStyles.Add(new(SizeType.Absolute,85));root.RowStyles.Add(new(SizeType.Percent,50));root.RowStyles.Add(new(SizeType.Absolute,240));root.RowStyles.Add(new(SizeType.Percent,50));
  var heading=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1};
  var brand=new FlowLayoutPanel{Dock=DockStyle.Top,Height=40,WrapContents=false,Margin=new Padding(0)};
  brand.Controls.Add(new Label{Text=">_<",Font=new Font("Consolas",20,FontStyle.Bold),ForeColor=ConnectorTheme.Accent,AutoSize=true,Margin=new Padding(0,0,10,0)});
  brand.Controls.Add(new Label{Text="TerminalLink",Font=new Font("Segoe UI",20,FontStyle.Bold),AutoSize=true,Margin=new Padding(0)});
  heading.Controls.Add(brand);heading.Controls.Add(ConnectorTheme.Label("PC 연결 프로그램  ·  실행 중인 Windows Terminal을 휴대폰과 연결합니다.",10,ConnectorTheme.Muted));root.Controls.Add(heading,0,0);
  var terminalCard=ConnectorTheme.Card();terminalCard.RowCount=3;terminalCard.RowStyles.Add(new(SizeType.Absolute,42));terminalCard.RowStyles.Add(new(SizeType.Absolute,32));terminalCard.RowStyles.Add(new(SizeType.Percent,100));
  var discoveryRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=new Padding(0)};discoveryRow.ColumnStyles.Add(new(SizeType.Percent,100));discoveryRow.ColumnStyles.Add(new(SizeType.AutoSize));
  discoveryRow.Controls.Add(ConnectorTheme.Label("공유할 Windows Terminal",11),0,0);var refresh=Button("목록 새로고침");refresh.Margin=new Padding(0);discoveryRow.Controls.Add(refresh,1,0);
  terminalCard.Controls.Add(discoveryRow,0,0);terminalCard.Controls.Add(automatic,0,1);terminalCard.Controls.Add(windows,0,2);root.Controls.Add(terminalCard,0,1);
  var connection=ConnectorTheme.Card();connection.RowCount=5;foreach(int height in new[]{28,52,54,29,25})connection.RowStyles.Add(new(SizeType.Absolute,height));
  connection.Controls.Add(ConnectorTheme.Label("휴대폰 연결",11),0,0);
  var addressRow=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=new Padding(0)};
  var addressLabel=ConnectorTheme.Label("접속 주소");addressLabel.Padding=new Padding(0,8,12,0);addressRow.Controls.Add(addressLabel);addressRow.Controls.Add(ConnectorTheme.Field(endpoint));
  var portLabel=ConnectorTheme.Label("TCP 포트");portLabel.Padding=new Padding(4,8,12,0);addressRow.Controls.Add(portLabel);addressRow.Controls.Add(ConnectorTheme.Field(port));connection.Controls.Add(addressRow,0,1);
  var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=new Padding(0)};start.BackColor=ConnectorTheme.Primary;actions.Controls.Add(start);
  var pairing=Button("휴대폰 등록 · QR");var revoke=Button("등록 해제");revoke.ForeColor=ConnectorTheme.Muted;actions.Controls.Add(pairing);actions.Controls.Add(revoke);connection.Controls.Add(actions,0,2);
  connection.Controls.Add(state,0,3);connection.Controls.Add(peer,0,4);root.Controls.Add(connection,0,2);
  var logCard=ConnectorTheme.Card();logCard.Margin=new Padding(0);logCard.RowCount=2;logCard.RowStyles.Add(new(SizeType.Absolute,30));logCard.RowStyles.Add(new(SizeType.Percent,100));logCard.Controls.Add(ConnectorTheme.Label("연결 기록",11),0,0);logCard.Controls.Add(logs,0,1);root.Controls.Add(logCard,0,3);Body.Controls.Add(root);
  if(preview){
   endpoint.Text="pc.example.com";windows.Items.Add("Windows Terminal · 창 2개",true);windows.Items.Add("Windows Terminal · 창 1개",false);automatic.Checked=true;
   state.Text="연결 대기 중 · 등록된 휴대폰만 접근";peer.Text="등록된 휴대폰: 내 휴대폰";start.Text="연결 대기 중지";logs.Text="12:00:00  연결 대기 시작\r\n12:00:01  등록된 휴대폰 연결됨\r\n12:00:02  터미널 목록 갱신됨";return;
  }
  identity=new();refresh.Click+=(_,_)=>RefreshWindows();
  endpoint.Text=string.IsNullOrWhiteSpace(identity.Config.Endpoint)?LocalAddress():identity.Config.Endpoint;port.Value=identity.Config.Port;
  shares.Automatic=identity.Config.AutoShareTerminals||launchArgs?.Contains("--auto-share")==true;
  automatic.Checked=shares.Automatic;
  automatic.CheckedChanged+=(_,_)=>{shares.Automatic=automatic.Checked;if(shares.Automatic)shares.EnableAll(TerminalWindows.Read());identity.Config.AutoShareTerminals=shares.Automatic;identity.Save();RefreshWindows();};
  windows.ItemCheck+=(_,e)=>{if(updatingWindows)return;shares.Check((TerminalWindow)windows.Items[e.Index],e.NewValue==CheckState.Checked);BeginInvoke((Action)UpdateAllowed);};
  start.Click+=(_,_)=>{
   if(server!=null){server.Dispose();server=null;start.Text="연결 대기 시작";state.Text="연결 대기 중지됨";return;}
   UpdateAllowed();lock(sync){if(permitted.Count==0&&!shares.Automatic){ConnectorDialog.Notice(this,"공유할 터미널을 하나 이상 체크하세요.");return;}}
   try{identity.Config.Port=(int)port.Value;identity.Config.Endpoint=endpoint.Text.Trim();identity.Save();server=new(identity,Allowed,Approve,Log);server.Start((int)port.Value);start.Text="연결 대기 중지";state.Text="연결 대기 중 · 등록된 휴대폰만 접근";}
   catch(Exception ex){server?.Dispose();server=null;ConnectorDialog.Notice(this,ex.Message);}
  };
  pairing.Click+=(_,_)=>{
   if(server==null){ConnectorDialog.Notice(this,"먼저 연결 대기를 시작하세요.");return;}
   if(identity.Config.PeerFingerprint!=null){ConnectorDialog.Notice(this,"이미 등록된 휴대폰이 있습니다. 교체하려면 등록을 해제하세요.");return;}
   if(string.IsNullOrWhiteSpace(endpoint.Text)||endpoint.Text.Contains('/')||endpoint.Text.Contains(' ')){ConnectorDialog.Notice(this,"공인 IP, 도메인 또는 같은 망의 PC 주소를 입력하세요.");return;}
   string code=identity.OpenPairing(endpoint.Text.Trim(),(int)port.Value);ShowPairing(code);
  };
  revoke.Click+=(_,_)=>{if(!ConnectorDialog.Confirm(this,"등록된 휴대폰의 접속 권한을 해제할까요?","등록 해제","등록 해제"))return;identity.Revoke();server?.Disconnect();UpdatePeer();Log("휴대폰 등록 해제됨");};
  FormClosing+=(_,_)=>{discovery.Stop();discovery.Dispose();server?.Dispose();identity.Dispose();};
  discovery.Tick+=async(_,_)=>{RefreshWindows(false);await AutomaticUpdates.Check(()=>!ContainsFocus&&OwnedForms.Length==0&&(server==null||server.UpdateIdle),SaveUpdateState,()=>{server?.PauseForUpdate();Close();},Log);};discovery.Start();
  RefreshWindows();UpdatePeer();
  string? restored=AutomaticUpdates.Resume();
  if(restored!=null)Shown+=(_,_)=>BeginInvoke((Action)(()=>RestoreUpdateState(restored)));
  else if(launchArgs?.Contains("--setup-session")==true)Shown+=(_,_)=>BeginInvoke((Action)(()=>{
   try{
    string Arg(string key){int i=Array.IndexOf(launchArgs,key);if(i<0||i+1>=launchArgs.Length)throw new ArgumentException("Missing "+key);return launchArgs[i+1];}
    long handle=long.Parse(Arg("--window"));int pid=int.Parse(Arg("--pid"));
    var address=IPAddress.Parse(Arg("--address"));if(address.AddressFamily!=AddressFamily.InterNetwork)throw new ArgumentException("IPv4 address required");
    int requestedPort=int.Parse(Arg("--port"));if(requestedPort<1024||requestedPort>65535)throw new ArgumentException("Invalid port");
    int index=-1;for(int i=0;i<windows.Items.Count;i++){var item=(TerminalWindow)windows.Items[i];if(item.Handle==handle&&item.Pid==pid&&Native.Valid(item)){index=i;break;}}
    if(index<0&&!shares.Automatic)throw new InvalidOperationException("지정한 터미널이 이미 닫혔습니다. 공유할 창을 직접 선택하세요.");
    endpoint.Text=address.ToString();port.Value=requestedPort;if(index>=0){shares.Check((TerminalWindow)windows.Items[index],true);windows.SetItemChecked(index,true);}
    if(shares.Automatic){shares.EnableAll(TerminalWindows.Read());identity.Config.AutoShareTerminals=true;identity.Save();}
    UpdateAllowed();
    start.PerformClick();
    if(server==null)throw new InvalidOperationException("연결 대기를 시작하지 못했습니다.");
    Log("현재 작업 터미널 공유 설정 완료 · "+address+":"+requestedPort);
    if(launchArgs.Contains("--pair"))pairing.PerformClick();
   }catch(Exception e){ConnectorDialog.Notice(this,e.Message,"초기 설정 확인");}
  }));
  Shown+=(_,_)=>BeginInvoke((Action)(()=>{if(updateRestored)AutomaticUpdates.Healthy();}));
 }
 sealed record UpdateWindow(long handle,int pid,long started);
 sealed record UpdateState(string endpoint,int port,bool automatic,bool serving,UpdateWindow[] windows);
 string SaveUpdateState(){
  var selected=Allowed().Select(w=>new UpdateWindow(w.Handle,w.Pid,System.Diagnostics.Process.GetProcessById(w.Pid).StartTime.ToUniversalTime().Ticks)).ToArray();
  return System.Text.Json.JsonSerializer.Serialize(new UpdateState(endpoint.Text,(int)port.Value,automatic.Checked,server!=null,selected));
 }
 void RestoreUpdateState(string json){try{
  var saved=System.Text.Json.JsonSerializer.Deserialize<UpdateState>(json)??throw new IOException("Invalid update state");endpoint.Text=saved.endpoint;port.Value=saved.port;automatic.Checked=saved.automatic;
  foreach(var w in TerminalWindows.Read())if(saved.windows.Any(old=>old.handle==w.Handle&&old.pid==w.Pid&&System.Diagnostics.Process.GetProcessById(w.Pid).StartTime.ToUniversalTime().Ticks==old.started))shares.Check(w,true);
  RefreshWindows();if(saved.serving){start.PerformClick();if(server==null)updateRestored=false;}
 }catch(Exception e){updateRestored=false;Log("업데이트 후 연결 설정 복원 확인 필요: "+e.Message);}}
 static string LocalAddress(){try{return Dns.GetHostAddresses(Dns.GetHostName()).First(a=>a.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(a)).ToString();}catch{return "";}}
 static Button Button(string text)=>ConnectorTheme.Button(text);
 void RefreshWindows(bool force=true){
  var current=TerminalWindows.Read(force);var allowed=shares.Update(current);
  var groups=TerminalWindows.Groups(current);
  string signature=string.Join(";",groups.Select(w=>TerminalWindows.Key(w)+":"+current.Count(x=>x.Pid==w.Pid)+":"+allowed.Any(x=>x.Pid==w.Pid)));
  lock(sync)permitted=allowed;
  if(!force&&signature==windowSignature)return;windowSignature=signature;
  updatingWindows=true;windows.BeginUpdate();try{windows.Items.Clear();foreach(var w in groups){int count=current.Count(x=>x.Pid==w.Pid);var label=w with{Title=w.Process+" · 창 "+count+"개"};windows.Items.Add(label,allowed.Any(x=>x.Pid==w.Pid));}}finally{windows.EndUpdate();updatingWindows=false;}
 }
 void UpdateAllowed(){lock(sync)permitted=shares.Update(TerminalWindows.Read());}
 List<TerminalWindow> Allowed(){lock(sync)return permitted.ToList();}
 void UpdatePeer()=>peer.Text=identity.Config.PeerFingerprint==null?"등록된 휴대폰 없음":"등록된 휴대폰: "+identity.Config.PeerName;
 void Log(string message){if(IsDisposed)return;BeginInvoke((Action)(()=>{if(logs.TextLength>12000)logs.Clear();logs.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+message+Environment.NewLine);UpdatePeer();}));}
 Task<bool> Approve(string name,string fingerprint){
  var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  BeginInvoke((Action)(()=>completion.TrySetResult(ConnectorDialog.Confirm(ActiveForm??this,$"이 휴대폰을 등록할까요?\n\n이름: {name}\n기기 인증서: {fingerprint[..16]}\n\n휴대폰에 표시된 값과 비교하세요.","새 휴대폰 등록","휴대폰 등록"))));
  return completion.Task;
 }
 void ShowPairing(string code){
  try{using var dialog=PairingDialog(code,(int)port.Value);dialog.ShowDialog(this);}finally{identity.ClosePairing();}
 }
 internal static ConnectorForm PairingDialog(string code,int tcpPort){
  var dialog=new ConnectorForm{Text="휴대폰 등록 · 5분 안에 스캔",ClientSize=new Size(480,700),MinimumSize=new Size(480,700),MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false};
  var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,Padding=new Padding(24),WrapContents=false,AutoScroll=true};
  using var qr=new QRCodeGenerator();using var data=qr.CreateQrCode(code,QRCodeGenerator.ECCLevel.M);using var png=new PngByteQRCode(data);
  using var stream=new MemoryStream(png.GetGraphic(5));using var image=Image.FromStream(stream);var bitmap=new Bitmap(image);dialog.Disposed+=(_,_)=>bitmap.Dispose();
  panel.Controls.Add(ConnectorTheme.Label("휴대폰 등록",18));
  panel.Controls.Add(ConnectorTheme.Label("앱에서 QR 등록을 선택하고 스캔하세요.\n외부 접속: 공유기 TCP "+tcpPort+" → 이 PC "+tcpPort,10,ConnectorTheme.Muted));
  panel.Controls.Add(new PictureBox{Image=bitmap,SizeMode=PictureBoxSizeMode.Zoom,Width=420,Height=340,BackColor=Color.White,Margin=new Padding(0,8,0,16)});
  var field=new TextBox{Text=code,ReadOnly=true,Multiline=true,Width=396,Height=48};var fieldPanel=ConnectorTheme.Field(field);fieldPanel.Height=68;fieldPanel.Margin=new Padding(0,0,0,12);panel.Controls.Add(fieldPanel);
  var copy=Button("등록 코드 복사");copy.Click+=(_,_)=>Clipboard.SetText(code);panel.Controls.Add(copy);
  var note=ConnectorTheme.Label("등록 코드에는 일회용 비밀값이 포함됩니다.\n등록된 휴대폰은 이후 자동으로 인증됩니다.",9,ConnectorTheme.Muted);note.Margin=new Padding(0,12,0,0);panel.Controls.Add(note);
  dialog.Body.Controls.Add(panel);return dialog;
 }
}
