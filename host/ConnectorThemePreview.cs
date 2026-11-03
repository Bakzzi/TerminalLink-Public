namespace TerminalLink;

// Explicit, offline presentation check: no Identity, terminal discovery, server or real registration code.
internal static class ConnectorThemePreview {
 internal static void Run(string directory){
  Directory.CreateDirectory(directory);var checks=new List<string>();
  void Check(bool result,string description){if(!result)throw new InvalidOperationException(description);checks.Add("PASS "+description);}
  void Prepare(ConnectorForm form){form.Preview=true;form.Opacity=0;form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-16000,-16000);}
  void Snapshot(ConnectorForm form,string name){form.PerformLayout();form.Refresh();Application.DoEvents();using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(directory,name+".png"));}
  using(var main=new MainForm(preview:true)){
   Prepare(main);main.Show();Snapshot(main,"connector-main");main.Size=main.MinimumSize;Snapshot(main,"connector-minimum");
   main.MaximizeAction.PerformClick();Application.DoEvents();Check(main.WindowState==FormWindowState.Maximized,"caption maximize works");
   var work=Screen.FromHandle(main.Handle).WorkingArea;var bounds=main.RectangleToScreen(main.ClientRectangle);Check(work.Contains(bounds),"maximized content stays inside the monitor work area");
   main.MaximizeAction.PerformClick();Application.DoEvents();Check(main.WindowState==FormWindowState.Normal,"caption restores the same window");
   main.MinimizeAction.PerformClick();Application.DoEvents();Check(main.WindowState==FormWindowState.Minimized,"caption minimize works");main.Close();
  }
  using(var qr=MainForm.PairingDialog("THEME-PREVIEW-ONLY-NOT-A-REGISTRATION-CODE",49443)){Prepare(qr);qr.Show();Snapshot(qr,"connector-pairing");qr.Close();}
  DialogResult Run(Action<ConnectorDialog> action){
   using var dialog=new ConnectorDialog("휴대폰 등록","이 휴대폰을 등록할까요?\n\n이름: 내 휴대폰\n기기 인증서: 미리보기용 예시\n\n휴대폰에 표시된 값과 비교하세요.",true,"휴대폰 등록");Prepare(dialog);
   Exception? error=null;dialog.Shown+=(_,_)=>dialog.BeginInvoke((Action)(()=>{try{action(dialog);}catch(Exception e){error=e;dialog.Close();}}));var result=dialog.ShowDialog();if(error!=null)throw error;return result;
  }
  Check(Run(dialog=>{Check(dialog.AcceptButton==dialog.DeclineButton,"registration and revoke dialogs default to cancel");Snapshot(dialog,"connector-confirm");dialog.DeclineButton.PerformClick();})!=DialogResult.Yes,"cancel cannot approve registration or revoke");
  Check(Run(dialog=>dialog.CloseAction.PerformClick())!=DialogResult.Yes,"title-bar close cannot approve registration or revoke");
  Check(Run(dialog=>{Check(dialog.CancelButton==dialog.DeclineButton,"Escape maps to cancel");((IButtonControl)dialog.CancelButton!).PerformClick();})!=DialogResult.Yes,"Escape action cannot approve registration or revoke");
  Check(Run(dialog=>dialog.ConfirmButton.PerformClick())==DialogResult.Yes,"only explicit confirmation approves");
  File.WriteAllLines(Path.Combine(directory,"connector-ui-checks.txt"),checks);
 }
}
