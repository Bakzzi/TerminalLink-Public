using System.Text.Json;
namespace TerminalLink;
internal static class AttachmentTransferChecks {
 internal static void Run(Action<bool,string> check){
  ColdPreparation(check);
  string hash=new('A',64),dir="/tmp/terminallink-attachments-v2-1000/"+new string('a',32);
  var mixed=new RemoteImages.Upload(null!,dir,[new("0.png",12,hash),new("1.file.pdf",19,hash,false,"보고서.pdf")]);
  RemoteImages.Validate(mixed);
  var pasted=new List<string>();int entered=0,verified=0;var drafts=new List<(int,string)>();
  RemoteImages.Deliver(mixed,"확인해 줘",()=>verified++,(n,t)=>drafts.Add((n,t)),pasted.Add,()=>entered++);
  check(entered==1&&pasted.Count==2&&pasted[0]==dir+"/0.png"&&pasted[1].Contains("- 보고서.pdf: \""+dir+"/1.file.pdf\"")&&pasted[1].Contains("원격 저장 경로"),"mixed SSH upload attaches image and references the remote document once");
  check(drafts[^1]==(1,pasted[1])&&verified>=3,"mixed attachment verifies complete native draft before Enter");
  var fileOnly=new RemoteImages.Upload(null!,dir,[new("0.file.txt",0,hash,false,"empty.txt")]);
  pasted.Clear();entered=0;
  RemoteImages.Deliver(fileOnly,"",()=>{},(n,t)=>{if(n!=0)throw new Exception("document became image");},pasted.Add,()=>entered++);
  check(entered==1&&pasted.Count==1&&pasted[0].Contains("첨부한 파일을 확인해 줘.")&&fileOnly.ImagePaths.Length==0,"file-only and empty file submit as a document rather than a photo");
  bool Reject(Action action){try{action();return false;}catch(IOException){return true;}}
  check(Reject(()=>RemoteImages.Validate(mixed with{photos=[new("../secret",1,hash,false,"x.pdf")]})),"remote path traversal is rejected");
  check(Reject(()=>RemoteImages.Validate(fileOnly with{photos=[new("0.file.pdf",FileUploads.MaxBytes+1L,hash,false,"x.pdf")]})),"oversize remote file is rejected before upload");
  check(Reject(()=>RemoteImages.Validate(fileOnly with{photos=[new("0.file.txt",1,hash,false,"bad\nname.txt")]})),"file display name cannot add a prompt line");
  check(Reject(()=>RemoteImages.Validate(fileOnly with{directory="/tmp/outside/"+new string('a',32)})),"remote reference remains inside private attachment storage");
  entered=0;int attempts=0;
  check(Reject(()=>RemoteImages.Deliver(mixed,"caption",()=>{if(++attempts==2)throw new IOException("changed identity");},(_,_)=>{},_=>{},()=>entered++))&&entered==0,"identity change during attachment never presses Enter");
  var old=JsonSerializer.Deserialize<RemoteImages.Photo>("{\"name\":\"0.png\",\"size\":1,\"sha256\":\""+hash+"\"}")!;
  check(old.image&&old.title=="","legacy image metadata still deserializes as an image");
  check(ImageInput.FilePrompt("caption",[new("C:\\fixture\\x.pdf","x.pdf")]).StartsWith("caption\n\n첨부 파일 (PC 저장 경로):\n"),"existing local file prompt is unchanged");
  check(RemoteImages.FileName(0,"자료.PDF")=="0.file.pdf"&&RemoteImages.FileName(1,"strange.$(cmd)")=="1.file","remote storage names are generated and preserve safe file extensions");
  // Run the actual local/SSH delivery, wait and draft parsers together. Previous
  // tests replaced draft validation with an accepting stub and missed U+00BB.
  foreach(string prefix in new[]{"›","»"})foreach(bool remote in new[]{false,true}){
   string native="";int imageCount=0,enterCount=0,reads=0,lag=1;var writes=new List<string>();
   ConsoleFrame Frame(){reads++;if(lag-->0)return new("fixture","\n\n? for shortcuts",188,62,2,58,[]);
    string value=string.Join("",Enumerable.Repeat("[Image #1] ",imageCount))+native;
    string rendered=prefix+" "+(value.Length==0?"Ask Codex to do anything":value);
    string[] lines=rendered.Split('\n');
    return new("fixture",rendered,188,62,value.Length==0?2:lines[^1].Length,lines.Length-1,[]);
   }
   void Draft(int n,string t)=>ImageInput.WaitDraft(Frame,n,t,()=>{},()=>{});
   void Paste(string value){writes.Add(value);if(value.EndsWith("0.png",StringComparison.Ordinal))imageCount++;else native=value;lag=1;}
   for(int pass=0;pass<2;pass++){
    native="";imageCount=0;
    if(remote)RemoteImages.Deliver(mixed,"첨부 확인 👋",()=>{},Draft,Paste,()=>enterCount++);
    else ImageInput.Deliver(["C:\\fixture\\0.png"],ImageInput.FilePrompt("첨부 확인 👋",[new("C:\\fixture\\report.pdf","report.pdf")]),()=>{},Draft,Paste,()=>enterCount++);
   }
   check(enterCount==2&&writes.Count==4&&reads>4,$"{prefix} {(remote?"SSH":"local")} image+file+caption first attempt and consecutive send survive repaint with no duplicate paste");
  }
  int sent=0;bool blocked=Reject(()=>ImageInput.Deliver(["C:\\fixture\\0.png"],"caption",()=>{},(n,t)=>ImageInput.WaitDraft(()=>new("fixture","» PC draft",188,62,2,0,[]),n,t,()=>{},()=>{}),_=>sent++,()=>sent++));
  check(blocked&&sent==0,"attachments do not overwrite an actual existing draft");
  sent=0;int observations=0;
  blocked=Reject(()=>ImageInput.WaitDraft(()=>{observations++;return new("fixture","», redraw unavailable",188,62,2,58,[]);},0,"",()=>{},()=>{}));
  check(blocked&&observations==8,"persistent unreadable attachment composer stops before input with bounded reads");
 }
 static void ColdPreparation(Action<bool,string> check){
  string thread="11111111-1111-4111-8111-111111111111";
  var record=new Conversation(thread,[],false);
  var frame=new ConsoleFrame("fixture","» Ask Codex to do anything",188,62,2,0,[],"console-start");
  foreach(int delay in new[]{0,6900,7100,11000,18000,28000}){
   long time=0;int pastes=0,enters=0;bool corroborated=false;
   LocalImageTarget.Connect(()=>frame,thread,f=>f.session,_=>false,_=>time>=delay?record:null,
    (_,id)=>corroborated=id==thread,()=>time>=delay,()=>time,()=>time+=100);
   ImageInput.Deliver(["one.png","two.png","three.png"],"caption",()=>{},(_,_)=>{},_=>pastes++,()=>enters++);
   check(corroborated&&time==delay&&pastes==4&&enters==1,$"Cold attachment catalog ready after {delay}ms completes the first submission once");
  }
  foreach(string failure in new[]{"never-ready","changed-console","shell","other-thread","ambiguous"}){
   long time=0;bool rejected=false;
   try{LocalImageTarget.Connect(()=>frame,thread,_=>failure=="changed-console"&&time>=400?"different-start":frame.session,
    _=>failure=="shell"&&time>=400,_=>time<500||failure=="never-ready"?null:failure=="other-thread"?record with{id="22222222-2222-4222-8222-222222222222"}:record,
    (_,_)=>failure!="ambiguous",()=>failure!="never-ready"&&time>=500,()=>time,()=>time+=100);
   }catch(IOException){rejected=true;}
   check(rejected&&time<=LocalImageTarget.PreparationMilliseconds,$"Preparation remains bounded and preserves original target rejection: {failure}");
  }
  check(LocalImageTarget.PreparationMilliseconds<LocalImageTarget.DeliverySeconds*1000,"Native attachment result deadline includes cold preparation and delivery");
 }
}
