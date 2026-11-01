using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

public class VideoTimeline : FrameworkElement
{
 public double Duration,SelectionStart,SelectionEnd,Position,ViewStart,ViewEnd;
 public double FrameStep=1/30.0;
 public BitmapSource[] Thumbnails=new BitmapSource[0];
 public double ThumbnailStart,ThumbnailEnd;
 public event EventHandler RangeChanged,SeekChanged,ViewChanged,EditStarted,EditCompleted;
 int mode;
 double anchor,originalStart,originalEnd;
 public bool InRangeGesture {get{return mode>=2;}}
 static readonly Brush Accent=new SolidColorBrush(Color.FromRgb(181,154,255));
 static readonly Brush Ink=new SolidColorBrush(Color.FromRgb(236,236,241));
 static readonly Brush Muted=new SolidColorBrush(Color.FromRgb(160,160,175));
 public VideoTimeline(){Focusable=true;MinHeight=112;Cursor=Cursors.Cross;ToolTip="Потяните края для обрезки. Shift + перетаскивание — новый фрагмент. Ctrl + перетаскивание — сдвиг. Колесо — масштаб, Shift + колесо — прокрутка.";}
 double Span {get{return Math.Max(.001,ViewEnd-ViewStart);}}
 public double TimeAt(double x){return Math.Max(0,Math.Min(Duration,ViewStart+Math.Max(0,Math.Min(ActualWidth,x))/Math.Max(1,ActualWidth)*Span));}
 public double PixelAt(double time){return (time-ViewStart)/Span*Math.Max(1,ActualWidth);}
 public void ResetView(){ViewStart=0;ViewEnd=Duration;NotifyView();}
 public void Zoom(double factor,double center){
  if(Duration<=0)return;
  double fraction=(center-ViewStart)/Span,span=Math.Max(Math.Min(.1,Duration),Math.Min(Duration,Span/factor));
  ViewStart=Math.Max(0,Math.Min(Duration-span,center-fraction*span));ViewEnd=ViewStart+span;NotifyView();
 }
 public void EnsureVisible(double time){if(time<ViewStart||time>ViewEnd){double span=Span;ViewStart=Math.Max(0,Math.Min(Duration-span,time-span/2));ViewEnd=Math.Min(Duration,ViewStart+span);NotifyView();}}
 void NotifyView(){InvalidateVisual();if(ViewChanged!=null)ViewChanged(this,EventArgs.Empty);}
 void Seek(double time){Position=time;InvalidateVisual();if(SeekChanged!=null)SeekChanged(this,EventArgs.Empty);}
 void Changed(){InvalidateVisual();if(RangeChanged!=null)RangeChanged(this,EventArgs.Empty);}
 public void BeginGesture(int gesture,double time){mode=gesture;anchor=time;originalStart=SelectionStart;originalEnd=SelectionEnd;if(mode>=2&&EditStarted!=null)EditStarted(this,EventArgs.Empty);UpdateGesture(time);}
 public void UpdateGesture(double time){
  time=Math.Max(0,Math.Min(Duration,time));double min=Math.Min(.1,Duration);
  if(mode==1){Seek(time);return;}
  if(mode==2){SelectionStart=Math.Max(0,Math.Min(SelectionEnd-min,time));Seek(SelectionStart);}
  if(mode==3){SelectionEnd=Math.Min(Duration,Math.Max(SelectionStart+min,time));Seek(SelectionEnd);}
  if(mode==4){double left=Math.Min(anchor,time),right=Math.Max(anchor,time);if(right-left<min){right=Math.Min(Duration,left+min);left=Math.Max(0,right-min);}SelectionStart=left;SelectionEnd=right;Seek(time);}
  if(mode==5){double shift=Math.Max(-originalStart,Math.Min(Duration-originalEnd,time-anchor));SelectionStart=originalStart+shift;SelectionEnd=originalEnd+shift;Seek(SelectionStart);}
  Changed();
 }
 public void EndGesture(){bool edited=mode>=2;mode=0;if(edited&&EditCompleted!=null)EditCompleted(this,EventArgs.Empty);InvalidateVisual();}
 protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e){base.OnMouseLeftButtonDown(e);if(Duration<=0)return;Focus();double x=e.GetPosition(this).X;int gesture=1;
  if((Keyboard.Modifiers&ModifierKeys.Shift)!=0)gesture=4;
  else if(Math.Abs(x-PixelAt(SelectionStart))<=12)gesture=2;
  else if(Math.Abs(x-PixelAt(SelectionEnd))<=12)gesture=3;
  else if((Keyboard.Modifiers&ModifierKeys.Control)!=0&&x>=PixelAt(SelectionStart)&&x<=PixelAt(SelectionEnd))gesture=5;
  BeginGesture(gesture,TimeAt(x));CaptureMouse();e.Handled=true;
 }
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(mode!=0)UpdateGesture(TimeAt(e.GetPosition(this).X));else Cursor=Math.Abs(e.GetPosition(this).X-PixelAt(SelectionStart))<12||Math.Abs(e.GetPosition(this).X-PixelAt(SelectionEnd))<12?Cursors.SizeWE:Cursors.Cross;}
 protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){if(mode!=0){UpdateGesture(TimeAt(e.GetPosition(this).X));EndGesture();ReleaseMouseCapture();e.Handled=true;}base.OnMouseLeftButtonUp(e);}
 protected override void OnLostMouseCapture(MouseEventArgs e){EndGesture();base.OnLostMouseCapture(e);}
 protected override void OnMouseWheel(MouseWheelEventArgs e){base.OnMouseWheel(e);if(Duration<=0)return;if((Keyboard.Modifiers&ModifierKeys.Shift)!=0){double span=Span;ViewStart=Math.Max(0,Math.Min(Duration-span,ViewStart-e.Delta/120.0*span*.15));ViewEnd=ViewStart+span;NotifyView();}else Zoom(e.Delta>0?1.3:1/1.3,TimeAt(e.GetPosition(this).X));e.Handled=true;}
 protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.Key==Key.Left||e.Key==Key.Right){double step=(Keyboard.Modifiers&ModifierKeys.Shift)!=0?1:FrameStep;Seek(Math.Max(0,Math.Min(Duration,Position+(e.Key==Key.Left?-step:step))));EnsureVisible(Position);e.Handled=true;}if(e.Key==Key.Escape&&InRangeGesture){SelectionStart=originalStart;SelectionEnd=originalEnd;Changed();EndGesture();ReleaseMouseCapture();e.Handled=true;}}
 void Text(DrawingContext dc,string text,double x,double y,Brush brush,int size=11){var f=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(f,new Point(Math.Max(0,Math.Min(ActualWidth-f.Width,x)),y));}
 protected override void OnRender(DrawingContext dc){base.OnRender(dc);double width=ActualWidth;if(width<1)return;double top=24,height=61;
  dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,width,ActualHeight));
  for(int i=0;i<=5;i++){double t=ViewStart+Span*i/5;string label=Studio.Time(t);if(Span>30)label=label.Split('.')[0];Text(dc,label,width*i/5,0,Muted);}
  var track=new Rect(0,top,width,height);dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(34,35,42)),null,track,5,5);
  dc.PushClip(new RectangleGeometry(track,5,5));
  if(Thumbnails.Length>0&&ThumbnailEnd>ThumbnailStart){for(int i=0;i<Thumbnails.Length;i++){var image=Thumbnails[i];if(image==null)continue;double start=ThumbnailStart+(ThumbnailEnd-ThumbnailStart)*i/Thumbnails.Length,end=ThumbnailStart+(ThumbnailEnd-ThumbnailStart)*(i+1)/Thumbnails.Length;double left=PixelAt(start),right=PixelAt(end);if(right<0||left>width)continue;var tile=new Rect(left,top,Math.Max(1,right-left),height);dc.PushClip(new RectangleGeometry(tile));double iw=Math.Max(tile.Width,height*image.PixelWidth/(double)image.PixelHeight);dc.DrawImage(image,new Rect(left+(tile.Width-iw)/2,top,iw,height));dc.Pop();}}
  double a=Math.Max(0,Math.Min(width,PixelAt(SelectionStart))),b=Math.Max(0,Math.Min(width,PixelAt(SelectionEnd)));
  var dim=new SolidColorBrush(Color.FromArgb(155,16,17,20));if(a>0)dc.DrawRectangle(dim,null,new Rect(0,top,a,height));if(b<width)dc.DrawRectangle(dim,null,new Rect(b,top,width-b,height));
  if(b>a){dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(35,181,154,255)),new Pen(Accent,2),new Rect(a+1,top+1,Math.Max(0,b-a-2),height-2));if(SelectionStart>=ViewStart)dc.DrawRoundedRectangle(Accent,null,new Rect(a,top,7,height),2,2);if(SelectionEnd<=ViewEnd)dc.DrawRoundedRectangle(Accent,null,new Rect(Math.Max(a,b-7),top,7,height),2,2);}
  double pos=PixelAt(Position);if(pos>=0&&pos<=width){dc.DrawLine(new Pen(Ink,2),new Point(pos,top),new Point(pos,top+height));}
  dc.Pop();
  Text(dc,"Края — обрезка  ·  Shift + drag — выделение  ·  Колесо — масштаб",0,91,Muted,10);
 }
}
