namespace TerminalLink;
internal static class NativeComposerChecks {
 internal static void Run(Action<bool,string> check){
  var empty=new ConsoleFrame("fixture","\n› Ask Codex to do anything\n? for shortcuts",100,3,2,1,[]);
  int reads=0,waits=0;
  NativeComposer.WaitEmpty(()=>{reads++;return empty;},_=>{},()=>waits++);
  check(reads==1&&waits==0,"ready native composer has no added polling delay");
  var current=empty with{text=new string('\n',58)+"» Ask Codex to do anything\n? for shortcuts",columns=188,rows=62,cursorX=2,cursorY=58};
  reads=waits=0;NativeComposer.WaitEmpty(()=>{reads++;return current;},_=>{},()=>waits++);
  check(reads==1&&waits==0,"observed U+00BB composer at row 58 is accepted on its first read");
  reads=waits=0;
  NativeComposer.WaitEmpty(()=>++reads<3?empty with{text="\n\n? for shortcuts"}:empty,_=>{},()=>waits++);
  check(reads==3&&waits==2,"blank repaint frames are reread before submitting");
  reads=waits=0;
  NativeComposer.WaitEmpty(()=>++reads==1?empty with{cursorY=0}:empty,_=>{},()=>waits++);
  check(reads==2&&waits==1,"cursor metadata from an older row does not claim PC typing");
  reads=waits=0;
  bool blocked=false;try{NativeComposer.WaitEmpty(()=>{reads++;return empty with{text="\n› Existing draft\n? for shortcuts"};},_=>{},()=>waits++);}catch(IOException e){blocked=e.Message.Contains("작성 중");}
  check(blocked&&reads==1&&waits==0,"visible PC draft remains protected without retrying input");
  reads=waits=0;blocked=false;
  try{NativeComposer.WaitEmpty(()=>{reads++;return empty with{text="\n\n? for shortcuts"};},_=>{},()=>waits++);}catch(IOException e){blocked=!e.Message.Contains("사용 중");}
  check(blocked&&reads==8&&waits==7,"persistent unreadable input stops with an honest bounded failure");
  reads=waits=0;blocked=false;
  try{NativeComposer.WaitEmpty(()=>{reads++;return empty with{text="\n\n? for shortcuts"};},_=>{if(reads==2)throw new IOException("owner changed");},()=>waits++);}catch(IOException e){blocked=e.Message=="owner changed";}
  check(blocked&&reads==2,"native target and request guards run again on every pre-send read");
 }
}
