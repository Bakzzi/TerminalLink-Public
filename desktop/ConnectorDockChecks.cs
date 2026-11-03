using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace TerminalLink;
internal static class ConnectorDockChecks {
 sealed class FixtureForm:Forms.Form {
  protected override bool ShowWithoutActivation=>true;
  protected override Forms.CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000000;return p;}}
 }
 internal static void Fixture(){
  ApplicationConfiguration.Initialize();
  using var input=new StreamReader(Console.OpenStandardInput());using var output=new StreamWriter(Console.OpenStandardOutput()){AutoFlush=true};
  using var form=new FixtureForm{Text="TerminalLink isolated connector fixture",Width=880,Height=810,MinimumSize=new(760,740),StartPosition=Forms.FormStartPosition.Manual,Location=new(-5000,-5000),ShowInTaskbar=false,FormBorderStyle=Forms.FormBorderStyle.None,BackColor=Color.FromArgb(21,23,25),Padding=new(1)};
  var caption=new Forms.Panel{Dock=Forms.DockStyle.Top,Height=37};var body=new Forms.Panel{Dock=Forms.DockStyle.Fill,Padding=new(24)};form.Controls.Add(body);form.Controls.Add(caption);
  var field=new Forms.TextBox{Text="fixture draft",Location=new(24,100),Width=300};body.Controls.Add(field);
  body.Controls.Add(new Forms.Label{Text=">_< TerminalLink · 휴대폰 연동 시험 화면",ForeColor=Color.White,AutoSize=true,Location=new(24,24)});
  Forms.Form? dialog=null;
  form.Shown+=(_,_)=>{
   using var self=Process.GetCurrentProcess();var target=new ConnectorWindow(form.Handle.ToInt64(),self.Id,self.StartTime.ToUniversalTime().Ticks,Environment.ProcessPath!,true);output.WriteLine(JsonSerializer.Serialize(target));
   _=Task.Run(()=>{string? line;while((line=input.ReadLine())!=null){string command=line;form.BeginInvoke(()=>{
    if(command=="close"){dialog?.Close();form.Close();}
    if(command=="probe")output.WriteLine(field.Text);
    if(command=="dialog"){
     dialog=new FixtureForm{Text="Isolated pairing fixture",Width=320,Height=200,ShowInTaskbar=false,StartPosition=Forms.FormStartPosition.CenterParent};
     dialog.Shown+=(_,_)=>output.WriteLine("modal:"+dialog.Handle.ToInt64());dialog.ShowDialog(form);dialog.Dispose();dialog=null;output.WriteLine("modal closed");
    }
    if(command=="cancel")dialog?.Close();
   });}});
  };
  Forms.Application.Run(form);
 }
 internal static void CrashHost(){
  // The supervisor kills ONLY this isolated fixture to verify pipe-loss recovery.
  using var input=new StreamReader(Console.OpenStandardInput());using var output=new StreamWriter(Console.OpenStandardOutput()){AutoFlush=true};
  var target=JsonSerializer.Deserialize<ConnectorWindow>(input.ReadLine()!)!;
  var app=new System.Windows.Application();var win=new Window{Width=1000,Height=800,Left=-5000,Top=-5000,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual};var host=new ConnectorHost();win.Content=host;
  win.Loaded+=async(_,_)=>{try{await Dispatcher.Yield(DispatcherPriority.Loaded);var lease=await ConnectorDockLease.Attach(target,host.Container);host.Target=target;host.Fit();output.WriteLine("attached");GC.KeepAlive(lease);}catch(Exception e){output.WriteLine(e.ToString());win.Close();}};app.Run(win);
 }
 internal static async Task<(Process process,ConnectorWindow target)> Start(){
  var p=Process.Start(DesktopProgram.Worker("--connector-fixture"))??throw new IOException("Fixture did not start");_=p.StandardError.ReadToEndAsync();
  var line=await p.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));return(p,JsonSerializer.Deserialize<ConnectorWindow>(line!)!);
 }
 internal static async Task Stop(Process process){
  if(!process.HasExited){await process.StandardInput.WriteLineAsync("close");await process.StandardInput.FlushAsync();await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}process.Dispose();
 }
}

