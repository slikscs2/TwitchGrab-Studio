using System;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;

public partial class Studio
{
 async Task TestEditing(List<string> report){
  clips.Clear();RefreshClips();TB("TrimIn").Text=Time(.3);TB("TrimOut").Text=Time(3.5);ResetJournal();
  string baseline=Snapshot();TB("Caption").Text="Новый титр • sliks37";Slide("Volume").Value=37;Slide("Speed").Value=1.5;Slide("Brightness").Value=.15;UI<System.Windows.Controls.CheckBox>("Mirror").IsChecked=true;Combo("FrameRate").SelectedIndex=1;clips.Add(new Clip{Start=1,End=2});RefreshClips();FlushJournal();string edited=Snapshot();
  HistoryStep(-1);if(Snapshot()!=baseline)throw new Exception("Undo did not restore text/audio/queue/range");HistoryStep(1);if(Snapshot()!=edited)throw new Exception("Redo did not restore complete project state");
  HistoryStep(-1);TB("Caption").Text="Другая ветка";FlushJournal();if(Btn("Redo").IsEnabled)throw new Exception("Redo branch was not invalidated");
  TB("TrimIn").Text="NaN";HistoryStep(-1);if(TB("TrimIn").Text=="NaN")throw new Exception("Undo did not repair an invalid trim field");
  report.Add("PASS full-project undo/redo, new edit branching and invalid-input recovery");
  clips.Clear();RefreshClips();TB("TrimIn").Text=Time(.3);TB("TrimOut").Text=Time(3.5);ResetJournal();
  timeline.BeginGesture(4,.5);timeline.UpdateGesture(2.5);timeline.EndGesture();var c=Range();if(Math.Abs(c.Start-.5)>.002||Math.Abs(c.End-2.5)>.002)throw new Exception("Timeline two-boundary selection lost a boundary");
  timeline.BeginGesture(5,1);timeline.UpdateGesture(3);timeline.EndGesture();c=Range();if(Math.Abs(c.Start-2)>.002||Math.Abs(c.End-4)>.002)throw new Exception("Timeline move did not preserve/clamp duration");
  HistoryStep(-1);c=Range();if(Math.Abs(c.Start-.5)>.002||Math.Abs(c.End-2.5)>.002)throw new Exception("Timeline gesture was not a single undo step");
  timeline.BeginGesture(3,2.5);timeline.UpdateGesture(0);timeline.EndGesture();c=Range();if(c.End-c.Start<.099)throw new Exception("Trim minimum was not enforced");
  report.Add("PASS timeline selection, edge trim, clamped range move and gesture undo");
  timeline.ResetView();timeline.Zoom(4,1);if(Math.Abs(timeline.ViewEnd-timeline.ViewStart-1)>.002)throw new Exception("Timeline zoom failed");timeline.EnsureVisible(3);if(timeline.ViewStart>3||timeline.ViewEnd<3)throw new Exception("Timeline viewport did not follow navigation");
  double t=timeline.TimeAt(timeline.PixelAt(3));if(Math.Abs(t-3)>.002)throw new Exception("Timeline pixel/time mapping failed");
  timeline.ResetView();await GenerateThumbnails();int generated=0;foreach(var image in timeline.Thumbnails)if(image!=null)generated++;if(generated!=10)throw new Exception("Actual FFmpeg thumbnail extraction failed: "+generated);
  report.Add("PASS timeline zoom, time mapping, navigation and 10 extracted video thumbnails");
  string saved=Path.Combine(root,"test-autosave.tgstudio");ResetJournal();if(!PersistRecovery(saved))throw new Exception("Recovery write failed");
  Slide("Volume").Value=28;FlushJournal();if(!PersistRecovery(saved))throw new Exception("Atomic recovery replacement failed");
  var p=new JavaScriptSerializer().Deserialize<Project>(File.ReadAllText(saved));if(p.Settings.Volume!=28||p.Settings.Caption!=TB("Caption").Text)throw new Exception("Recovery settings differ from editor");
  string valid=File.ReadAllText(saved);TB("TextSize").Text="bad";if(!PersistRecovery(saved)||File.ReadAllText(saved)!=valid)throw new Exception("Invalid fields overwrote the last valid autosave");TB("TextSize").Text="54";
  await LoadProjectFile(saved);if(Settings().Volume!=28||Label("ProjectState").Text!="Проект открыт")throw new Exception("Recovery project did not reopen correctly");
  report.Add("PASS atomic autosave, last-valid-state fallback and real recovery project loading");
  string realRecovery=recoveryRoot;try{recoveryRoot=Path.Combine(root,"test-recovery");Directory.CreateDirectory(recoveryRoot);File.Copy(saved,Path.Combine(recoveryRoot,"session-validation.tgstudio"),true);FindRecovery();if(availableRecovery==null||Btn("RecoverSession").Visibility!=Visibility.Visible)throw new Exception("Recovery discovery/banner failed");}finally{recoveryRoot=realRecovery;availableRecovery=null;Btn("RecoverSession").Visibility=Visibility.Collapsed;}
  report.Add("PASS recovery discovery without accessing or overwriting real user recovery files");
  TB("TrimIn").Text=Time(.5);TB("TrimOut").Text=Time(2.8);Slide("Seek").Value=1.2;SyncTimeline();await RenderFrame();await GenerateThumbnails();Screenshot("studio-editor.png");
 }
}
