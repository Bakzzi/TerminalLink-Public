using System.Windows;
using Forms=System.Windows.Forms;
using Drawing=System.Drawing;

namespace TerminalLink;
public partial class DesktopWindow {
 Forms.NotifyIcon? trayIcon;
 Forms.ContextMenuStrip? trayMenu;
 Drawing.Icon? trayImage;
 bool inTray,trayTaskbar;
 WindowState trayRestoreState=WindowState.Normal;
 void InitializeTray(){
  StateChanged+=(_,_)=>{
   if(closing)return;
   if(WindowState==WindowState.Minimized){MinimizeToTray();return;}
   trayRestoreState=WindowState;
   if(inTray)RestoreFromTray();
  };
  Closed+=(_,_)=>DisposeTray();
 }
 void EnsureTray(){
  if(trayIcon!=null)return;
  try{
   trayImage=DesktopIcon.CreateTrayIcon();
   trayMenu=new Forms.ContextMenuStrip{BackColor=Drawing.Color.FromArgb(35,38,41),ForeColor=Drawing.Color.FromArgb(233,234,237),ShowImageMargin=false};
   trayMenu.Items.Add("열기",null,(_,_)=>Dispatcher.Invoke(RestoreFromTray));
   trayMenu.Items.Add("종료",null,(_,_)=>Dispatcher.Invoke(Close));
   trayIcon=new Forms.NotifyIcon{Text="TerminalLink Desktop",Icon=trayImage,ContextMenuStrip=trayMenu};
   trayIcon.MouseClick+=(_,e)=>{if(e.Button==Forms.MouseButtons.Left)Dispatcher.Invoke(RestoreFromTray);};
  }catch{DisposeTray();throw;}
 }
 void MinimizeToTray(){
  if(inTray||closing)return;
  try{
   EnsureTray();trayIcon!.Visible=true;trayTaskbar=ShowInTaskbar;inTray=true;
   ShowInTaskbar=false;Hide();
  }catch(Exception e){WindowState=trayRestoreState;Error("트레이 아이콘을 만들지 못했습니다. "+e.Message);}
 }
 void RestoreFromTray(){
  if(closing)return;
  if(inTray){var restore=trayRestoreState;inTray=false;ShowInTaskbar=trayTaskbar;Show();WindowState=restore;}
  if(trayIcon!=null)trayIcon.Visible=false;
  if(!demo)Activate();
 }
 void DisposeTray(){
  if(trayIcon!=null){trayIcon.Visible=false;trayIcon.Dispose();trayIcon=null;}
  trayMenu?.Dispose();trayMenu=null;trayImage?.Dispose();trayImage=null;
 }
}
