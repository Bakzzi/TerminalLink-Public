using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
namespace TerminalLink;

// Same-user connector IPC. Desktop does not own another request journal/server.
internal static class RequestClient {
 internal static async Task<JsonElement> Send(JsonElement command,CancellationToken token){
  string name="TerminalLink.Requests.v1."+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User!.Value)))[..24];
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(55));
  await using var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
  try{await pipe.ConnectAsync(2000,deadline.Token);}catch(Exception e)when(e is IOException or TimeoutException or OperationCanceledException){throw new RequestNotSentException("PC 연결 프로그램의 요청 연결을 확인해 주세요. ‘휴대폰 연동’에서 연결 대기를 켜면 됩니다.");}
  byte[] data=JsonSerializer.SerializeToUtf8Bytes(new{Version=1,Operation="native",Native=command}),size=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(size,data.Length);
  await pipe.WriteAsync(size,deadline.Token);await pipe.WriteAsync(data,deadline.Token);await pipe.FlushAsync(deadline.Token);
  await pipe.ReadExactlyAsync(size,deadline.Token);int length=BinaryPrimitives.ReadInt32LittleEndian(size);if(length<1||length>4*1024*1024)throw new IOException("요청 응답 형식이 올바르지 않습니다.");
  data=new byte[length];await pipe.ReadExactlyAsync(data,deadline.Token);using var reply=JsonDocument.Parse(data);
  if(!reply.RootElement.TryGetProperty("Native",out var result)||result.ValueKind!=JsonValueKind.Object)throw new IOException("PC 연결 프로그램을 업데이트해 주세요.");
  return result.Clone();
 }
}
internal sealed class RequestPendingException(string message):IOException(message);
internal sealed class RequestNotSentException(string message):IOException(message);
