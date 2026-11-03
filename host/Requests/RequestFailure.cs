namespace TerminalLink.Requests;

// Fixed codes only: never persist or display arbitrary terminal/exception content.
internal static class RequestFailure {
 static readonly (string code,string message)[] known=[
  ("prompt-unavailable","요청이 이미 끝났거나 화면이 바뀌었습니다. 앱에서 확인하세요."),
  ("question-changed","다른 질문으로 바뀌었습니다. 새 요청을 확인하세요."),
  ("choices-changed","선택지가 바뀌었습니다. 새 요청을 확인하세요."),
  ("request-draft","PC에서 답변을 작성 중입니다. 앱에서 확인하세요."),
  ("selection-unconfirmed","선택 변경을 확인하지 못했습니다. 앱에서 확인하세요."),
  ("selection-mismatch","선택을 확인하지 못했습니다. 앱에서 확인하세요."),
  ("selection-changed","PC의 선택이 바뀌어 전송하지 않았습니다."),
  ("answer-unconfirmed","요청 답변의 입력을 확인하지 못해 전송하지 않았습니다. 앱에서 확인하세요.")
 ];
 internal static string Code(string message)=>known.FirstOrDefault(x=>x.message==message).code??"RejectedBeforeWrite";
 internal static string Message(string code)=>known.FirstOrDefault(x=>x.code==code).message??"전송하지 못했습니다. 작성한 답변은 유지됩니다. 다시 확인해 주세요.";
}
