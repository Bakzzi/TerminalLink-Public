using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using Wpf=System.Windows.Controls;
using Media=System.Windows.Media;

namespace TerminalLink;

public sealed class MessageCard : Wpf.Border {
 public static readonly DependencyProperty MessageProperty=DependencyProperty.Register(nameof(Message),typeof(object),typeof(MessageCard),new PropertyMetadata(null,(d,e)=>((MessageCard)d).Render(e.NewValue as ChatRow)));
 public object Message{get=>GetValue(MessageProperty);set=>SetValue(MessageProperty,value);}
 static Media.SolidColorBrush Brush(string color)=>(Media.SolidColorBrush)new Media.BrushConverter().ConvertFromString(color)!;
 void Render(ChatRow? row){
  if(row==null){Child=null;return;}
  var panel=new Wpf.StackPanel();
  var speaker=new Wpf.TextBlock{FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=Brush(row.Mine?"#73D8CE":"#A8B2BC"),Margin=new(0,0,0,10),ToolTip=row.DeliveryHint};
  speaker.SetBinding(Wpf.TextBlock.TextProperty,new System.Windows.Data.Binding(nameof(ChatRow.Speaker)){Source=row});panel.Children.Add(speaker);
  var doc=new FlowDocument{FontFamily=new Media.FontFamily("Malgun Gothic, Segoe UI"),FontSize=15,Foreground=Brush("#E0E3E7"),Background=Media.Brushes.Transparent,PagePadding=new(0),LineHeight=25};
  string[] lines=row.Text.Replace("\r","").Split('\n');bool code=false;var codeLines=new List<string>();
  for(int i=0;i<lines.Length;i++){
   string line=lines[i];if(line.TrimStart().StartsWith("```")){if(code){doc.Blocks.Add(Code(string.Join("\n",codeLines)));codeLines.Clear();}code=!code;continue;}
   if(code){codeLines.Add(line);continue;}
   if(i+1<lines.Length&&line.Contains('|')&&Regex.IsMatch(lines[i+1],@"^\s*\|?\s*:?-{3,}:?\s*\|")){
    var rows=new List<string[]>{Cells(line)};i++;
    while(i+1<lines.Length&&lines[i+1].Contains('|')&&!string.IsNullOrWhiteSpace(lines[i+1]))rows.Add(Cells(lines[++i]));
    var table=new Table{CellSpacing=0,Margin=new(0,8,0,16)};int count=rows.Max(r=>r.Length);
    for(int c=0;c<count;c++)table.Columns.Add(new TableColumn());var group=new TableRowGroup();table.RowGroups.Add(group);
    for(int r=0;r<rows.Count;r++){var tr=new TableRow();group.Rows.Add(tr);for(int c=0;c<count;c++){var p=Paragraph(c<rows[r].Length?rows[r][c]:"");p.Margin=new(0);tr.Cells.Add(new TableCell(p){Padding=new(10,7,10,7),BorderBrush=Brush("#3B4248"),BorderThickness=new(0.5),Background=r==0?Brush("#282F33"):Media.Brushes.Transparent,FontWeight=r==0?FontWeights.SemiBold:FontWeights.Normal});}}
    doc.Blocks.Add(table);continue;
   }
   if(string.IsNullOrWhiteSpace(line)){doc.Blocks.Add(new Paragraph{Margin=new(0,3,0,3),FontSize=4});continue;}
   var paragraph=Paragraph(line);if(line.StartsWith('#')){paragraph=Paragraph(line.TrimStart('#',' '));paragraph.FontSize=18;paragraph.FontWeight=FontWeights.SemiBold;paragraph.Margin=new(0,12,0,8);}doc.Blocks.Add(paragraph);
  }
  if(codeLines.Count>0)doc.Blocks.Add(Code(string.Join("\n",codeLines)));
  var box=new Wpf.RichTextBox{Document=doc,IsReadOnly=true,IsDocumentEnabled=true,BorderThickness=new(0),Padding=new(0),Background=Media.Brushes.Transparent,Foreground=Brush("#E0E3E7"),VerticalScrollBarVisibility=Wpf.ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=Wpf.ScrollBarVisibility.Disabled};
  var menu=new Wpf.ContextMenu();var copy=new Wpf.MenuItem{Header="메시지 전체 복사"};copy.Click+=(_,_)=>System.Windows.Clipboard.SetText(row.Text);menu.Items.Add(copy);box.ContextMenu=menu;panel.Children.Add(box);
  Child=panel;Padding=row.Mine?new Thickness(18,15,18,15):new Thickness(4,6,4,6);CornerRadius=new(12);Background=row.Mine?Brush("#22292B"):Media.Brushes.Transparent;
 }
 static string[] Cells(string line)=>line.Trim().Trim('|').Split('|').Select(s=>s.Trim()).ToArray();
 static Paragraph Code(string text)=>new(new Run(text)){FontFamily=new Media.FontFamily("Cascadia Mono, Consolas"),FontSize=13,Background=Brush("#0D1114"),Padding=new(14),Margin=new(0,8,0,12)};
 static Paragraph Paragraph(string text){
  var p=new Paragraph{Margin=new(0,0,0,3)};int at=0;
  foreach(Match m in Regex.Matches(text,@"\*\*(.+?)\*\*|`([^`]+)`|\[([^\]]+)\]\(([^)]+)\)")){
   if(m.Index>at)p.Inlines.Add(new Run(text[at..m.Index]));
   if(m.Groups[1].Success)p.Inlines.Add(new Bold(new Run(m.Groups[1].Value)));
   else if(m.Groups[2].Success)p.Inlines.Add(new Run(m.Groups[2].Value){FontFamily=new Media.FontFamily("Consolas"),Background=Brush("#2C3438")});
   else if(Uri.TryCreate(m.Groups[4].Value,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http"){
    var link=new Hyperlink(new Run(m.Groups[3].Value)){NavigateUri=uri,Foreground=Brush("#73D8CE")};link.RequestNavigate+=(_,e)=>{try{Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});}catch{}e.Handled=true;};p.Inlines.Add(link);
   }else p.Inlines.Add(new Run(m.Groups[3].Value+" ("+m.Groups[4].Value+")"));
   at=m.Index+m.Length;
  }
  if(at<text.Length)p.Inlines.Add(new Run(text[at..]));return p;
 }
}