public partial class DesktopWindow {
 internal async Task CheckPhoneUi(){
  var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new IOException(name);passed.Add("PASS "+name);}
  try{await Dispatcher.Yield(DispatcherPriority.Loaded);await CheckPhoneLink(Check);}
  catch(Exception e){passed.Add("FAIL "+e);Environment.ExitCode=1;}
  finally{File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"desktop-phone-checks.txt"),passed);Close();}
 }
 async Task CheckPhoneLink(Action<bool,string> check){
  string draft=Composer.Text;var fixture=await ConnectorDockChecks.Start();Process? crash=null;var original=ConnectorNative.Capture(fixture.target);
  try{
   var tab=selected!;Composer.Text="Preserved phone page draft";var rows=tab.Messages.ToArray();var source=Messages.ItemsSource;var originalFrame=tab.Frame;var originalThread=tab.Original;
   UpdateLayout();var row=(System.Windows.Controls.ListBoxItem)Tabs.ItemContainerGenerator.ContainerFromItem(tab);row.ApplyTemplate();
   var background=(System.Windows.Controls.Border)row.Template.FindName("item",row);
   check(background.Background.ToString()=="#FF292E32","conversation page highlights its selected terminal tab");
   DemoConnector=()=>Task.Run(ConnectorNative.FindFixture);
   await EnsurePhoneLink(false,false);await Task.Delay(100);UpdateLayout();
   check(phoneLease?.Healthy==true&&!phoneView&&!ConnectorNative.IsWindowVisible(new(fixture.target.Handle)),"startup adopts and hides the existing connector without opening the phone page");
   await ShowPhoneLink();await Task.Delay(100);UpdateLayout();
   check(phoneLease?.Healthy==true&&ConnectorNative.GetParent(new(fixture.target.Handle))==IntPtr.Zero&&ConnectorNative.GetProp(new(fixture.target.Handle),ConnectorNative.Marker)==new IntPtr(Environment.ProcessId),"phone page hosts the exact connector window while keeping its lifetime independent: "+(phoneLease==null?PhoneLinkMessage.Text:"attached"));
   check(phoneView&&ConversationPane.Visibility==Visibility.Hidden&&PhoneLinkPane.Visibility==Visibility.Visible&&FileBoxButton.IsVisible&&PhoneLinkButton.IsVisible,"phone link replaces the full right pane while preserving the sidebar");
   WindowState=WindowState.Minimized;await Task.Delay(100);
   check(inTray&&!ConnectorNative.IsWindowVisible(new(fixture.target.Handle))&&ConnectorNative.Same(fixture.target),"tray minimize hides the hosted connector while its exact process stays alive");
   RestoreFromTray();await Task.Delay(100);
   check(phoneView&&phoneLease?.Healthy==true&&ConnectorNative.IsWindowVisible(new(fixture.target.Handle)),"tray restore keeps the phone page and reuses the same hosted connector");
   check(background.Background.ToString()=="#00FFFFFF"&&selected==tab&&Tabs.SelectedItem==tab,"phone page clears the terminal highlight without discarding conversation state");
   var before=tab.HasUnread;AcknowledgeVisibleReply();check(tab.HasUnread==before,"hidden conversation is not acknowledged as read");
   SetPhoneView(false);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   check(!ConnectorNative.IsWindowVisible(new(fixture.target.Handle)),"conversation view hides the connector window while keeping the same process");
   check(background.Background.ToString()=="#FF292E32","returning to conversation restores its terminal highlight");
   check(!phoneView&&ReferenceEquals(source,Messages.ItemsSource)&&rows.SequenceEqual(tab.Messages)&&ReferenceEquals(originalFrame,tab.Frame)&&ReferenceEquals(originalThread,tab.Original)&&Composer.Text=="Preserved phone page draft","return to conversation preserves draft, original records and message controls without reload");
   await ShowPhoneLink();check(phoneLease?.Target==fixture.target&&ConnectorNative.IsWindowVisible(new(fixture.target.Handle)),"reopening phone page reuses the same connector window");
   double width=Width,height=Height;Width=840;Height=570;UpdateLayout();await Task.Delay(150);UpdateLayout();
   check(PhoneHorizontal.Visibility==Visibility.Visible&&PhoneVertical.Visibility==Visibility.Visible,"small desktop exposes scrolling instead of cutting off connector controls");
   Width=width;Height=height;UpdateLayout();await Task.Delay(100);
   await fixture.process.StandardInput.WriteLineAsync("dialog");await fixture.process.StandardInput.FlushAsync();
   string? modal=await fixture.process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3));
   check(modal?.StartsWith("modal:")==true&&!ConnectorNative.IsWindowEnabled(new(fixture.target.Handle)),"connector modal dialog opens and disables its own controls while docked");
   await fixture.process.StandardInput.WriteLineAsync("cancel");await fixture.process.StandardInput.FlushAsync();
   check(await fixture.process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3))=="modal closed","connector dialog cancellation returns to the same embedded window");
   await ClosePhoneLink();SetPhoneView(false);
   check(ConnectorNative.Same(fixture.target)&&ConnectorNative.GetParent(new(fixture.target.Handle))==IntPtr.Zero&&ConnectorNative.GetStyle(new(fixture.target.Handle),-16).ToInt64()==original.Style,"normal detach restores connector window and styles without terminating it");
   await fixture.process.StandardInput.WriteLineAsync("probe");await fixture.process.StandardInput.FlushAsync();
   check(await fixture.process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3))=="fixture draft","connector input state survives docking and restoration");
   await EnsurePhoneLink(false,false);await ClosePhoneLink(true);await Task.Delay(100);
   check(ConnectorNative.Same(fixture.target)&&!ConnectorNative.IsWindowVisible(new(fixture.target.Handle))&&ConnectorNative.GetProp(new(fixture.target.Handle),ConnectorNative.Marker)==IntPtr.Zero,"normal app exit releases the dock but leaves the connector alive and hidden");
   await EnsurePhoneLink(false,false);await ShowPhoneLink();await Task.Delay(100);
   check(phoneLease?.Healthy==true&&phoneLease.Target==fixture.target&&ConnectorNative.IsWindowVisible(new(fixture.target.Handle)),"next app session reuses the background connector without starting a duplicate");
   await ClosePhoneLink();SetPhoneView(false);
   crash=Process.Start(DesktopProgram.Worker("--connector-crash-fixture"))!;_=crash.StandardError.ReadToEndAsync();
   await crash.StandardInput.WriteLineAsync(JsonSerializer.Serialize(fixture.target));await crash.StandardInput.FlushAsync();
   string? crashReady=await crash.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));check(crashReady=="attached","isolated crash fixture attached before forced-exit validation: "+crashReady);
   crash.Kill();await crash.WaitForExitAsync();
   for(int i=0;i<50&&ConnectorNative.GetProp(new(fixture.target.Handle),ConnectorNative.Marker)!=IntPtr.Zero;i++)await Task.Delay(50);
   check(ConnectorNative.Same(fixture.target)&&ConnectorNative.GetParent(new(fixture.target.Handle))==IntPtr.Zero&&ConnectorNative.GetProp(new(fixture.target.Handle),ConnectorNative.Marker)==IntPtr.Zero,"desktop process crash restores the existing connector without closing its window");
   DemoConnector=()=>Task.FromResult<ConnectorWindow?>(null);await ShowPhoneLink();
   check(PhoneLinkStatus.Visibility==Visibility.Visible&&PhoneLinkRetry.IsEnabled&&phoneLease==null,"missing connector has a retry state instead of a blank or endless loading page");
   SetPhoneView(false);
  }finally{await ClosePhoneLink();if(crash!=null){if(!crash.HasExited)crash.Kill();crash.Dispose();}await ConnectorDockChecks.Stop(fixture.process);DemoConnector=null;SetPhoneView(false);Composer.Text=draft;}
 }
}
