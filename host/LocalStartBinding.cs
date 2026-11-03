namespace TerminalLink;

internal static partial class LocalDaemonFeed {
 internal static Catalog CaptureStart(){Refresh();lock(gate){if(!catalog.available||Daemon()?.key!=catalog.daemon)throw new IOException("새 대화 연결을 확인하지 못했습니다. 잠시 후 다시 시도하세요.");return catalog;}}
 internal static Conversation? BindStart(Catalog before,ConsoleFrame frame,string text){
  string identity=Identity(frame);if(identity.Length==0||ShellActive(frame)||Anchors(frame).Length==0)return null;
  Refresh();Catalog current;lock(gate)current=catalog;
  if(!current.available||current.daemon!=before.daemon||Daemon()?.key!=current.daemon)return null;
  var original=MatchStart(before,current,frame,text);if(original==null)return null;
  var fresh=ReloadVerified(original,current.daemon);if(fresh==null||!Compatible(frame,fresh.conversation))return null;
  lock(gate){bindings[identity]=fresh.conversation.id;Saved.Remember(identity,current.daemon,fresh.conversation);}
  return fresh.conversation;
 }
 internal static Candidate? MatchStart(Catalog before,Catalog current,ConsoleFrame frame,string text){
  if(Anchors(frame).Length==0)return null;
  var matches=StartCandidates(before,current,text).Where(c=>Compatible(frame,c.conversation)).ToArray();
  return matches.Length==1?matches[0]:null;
 }
 internal static Candidate[] StartCandidates(Catalog before,Catalog current,string text){
  if(!before.available||!current.available||before.daemon!=current.daemon)return Array.Empty<Candidate>();
  return current.candidates.Where(c=>{
   var previous=before.candidates.Where(p=>p.conversation.id==c.conversation.id).ToArray();
   if(previous.Length>1||previous.Any(p=>p.conversation.messages.Length>0||p.conversation.requests?.Length>0))return false;
   var users=c.conversation.messages.Where(m=>m.role=="user").ToArray();
   return users.Length==1&&CodexStartInput.Normalize(users[0].text)==CodexStartInput.Normalize(text);
  }).ToArray();
 }
}
