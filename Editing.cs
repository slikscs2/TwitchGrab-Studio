using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

public partial class Studio
{
 VideoTimeline timeline;
 DispatcherTimer journalDelay=new DispatcherTimer(),autosaveTimer=new DispatcherTimer(),thumbnailDelay=new DispatcherTimer();
 List<string> journal=new List<string>();int journalIndex=-1;
 bool restoringJournal,startupRequested,closingEditor;
 int thumbnailVersion;
 bool thumbnailsRunning;
 ProcessHolder thumbnailProcess=new ProcessHolder();
 class ProcessHolder {public System.Diagnostics.Process Current;}
 string recoveryRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TwitchGrabStudio","Recovery");
 string recoverySession="session-"+Guid.NewGuid().ToString("N")+".tgstudio",lastRecoverySnapshot,availableRecovery;
 string Snapshot(){var c=Range();return new JavaScriptSerializer().Serialize(new Project{Version=3,Source=source,In=c.Start,Out=c.End,Clips=clips.Select(v=>new Clip{Start=v.Start,End=v.End}).ToList(),Settings=Settings(false)});}
 void InitEditing(){
  timeline=new VideoTimeline();UI<Border>("TimelineHost").Child=timeline;
  timeline.RangeChanged+=delegate{double a=timeline.SelectionStart,b=timeline.SelectionEnd;loading=true;TB("TrimIn").Text=Time(a);TB("TrimOut").Text=Time(b);loading=false;QueueJournal();};
  timeline.SeekChanged+=delegate{Slide("Seek").Value=timeline.Position;};
  timeline.EditStarted+=delegate{FlushJournal();};timeline.EditCompleted+=delegate{SyncTimeline();FlushJournal();};
  timeline.ViewChanged+=delegate{thumbnailVersion++;Kill(thumbnailProcess.Current);thumbnailDelay.Stop();thumbnailDelay.Start();};
  Btn("TimelineFull").Click+=delegate{timeline.ResetView();};
  Btn("TimelineIn").Click+=delegate{timeline.Zoom(2,Slide("Seek").Value);};Btn("TimelineOut").Click+=delegate{timeline.Zoom(.5,Slide("Seek").Value);};
  Btn("Undo").Click+=delegate{HistoryStep(-1);};Btn("Redo").Click+=delegate{HistoryStep(1);};
  Btn("StepBack").Click+=delegate{StepFrame(-1);};Btn("StepForward").Click+=delegate{StepFrame(1);};
  Btn("RecoverSession").Click+=async delegate{if(availableRecovery!=null){await LoadProjectFile(availableRecovery);Btn("RecoverSession").Visibility=Visibility.Collapsed;Status("Автокопия восстановлена. Сохраните проект под нужным именем.");}};
  journalDelay.Interval=TimeSpan.FromMilliseconds(500);journalDelay.Tick+=delegate{journalDelay.Stop();FlushJournal();};
  autosaveTimer.Interval=TimeSpan.FromSeconds(10);autosaveTimer.Tick+=delegate{if(!selfTesting)PersistRecovery();};autosaveTimer.Start();
  thumbnailDelay.Interval=TimeSpan.FromMilliseconds(450);thumbnailDelay.Tick+=async delegate{thumbnailDelay.Stop();if(!selfTesting)await GenerateThumbnails();};
  window.Loaded+=delegate{if(!selfTesting&&!startupRequested)FindRecovery();};
  window.Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e){if(!e.Cancel&&!selfTesting)PersistRecovery();};
  window.Closed+=delegate{closingEditor=true;journalDelay.Stop();autosaveTimer.Stop();thumbnailDelay.Stop();thumbnailVersion++;Kill(thumbnailProcess.Current);};
  UpdateHistoryButtons();
 }
 void StepFrame(int direction){if(source==null)return;Slide("Seek").Value=Math.Max(0,Math.Min(duration,Slide("Seek").Value+direction/(Combo("FrameRate").SelectedIndex==1?60.0:30)));timeline.EnsureVisible(Slide("Seek").Value);}
 void SyncTimeline(){if(timeline==null)return;if(!timeline.InRangeGesture){try{var c=Range();timeline.SelectionStart=c.Start;timeline.SelectionEnd=c.End;}catch{}}timeline.Position=Slide("Seek").Value;timeline.InvalidateVisual();}
 void ResetJournal(){journalDelay.Stop();journal.Clear();journalIndex=-1;try{journal.Add(Snapshot());journalIndex=0;}catch{}UpdateHistoryButtons();}
 void QueueJournal(){if(loading||restoringJournal||source==null||selfTesting)return;Label("ProjectState").Text="Есть изменения";if(timeline!=null&&timeline.InRangeGesture)return;journalDelay.Stop();journalDelay.Start();}
 void FlushJournal(){if(source==null||loading||restoringJournal)return;string state;try{state=Snapshot();}catch{return;}if(journalIndex>=0&&journal[journalIndex]==state)return;
  if(journalIndex+1<journal.Count)journal.RemoveRange(journalIndex+1,journal.Count-journalIndex-1);
  journal.Add(state);if(journal.Count>60)journal.RemoveAt(0);journalIndex=journal.Count-1;UpdateHistoryButtons();
 }
 void UpdateHistoryButtons(){if(Btn("Undo")!=null)Btn("Undo").IsEnabled=journalIndex>0;if(Btn("Redo")!=null)Btn("Redo").IsEnabled=journalIndex>=0&&journalIndex<journal.Count-1;}
 void HistoryStep(int direction){if(source==null||loading||exporting||previewBuilding||timeline.InRangeGesture)return;journalDelay.Stop();
  if(direction<0){try{Snapshot();}catch{if(journalIndex>=0)RestoreSnapshot(journal[journalIndex]);return;}}
  FlushJournal();int target=journalIndex+direction;if(target<0||target>=journal.Count)return;journalIndex=target;RestoreSnapshot(journal[journalIndex]);UpdateHistoryButtons();
 }
 void RestoreSnapshot(string data){
  var p=new JavaScriptSerializer().Deserialize<Project>(data);if(p.Source!=source)return;
  restoringJournal=true;loading=true;
  try{Apply(p.Settings);TB("TrimIn").Text=Time(p.In);TB("TrimOut").Text=Time(p.Out);clips=p.Clips;RefreshClips();Slide("Seek").Value=p.In;}finally{loading=false;restoringJournal=false;}
  SyncTimeline();Changed(false);Label("ProjectState").Text="Есть изменения";Status("Изменение "+(journalIndex+1)+" из "+journal.Count+" • Ctrl+Z / Ctrl+Y");
 }
 void NewEditorSession(){recoverySession="session-"+Guid.NewGuid().ToString("N")+".tgstudio";lastRecoverySnapshot=null;}
 bool PersistRecovery(string destination=null){if(source==null||loading||restoringJournal)return false;
  try{string state;try{state=Snapshot();}catch{if(journalIndex<0)return false;state=journal[journalIndex];}
   if(destination==null&&state==lastRecoverySnapshot)return true;
   string path=destination??Path.Combine(recoveryRoot,recoverySession);AtomicProjectWrite(path,state);if(destination==null){lastRecoverySnapshot=state;Label("ProjectState").Text="Автокопия • "+DateTime.Now.ToString("HH:mm:ss");}return true;
  }catch(Exception e){Status("Автосохранение не удалось: "+e.Message);return false;}
 }
 static void AtomicProjectWrite(string path,string data){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,data,new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
 void FindRecovery(){try{if(!Directory.Exists(recoveryRoot))return;foreach(string path in Directory.GetFiles(recoveryRoot,"session-*.tgstudio").OrderByDescending(File.GetLastWriteTimeUtc).Take(50)){try{if(new FileInfo(path).Length>300000)continue;var p=new JavaScriptSerializer().Deserialize<Project>(File.ReadAllText(path));if(p!=null&&p.Settings!=null&&p.Clips!=null&&p.Clips.Count<=20&&File.Exists(p.Source)){availableRecovery=path;Btn("RecoverSession").Visibility=Visibility.Visible;Btn("RecoverSession").ToolTip=Path.GetFileName(p.Source)+" • "+File.GetLastWriteTime(path).ToString("g");return;}}catch{}}}catch{}}
 async Task GenerateThumbnails(){
  if(timeline==null||source==null||loading||closingEditor||thumbnailsRunning)return;
  int version=thumbnailVersion;string input=source;double start=timeline.ViewStart,end=timeline.ViewEnd;
  string job=Path.Combine(cacheRoot,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(job);thumbnailsRunning=true;
  var images=new BitmapSource[10];timeline.Thumbnails=images;timeline.ThumbnailStart=start;timeline.ThumbnailEnd=end;timeline.InvalidateVisual();
  try{for(int i=0;i<images.Length;i++){
   if(version!=thumbnailVersion||closingEditor)break;
   double at=Math.Min(Math.Max(0,duration-.08),start+(end-start)*(i+.5)/images.Length);string output=Path.Combine(job,"t"+i+".jpg");
   var r=await Run(ffmpeg,"-hide_banner -loglevel error -nostdin -ss "+N(at)+" -i "+Q(input)+" -frames:v 1 -vf scale=160:-2 -update 1 "+Q(output),job,p=>thumbnailProcess.Current=p,null);thumbnailProcess.Current=null;
   if(version!=thumbnailVersion||closingEditor)break;
   if(r.Code==0&&File.Exists(output)){images[i]=ReadBitmap(output);timeline.InvalidateVisual();}
  }}finally{thumbnailProcess.Current=null;CleanJob(job);thumbnailsRunning=false;if(version!=thumbnailVersion&&!closingEditor&&!selfTesting){thumbnailDelay.Stop();thumbnailDelay.Start();}}
 }
}
