namespace TerminalLink;

// Exception messages from the worker can contain a complete SSH command, its
// arguments, and private record paths. They are diagnostics, not UI copy.
internal static class DesktopErrorText {
 internal const string RemoteTimeout="원격 대화 확인 시간이 초과됐습니다. 전송 결과는 대화에서 확인해 주세요. 작성한 내용은 유지하며 자동으로 다시 보내지 않습니다.";
 internal const string Unexpected="처리 중 오류가 발생했습니다. 작성한 내용은 유지됩니다. 상태를 다시 확인해 주세요.";
 internal static string Display(string? value){
  string text=value?.Trim()??"";if(text.Length==0)return "";
  bool command=text.StartsWith("Command '",StringComparison.OrdinalIgnoreCase)||text.StartsWith("Command \"",StringComparison.OrdinalIgnoreCase)||text.Contains("python3 -c ",StringComparison.OrdinalIgnoreCase);
  if(command)return text.Contains("timed out",StringComparison.OrdinalIgnoreCase)||text.Contains("timeout",StringComparison.OrdinalIgnoreCase)?RemoteTimeout:"원격 대화 확인에 실패했습니다. 상태를 다시 확인해 주세요. 작성한 내용은 유지됩니다.";
  if(text.Length>400||text.Count(c=>c=='\n')>4||text.Contains("Traceback (most recent call last)",StringComparison.Ordinal)||text.Contains(" at Renci.SshNet.",StringComparison.Ordinal))return Unexpected;
  if(text=="사진을 보낼 대화가 바뀌었거나 아직 연결되지 않았습니다.")return "전송할 대화의 연결을 확인하지 못했습니다. 작성한 내용은 유지됩니다. 잠시 후 다시 확인해 주세요.";
  if(text=="현재 Codex 대화를 확인하지 못했습니다. 터미널의 최신 대화를 확인하세요.")return "현재 터미널과 대화 원본이 일치하는지 확인하지 못해 전송하지 않았습니다. 작성한 내용은 유지됩니다.";
  text=text.Replace("사진을 보낼", "전송할").Replace("사진 전송을 중단했습니다.", "전송을 중단했습니다.");
  return text;
 }
}
