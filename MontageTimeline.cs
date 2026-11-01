using System;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
public class MontageTimeline : FrameworkElement {
 public System.Collections.Generic.Dictionary<string,System.Windows.Media.Imaging.BitmapSource> Pictures=new System.Collections.Generic.Dictionary<string,System.Windows.Media.Imaging.BitmapSource>();
 public MontageDocument Document;public string SelectedId,SelectedTrack;public double Position,Pixels=55,Offset;
 public event EventHandler Selected,Seeked,EditStarted,Edited,EditCompleted;
 MontageClip dragged,original;int gesture;double anchor;int originalRow;
 const double Header=142,Row=66,Top=30;
 public MontageTimeline(){Focusable=true;MinHeight=150;ClipToBounds=true;ToolTip="Перетаскивание — переместить клип. Края — обрезать. Alt — без привязки. Колесо — масштаб, Shift+колесо — прокрутка.";}
 Brush B(string s){return new SolidColorBrush((Color)ColorConverter.ConvertFromString(s));}
 void Text(DrawingContext d,string s,double x,double y,Brush b,double size=11,double width=400){var t=new FormattedText(s,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,b,VisualTreeHelper.GetDpi(this).PixelsPerDip);t.MaxTextWidth=Math.Max(2,width);t.Trimming=TextTrimming.CharacterEllipsis;d.DrawText(t,new Point(x,y));}
 public double At(double x){return Math.Max(0,Offset+(x-Header)/Pixels);}
 public double X(double t){return Header+(t-Offset)*Pixels;}
 public void Fit(){Offset=0;Pixels=Math.Max(.01,Math.Max(50,ActualWidth-Header-15)/Math.Max(5,Document.Duration));InvalidateVisual();}
 public void Zoom(double factor){Pixels=Math.Max(.01,Math.Min(600,Pixels*factor));InvalidateVisual();}
 protected override void OnRender(DrawingContext d){base.OnRender(d);if(Document==null)return;d.DrawRectangle(B("#191a1f"),null,new Rect(0,0,ActualWidth,ActualHeight));double step=Pixels>150?.5:Pixels>40?1:Pixels>8?5:Pixels>1?30:Pixels>.1?300:3600;for(double t=Math.Ceiling(Offset/step)*step;X(t)<ActualWidth;t+=step){double x=X(t);if(x<Header)continue;d.DrawLine(new Pen(B("#303138"),1),new Point(x,Top),new Point(x,ActualHeight));Text(d,TimeLabel(t),x+3,4,B("#a0a0af"));}
  for(int i=0;i<Document.Tracks.Count;i++){var t=Document.Tracks[i];double y=Top+i*Row;d.DrawRectangle(B(t.Id==SelectedTrack?"#302742":"#22232a"),new Pen(B("#303138"),1),new Rect(0,y,Header-2,Row));Text(d,t.Name,8,y+6,B("#ececf1"),12,Header-14);Text(d,(t.Hidden?"○":"●")+"     "+(t.Muted?"×♫":"♫")+"     "+(t.Locked?"▣":"□"),10,y+35,B("#b59aff"));
   d.PushClip(new RectangleGeometry(new Rect(Header,y,Math.Max(1,ActualWidth-Header),Row)));
   foreach(var c in Document.Items.Where(c=>c.Track==t.Id)){double x=X(c.Start),w=Math.Max(4,c.Length*Pixels);var r=new Rect(x,y+5,w,Row-10);string col=t.Kind=="text"?"#71519a":t.Kind=="audio"?"#296b58":"#226779";d.PushOpacity(t.Hidden?.35:1);d.DrawRoundedRectangle(B(col),new Pen(B(c.Id==SelectedId?"#ececf1":"#39818b"),c.Id==SelectedId?2:1),r,4,4);var a=Document.Assets.FirstOrDefault(v=>v.Id==c.Asset);System.Windows.Media.Imaging.BitmapSource picture;if(a!=null&&Pictures.TryGetValue(a.Id,out picture)&&w>30){d.PushClip(new RectangleGeometry(new Rect(x+3,y+27,w-6,Row-37)));if(t.Kind=="audio")d.DrawImage(picture,new Rect(x+3,y+29,w-6,Row-38));else{double tile=60;double start=Math.Max(x+3,Header);for(double xx=start;xx<x+w-3&&xx<ActualWidth;xx+=tile)d.DrawImage(picture,new Rect(xx,y+29,tile,Row-38));}d.Pop();}string name=t.Kind=="text"?c.Text:a==null?"Медиа":System.IO.Path.GetFileName(a.Path);Text(d,name??"Текст",x+7,y+9,B("#ffffff"),11,w-12);if(w>45&&(a==null||!Pictures.ContainsKey(a.Id)))Text(d,TimeLabel(c.Length),x+7,y+32,B("#bce5df"),11,w-12);if(c.Id==SelectedId){d.DrawRectangle(B("#ececf1"),null,new Rect(x,y+10,3,Row-20));d.DrawRectangle(B("#ececf1"),null,new Rect(x+w-3,y+10,3,Row-20));}d.Pop();}
   d.Pop();
  }
  double cursor=X(Position);if(cursor>=Header){d.DrawLine(new Pen(B("#ffffff"),1.5),new Point(cursor,0),new Point(cursor,ActualHeight));d.DrawEllipse(B("#ffffff"),null,new Point(cursor,4),4,4);}Text(d,"ДОРОЖКИ",10,5,B("#a0a0af"));
 }
 string TimeLabel(double t){return Studio.Time(t).Substring(0,8)+(t%1>.01?"."+((int)(t*10)%10):"");}
 MontageClip Copy(MontageClip c){return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<MontageClip>(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(c));}
 public void BeginClipGesture(string id,int mode,double time){dragged=Document.Items.FirstOrDefault(c=>c.Id==id);if(dragged==null)return;var tr=Document.Tracks.First(t=>t.Id==dragged.Track);if(tr.Locked){dragged=null;return;}original=Copy(dragged);originalRow=Document.Tracks.IndexOf(tr);gesture=mode;anchor=time;if(EditStarted!=null)EditStarted(this,EventArgs.Empty);}
 public void MoveClipGesture(double time,int row,bool snap){if(dragged==null)return;double delta=time-anchor;Func<double,double> grid=v=>snap?Math.Round(v*10)/10:v;var a=Document.Assets.FirstOrDefault(v=>v.Id==dragged.Asset);
  if(gesture==1){dragged.Start=Math.Max(0,Math.Min(86400-dragged.Length,grid(original.Start+delta)));if(snap){double[] edges=Document.Items.Where(v=>v.Id!=dragged.Id).SelectMany(v=>new[]{v.Start,v.Start+v.Length,v.Start-dragged.Length,v.Start+v.Length-dragged.Length}).Concat(new[]{Position,Position-dragged.Length,0.0}).ToArray();foreach(double edge in edges)if(edge>=0&&Math.Abs(edge-dragged.Start)*Pixels<7){dragged.Start=edge;break;}}if(row>=0&&row<Document.Tracks.Count){var t=Document.Tracks[row];if(!t.Locked&&t.Kind==Document.Tracks[originalRow].Kind)dragged.Track=t.Id;}}
  else if(gesture==2){double start=Math.Max(0,Math.Min(original.Start+original.Length-.1,grid(original.Start+delta)));if(a!=null&&a.Kind!="image")start=Math.Max(start,original.Start-original.In/original.Speed);double diff=start-original.Start;dragged.Start=start;dragged.In=a!=null&&a.Kind!="image"?Math.Max(0,original.In+diff*original.Speed):0;dragged.Length=original.Length-diff;}
  else {double max=a==null||a.Kind=="image"?86400-original.Start:(a.Duration-original.In)/original.Speed;dragged.Length=Math.Max(.1,Math.Min(max,grid(original.Length+delta)));}
  InvalidateVisual();if(Edited!=null)Edited(this,EventArgs.Empty);
 }
 public void EndClipGesture(){if(dragged==null)return;dragged=null;gesture=0;if(EditCompleted!=null)EditCompleted(this,EventArgs.Empty);}
 protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e){base.OnMouseLeftButtonDown(e);Focus();Point p=e.GetPosition(this);int row=(int)Math.Floor((p.Y-Top)/Row);if(p.X<Header&&row>=0&&row<Document.Tracks.Count){var t=Document.Tracks[row];SelectedTrack=t.Id;SelectedId=null;if(p.Y-(Top+row*Row)>30){if(p.X<42)t.Hidden=!t.Hidden;else if(p.X<90)t.Muted=!t.Muted;else t.Locked=!t.Locked;if(Edited!=null)Edited(this,EventArgs.Empty);if(EditCompleted!=null)EditCompleted(this,EventArgs.Empty);}if(Selected!=null)Selected(this,EventArgs.Empty);InvalidateVisual();return;}
  if(p.Y<Top){Position=At(p.X);if(Seeked!=null)Seeked(this,EventArgs.Empty);CaptureMouse();gesture=4;return;}
  if(row>=0&&row<Document.Tracks.Count){var t=Document.Tracks[row];var c=Document.Items.LastOrDefault(v=>v.Track==t.Id&&At(p.X)>=v.Start&&At(p.X)<=v.Start+v.Length);SelectedTrack=t.Id;SelectedId=c==null?null:c.Id;if(Selected!=null)Selected(this,EventArgs.Empty);if(c!=null){int mode=Math.Abs(p.X-X(c.Start))<8?2:Math.Abs(p.X-X(c.Start+c.Length))<8?3:1;BeginClipGesture(c.Id,mode,At(p.X));CaptureMouse();}else{Position=At(p.X);if(Seeked!=null)Seeked(this,EventArgs.Empty);}InvalidateVisual();}
 }
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(!IsMouseCaptured)return;var p=e.GetPosition(this);if(gesture==4){Position=At(p.X);if(Seeked!=null)Seeked(this,EventArgs.Empty);}else MoveClipGesture(At(p.X),(int)Math.Floor((p.Y-Top)/Row),(Keyboard.Modifiers&ModifierKeys.Alt)==0);}
 protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){EndClipGesture();gesture=0;ReleaseMouseCapture();base.OnMouseLeftButtonUp(e);}
 protected override void OnLostMouseCapture(MouseEventArgs e){EndClipGesture();gesture=0;base.OnLostMouseCapture(e);}
 protected override void OnMouseWheel(MouseWheelEventArgs e){if((Keyboard.Modifiers&ModifierKeys.Shift)!=0)Offset=Math.Max(0,Offset-e.Delta/120*100/Pixels);else Zoom(e.Delta>0?1.2:1/1.2);InvalidateVisual();e.Handled=true;}
}
