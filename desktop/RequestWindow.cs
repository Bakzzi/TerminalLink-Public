using System.Windows;
using System.Windows.Input;
using Wpf=System.Windows.Controls;
using Media=System.Windows.Media;
namespace TerminalLink;

internal sealed class RequestWindow : ThemedWindow {
 readonly DesktopTab tab;readonly QueuedQuestion question;readonly string thread,draftKey;readonly bool allowFree;readonly Func<int,string,Task> answer;
 internal readonly Wpf.TextBlock Status=new(){TextWrapping=TextWrapping.Wrap,MinHeight=44,Margin=new(4,12,4,12)};
 internal readonly Wpf.TextBox Draft=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=110,VerticalScrollBarVisibility=Wpf.ScrollBarVisibility.Hidden,Margin=new(4,12,4,4)};
 internal readonly Wpf.TextBox QuestionText;
 internal readonly Wpf.Button SendButton;internal readonly List<Wpf.RadioButton> Choices=new();
 internal bool Sending;bool complete,closed,restoring;string error="";
 bool resultPending;
 internal RequestWindow(DesktopTab tab,QueuedQuestion question,Func<int,string,Task> answer,bool allowFree=true){
  this.tab=tab;this.question=question;this.answer=answer;this.allowFree=allowFree;thread=tab.Original?.id??"";
  Title="요청 답변";Width=580;SizeToContent=SizeToContent.Height;MaxHeight=760;FontSize=15;
  var panel=new Wpf.StackPanel{Margin=new(25)};QuestionText=Selectable(question.title);QuestionText.FontSize=18;QuestionText.Margin=new(4,0,4,18);panel.Children.Add(QuestionText);
  draftKey=thread+"\n"+question.id+"\n"+question.title+"\n"+string.Join("\n",question.options);
  // Parsed native follow-ups already include their free-text row.
  var options=question.options.AsEnumerable();if(allowFree&&question.id.Length==0&&question.options.LastOrDefault()=="직접 입력")options=options.SkipLast(1);
  foreach(string text in allowFree?options.Append("직접 입력"):options){
   var textBox=Selectable(text);
   var radio=new Wpf.RadioButton{Content=textBox,Foreground=Foreground,Margin=new(4,5,4,5),GroupName="answer"};
   textBox.PreviewMouseLeftButtonDown+=(_,_)=>{if(radio.IsEnabled)radio.IsChecked=true;};
   radio.Checked+=(_,_)=>Save();Choices.Add(radio);panel.Children.Add(radio);
  }
  Draft.Visibility=allowFree?Visibility.Visible:Visibility.Collapsed;
  restoring=true;
  if(tab.RequestDrafts.TryGetValue(draftKey,out var saved)){Draft.Text=saved.text;if(saved.choice>=0&&saved.choice<Choices.Count)Choices[saved.choice].IsChecked=true;}
  else if(question.options.Length==0&&Choices.Count>0)Choices[0].IsChecked=true;
  restoring=false;
  Draft.PreviewMouseLeftButtonDown+=(_,_)=>ChooseFree();Draft.GotKeyboardFocus+=(_,_)=>ChooseFree();
  Draft.TextChanged+=(_,_)=>{if(!restoring){ChooseFree();Save();}};
  panel.Children.Add(Draft);panel.Children.Add(Status);SendButton=DesktopWindow.Button("답변 보내기");SendButton.Background=Brush("#397B74");Status.Foreground=Brush("#A8B2BC");panel.Children.Add(SendButton);
  SendButton.Click+=async(_,_)=>await Submit();
  SetBody(new Wpf.ScrollViewer{Content=panel,VerticalScrollBarVisibility=Wpf.ScrollBarVisibility.Hidden});
  ContentRendered+=(_,_)=>{SizeToContent=SizeToContent.Manual;};Closed+=(_,_)=>closed=true;
  Refresh(tab);
 }
 Wpf.TextBox Selectable(string text)=>new(){Text=text,IsReadOnly=true,IsReadOnlyCaretVisible=true,TextWrapping=TextWrapping.Wrap,BorderThickness=new(0),Padding=new(0),Background=Media.Brushes.Transparent,Foreground=Foreground,VerticalContentAlignment=VerticalAlignment.Center};
 void Save(){if(!restoring&&!Sending&&!complete)tab.RequestDrafts[draftKey]=(Choices.FindIndex(c=>c.IsChecked==true),Draft.Text);}
 void ChooseFree(){if(allowFree&&!Sending&&!complete&&Choices.Count>0)Choices[^1].IsChecked=true;}
 bool Same(DesktopTab current){var q=question.id.Length>0?current.Original?.requests?.FirstOrDefault(q=>q.id==question.id):current.Request;return current==tab&&current.Original?.id==thread&&q!=null&&q.id==question.id&&q.title==question.title&&q.options.SequenceEqual(question.options);}
 internal async Task Submit(){
  if(Sending||complete||closed)return;
  if(!resultPending&&(!tab.Ready||!Same(tab))){Refresh(tab);return;}
  int selected=Choices.FindIndex(c=>c.IsChecked==true);if(selected<0){Status.Text=error="답변을 선택하세요.";return;}
  int choice=allowFree&&selected==Choices.Count-1?-1:selected;string text=choice<0?Draft.Text:"";
  if(choice<0&&string.IsNullOrWhiteSpace(text)){Status.Text=error="답변을 입력하세요.";return;}
  Sending=true;error="";SendButton.IsEnabled=false;SendButton.Content="전송 중…";Draft.IsEnabled=false;foreach(var option in Choices)option.IsEnabled=false;Status.Text="답변을 보내고 있습니다.";
  try{await answer(choice,text);complete=true;tab.RequestDrafts.Remove(draftKey);if(!closed)Close();}
  catch(Exception e){resultPending=e is RequestPendingException;error=e is TimeoutException?"전송 결과를 확인하지 못했습니다. 작성한 답변은 유지했어요. 대화를 확인한 뒤 다시 시도하세요.":DesktopErrorText.Display(e.Message);}
  finally{Sending=false;if(!complete&&!closed){Draft.IsEnabled=allowFree&&!resultPending;foreach(var option in Choices)option.IsEnabled=!resultPending;SendButton.Content=resultPending?"전송 결과 확인":"답변 보내기";Refresh(tab);}}
 }
 internal void Refresh(DesktopTab current){
  if(Sending||complete||closed)return;
  resultPending|=tab.RequestSubmissions.ContainsKey(thread+"\n"+question.id);
  bool same=Same(current);SendButton.IsEnabled=resultPending||current.Ready&&same;
  if(resultPending){Draft.IsEnabled=false;foreach(var option in Choices)option.IsEnabled=false;SendButton.Content="전송 결과 확인";Status.Text=error.Length>0?error:"전송 결과를 확인하고 있습니다. 자동으로 다시 보내지 않습니다.";}
  else Status.Text=!current.Ready?"연결 확인 중입니다. 읽던 내용과 작성한 답변은 유지됩니다.":!same?"이 요청이 완료되거나 변경되었습니다.":error;
 }
}
