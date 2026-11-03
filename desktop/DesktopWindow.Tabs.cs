using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using Wpf=System.Windows.Controls;
using Media=System.Windows.Media;

namespace TerminalLink;
public partial class DesktopWindow {
 const string TabDragFormat="TerminalLink.Desktop.TabOrder.v1";
 readonly TabOrderStore tabOrder;
 bool reorderingTabs,addingTab;
 DesktopTab? pressedTab,draggingTab;
 System.Windows.Point tabPressPoint;
 long lastDragScroll;
 TabDropMarker? dropMarker;
 internal Func<ConsoleTarget,bool>? DemoConfirmAdd;
 internal Func<ConsoleTarget,Task<ConsoleTarget>>? DemoOpenTab;
 internal Func<ConsoleTarget,bool>? DemoConfirmClose;
 internal Func<ConsoleTarget,Task>? DemoCloseTab;

 void InitializeTabs(){
  AddTabButton.Click+=async(_,_)=>await Action(AddTab);
  Tabs.AllowDrop=true;
  Tabs.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Up or Key.Down or Key.Home or Key.End or Key.Enter)SetPhoneView(false);};
  Tabs.PreviewMouseLeftButtonDown+=(_,e)=>{
   if(IsTabButton(e.OriginalSource as DependencyObject))return;
   pressedTab=TabAt(e.GetPosition(Tabs))?.DataContext as DesktopTab;if(pressedTab==null)return;
   tabPressPoint=e.GetPosition(Tabs);Tabs.CaptureMouse();e.Handled=true;
  };
  Tabs.PreviewMouseLeftButtonUp+=(_,e)=>{
   if(pressedTab==null)return;
   var clicked=pressedTab;pressedTab=null;Tabs.ReleaseMouseCapture();
   if(clicked!=null){if(TabAt(e.GetPosition(Tabs))?.DataContext==clicked){SetPhoneView(false);Tabs.SelectedItem=clicked;}e.Handled=true;}
  };
  Tabs.LostMouseCapture+=(_,_)=>pressedTab=null;
  Tabs.PreviewMouseMove+=(_,e)=>{
   if(pressedTab==null||e.LeftButton!=MouseButtonState.Pressed)return;var point=e.GetPosition(Tabs);
   if(Math.Abs(point.X-tabPressPoint.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-tabPressPoint.Y)<SystemParameters.MinimumVerticalDragDistance)return;
   draggingTab=pressedTab;pressedTab=null;Tabs.ReleaseMouseCapture();
   try{System.Windows.DragDrop.DoDragDrop(Tabs,new System.Windows.DataObject(TabDragFormat,draggingTab.Id),System.Windows.DragDropEffects.Move);}
   finally{draggingTab=null;ClearDropMarker();}
   e.Handled=true;
  };
  Tabs.PreviewDragOver+=(_,e)=>{
   e.Handled=true;e.Effects=System.Windows.DragDropEffects.None;
   if(!OwnTabDrag(e)){ClearDropMarker();return;}
   var point=e.GetPosition(Tabs);var viewer=FindScroll(Tabs);
   if(Environment.TickCount64-lastDragScroll>120&&viewer!=null){
    if(point.Y<24)viewer.LineUp();else if(point.Y>Tabs.ActualHeight-24)viewer.LineDown();
    lastDragScroll=Environment.TickCount64;Tabs.UpdateLayout();
   }
   var drop=TabDropAt(point);ShowDropMarker(drop.y);e.Effects=System.Windows.DragDropEffects.Move;
  };
  Tabs.PreviewDragLeave+=(_,_)=>ClearDropMarker();
  Tabs.PreviewDrop+=(_,e)=>{
   e.Handled=true;e.Effects=System.Windows.DragDropEffects.None;
   if(OwnTabDrag(e)){MoveTab(draggingTab!,TabDropAt(e.GetPosition(Tabs)).index);e.Effects=System.Windows.DragDropEffects.Move;}
   ClearDropMarker();
  };
  UpdateTabControls();
 }
 static bool IsTabButton(DependencyObject? element){
  while(element!=null&&element is not Wpf.ListBoxItem){if(element is Wpf.Primitives.ButtonBase)return true;element=element is Media.Visual?Media.VisualTreeHelper.GetParent(element):LogicalTreeHelper.GetParent(element);}return false;
 }
 async void CloseTabClicked(object sender,RoutedEventArgs e){e.Handled=true;if(sender is Wpf.Button{DataContext:DesktopTab tab})await Action(()=>CloseTab(tab));}
 async Task CloseTab(DesktopTab? tab){
  if(tab==null||!tab.CanClose||!tabs.Contains(tab)||closing)return;
  var target=tab.Target;tab.SetClosing(true);
  try{
   bool confirm=demo&&DemoConfirmClose!=null?DemoConfirmClose(target):MessageDialog.Confirm(this,"탭 닫기",$"‘{target.Name}’ 탭을 닫을까요?\n이 탭에서 실행 중인 작업도 종료될 수 있습니다.","탭 닫기",true);
   if(!confirm||closing||!tabs.Contains(tab)||tab.Id!=target.Id)return;
   if(demo){if(DemoCloseTab!=null)await DemoCloseTab(target);if(!closing){tabs.Remove(tab);EnsureTabSelection();}}
   else{await Task.Run(()=>CloseTerminal.Close(target,()=>target.Valid,life.Token));if(!closing)await Refresh();}
  }finally{tab.SetClosing(false);}
 }
 void EnsureTabSelection(){
  if(tabs.Count>0){
   if(selected==null||!tabs.Contains(selected)){var active=tabs.Where(t=>t.Name==t.Target.Window.Title).ToArray();Tabs.SelectedItem=tabs.FirstOrDefault(t=>t.Id==updateSelection)??(active.Length==1?active[0]:tabs[0]);updateSelection="";}
  }else{
   requestWindow?.Close();selected=null;Tabs.SelectedItem=null;selectionVersion++;
   Composer.Clear();Messages.ItemsSource=null;TerminalText.Clear();TabTitle.Text="TerminalLink";AttachmentLabel();
   Composer.IsEnabled=SendButton.IsEnabled=AttachButton.IsEnabled=NativeButton.IsEnabled=false;
   RequestButton.Visibility=Visibility.Collapsed;LatestButton.Visibility=Visibility.Hidden;ActivityText.Text="";Footer.Text="열려 있는 터미널 없음";
   EmptyText.Text="열려 있는 Windows Terminal이 없습니다.\n평소 사용하던 Windows Terminal을 열면 자동으로 표시됩니다.";EmptyText.Visibility=Visibility.Visible;
  }
  UpdateTabControls();
 }
 Wpf.ListBoxItem? TabAt(System.Windows.Point point)=>Tabs.InputHitTest(point) is DependencyObject hit?Wpf.ItemsControl.ContainerFromElement(Tabs,hit) as Wpf.ListBoxItem:null;
 bool OwnTabDrag(System.Windows.DragEventArgs e)=>draggingTab!=null&&tabs.Contains(draggingTab)&&e.Data.GetDataPresent(TabDragFormat)&&e.Data.GetData(TabDragFormat) is string id&&id==draggingTab.Id;
 (int index,double y) TabDropAt(System.Windows.Point point){
  var item=TabAt(point);
  if(item?.DataContext is DesktopTab target){var top=item.TranslatePoint(new System.Windows.Point(0,0),Tabs).Y;bool after=point.Y>=top+item.ActualHeight/2;return(tabs.IndexOf(target)+(after?1:0),top+(after?item.ActualHeight:0));}
  // Blank space beneath the final row appends to the list. Above its first row
  // inserts at the beginning, including when the list has been scrolled.
  for(int i=0;i<tabs.Count;i++)if(Tabs.ItemContainerGenerator.ContainerFromIndex(i) is Wpf.ListBoxItem row){var top=row.TranslatePoint(new System.Windows.Point(0,0),Tabs).Y;if(top>=point.Y)return(i,top);}
  var last=Tabs.ItemContainerGenerator.ContainerFromIndex(tabs.Count-1) as Wpf.ListBoxItem;
  return(tabs.Count,last==null?point.Y:last.TranslatePoint(new System.Windows.Point(0,last.ActualHeight),Tabs).Y);
 }
 void ShowDropMarker(double y){
  if(dropMarker==null&&AdornerLayer.GetAdornerLayer(Tabs) is AdornerLayer layer){dropMarker=new(Tabs);layer.Add(dropMarker);}
  if(dropMarker!=null){dropMarker.Y=Math.Clamp(y,1,Math.Max(1,Tabs.ActualHeight-1));dropMarker.InvalidateVisual();}
 }
 void ClearDropMarker(){if(dropMarker==null)return;AdornerLayer.GetAdornerLayer(Tabs)?.Remove(dropMarker);dropMarker=null;}
 void KeepTabSelection(System.Action change){
  var current=Tabs.SelectedItem;reorderingTabs=true;
  try{change();if(current!=null&&Tabs.Items.Contains(current))Tabs.SelectedItem=current;}
  finally{reorderingTabs=false;}
 }
 void MoveTab(DesktopTab tab,int insertion){
  int from=tabs.IndexOf(tab);if(from<0)return;insertion=Math.Clamp(insertion,0,tabs.Count);int to=insertion>from?insertion-1:insertion;if(to==from)return;
  KeepTabSelection(()=>tabs.Move(from,to));
  try{tabOrder.Save(tabs);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){Error("순서는 변경했지만 저장하지 못했습니다. 앱을 다시 열면 이전 순서로 표시될 수 있어요.");}
 }
 void ApplyTabOrder(){
  var wanted=tabOrder.Arrange(tabs);KeepTabSelection(()=>{for(int i=0;i<wanted.Length;i++){int old=tabs.IndexOf(wanted[i]);if(old!=i)tabs.Move(old,i);}});
 }
 void UpdateTabControls(){AddTabButton.IsEnabled=!addingTab&&selected!=null&&tabs.Contains(selected);AddTabButton.Content=addingTab?"추가 중…":"＋ 새 탭";AddTabButton.ToolTip=selected==null?"Windows Terminal을 열면 여기에 탭을 추가할 수 있어요.":"선택한 Windows Terminal 창에 새 탭 추가";}
 async Task AddTab(){
  var source=selected?.Target;if(source==null||addingTab||closing)return;addingTab=true;UpdateTabControls();
  try{
   bool confirm=demo&&DemoConfirmAdd!=null?DemoConfirmAdd(source):MessageDialog.Confirm(this,"새 탭","선택한 Windows Terminal 창에 새 탭을 추가할까요?","새 탭 만들기");
   if(!confirm||closing)return;
   ConsoleTarget added;
   if(demo){added=DemoOpenTab!=null?await DemoOpenTab(source):new(source.OriginalWindow,tabs.Max(t=>t.Target.Pid)+1,DateTime.UtcNow.Ticks,"새 탭 · 미리보기");if(!closing)tabs.Add(new(added,receipts));}
   else{added=await Task.Run(()=>NewTerminal.Open(source,DesktopSession.Windows,life.Token));if(!closing)await Refresh();}
   if(!closing){SetPhoneView(false);Tabs.SelectedItem=tabs.FirstOrDefault(t=>t.Id==added.Id);}
  }finally{addingTab=false;if(!closing)UpdateTabControls();}
 }
 sealed class TabDropMarker:Adorner{
  internal TabDropMarker(UIElement element):base(element){IsHitTestVisible=false;}
  internal double Y;
  protected override void OnRender(Media.DrawingContext dc){var pen=new Media.Pen(Media.Brushes.Turquoise,2);dc.DrawLine(pen,new System.Windows.Point(5,Y),new System.Windows.Point(Math.Max(5,AdornedElement.RenderSize.Width-5),Y));}
 }
}
