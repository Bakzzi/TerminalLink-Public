namespace TerminalLink;

// Only reads are retried. No paste, Enter, focus change or history lookup happens
// while waiting for the terminal's cursor and freshly drawn composer to agree.
internal static class NativeComposer {
 // Current Codex draws the live composer with U+00BB; earlier builds used
 // U+203A. Normalize only a line's prompt prefix, never submitted content.
 internal static ConsoleFrame Normalize(ConsoleFrame frame)=>frame with{text=System.Text.RegularExpressions.Regex.Replace(frame.text,@"(?m)^(\s*)»", "$1›")};
 internal static void WaitEmpty(Func<ConsoleFrame> read,Action<ConsoleFrame> current,Action? wait=null){
  wait??=()=>Thread.Sleep(100);
  for(int attempt=0;attempt<8;attempt++){
   var frame=read();current(frame);
   try{NotificationInput.EmptyMain(frame);return;}
   catch(IOException e){
    // This error means actual non-placeholder text was observed at the prompt.
    // Preserve it immediately; refreshing must never clear or replace that text.
    if(e.Message=="PC에 작성 중인 내용이 있습니다. 앱에서 확인하세요.")throw;
    if(attempt==7)throw new IOException("터미널의 빈 입력칸을 확인하지 못했습니다. 작성한 글은 유지됩니다. 잠시 후 다시 보내 주세요.",e);
   }
   wait();
  }
 }
}
