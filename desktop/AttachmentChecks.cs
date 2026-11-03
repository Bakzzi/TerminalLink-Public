using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace TerminalLink;
internal static class AttachmentChecks {
 internal static string Envelope(string caption,params string[] paths)=>string.Join("\n",paths.Select((p,i)=>$"<image name=[Image #{i+1}] path=\"{p}\">\n</image>"))+"\n"+string.Join(" ",paths.Select((_,i)=>$"[Image #{i+1}]"))+(caption.Length>0?" "+caption:"");
 internal static void Run(Action<bool,string> check){
  var fileReceipt=new SentEchoes.Receipt("files",1,"확인",["보고서.pdf"],-1);
  string fileBody=ImageInput.FilePrompt("확인",[new("/tmp/terminallink-attachments-v2-1000/"+new string('a',32)+"/0.file.pdf","보고서.pdf")],"원격");
  check(SentEchoes.Matches(fileBody,fileReceipt),"remote document original replaces its immediate display receipt");
  check(!SentEchoes.Matches(fileBody,fileReceipt with{Files=["different.pdf"]}),"different remote document cannot consume a display receipt");
  const string remote="/tmp/terminallink-attachments-1000/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/0.png";
  const string other="/tmp/terminallink-attachments-1000/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/0.png";
  const string local="C:/fixture/attachments/photo.png";
  const string caption="사진을 확인해 줘.\n\n두 번째 줄과 [Image #1] 설명도 그대로.";
  string raw=Envelope(caption,remote);
  var receipt=new SentEchoes.Receipt("t",1,caption,new[]{"screenshot.png"},-1,new[]{remote});
  var projected=AttachmentMessage.Read(raw);
  check(projected.Caption==caption&&projected.ImageCount==1&&projected.ImagePaths.Single()==remote,"remote envelope decodes only its transport prefix and preserves the complete caption");
  check(SentEchoes.Matches(raw,receipt),"remote image envelope matches the successful send with its delivered image path");
  check(!SentEchoes.Matches(Envelope(caption,other),receipt)&&!SentEchoes.Matches(caption,receipt)&&!SentEchoes.Matches(Envelope(caption+" changed",remote),receipt),"different image path, missing image or changed caption cannot consume a send receipt");
  check(SentEchoes.Matches("[Image #1] "+caption,receipt)&&SentEchoes.Matches(Envelope(caption,local),receipt with{ImagePaths=new[]{@"c:\fixture\attachments\photo.png"}}),"local marker-only records and Windows path separators reconcile the same attachment");
  check(!AttachmentMessage.SamePath("/tmp/Photo.png","/tmp/photo.png"),"remote image paths retain case-sensitive identity");
  check(!projected.Display.Contains("<image")&&!projected.Display.Contains(remote)&&projected.Display==caption+"\n\n📎 이미지 1","restored remote messages show caption and attachment label without transport XML or file path");
  foreach(string text in new[]{"```xml\n"+raw+"\n```","> "+raw,"Show this example:\n"+raw,raw.Replace("</image>","not a closing tag"),raw.Replace("[Image #1] path", "[Image #2] path")})
   check(AttachmentMessage.Read(text).Display==text,"quoted, fenced, embedded or malformed image examples are preserved");
  var two=receipt with{Files=new[]{"first.png","second.jpg"},ImagePaths=new[]{remote,other}};
  check(SentEchoes.Matches(Envelope(caption,remote,other),two)&&!SentEchoes.Matches(raw,two),"multiple images require the same count and ordered delivered paths");
  check(SentEchoes.Matches(Envelope("",remote),receipt with{Text=""})&&AttachmentMessage.Read(Envelope("",remote)).Display=="📎 이미지 1","image-only sends reconcile and remain visible without an empty bubble");
  var mixed=receipt with{Files=new[]{"photo.png","notes.txt"}};
  string files=ImageInput.FilePrompt(caption,new[]{new FileUploads.Attachment("C:/fixture/notes.txt","notes.txt")});
  check(SentEchoes.Matches(Envelope(files,remote),mixed)&&!SentEchoes.Matches(raw,mixed),"mixed image/file receipts require the file reference as well as the image");
  var fileOnly=receipt with{Files=new[]{"notes.txt"},ImagePaths=Array.Empty<string>()};
  check(SentEchoes.Matches(files,fileOnly)&&!SentEchoes.Matches(caption,fileOnly),"file-only receipt cannot match a plain caption without its attachment");
  var target=new ConsoleTarget(new(0,0,"fixture","WindowsTerminal"),1,1,"fixture");var frame=new ConsoleFrame("fixture","",80,25,0,0,Array.Empty<int>());
  const string literal="[Image #1] is part of my caption";
  var literalTab=new DesktopTab(target);var initial=new Conversation("literal",new[]{new ConversationMessage("assistant","final","Earlier",1)},false);literalTab.Accept(frame,initial);
  literalTab.ConfirmSent(initial.id,1,literal,receipt.Files,new[]{remote});
  check(literalTab.Messages[^1].Text==literal+"\n\n📎 screenshot.png","attachment echo does not reinterpret literal image markers in the user's caption");
  literalTab.Accept(frame,initial with{messages=initial.messages.Append(new("user","",Envelope(literal,remote),2)).ToArray()});
  check(literalTab.Messages.Count==2&&literalTab.Messages[^1].Text==literal+"\n\n📎 screenshot.png"&&SentEchoes.Matches("[Image #1] "+literal,receipt with{Text=literal}),"confirmed remote and local captions retain literal image markers without duplicate attachments");
  foreach(string source in new[]{"record","remote"}){
   string body=source=="record"?"[Image #1] "+caption:raw;
   var original=new Conversation("thread-"+source,new[]{new ConversationMessage("assistant","final","Earlier answer",1)},false,source:source);
   var final=original with{messages=original.messages.Append(new("user","",body,2)).Append(new("assistant","commentary","Working",3)).ToArray()};
   var tab=new DesktopTab(target);tab.Accept(frame,original);tab.ConfirmSent(original.id,1,caption,receipt.Files,receipt.ImagePaths);var first=tab.Messages[0];
   check(tab.Messages.Count==2&&tab.Messages[^1].Text.Contains("screenshot.png"),source+" attachment echo appears immediately");
   tab.Accept(frame,final);
   check(tab.Messages.Count==3&&tab.Messages.Count(m=>m.Mine)==1&&tab.Messages[1].Sequence==2&&tab.Messages[1].Text==caption+"\n\n📎 screenshot.png"&&ReferenceEquals(first,tab.Messages[0]),source+" original replaces the echo once and retains the selected filename and earlier row");
   var row=tab.Messages[1];tab.Accept(frame,final with{});
   check(ReferenceEquals(row,tab.Messages[1])&&final.messages[1].text==body,source+" repeated snapshots preserve display objects and leave original message bytes unchanged");
   var early=new DesktopTab(target);early.Accept(frame,final);early.ConfirmSent(original.id,1,caption,receipt.Files,receipt.ImagePaths);
   check(early.Messages.Count(m=>m.Mine)==1,source+" record arriving before send acknowledgement does not create a second bubble");
   tab.ConfirmSent(original.id,3,caption,receipt.Files,receipt.ImagePaths);var repeated=final with{messages=final.messages.Append(new("user","",body,4)).ToArray()};tab.Accept(frame,repeated);
   check(tab.Messages.Count(m=>m.Mine)==2&&tab.Messages.Where(m=>m.Mine).All(m=>m.Sequence>0),source+" two genuine identical sends remain two separate messages");
   var reopened=new DesktopTab(target);reopened.Accept(frame,repeated);
   check(reopened.Messages.Count(m=>m.Mine)==2&&reopened.Messages.Where(m=>m.Mine).All(m=>m.Text==caption+"\n\n📎 이미지 1"),source+" fresh tabs render historical attachment records without an echo receipt");
   tab.Accept(frame,original with{id="different-thread",messages=new[]{new ConversationMessage("user","",body,2)}});
   check(tab.Messages.Count==1&&tab.Messages[0].Text==caption+"\n\n📎 이미지 1",source+" conversation switch cannot reuse another thread filename or pending echo");
  }
  var feed=new CodexFeed();
  feed.AddRecord(JsonSerializer.SerializeToUtf8Bytes(new{type="response_item",payload=new{type="message",role="user",content=new object[]{new{type="input_text",text=$"<image name=[Image #1] path=\"{local}\">"},new{type="input_image",image_url="data:image/png;base64,synthetic"},new{type="input_text",text="</image>"},new{type="input_text",text="[Image #1] "+caption}}}}));feed.Publish();
  check(new ChatRow(feed.Snapshot!.messages.Single()).Text==caption+"\n\n📎 이미지 1","unchanged local Codex record reader feeds the same attachment presentation");
 }
}
public partial class DesktopWindow {
 internal async Task CheckAttachmentUi(){
  var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception(name);passed.Add("PASS "+name);}
  try{
   if(!demo)throw new InvalidOperationException("Synthetic attachment check only");
   SentEchoChecks.Run(Check);AttachmentChecks.Run(Check);
   var tab=selected!;string thread=tab.Original!.id;long after=tab.Original.messages[^1].sequence;
   const string path="/tmp/terminallink-attachments-1000/cccccccccccccccccccccccccccccccc/0.png",caption="첨부한 화면을 확인해 줘.\n질문과 사진은 하나의 메시지로 보여야 해.";
   Composer.Text=caption;tab.Attachments.Add("capture.png");AttachmentLabel();int sends=0;DemoSend=()=>{sends++;return Task.CompletedTask;};
   await Send();Check(sends==1&&Composer.Text==""&&tab.Messages[^1].Text.Contains("capture.png"),"UI acknowledgement immediately shows one attachment message and clears the sent draft");
   var source=tab.Original! with{messages=tab.Original.messages.Append(new("user","",AttachmentChecks.Envelope(caption,path),after+1)).Append(new("assistant","commentary","사진을 확인하고 있어요.",after+2)).ToArray(),source="remote"};
   tab.Accept(tab.Frame!,source);RenderState();Bottom();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);UpdateLayout();
   Check(tab.Messages.Count(m=>m.Mine&&m.Text.Contains(caption))==1&&tab.Messages[^2].Sequence>0&&!tab.Messages[^2].Text.Contains("<image")&&tab.Messages[^2].Text.Contains("capture.png"),"rendered remote record replaces its echo while progress remains visible");
   DialogSnapshot(this,"desktop-attachment-preview.png");
  }catch(Exception e){passed.Add("FAIL "+e);Environment.ExitCode=1;}
  finally{File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"desktop-attachment-checks.txt"),passed);Close();}
 }
}
