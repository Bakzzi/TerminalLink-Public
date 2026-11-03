using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TerminalLink;

// The connector remains WinForms; these presentation-only colors mirror Desktop Theme.xaml.
internal static class ConnectorTheme {
 internal static readonly Color Background=Color.FromArgb(21,23,25), Surface=Color.FromArgb(16,18,20),
  Input=Color.FromArgb(35,38,41), ButtonColor=Color.FromArgb(40,44,48), Hover=Color.FromArgb(59,66,72),
  Text=Color.FromArgb(233,234,237), Muted=Color.FromArgb(135,147,154), Accent=Color.FromArgb(115,216,206),
  Primary=Color.FromArgb(57,123,116), Border=Color.FromArgb(48,54,60);
 internal static Label Label(string text,float size=10,Color? color=null)=>new(){Text=text,AutoSize=true,ForeColor=color??Text,Font=new Font("맑은 고딕",size),Margin=new Padding(0,0,0,8)};
 internal static Button Button(string text,bool primary=false)=>new ThemeButton{Text=text,BackColor=primary?Primary:ButtonColor};
 internal static Panel Field(Control input){
  input.BackColor=Input;input.ForeColor=Text;input.Dock=DockStyle.Fill;
  if(input is TextBox text)text.BorderStyle=BorderStyle.None;
  if(input is NumericUpDown number)number.BorderStyle=BorderStyle.None;
  var panel=new Panel{BackColor=Input,Padding=new Padding(12,9,12,8),Height=40,Width=input.Width+24,Margin=new Padding(0,0,12,0)};panel.Controls.Add(input);return panel;
 }
 internal static TableLayoutPanel Card(){
  return new(){Dock=DockStyle.Fill,BackColor=Surface,Padding=new Padding(18),ColumnCount=1,Margin=new Padding(0,0,0,12)};
 }
 internal static GraphicsPath Rounded(Rectangle rectangle,int radius){
  var path=new GraphicsPath();int d=radius*2;
  path.AddArc(rectangle.Left,rectangle.Top,d,d,180,90);path.AddArc(rectangle.Right-d,rectangle.Top,d,d,270,90);
  path.AddArc(rectangle.Right-d,rectangle.Bottom-d,d,d,0,90);path.AddArc(rectangle.Left,rectangle.Bottom-d,d,d,90,90);path.CloseFigure();return path;
 }
}

internal sealed class ThemeButton : Button {
 bool hover,pressed;
 internal ThemeButton(){AutoSize=true;Padding=new Padding(14,9,14,9);Margin=new Padding(0,0,10,0);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;ForeColor=ConnectorTheme.Text;BackColor=ConnectorTheme.ButtonColor;Cursor=Cursors.Hand;UseVisualStyleBackColor=false;}
 protected override void OnMouseEnter(EventArgs e){hover=true;base.OnMouseEnter(e);Invalidate();}
 protected override void OnMouseLeave(EventArgs e){hover=false;pressed=false;base.OnMouseLeave(e);Invalidate();}
 protected override void OnMouseDown(MouseEventArgs e){pressed=true;base.OnMouseDown(e);Invalidate();}
 protected override void OnMouseUp(MouseEventArgs e){pressed=false;base.OnMouseUp(e);Invalidate();}
 protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
 protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
 protected override void OnPaint(PaintEventArgs e){
  e.Graphics.Clear(Parent?.BackColor??ConnectorTheme.Background);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
  var rect=new Rectangle(1,1,Width-3,Height-3);using var path=ConnectorTheme.Rounded(rect,Math.Max(4,7*DeviceDpi/96));
  using var brush=new SolidBrush(!Enabled?ConnectorTheme.Input:pressed?ConnectorTheme.Primary:hover?(Tag as string=="close"?Color.FromArgb(199,68,68):ConnectorTheme.Hover):BackColor);e.Graphics.FillPath(brush,path);
  if(Focused&&ShowFocusCues){using var pen=new Pen(ConnectorTheme.Accent);e.Graphics.DrawPath(pen,path);}
  TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled?ForeColor:ConnectorTheme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
 }
}

