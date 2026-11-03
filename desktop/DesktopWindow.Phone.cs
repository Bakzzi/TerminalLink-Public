using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Media=System.Windows.Media;

namespace TerminalLink;
public partial class DesktopWindow {
 bool phoneView,phoneAttaching,phoneClosing,phoneClosed,phoneScrollChanging;
 ConnectorHost? phoneHost;
 ConnectorDockLease? phoneLease;
 Task phoneWork=Task.CompletedTask;
 readonly DispatcherTimer phoneHealth=new(){Interval=TimeSpan.FromSeconds(2)};
 internal Func<Task<ConnectorWindow?>>? DemoConnector;
 void InitializePhoneLink(){
  PhoneLinkButton.Click+=(_,_)=>phoneWork=ShowPhoneLink();
  PhoneLinkRetry.Click+=(_,_)=>phoneWork=ShowPhoneLink(true);
  PhoneHorizontal.ValueChanged+=(_,_)=>{if(phoneHost!=null&&!phoneScrollChanging){phoneHost.Horizontal=PhoneHorizontal.Value;phoneHost.Fit();}};
  PhoneVertical.ValueChanged+=(_,_)=>{if(phoneHost!=null&&!phoneScrollChanging){phoneHost.Vertical=PhoneVertical.Value;phoneHost.Fit();}};
  phoneHealth.Tick+=async(_,_)=>{
   if(closing||phoneAttaching||phoneClosing)return;
   if(phoneLease?.Healthy==true){phoneHost?.Fit();return;}
   // Adopt a connector that was started/replaced independently without navigating
   // away from the conversation or creating a second phone connection.
   phoneWork=EnsurePhoneLink(false,false);await phoneWork;
  };
 }
 void PhoneStatus(string text){PhoneLinkMessage.Text=DesktopErrorText.Display(text);PhoneLinkStatus.Visibility=Visibility.Visible;PhoneLinkRetry.IsEnabled=!phoneAttaching;PhoneHorizontal.Visibility=PhoneVertical.Visibility=Visibility.Collapsed;}
 void SetPhoneView(bool show){
  phoneView=show;
  // Retain the conversation selection for draft/scroll restoration, but the
  // active navigation highlight belongs only to the page currently displayed.
  Tabs.Tag=show?"phone-page":null;
  // Hidden reserves the existing layout. Do not replace, clear or reload chat controls.
  ConversationPane.Visibility=show?Visibility.Hidden:Visibility.Visible;
  PhoneLinkPane.Visibility=show?Visibility.Visible:Visibility.Hidden;
  PhoneLinkButton.Background=(Media.Brush)new Media.BrushConverter().ConvertFromString(show?"#34514F":"#282C30")!;
  if(show)requestWindow?.Close();
  else Dispatcher.BeginInvoke(AcknowledgeVisibleReply,DispatcherPriority.ContextIdle);
 }
 Task ShowPhoneLink(bool allowStart=true)=>EnsurePhoneLink(true,allowStart);
 async Task EnsurePhoneLink(bool show,bool allowStart){
  if(closing||phoneClosing)return;if(show)SetPhoneView(true);if(phoneAttaching)return;
  if(phoneLease?.Healthy==true){PhoneLinkStatus.Visibility=Visibility.Collapsed;phoneHost!.Visibility=Visibility.Visible;phoneHost.Fit();return;}
  phoneAttaching=true;PhoneStatus("연결 프로그램을 확인하고 있어요.");
  try{
   if(phoneLease!=null){if(phoneHost!=null)phoneHost.Target=null;await phoneLease.Detach(true);phoneLease=null;}
   ConnectorWindow? target=demo?(DemoConnector==null?null:await DemoConnector()):await Task.Run(ConnectorNative.Find);
   if(target==null&&!demo&&allowStart){
    if(!File.Exists(ConnectorNative.Installed))throw new IOException("TerminalLink 연결 프로그램을 먼저 설치해 주세요.");
    if(!await Task.Run(ConnectorNative.ProcessRunning))Process.Start(new ProcessStartInfo(ConnectorNative.Installed){UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden});
    for(int i=0;i<25&&target==null&&!closing;i++){await Task.Delay(200);target=await Task.Run(ConnectorNative.Find);}
   }
   if(target==null){PhoneLinkRetry.Content=demo?"다시 확인":"연동 시작";PhoneStatus(demo?"미리보기에서는 실제 휴대폰 연결을 변경하지 않습니다.":"휴대폰 연동을 시작하면 이 화면에서 연결을 설정할 수 있어요.");return;}
   if(phoneHost==null){
    phoneHost=new ConnectorHost();phoneHost.ExtentChanged+=(x,y,w,h)=>{
     phoneScrollChanging=true;
     PhoneHorizontal.Maximum=x;PhoneVertical.Maximum=y;PhoneHorizontal.ViewportSize=w;PhoneVertical.ViewportSize=h;
     PhoneHorizontal.Value=phoneHost.Horizontal;PhoneVertical.Value=phoneHost.Vertical;
     PhoneHorizontal.Visibility=x>0.5?Visibility.Visible:Visibility.Collapsed;PhoneVertical.Visibility=y>0.5?Visibility.Visible:Visibility.Collapsed;
     phoneScrollChanging=false;
    };PhoneHostSlot.Child=phoneHost;
   }
   phoneHost.Visibility=Visibility.Visible;UpdateLayout();await Dispatcher.Yield(DispatcherPriority.Loaded);
   phoneLease=await ConnectorDockLease.Attach(target,phoneHost.Container);
   phoneHost.Target=target;phoneHost.Fit();PhoneLinkStatus.Visibility=Visibility.Collapsed;PhoneLinkRetry.Content="다시 확인";phoneHealth.Start();
  }catch(Exception e){if(phoneHost!=null){phoneHost.Visibility=Visibility.Hidden;phoneHost.Target=null;}PhoneStatus(e.Message);}
  finally{phoneAttaching=false;PhoneLinkRetry.IsEnabled=true;}
 }
 async Task ClosePhoneLink(bool background=false){
  phoneHealth.Stop();await phoneWork;
  if(phoneHost!=null)phoneHost.Target=null;
  if(phoneLease!=null){await phoneLease.Detach(background);phoneLease=null;}
  if(phoneHost!=null){phoneHost.Target=null;phoneHost.Dispose();PhoneHostSlot.Child=null;phoneHost=null;}
 }
}
