import ctypes as C, ctypes.wintypes as W, os, subprocess, threading
k=C.WinDLL('kernel32',use_last_error=True)
H=W.HANDLE
class Coord(C.Structure):_fields_=[('x',C.c_short),('y',C.c_short)]
class SI(C.Structure):
 _fields_=[('cb',W.DWORD),('reserved',W.LPWSTR),('desktop',W.LPWSTR),('title',W.LPWSTR),('x',W.DWORD),('y',W.DWORD),('xs',W.DWORD),('ys',W.DWORD),('xc',W.DWORD),('yc',W.DWORD),('fill',W.DWORD),('flags',W.DWORD),('show',W.WORD),('rlen',W.WORD),('r2',C.c_void_p),('i',H),('o',H),('e',H)]
class SIX(C.Structure):_fields_=[('info',SI),('attrs',C.c_void_p)]
class PI(C.Structure):_fields_=[('process',H),('thread',H),('pid',W.DWORD),('tid',W.DWORD)]
k.CreatePipe.argtypes=[C.POINTER(H),C.POINTER(H),C.c_void_p,W.DWORD]
k.CreatePseudoConsole.argtypes=[Coord,H,H,W.DWORD,C.POINTER(H)]
k.InitializeProcThreadAttributeList.argtypes=[C.c_void_p,W.DWORD,W.DWORD,C.POINTER(C.c_size_t)]
k.UpdateProcThreadAttribute.argtypes=[C.c_void_p,W.DWORD,C.c_size_t,C.c_void_p,C.c_size_t,C.c_void_p,C.c_void_p]
k.CreateProcessW.argtypes=[W.LPCWSTR,W.LPWSTR,C.c_void_p,C.c_void_p,W.BOOL,W.DWORD,C.c_void_p,W.LPCWSTR,C.POINTER(SIX),C.POINTER(PI)]
k.ReadFile.argtypes=[H,C.c_void_p,W.DWORD,C.POINTER(W.DWORD),C.c_void_p]
k.WriteFile.argtypes=[H,C.c_void_p,W.DWORD,C.POINTER(W.DWORD),C.c_void_p]
k.CloseHandle.argtypes=[H];k.ClosePseudoConsole.argtypes=[H]
k.TerminateProcess.argtypes=[H,W.UINT];k.WaitForSingleObject.argtypes=[H,W.DWORD]
k.GetExitCodeProcess.argtypes=[H,C.POINTER(W.DWORD)]
k.DeleteProcThreadAttributeList.argtypes=[C.c_void_p]
def check(ok):
 if not ok:raise C.WinError(C.get_last_error())
class Terminal:
 def __init__(self,args,cwd,env):
  ir,iw,orr,ow=H(),H(),H(),H();check(k.CreatePipe(C.byref(ir),C.byref(iw),None,0));check(k.CreatePipe(C.byref(orr),C.byref(ow),None,0))
  self.pc=H();assert k.CreatePseudoConsole(Coord(110,40),ir,ow,0,C.byref(self.pc))==0
  k.CloseHandle(ir);k.CloseHandle(ow);self.input=iw;self.output=orr
  size=C.c_size_t();k.InitializeProcThreadAttributeList(None,1,0,C.byref(size));attrs=C.create_string_buffer(size.value)
  check(k.InitializeProcThreadAttributeList(attrs,1,0,C.byref(size)))
  check(k.UpdateProcThreadAttribute(attrs,0,0x20016,self.pc,C.sizeof(H),None,None))
  si=SIX();si.info.cb=C.sizeof(si);si.attrs=C.cast(attrs,C.c_void_p);self.pi=PI()
  envbuf=C.create_unicode_buffer('\0'.join(f'{key}={value}' for key,value in sorted(env.items()))+'\0\0')
  cmd=C.create_unicode_buffer(subprocess.list2cmdline(args))
  try:check(k.CreateProcessW(None,cmd,None,None,False,0x80000|0x400,envbuf,str(cwd),C.byref(si),C.byref(self.pi)))
  finally:k.DeleteProcThreadAttributeList(attrs)
  k.CloseHandle(self.pi.thread);self.chunks=[]
  threading.Thread(target=self.read,daemon=True).start()
 def write(self,data):
  n=W.DWORD();check(k.WriteFile(self.input,data,len(data),C.byref(n),None))
 def read(self):
  buf=C.create_string_buffer(16384);n=W.DWORD()
  while k.ReadFile(self.output,buf,len(buf),C.byref(n),None) and n.value:
   data=buf.raw[:n.value];self.chunks.append(data)
   if b'\x1b[6n' in data:
    try:self.write(b'\x1b[1;1R')
    except OSError:pass
 def alive(self):
  code=W.DWORD();check(k.GetExitCodeProcess(self.pi.process,C.byref(code)));return code.value==259
 def close(self):
  # Terminate only the child created by this isolated fixture.
  if self.alive():k.TerminateProcess(self.pi.process,0);k.WaitForSingleObject(self.pi.process,3000)
  k.CloseHandle(self.pi.process);k.ClosePseudoConsole(self.pc);k.CloseHandle(self.input);k.CloseHandle(self.output)
