package com.terminallink.remote;
import android.graphics.Typeface;
import android.graphics.Rect;
import android.graphics.drawable.GradientDrawable;
import android.text.*;
import android.view.*;
import android.widget.*;
import java.util.*;

// Native text and cells only: no WebView, HTML loading, scripts, or network resources.
final class TableTranscript {
 final ReadingView owner;final MainActivity activity;final LinearLayout root;
 final ArrayList<TextView> texts=new ArrayList<>();
 final ArrayList<Part> parts=new ArrayList<>();
 final ArrayList<Grid> grids=new ArrayList<>();
 final TextView measureCell;
 final ViewTreeObserver.OnPreDrawListener viewport=()->!refreshViewport();
 static final class Part {final View view;final int start,end;Part(View v,int s,int e){view=v;start=s;end=e;}}
 TableTranscript(ReadingView view){
  owner=view;activity=view.activity;
  measureCell=new TextView(activity);measureCell.setLayoutParams(new ViewGroup.LayoutParams(-2,-2));measureCell.setBreakStrategy(Layout.BREAK_STRATEGY_HIGH_QUALITY);
  root=new LinearLayout(activity){
   @Override protected void onAttachedToWindow(){super.onAttachedToWindow();getViewTreeObserver().addOnPreDrawListener(viewport);}
   @Override protected void onDetachedFromWindow(){if(getViewTreeObserver().isAlive())getViewTreeObserver().removeOnPreDrawListener(viewport);super.onDetachedFromWindow();}
  };root.setOrientation(LinearLayout.VERTICAL);
  root.setPadding(activity.dp(12),activity.dp(12),activity.dp(12),activity.dp(20));root.setVisibility(View.GONE);
 }
 boolean active(){return root.getVisibility()==View.VISIBLE;}
 boolean selected(){for(TextView text:texts)if(text.hasSelection())return true;return false;}
 void clearSelection(){for(TextView text:texts)if(text.getText() instanceof Spannable)Selection.removeSelection((Spannable)text.getText());}
 void clear(){root.removeAllViews();texts.clear();parts.clear();grids.clear();root.setVisibility(View.GONE);}
 TextView text(CharSequence value){
  TextView t=new TextView(activity);t.setTextColor(activity.FG);t.setTextSize(owner.fontSp);
  t.setText(value);t.setLineSpacing(activity.dp(5),1f);t.setTextIsSelectable(true);t.setRevealOnFocusHint(false);
  t.setBreakStrategy(Layout.BREAK_STRATEGY_HIGH_QUALITY);
  t.setOnKeyListener((v,k,e)->selected()?false:activity.hardware(k,e));texts.add(t);return t;
 }
 void show(SpannableStringBuilder value,List<MarkdownTables.Block> blocks,boolean sameSession){
  Map<String,Integer> positions=new HashMap<>();
  if(sameSession)for(Grid grid:grids)positions.put(grid.key,grid.scroll.getScrollX());
  clear();root.setVisibility(View.VISIBLE);int cursor=0,index=0;
  for(MarkdownTables.Block block:blocks){
   if(cursor<block.start)addText(value.subSequence(cursor,block.start),cursor,block.start);
   Grid grid=new Grid(block,index++);grids.add(grid);root.addView(grid,new LinearLayout.LayoutParams(-1,-2));parts.add(new Part(grid,block.start,block.end));
   grid.restoreX=positions.containsKey(grid.key)?positions.get(grid.key):0;cursor=block.end;
  }
  if(cursor<value.length())addText(value.subSequence(cursor,value.length()),cursor,value.length());
 }
 void addText(CharSequence value,int start,int end){TextView t=text(value);root.addView(t,new LinearLayout.LayoutParams(-1,-2));parts.add(new Part(t,start,end));}
 void font(){for(TextView t:texts)t.setTextSize(owner.fontSp);for(Grid g:grids){g.lastWidth=-1;g.requestLayout();}}
 boolean visible(long[] range){
  Rect visible=new Rect();if(!root.getLocalVisibleRect(visible))return false;
  for(Part part:parts){
   if(range[2]<part.start||range[1]>=part.end)continue;
   Rect bounds=new Rect(0,0,part.view.getWidth(),part.view.getHeight());root.offsetDescendantRectToMyCoords(part.view,bounds);
   if(part.view instanceof TextView){
    TextView t=(TextView)part.view;Layout layout=t.getLayout();if(layout==null)continue;
    int first=Math.max(0,(int)range[1]-part.start),last=Math.min(t.length()-1,(int)range[2]-part.start);
    bounds.top+=t.getTotalPaddingTop()+layout.getLineTop(layout.getLineForOffset(first));
    bounds.bottom=bounds.top-layout.getLineTop(layout.getLineForOffset(first))+layout.getLineBottom(layout.getLineForOffset(last));
   }
   if(bounds.bottom>visible.top&&bounds.top<visible.bottom)return true;
  }return false;
 }
 // Every table retains its place and measured height. Only nearby cells own native views.
 // The 24-table/1024-cell budget applies to prefetch, never to transcript content.
 boolean refreshViewport(){
  if(!active()||root.getWidth()==0)return false;
  Rect visible=new Rect();if(!root.getLocalVisibleRect(visible))return false;
  int margin=Math.max(activity.dp(200),visible.height()),used=0;
  HashSet<Grid> wanted=new HashSet<>();ArrayList<Grid> nearby=new ArrayList<>();
  for(Grid grid:grids){
   if(grid.lastWidth<1)continue;
   if(grid.selected()||grid.getBottom()>visible.top&&grid.getTop()<visible.bottom){wanted.add(grid);used+=grid.cellCount();}
   else if(grid.getBottom()>visible.top-margin&&grid.getTop()<visible.bottom+margin)nearby.add(grid);
  }
  nearby.sort((a,b)->Integer.compare(a.distance(visible),b.distance(visible)));
  for(Grid grid:nearby)if(wanted.size()<24&&used+grid.cellCount()<=1024){wanted.add(grid);used+=grid.cellCount();}
  boolean changed=false;
  for(Grid grid:grids)if(!wanted.contains(grid))changed|=grid.activate(false);
  for(Grid grid:grids)if(wanted.contains(grid))changed|=grid.activate(true);
  return changed;
 }
 int activeCells(){int count=0;for(Grid grid:grids)for(TextView[] row:grid.rows)count+=row.length;return count;}
 void styleCell(TextView cell,int r,int c,MarkdownTables.Block data){
  cell.setTextSize(owner.fontSp);cell.setTypeface(Typeface.DEFAULT,r==0?Typeface.BOLD:Typeface.NORMAL);
  cell.setTextDirection(View.TEXT_DIRECTION_FIRST_STRONG);cell.setGravity(Gravity.CENTER_VERTICAL|(data.align[c]==1?Gravity.CENTER_HORIZONTAL:data.align[c]==2?Gravity.RIGHT:Gravity.LEFT));
  cell.setLineSpacing(activity.dp(2),1f);cell.setPadding(activity.dp(10),activity.dp(10),activity.dp(10),activity.dp(10));cell.setMinHeight(activity.dp(44));
 }
 final class Grid extends LinearLayout {
  final MarkdownTables.Block block;final HorizontalScrollView scroll;final TableLayout table;final TextView hint;
  final ArrayList<TextView[]> rows=new ArrayList<>();final String key;int lastWidth=-1,restoreX,tableWidth,tableHeight;int[] widths,rowHeights;
  Grid(MarkdownTables.Block data,int index){
   super(activity);block=data;key=index+":"+data.rows.get(0).toString();setOrientation(VERTICAL);setPadding(0,activity.dp(4),0,activity.dp(12));setTag("conversation-table");
   hint=new TextView(activity);hint.setText("표를 좌우로 밀어 보세요 ↔");hint.setTextSize(12);hint.setTextColor(activity.MUTED);hint.setPadding(0,0,0,activity.dp(5));hint.setVisibility(GONE);
   addView(hint,new LinearLayout.LayoutParams(-1,-2));
   scroll=new HorizontalScrollView(activity);scroll.setHorizontalScrollBarEnabled(true);scroll.setFillViewport(false);scroll.setTag("table-scroll");
   table=new TableLayout(activity){@Override protected void onMeasure(int ws,int hs){if(rows.isEmpty())setMeasuredDimension(tableWidth,tableHeight);else super.onMeasure(ws,hs);}};table.setTag("table-grid");
   scroll.addView(table,new HorizontalScrollView.LayoutParams(-2,-2));addView(scroll,new LinearLayout.LayoutParams(-1,-2));
  }
  int cellCount(){return block.rows.size()*block.align.length;}
  int distance(Rect viewport){return Math.max(0,Math.max(viewport.top-getBottom(),getTop()-viewport.bottom));}
  boolean selected(){for(TextView[] row:rows)for(TextView cell:row)if(cell.hasSelection())return true;return false;}
  boolean activate(boolean enabled){
   if(enabled==!rows.isEmpty()||enabled&&widths==null)return false;
   if(!enabled){for(TextView[] row:rows)texts.removeAll(Arrays.asList(row));rows.clear();table.removeAllViews();return true;}
   for(int r=0;r<block.rows.size();r++){
    TableRow row=new TableRow(activity);TextView[] cells=new TextView[block.align.length];
    for(int c=0;c<cells.length;c++){
     TextView cell=text(block.rows.get(r).get(c));cells[c]=cell;styleCell(cell,r,c,block);
     GradientDrawable bg=new GradientDrawable();bg.setColor(r==0?0xff26394d:r%2==0?0xff172536:0xff111e2d);bg.setStroke(activity.dp(1),0xff415268);cell.setBackground(bg);
     cell.setContentDescription((r==0?"열 제목: ":block.rows.get(0).get(c)+": ")+block.rows.get(r).get(c));
     row.addView(cell,new TableRow.LayoutParams(widths[c],-1));
    }
    rows.add(cells);table.addView(row,new TableLayout.LayoutParams(-2,rowHeights[r]));
   }
   return true;
  }
  void sizeCells(int available){
   widths=new int[block.align.length];rowHeights=new int[block.rows.size()];tableWidth=0;tableHeight=0;
   for(int c=0;c<widths.length;c++){
    int desired=activity.dp(68);
    for(int r=0;r<block.rows.size();r++){
     styleCell(measureCell,r,c,block);
     for(String line:block.rows.get(r).get(c).split("\n",-1))desired=Math.max(desired,(int)Math.ceil(measureCell.getPaint().measureText(line))+activity.dp(22));
    }
    widths[c]=Math.min(activity.dp(224),desired);tableWidth+=widths[c];
   }
   boolean wide=tableWidth>available;
   if(!wide){int extra=available-tableWidth;for(int c=0;c<widths.length;c++)widths[c]+=extra/widths.length+(c<extra%widths.length?1:0);tableWidth=available;}
   for(int r=0;r<block.rows.size();r++){
    for(int c=0;c<widths.length;c++){
     styleCell(measureCell,r,c,block);measureCell.setText(block.rows.get(r).get(c));
     measureCell.measure(View.MeasureSpec.makeMeasureSpec(widths[c],View.MeasureSpec.EXACTLY),View.MeasureSpec.makeMeasureSpec(0,View.MeasureSpec.UNSPECIFIED));
     rowHeights[r]=Math.max(rowHeights[r],measureCell.getMeasuredHeight());
    }
    tableHeight+=rowHeights[r];
   }
   measureCell.setText("");
   for(int r=0;r<rows.size();r++){
    for(int c=0;c<widths.length;c++){TableRow.LayoutParams p=(TableRow.LayoutParams)rows.get(r)[c].getLayoutParams();p.width=widths[c];rows.get(r)[c].setLayoutParams(p);}
    View row=table.getChildAt(r);TableLayout.LayoutParams p=(TableLayout.LayoutParams)row.getLayoutParams();p.height=rowHeights[r];row.setLayoutParams(p);
   }
   table.setLayoutParams(new HorizontalScrollView.LayoutParams(tableWidth,tableHeight));hint.setVisibility(wide?VISIBLE:GONE);
  }
  @Override protected void onMeasure(int ws,int hs){
   int available=View.MeasureSpec.getSize(ws)-getPaddingLeft()-getPaddingRight();
   if(available>0&&available!=lastWidth){lastWidth=available;sizeCells(available);}
   super.onMeasure(ws,hs);
  }
  @Override protected void onLayout(boolean changed,int l,int t,int r,int b){super.onLayout(changed,l,t,r,b);if(restoreX>=0){scroll.scrollTo(restoreX,0);restoreX=-1;}}
 }
}