internal class ConnectorForm : Form {
 [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
 [DllImport("user32.dll")] static extern bool ReleaseCapture();
 [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
 internal readonly Panel Body=new(){Dock=DockStyle.Fill,BackColor=ConnectorTheme.Background};
 internal readonly Button MinimizeAction,MaximizeAction,CloseAction;
 internal bool AllowResize=true;
 internal bool Preview;
 protected override bool ShowWithoutActivation=>Preview;
 protected override CreateParams CreateParams {get{var cp=base.CreateParams;cp.Style=(cp.Style&~0x00C00000)|0x00040000|0x00080000;return cp;}}
 internal ConnectorForm(){
  BackColor=ConnectorTheme.Border;ForeColor=ConnectorTheme.Text;Font=new Font("맑은 고딕",10);StartPosition=FormStartPosition.CenterParent;ShowIcon=false;DoubleBuffered=true;AutoScaleMode=AutoScaleMode.Dpi;FormBorderStyle=FormBorderStyle.None;Padding=new Padding(1);
  var caption=new Panel{Dock=DockStyle.Top,Height=37,BackColor=ConnectorTheme.Surface};
  var actions=new FlowLayoutPanel{Dock=DockStyle.Right,AutoSize=true,WrapContents=false,Margin=Padding.Empty};
  Button Caption(string text,string name){var button=ConnectorTheme.Button(text);button.AutoSize=false;button.Size=new Size(44,36);button.Margin=Padding.Empty;button.Padding=Padding.Empty;button.BackColor=ConnectorTheme.Surface;button.AccessibleName=name;button.TabStop=false;actions.Controls.Add(button);return button;}
  MinimizeAction=Caption("—","최소화");MaximizeAction=Caption("□","최대화 또는 복원");CloseAction=Caption("×","닫기");CloseAction.Tag="close";
  MinimizeAction.Click+=(_,_)=>WindowState=FormWindowState.Minimized;MaximizeAction.Click+=(_,_)=>ToggleMaximize();CloseAction.Click+=(_,_)=>Close();
  var title=new Label{AutoSize=false,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(14,0,0,0),ForeColor=ConnectorTheme.Muted,Font=new Font("맑은 고딕",9)};TextChanged+=(_,_)=>title.Text=">_<   "+Text;
  void Drag(object? sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);}}
  title.MouseDown+=Drag;caption.MouseDown+=Drag;title.DoubleClick+=(_,_)=>{if(MaximizeBox&&AllowResize)ToggleMaximize();};
  caption.Controls.Add(title);caption.Controls.Add(actions);Controls.Add(Body);Controls.Add(caption);
  Shown+=(_,_)=>{MinimizeAction.Visible=MinimizeBox;MaximizeAction.Visible=MaximizeBox&&AllowResize;};
  SizeChanged+=(_,_)=>MaximizeAction.Text=WindowState==FormWindowState.Maximized?"❐":"□";
 }
 void ToggleMaximize(){MaximizedBounds=Screen.FromHandle(Handle).WorkingArea;WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
 protected override void WndProc(ref Message m){
  if(m.Msg==0x83&&m.WParam!=IntPtr.Zero){m.Result=IntPtr.Zero;return;} // Client-drawn caption.
  base.WndProc(ref m);
  if(m.Msg!=0x84||!AllowResize||WindowState!=FormWindowState.Normal)return;
  long packed=m.LParam.ToInt64();var p=PointToClient(new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff)));int edge=Math.Max(5,6*DeviceDpi/96);
  bool left=p.X<edge,right=p.X>=ClientSize.Width-edge,top=p.Y<edge,bottom=p.Y>=ClientSize.Height-edge;
  int hit=top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:right?11:0;if(hit!=0)m.Result=(IntPtr)hit;
 }
 protected override void OnHandleCreated(EventArgs e){
  base.OnHandleCreated(e);
  // DWM still owns the outside border and rounded corner treatment.
  if(OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)){
   void Set(int attribute,int value)=>DwmSetWindowAttribute(Handle,attribute,ref value,sizeof(int));
   Set(20,1);Set(34,ColorTranslator.ToWin32(ConnectorTheme.Border));Set(35,ColorTranslator.ToWin32(ConnectorTheme.Surface));Set(36,ColorTranslator.ToWin32(ConnectorTheme.Text));
  }
 }
}

internal sealed class ConnectorDialog : ConnectorForm {
 internal readonly Button ConfirmButton,DeclineButton;
 internal ConnectorDialog(string title,string message,bool confirm=false,string accept="확인"){
  Text=title;ClientSize=new Size(500,200);MinimumSize=new Size(400,160);AllowResize=false;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;
  var layout=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,Padding=new Padding(24)};
  var label=ConnectorTheme.Label(message,11);label.MaximumSize=new Size(450,0);label.Margin=new Padding(0,0,0,22);layout.Controls.Add(label);
  var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};
  ConfirmButton=ConnectorTheme.Button(accept,true);ConfirmButton.DialogResult=DialogResult.Yes;actions.Controls.Add(ConfirmButton);
  DeclineButton=ConnectorTheme.Button("취소");DeclineButton.DialogResult=DialogResult.Cancel;DeclineButton.Visible=confirm;actions.Controls.Add(DeclineButton);
  layout.Controls.Add(actions);Body.AutoSize=true;Body.Controls.Add(layout);AcceptButton=confirm?DeclineButton:ConfirmButton;CancelButton=DeclineButton;
  Shown+=(_,_)=>{if(confirm)DeclineButton.Focus();else ConfirmButton.Focus();};
 }
 internal static void Notice(IWin32Window owner,string message,string title="TerminalLink") {using var dialog=new ConnectorDialog(title,message);dialog.ShowDialog(owner);}
 internal static bool Confirm(IWin32Window owner,string message,string title,string accept="확인") {using var dialog=new ConnectorDialog(title,message,true,accept);return dialog.ShowDialog(owner)==DialogResult.Yes;}
}
