using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
namespace TerminalLink;

internal record Frame(long Version,byte[] Png,int Width,int Height);
internal sealed class WindowCapture : IDisposable {
 [ComImport,Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IItemInterop {
  IntPtr CreateForWindow(IntPtr window,ref Guid iid);
  IntPtr CreateForMonitor(IntPtr monitor,ref Guid iid);
 }
 [DllImport("combase.dll",CharSet=CharSet.Unicode)] static extern int WindowsCreateString(string s,int n,out IntPtr h);
 [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr h);
 [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr name,ref Guid iid,out IntPtr factory);
 [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adapter,int type,IntPtr software,uint flags,IntPtr levels,uint count,uint sdk,out IntPtr device,out int level,out IntPtr context);
 [DllImport("d3d11.dll")] static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgi,out IntPtr device);
 readonly IDirect3DDevice device;
 readonly GraphicsCaptureItem item;
 readonly Direct3D11CaptureFramePool pool;
 readonly GraphicsCaptureSession session;
 volatile bool stopped;
 int busy;
 long serial,lastFrame;
 internal Frame? Latest;
 internal string? Error;
 internal WindowCapture(IntPtr hwnd) {
  if(!GraphicsCaptureSession.IsSupported())throw new NotSupportedException("Windows 창 캡처를 지원하지 않습니다.");
  Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero,1,IntPtr.Zero,0x20,IntPtr.Zero,0,7,out var d,out _,out var ctx));
  try {
   var iid=new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
   Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d,in iid,out var dxgi));
   try {Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi,out var abi));try{device=WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(abi);}finally{Marshal.Release(abi);}}
   finally{Marshal.Release(dxgi);}
  } finally{Marshal.Release(ctx);Marshal.Release(d);}
  const string runtime="Windows.Graphics.Capture.GraphicsCaptureItem";
  WindowsCreateString(runtime,runtime.Length,out var name);
  try {
   var iid=new Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
   Marshal.ThrowExceptionForHR(RoGetActivationFactory(name,ref iid,out var f));
   try {
    var factory=(IItemInterop)Marshal.GetObjectForIUnknown(f);
    var itemId=new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
    var abi=factory.CreateForWindow(hwnd,ref itemId);
    try{item=WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(abi);}finally{Marshal.Release(abi);}
   } finally{Marshal.Release(f);}
  } finally{WindowsDeleteString(name);}
  pool=Direct3D11CaptureFramePool.CreateFreeThreaded(device,DirectXPixelFormat.B8G8R8A8UIntNormalized,2,item.Size);
  session=pool.CreateCaptureSession(item);session.IsCursorCaptureEnabled=true;
  item.Closed+=(_,_)=>{Error="터미널 창이 닫혔습니다.";stopped=true;Latest=null;};
  pool.FrameArrived+=Arrived;
  session.StartCapture();
 }
 async void Arrived(Direct3D11CaptureFramePool sender,object args) {
  if(stopped)return;
  if(Interlocked.CompareExchange(ref busy,1,0)!=0){try{using var dropped=sender.TryGetNextFrame();}catch{}return;}
  try {
   using var frame=sender.TryGetNextFrame();
   if(frame==null)return;
   var size=frame.ContentSize;
   if(size.Width<=0||size.Height<=0||size.Width>4096||size.Height>2160){Error="지원 범위를 벗어난 창 크기";return;}
   if(Environment.TickCount64-lastFrame<100)return;
   lastFrame=Environment.TickCount64;
   using var sw=await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface,BitmapAlphaMode.Ignore);
   int width=Math.Min(size.Width,sw.PixelWidth),height=Math.Min(size.Height,sw.PixelHeight);
   var buffer=new Windows.Storage.Streams.Buffer((uint)(sw.PixelWidth*sw.PixelHeight*4));
   sw.CopyToBuffer(buffer);byte[] bytes=new byte[buffer.Length];
   using(var reader=DataReader.FromBuffer(buffer))reader.ReadBytes(bytes);
   using var bmp=new Bitmap(width,height,PixelFormat.Format32bppRgb);
   var data=bmp.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppRgb);
   try{for(int y=0;y<height;y++)Marshal.Copy(bytes,y*sw.PixelWidth*4,data.Scan0+y*data.Stride,width*4);}finally{bmp.UnlockBits(data);}
   using var output=new MemoryStream();bmp.Save(output,ImageFormat.Png);
   if(!stopped)Latest=new(Interlocked.Increment(ref serial),output.ToArray(),width,height);
   Error=null;
   if(size.Width!=sw.PixelWidth||size.Height!=sw.PixelHeight)pool.Recreate(device,DirectXPixelFormat.B8G8R8A8UIntNormalized,2,size);
  } catch(Exception e){if(!stopped)Error="캡처 오류: "+e.Message;}
  finally{Volatile.Write(ref busy,0);}
 }
 public void Dispose() {
  stopped=true;pool.FrameArrived-=Arrived; SpinWait.SpinUntil(()=>Volatile.Read(ref busy)==0,2000);session.Dispose();pool.Dispose();device.Dispose();Latest=null;
 }
}