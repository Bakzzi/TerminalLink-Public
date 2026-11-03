using System.Net;
using System.Net.Sockets;
using QRCoder;
namespace TerminalLink;
internal sealed class MainForm : Form {
 readonly Identity identity=new();
 readonly CheckedListBox windows=new(){Dock=DockStyle.Fill,CheckOnClick=true,BackColor=Color.FromArgb(20,27,39),ForeColor=Color.White,BorderStyle=BorderStyle.FixedSingle};
 readonly TextBox endpoint=new(){Width=230};
 readonly NumericUpDown port=new(){Minimum=1024,Maximum=65535,Width=90,Value=49443};
 readonly Label state=new(){AutoSize=true,Text="공유할 창을 선택하고 연결 대기를 시작하세요.",ForeColor=Color.LightSkyBlue};
 readonly Label peer=new(){AutoSize=true};
 readonly TextBox logs=new(){Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,BackColor=Color.FromArgb(20,27,39),ForeColor=Color.Gainsboro,BorderStyle=BorderStyle.None};
 readonly object sync=new();
 List<TerminalWindow> permitted=new();
 RemoteServer? server;
 readonly WindowShareSelection shares=new();readonly System.Windows.Forms.Timer discovery=new(){Interval=2000};
 readonly CheckBox automatic=new(){Text="새 터미널 창 자동 공유",AutoSize=true};bool updatingWindows;
 string windowSignature="";
 readonly Button start=Button("연결 대기 시작");
 internal MainForm(string[]? launchArgs=null){
  Text="TerminalLink · PC";Width=880;Height=780;MinimumSize=new Size(700,650);Font=new Font("맑은 고딕",10);BackColor=Color.FromArgb(12,18,29);ForeColor=Color.White;
  var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=9};
  root.RowStyles.Add(new(SizeType.Absolute,45));root.RowStyles.Add(new(SizeType.Absolute,48));root.RowStyles.Add(new(SizeType.Absolute,40));
  root.RowStyles.Add(new(SizeType.Percent,55));root.RowStyles.Add(new(SizeType.Absolute,56));root.RowStyles.Add(new(SizeType.Absolute,52));
  root.RowStyles.Add(new(SizeType.Absolute,38));root.RowStyles.Add(new(SizeType.Absolute,38));root.RowStyles.Add(new(SizeType.Percent,45));
  root.Controls.Add(new Label{Text="TerminalLink",Font=new Font("Segoe UI",23,FontStyle.Bold),AutoSize=true},0,0);
  root.Controls.Add(new Label{Text="실행 중인 터미널을 휴대폰의 텍스트 화면으로 읽고 입력합니다.\n같은 Windows Terminal 프로세스의 창들은 함께 공유됩니다. 기존 작업은 유지됩니다.",AutoSize=true},0,1);
  var refresh=Button("터미널 목록 새로고침");refresh.Click+=(_,_)=>RefreshWindows();var discoveryRow=new FlowLayoutPanel{Dock=DockStyle.Fill};discoveryRow.Controls.Add(refresh);discoveryRow.Controls.Add(automatic);
  root.Controls.Add(discoveryRow,0,2);root.Controls.Add(windows,0,3);
  var addressRow=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(0,8,0,0)};
  addressRow.Controls.Add(new Label{Text="접속 주소",AutoSize=true,Padding=new Padding(0,6,0,0)});addressRow.Controls.Add(endpoint);
  addressRow.Controls.Add(new Label{Text="TCP 포트",AutoSize=true,Padding=new Padding(10,6,0,0)});addressRow.Controls.Add(port);root.Controls.Add(addressRow,0,4);
  var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};actions.Controls.Add(start);
  var pairing=Button("휴대폰 등록 · QR");var revoke=Button("등록 해제");actions.Controls.Add(pairing);actions.Controls.Add(revoke);root.Controls.Add(actions,0,5);
  root.Controls.Add(state,0,6);root.Controls.Add(peer,0,7);root.Controls.Add(logs,0,8);Controls.Add(root);
  endpoint.Text=string.IsNullOrWhiteSpace(identity.Config.Endpoint)?LocalAddress():identity.Config.Endpoint;port.Value=identity.Config.Port;
  shares.Automatic=identity.Config.AutoShareTerminals||launchArgs?.Contains("--auto-share")==true;
  automatic.Checked=shares.Automatic;
  automatic.CheckedChanged+=(_,_)=>{shares.Automatic=automatic.Checked;if(shares.Automatic)shares.EnableAll(TerminalWindows.Read());identity.Config.AutoShareTerminals=shares.Automatic;identity.Save();RefreshWindows();};
  windows.ItemCheck+=(_,e)=>{if(updatingWindows)return;shares.Check((TerminalWindow)windows.Items[e.Index],e.NewValue==CheckState.Checked);BeginInvoke((Action)UpdateAllowed);};
  start.Click+=(_,_)=>{
   if(server!=null){server.Dispose();server=null;start.Text="연결 대기 시작";state.Text="연결 대기 중지됨";return;}
   UpdateAllowed();lock(sync){if(permitted.Count==0&&!shares.Automatic){MessageBox.Show(this,"공유할 터미널을 하나 이상 체크하세요.");return;}}
   try{identity.Config.Port=(int)port.Value;identity.Config.Endpoint=endpoint.Text.Trim();identity.Save();server=new(identity,Allowed,Approve,Log);server.Start((int)port.Value);start.Text="연결 대기 중지";state.Text="연결 대기 중 · 등록된 휴대폰만 접근";}
   catch(Exception ex){server?.Dispose();server=null;MessageBox.Show(this,ex.Message);}
  };
  pairing.Click+=(_,_)=>{
   if(server==null){MessageBox.Show(this,"먼저 연결 대기를 시작하세요.");return;}
   if(identity.Config.PeerFingerprint!=null){MessageBox.Show(this,"이미 등록된 휴대폰이 있습니다. 교체하려면 등록을 해제하세요.");return;}
   if(string.IsNullOrWhiteSpace(endpoint.Text)||endpoint.Text.Contains('/')||endpoint.Text.Contains(' ')){MessageBox.Show(this,"공인 IP, 도메인 또는 같은 망의 PC 주소를 입력하세요.");return;}
   string code=identity.OpenPairing(endpoint.Text.Trim(),(int)port.Value);ShowPairing(code);
  };
  revoke.Click+=(_,_)=>{if(MessageBox.Show(this,"등록된 휴대폰의 접속 권한을 해제할까요?","등록 해제",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;identity.Revoke();server?.Disconnect();UpdatePeer();Log("휴대폰 등록 해제됨");};
  FormClosing+=(_,_)=>{discovery.Stop();discovery.Dispose();server?.Dispose();identity.Dispose();};
  discovery.Tick+=(_,_)=>RefreshWindows(false);discovery.Start();
  RefreshWindows();UpdatePeer();
  if(launchArgs?.Contains("--setup-session")==true)Shown+=(_,_)=>BeginInvoke((Action)(()=>{
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
   }catch(Exception e){MessageBox.Show(this,e.Message,"초기 설정 확인");}
  }));
 }
 static string LocalAddress(){try{return Dns.GetHostAddresses(Dns.GetHostName()).First(a=>a.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(a)).ToString();}catch{return "";}}
 static Button Button(string text)=>new(){Text=text,AutoSize=true,Padding=new Padding(8,5,8,5),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(32,47,67),ForeColor=Color.White,Margin=new Padding(0,2,10,2)};
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
  BeginInvoke((Action)(()=>completion.TrySetResult(MessageBox.Show(ActiveForm??this,$"이 휴대폰을 등록할까요?\n\n이름: {name}\n기기 인증서: {fingerprint[..16]}\n\n휴대폰에 표시된 값과 비교하세요.","새 휴대폰 등록",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)));
  return completion.Task;
 }
 void ShowPairing(string code){
  using var dialog=new Form{Text="휴대폰 등록 · 5분 안에 스캔",Width=510,Height=660,StartPosition=FormStartPosition.CenterParent,BackColor=BackColor,ForeColor=ForeColor,Font=Font};
  var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,Padding=new Padding(20),WrapContents=false,AutoScroll=true};
  using var qr=new QRCodeGenerator();using var data=qr.CreateQrCode(code,QRCodeGenerator.ECCLevel.M);using var png=new PngByteQRCode(data);
  using var stream=new MemoryStream(png.GetGraphic(5));using var image=Image.FromStream(stream);
  panel.Controls.Add(new Label{Text="Android 앱에서 QR 등록을 선택하세요.\n외부 접속: 공유기 TCP "+port.Value+" → 이 PC "+port.Value+"\n등록 후 접속 주소를 변경할 수 있습니다.",AutoSize=true,Margin=new Padding(0,0,0,12)});
  panel.Controls.Add(new PictureBox{Image=image,SizeMode=PictureBoxSizeMode.Zoom,Width=430,Height=370,BackColor=Color.White});
  var field=new TextBox{Text=code,ReadOnly=true,Multiline=true,Width=430,Height=55};panel.Controls.Add(field);
  var copy=Button("등록 코드 복사");copy.Click+=(_,_)=>Clipboard.SetText(code);panel.Controls.Add(copy);
  panel.Controls.Add(new Label{Text="등록 코드에는 일회용 비밀값이 포함됩니다.\n등록된 휴대폰은 이후 자동으로 인증됩니다.",AutoSize=true});
  dialog.Controls.Add(panel);dialog.ShowDialog(this);identity.ClosePairing();
 }
}