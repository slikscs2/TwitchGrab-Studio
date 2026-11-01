using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

[assembly:System.Reflection.AssemblyVersion("7.0.0.0")]
[assembly:System.Reflection.AssemblyFileVersion("7.0.0.0")]
[assembly:System.Reflection.AssemblyProduct("TwitchGrab Studio")]
[assembly:System.Reflection.AssemblyCompany("sliks37")]

public class Clip { public double Start {get;set;} public double End {get;set;} public override string ToString() { return Studio.Time(Start)+" → "+Studio.Time(End); } }
public class EditSettings {
 public int Aspect {get;set;} public int Fit {get;set;} public double X {get;set;} public double Y {get;set;}
 public string Caption {get;set;} public int Style {get;set;} public double Font {get;set;} public double TextY {get;set;}
 public double Volume {get;set;} public int Resolution {get;set;} public string Subtitles {get;set;}
 public int Layout {get;set;} public double CamX {get;set;} public double CamY {get;set;} public double CamW {get;set;} public double CamH {get;set;} public double CamPanel {get;set;}
 public string Music {get;set;} public double MusicVolume {get;set;} public bool MusicFade {get;set;}
 public double Speed {get;set;} public double Brightness {get;set;} public double Contrast {get;set;} public double Saturation {get;set;} public bool Mirror {get;set;} public int Fps {get;set;}
 public EditSettings(){CamW=25;CamH=30;CamPanel=30;MusicVolume=25;MusicFade=true;Speed=Contrast=Saturation=1;Fps=30;}
}
public class Project {
 public MontageDocument Montage {get;set;}
 public int Version {get;set;} public string Source {get;set;} public double In {get;set;} public double Out {get;set;}
 public List<Clip> Clips {get;set;} public EditSettings Settings {get;set;}
}
class RunResult { public int Code; public string Output; public string Error; }

public partial class Studio
{
 static readonly System.Threading.Mutex appMutex=new System.Threading.Mutex(false,"TwitchGrabStudio");
 Window window;
 readonly string root=AppDomain.CurrentDomain.BaseDirectory;
 string ffmpeg,python,source,subtitle,lastDownload,music;
 double duration,doneSeconds,totalSeconds;
 int sourceWidth,sourceHeight,segments,totalSegments,frameVersion;
 bool hasAudio,downloading,downloadStopping,muxing,exporting,frameBusy,loading,playing,previewBuilding;
 DateTime downloadBegan;
 Process downloadProcess,exportProcess;
 DispatcherTimer ticks=new DispatcherTimer(),frameDelay=new DispatcherTimer();
 List<Clip> clips=new List<Clip>();
 string cacheRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TwitchGrabStudio","Cache");
 bool selfTesting,previewCancelRequested;
 Process previewProcess;
 bool exportCancelRequested;
 string previewJob;
 bool cameraDragging;
 Point cameraStart;
 TextBox TB(string n) { return (TextBox)window.FindName(n); }
 TextBlock Label(string n) { return (TextBlock)window.FindName(n); }
 Button Btn(string n) { return (Button)window.FindName(n); }
 Slider Slide(string n) { return (Slider)window.FindName(n); }
 ComboBox Combo(string n) { return (ComboBox)window.FindName(n); }
 ProgressBar Bar(string n) { return (ProgressBar)window.FindName(n); }
 T UI<T>(string n) where T:class { return window.FindName(n) as T; }
 void Dispatch(Action action) { if (!window.Dispatcher.HasShutdownStarted) window.Dispatcher.BeginInvoke(action); }
 static string N(double v) { return v.ToString("0.######",CultureInfo.InvariantCulture); }
 static string Q(string s) { return "\""+s.Replace("\"","")+"\""; }
 public static string Time(double secs) { var t=TimeSpan.FromSeconds(Math.Max(0,secs)); return String.Format("{0:00}:{1:00}:{2:00}.{3:000}",(int)t.TotalHours,t.Minutes,t.Seconds,t.Milliseconds); }
 static string ShortTime(double secs) { return Time(secs).Split('.')[0]; }
 static string Size(long b) { return b>1073741824L ? (b/1073741824.0).ToString("0.00")+" ГБ" : (b/1048576.0).ToString("0.0")+" МБ"; }
 void Status(string s) { Label("TaskStatus").Text=s;Label("TaskStatus").ToolTip=s; }

 [STAThread] public static void Main(string[] args) {
  try {
   var app=new Application(); app.ShutdownMode=ShutdownMode.OnMainWindowClose;
   var studio=new Studio();
   if(args.Contains("--self-test")||args.Contains("--montage-test")) {
    studio.selfTesting=true;
    studio.window.Loaded+=async delegate { try { if(args.Contains("--montage-test")){var report=new List<string>();await studio.TestMontage(report);File.WriteAllLines(Path.Combine(studio.root,"studio-test.txt"),report);}else await studio.SelfTest(); } catch(Exception ex) { File.WriteAllText(Path.Combine(studio.root,"studio-test.txt"),"FAIL\r\n"+ex); Environment.ExitCode=1; } finally { studio.window.Close(); } };
   }
   else if(args.Length>0&&File.Exists(args[0])){studio.startupRequested=true;studio.window.Loaded+=async delegate {if(String.Equals(Path.GetExtension(args[0]),".tgstudio",StringComparison.OrdinalIgnoreCase))await studio.LoadProjectFile(args[0]);else await studio.OpenMedia(args[0]);};}
   app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e) { studio.Status("Ошибка: "+e.Exception.Message); e.Handled=true; };
   app.Run(studio.window);
  } catch(Exception ex) { MessageBox.Show("TwitchGrab Studio: "+ex.Message,"Не удалось запустить приложение"); Environment.ExitCode=1; }
 }

 public Studio() {
  using(var reader=System.Xml.XmlReader.Create(Path.Combine(root,"Studio.xaml"))) window=(Window)XamlReader.Load(reader);
  string engine=Path.Combine(root,"engine");
  ffmpeg=Directory.GetFiles(engine,"ffmpeg.exe",SearchOption.AllDirectories).First();
  python=Directory.GetFiles(engine,"python.exe",SearchOption.AllDirectories).First();
  Directory.CreateDirectory(cacheRoot);
  string logo=Path.Combine(root,"assets","logo.png");
  if(File.Exists(logo)) { UI<Image>("BrandLogo").Source=ReadBitmap(logo); window.Icon=BitmapFrame.Create(ReadBitmap(logo)); }
  else { UI<Image>("BrandLogo").Visibility=Visibility.Collapsed; }
  TB("DownloadFolder").Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),"TwitchGrab");
  Btn("Youtube").Click+=delegate { Link("https://www.youtube.com/@slikscs2"); };
  Btn("Telegram").Click+=delegate { Link("https://t.me/sliksy37"); };
  Btn("Twitch").Click+=delegate { Link("https://www.twitch.tv/sliks37"); };
  Btn("Github").Click+=delegate { Link("https://github.com/slikscs2"); };
  Btn("NavDownload").Click+=delegate { Page(false); };
  Btn("NavEditor").Click+=delegate { Page(true); };
  Btn("NavProject").Click+=delegate { SaveProject(); };
  Btn("LoadProject").Click+=async delegate { await LoadProject(); };
  Btn("ImportTop").Click+=async delegate { if(montageMode)await ImportMontage(null);else await Import(); };
  Btn("LibraryAdd").Click+=async delegate {await Import();};
  Btn("BrowseFolder").Click+=delegate { using(var d=new System.Windows.Forms.FolderBrowserDialog()) {d.SelectedPath=TB("DownloadFolder").Text; if(d.ShowDialog()==System.Windows.Forms.DialogResult.OK) TB("DownloadFolder").Text=d.SelectedPath;} };
  Btn("DownloadStart").Click+=async delegate { await Download(); };
  Btn("DownloadStop").Click+=delegate { StopDownload(); };
  Btn("DownloadOpen").Click+=delegate { try { string p=Path.GetFullPath(TB("DownloadFolder").Text); Directory.CreateDirectory(p); Process.Start("explorer.exe",Q(p)); } catch(Exception e) { Status(e.Message); } };
  Btn("EditDownloaded").Click+=async delegate { if(lastDownload!=null) await OpenMedia(lastDownload); };
  Btn("SetIn").Click+=delegate { TB("TrimIn").Text=Time(Slide("Seek").Value); SelectionInfo(); };
  Btn("SetOut").Click+=delegate { TB("TrimOut").Text=Time(Slide("Seek").Value); SelectionInfo(); };
  TB("TrimIn").TextChanged+=delegate { SelectionInfo(); }; TB("TrimOut").TextChanged+=delegate { SelectionInfo(); };
  Btn("AddClip").Click+=delegate { try { Clip c=Range(); if(clips.Count>=20) throw new Exception("В одном проекте поддерживается до 20 фрагментов."); clips.Add(c); RefreshClips(); Status("Фрагмент добавлен. При экспорте очередь будет склеена в один ролик."); } catch(Exception e) { Status(e.Message); } };
  Btn("RemoveClip").Click+=delegate { int i=UI<ListBox>("ClipList").SelectedIndex; if(i>=0) {clips.RemoveAt(i);RefreshClips();} };
  Btn("ClipUp").Click+=delegate { MoveClip(-1); }; Btn("ClipDown").Click+=delegate { MoveClip(1); };
  Btn("ClipUpdate").Click+=delegate { try {int i=UI<ListBox>("ClipList").SelectedIndex;if(i<0)throw new Exception("Выберите фрагмент в очереди, затем измените границы и нажмите «Обновить».");clips[i]=Range();RefreshClips();UI<ListBox>("ClipList").SelectedIndex=i;Status("Границы фрагмента обновлены.");}catch(Exception e){Status(e.Message);} };
  UI<ListBox>("ClipList").SelectionChanged+=delegate { var c=UI<ListBox>("ClipList").SelectedItem as Clip; if(c!=null) { TB("TrimIn").Text=Time(c.Start); TB("TrimOut").Text=Time(c.End); Slide("Seek").Value=c.Start; } };
  Btn("ImportSubs").Click+=delegate { var d=new OpenFileDialog {Filter="Субтитры|*.srt;*.ass",CheckFileExists=true}; if(d.ShowDialog(window)==true) { subtitle=d.FileName; Label("SubsName").Text=Path.GetFileName(subtitle); Changed(); } };
  Btn("ClearSubs").Click+=delegate { subtitle=null; Label("SubsName").Text=""; Changed(); };
  Btn("ImportMusic").Click+=async delegate {await ImportMusic();};
  Btn("ClearMusic").Click+=delegate {music=null;Label("MusicName").Text="";Label("LibraryMusic").Text="Музыка не добавлена";Changed();};
  UI<CheckBox>("ShowSource").Checked+=delegate {Changed(false);}; UI<CheckBox>("ShowSource").Unchecked+=delegate {Changed(false);};
  var image=UI<Image>("PreviewFrame");
  image.MouseLeftButtonDown+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs e){if(source==null||loading||exporting||previewBuilding||UI<CheckBox>("ShowSource").IsChecked!=true)return;cameraStart=ImagePoint(e.GetPosition(image));cameraDragging=true;frameDelay.Stop();image.CaptureMouse();DrawCameraDrag(cameraStart);e.Handled=true;};
  image.MouseMove+=delegate(object sender,System.Windows.Input.MouseEventArgs e){if(cameraDragging)DrawCameraDrag(ImagePoint(e.GetPosition(image)));};
  image.LostMouseCapture+=delegate {if(cameraDragging){cameraDragging=false;UI<System.Windows.Shapes.Rectangle>("CameraDrag").Visibility=Visibility.Collapsed;Changed();}};
  image.MouseLeftButtonUp+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs e){if(!cameraDragging)return;Point end=ImagePoint(e.GetPosition(image));double cw=Math.Max(.05,Math.Abs(end.X-cameraStart.X)),ch=Math.Max(.05,Math.Abs(end.Y-cameraStart.Y)),left=Math.Min(Math.Min(cameraStart.X,end.X),1-cw),top=Math.Min(Math.Min(cameraStart.Y,end.Y),1-ch);cameraDragging=false;image.ReleaseMouseCapture();UI<System.Windows.Shapes.Rectangle>("CameraDrag").Visibility=Visibility.Collapsed;loading=true;Slide("CamW").Value=cw*100;Slide("CamH").Value=ch*100;Slide("CamX").Value=cw>=1?0:left/(1-cw)*100;Slide("CamY").Value=ch>=1?0:top/(1-ch)*100;loading=false;Changed();Status("Область вебкамеры выбрана. Снимите галочку исходника, чтобы увидеть компоновку.");};
  UI<CheckBox>("MusicFade").Checked+=delegate {Changed();}; UI<CheckBox>("MusicFade").Unchecked+=delegate {Changed();};
  Btn("Export").Click+=async delegate { await Export(); };
  Btn("CancelExport").Click+=delegate { if(exporting) { exportCancelRequested=true; Status("Отмена экспорта…"); Kill(exportProcess); } };
  Btn("PlayPreview").Click+=async delegate { await PlayPreview(); };
  UI<MediaElement>("Player").MediaEnded+=delegate { StopPlayer(); };
  UI<MediaElement>("Player").MediaFailed+=delegate { StopPlayer(); Status("Не удалось воспроизвести предпросмотр средствами Windows. Кадры и экспорт работают через FFmpeg."); };
  foreach(string n in new[]{"Aspect","FitMode","TextStyle","Resolution","Layout","FrameRate"}) Combo(n).SelectionChanged+=delegate { Changed(); };
  foreach(string n in new[]{"CropX","CropY","TextY","Volume","CamX","CamY","CamW","CamH","CamPanel","MusicVolume","Speed","Brightness","Contrast","Saturation"}) Slide(n).ValueChanged+=delegate { Changed(); };
  UI<CheckBox>("Mirror").Checked+=delegate {Changed();};UI<CheckBox>("Mirror").Unchecked+=delegate {Changed();};
  Btn("ResetColor").Click+=delegate {Slide("Brightness").Value=0;Slide("Contrast").Value=1;Slide("Saturation").Value=1;UI<CheckBox>("Mirror").IsChecked=false;Changed();};
  TB("Caption").TextChanged+=delegate { Changed(); }; TB("TextSize").TextChanged+=delegate { Changed(); };
  Slide("Seek").ValueChanged+=delegate { Label("CursorTime").Text=Time(Slide("Seek").Value)+" / "+Time(duration);SyncTimeline();Changed(false); };
  Slide("Seek").IsMoveToPointEnabled=true;Slide("Seek").SmallChange=1;Slide("Seek").LargeChange=10;
  window.PreviewKeyDown+=async delegate(object sender,System.Windows.Input.KeyEventArgs e){
   if(montageMode&&(e.Key==System.Windows.Input.Key.Z||e.Key==System.Windows.Input.Key.Y))return;
   if((System.Windows.Input.Keyboard.Modifiers&System.Windows.Input.ModifierKeys.Control)!=0){if(UI<Grid>("EditorPage").Visibility==Visibility.Visible&&(e.Key==System.Windows.Input.Key.Z||e.Key==System.Windows.Input.Key.Y)){e.Handled=true;HistoryStep(e.Key==System.Windows.Input.Key.Y||(System.Windows.Input.Keyboard.Modifiers&System.Windows.Input.ModifierKeys.Shift)!=0?1:-1);}else if(e.Key==System.Windows.Input.Key.S){e.Handled=true;SaveProject();}else if(e.Key==System.Windows.Input.Key.O){e.Handled=true;await Import();}return;}
   if(System.Windows.Input.Keyboard.FocusedElement is TextBox)return;
   if(UI<Grid>("EditorPage").Visibility!=Visibility.Visible)return;
   if(e.Key==System.Windows.Input.Key.I){TB("TrimIn").Text=Time(Slide("Seek").Value);e.Handled=true;}
   if(e.Key==System.Windows.Input.Key.O){TB("TrimOut").Text=Time(Slide("Seek").Value);e.Handled=true;}
   if(e.Key==System.Windows.Input.Key.Space){e.Handled=true;await PlayPreview();}
  };
  frameDelay.Interval=TimeSpan.FromMilliseconds(350); frameDelay.Tick+=async delegate { frameDelay.Stop(); await RenderFrame(); };
  ticks.Interval=TimeSpan.FromSeconds(1); ticks.Tick+=delegate { if(downloading&&!muxing) UpdateDownload(); }; ticks.Start();
  window.DragOver+=delegate(object sender,DragEventArgs e) { e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true; };
  window.SourceInitialized+=delegate { try {int dark=1;DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(window).Handle,20,ref dark,4);}catch{} };
  window.Drop+=async delegate(object sender,DragEventArgs e) { if(e.Data.GetDataPresent(DataFormats.FileDrop)) {string[] f=(string[])e.Data.GetData(DataFormats.FileDrop);if(f.Length>0){if(montageMode){foreach(var file in f)await ProbeMontageAsset(file);}else await OpenMedia(f[0]);}} };
  window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e) { if(downloading||exporting||previewBuilding) {e.Cancel=true;Status("Дождитесь завершения операции или нажмите отмену перед закрытием.");} };
  window.Closed+=delegate { ticks.Stop();frameDelay.Stop();UI<MediaElement>("Player").Close();CleanJob(previewJob); };
  Page(false);
  InitEditing();
  InitMontage();
 }
 Point ImagePoint(Point point){var image=UI<Image>("PreviewFrame");double k=Math.Min(image.ActualWidth/sourceWidth,image.ActualHeight/sourceHeight),w=sourceWidth*k,h=sourceHeight*k;return new Point(Math.Max(0,Math.Min(1,(point.X-(image.ActualWidth-w)/2)/w)),Math.Max(0,Math.Min(1,(point.Y-(image.ActualHeight-h)/2)/h)));}
 void DrawCameraDrag(Point end){var image=UI<Image>("PreviewFrame");double k=Math.Min(image.ActualWidth/sourceWidth,image.ActualHeight/sourceHeight),w=sourceWidth*k,h=sourceHeight*k;var box=UI<System.Windows.Shapes.Rectangle>("CameraDrag");Canvas.SetLeft(box,(image.ActualWidth-w)/2+Math.Min(cameraStart.X,end.X)*w);Canvas.SetTop(box,(image.ActualHeight-h)/2+Math.Min(cameraStart.Y,end.Y)*h);box.Width=Math.Abs(end.X-cameraStart.X)*w;box.Height=Math.Abs(end.Y-cameraStart.Y)*h;box.Visibility=Visibility.Visible;}

 [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
 static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);

 void Link(string url) { try {Process.Start(new ProcessStartInfo(url){UseShellExecute=true});} catch(Exception e){Status(e.Message);} }
 void Page(bool editor) {
  if(montagePlaying)StopMontagePlayer();montageMode=false;if(montageNav!=null){montageNav.Background=MC("#191a1f");montageNav.Foreground=MC("#a0a0af");}if(montagePage!=null)montagePage.Visibility=Visibility.Collapsed;
  UI<Grid>("EditorPage").Visibility=editor?Visibility.Visible:Visibility.Collapsed;
  UI<Grid>("DownloadPage").Visibility=editor?Visibility.Collapsed:Visibility.Visible;
  window.Title="TwitchGrab Studio 7.0 — sliks37";
  foreach(string name in new[]{"NavEditor","NavDownload"}){bool active=name==(editor?"NavEditor":"NavDownload");Btn(name).Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(active?"#302742":"#191a1f"));Btn(name).Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(active?"#b59aff":"#a0a0af"));Btn(name).BorderThickness=new Thickness(0);}
 }

 static Task<RunResult> Run(string exe,string args,string dir,Action<Process> started,Action<string> line) {
  return Task.Run(delegate {
   using(var p=new Process()) {
    var output=new StringBuilder();var error=new StringBuilder();
    p.StartInfo=new ProcessStartInfo(exe,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=dir};
    p.OutputDataReceived+=delegate(object s,DataReceivedEventArgs e) {if(e.Data!=null){if(output.Length<180000) output.AppendLine(e.Data);if(line!=null) line(e.Data);}};
    p.ErrorDataReceived+=delegate(object s,DataReceivedEventArgs e) {if(e.Data!=null){if(error.Length<180000) error.AppendLine(e.Data);if(line!=null) line(e.Data);}};
    p.Start();if(started!=null) started(p);p.BeginOutputReadLine();p.BeginErrorReadLine();p.WaitForExit();
    return new RunResult{Code=p.ExitCode,Output=output.ToString(),Error=error.ToString()};
   }
  });
 }
 static void Kill(Process p) { Task.Run(delegate {try {if(p!=null&&!p.HasExited) {using(var k=Process.Start(new ProcessStartInfo("taskkill.exe","/PID "+p.Id+" /T /F"){UseShellExecute=false,CreateNoWindow=true})) k.WaitForExit();}} catch {} }); }
 static BitmapImage ReadBitmap(string path) {var b=new BitmapImage();using(var stream=File.OpenRead(path)){b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.StreamSource=stream;b.EndInit();}b.Freeze();return b;}
 static bool Normalize(string input,out string normalized) {
  normalized=null;input=input.Trim();if(input.IndexOfAny(new[]{'"','\r','\n'})>=0) return false;if(!input.Contains("://"))input="https://"+input;
  Uri u;if(!Uri.TryCreate(input,UriKind.Absolute,out u)||!(u.Scheme=="http"||u.Scheme=="https")||!(u.Host=="www.twitch.tv"||u.Host=="twitch.tv"||u.Host=="m.twitch.tv")||u.UserInfo!=""||!u.IsDefaultPort) return false;
  string path=u.AbsolutePath.Trim('/');if(!Regex.IsMatch(path,@"^(videos/\d+|[A-Za-z0-9_]{1,25})$")||Regex.IsMatch(path,@"^(directory|downloads|settings|subscriptions|inventory|wallet|search|videos)$",RegexOptions.IgnoreCase))return false;
  normalized="https://www.twitch.tv/"+path;return true;
 }
 void DownloadLog(string s) {
  if(s.StartsWith("@@PROGRESS|")){string[] f=s.Split('|');if(f.Length==5){double.TryParse(f[1],NumberStyles.Float,CultureInfo.InvariantCulture,out doneSeconds);double.TryParse(f[2],NumberStyles.Float,CultureInfo.InvariantCulture,out totalSeconds);int.TryParse(f[3],out segments);int.TryParse(f[4],out totalSegments);}return;}
  var box=TB("DownloadLog"); if(box.Text.Length>30000)box.Text=box.Text.Substring(box.Text.Length-15000);box.AppendText(s+Environment.NewLine);box.ScrollToEnd();
 }
 string activeDownload;
 void UpdateDownload() {
  long bytes=0;try{if(File.Exists(activeDownload)) bytes=new FileInfo(activeDownload).Length;}catch{}
  double elapsed=Math.Max(1,(DateTime.Now-downloadBegan).TotalSeconds),speed=bytes/1048576.0/elapsed;
  Label("DownloadSize").Text=Size(bytes);Label("DownloadSpeed").Text=speed.ToString("0.00")+" МБ/с";
  Bar("DownloadProgress").IsIndeterminate=totalSeconds<=0;
  if(totalSeconds>0) {
   double f=Math.Min(1,doneSeconds/totalSeconds);Bar("DownloadProgress").Value=f*100;
   Label("DownloadPercent").Text=(f*100).ToString("0.0")+"%";
   Label("DownloadDuration").Text=ShortTime(doneSeconds)+" / "+ShortTime(totalSeconds);
   Label("DownloadEta").Text=f>0?ShortTime(elapsed*(1-f)/f):"Расчёт…";
   Label("DownloadStatus").Text="Скачивание записи • фрагменты "+segments+" / "+totalSegments;
   Label("DownloadNote").Text=f>0?"Общий размер: ≈ "+Size((long)(bytes/f))+". Размер и время — приблизительные оценки.":"Получаем данные записи…";
  } else {
   Label("DownloadPercent").Text=bytes>0?"LIVE":"···";Label("DownloadDuration").Text=ShortTime(doneSeconds)+" / эфир";
   Label("DownloadEta").Text="Без лимита";Label("DownloadStatus").Text=bytes>0?"Запись прямого эфира":"Подключение к Twitch…";
  }
  if(downloadStopping)Label("DownloadStatus").Text="Остановка и сохранение скачанной части…";
 }
 void StopDownload() { if(!downloading||muxing)return;downloadStopping=true;Btn("DownloadStop").IsEnabled=false;Kill(downloadProcess); }
 async Task Download() {
  if(downloading)return;string url;if(!Normalize(TB("DownloadUrl").Text,out url)){Status("Введите ссылку на канал Twitch или запись twitch.tv/videos/номер.");return;}
  try {
   string folder=Path.GetFullPath(TB("DownloadFolder").Text);Directory.CreateDirectory(folder);
   activeDownload=Path.Combine(folder,new Uri(url).AbsolutePath.Trim('/').Replace('/','_')+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N").Substring(0,6)+".ts");
   bool mp4=UI<CheckBox>("DownloadMp4").IsChecked==true;
   downloading=true;downloadStopping=false;muxing=false;doneSeconds=totalSeconds=0;segments=totalSegments=0;downloadBegan=DateTime.Now;
   Btn("DownloadStart").IsEnabled=false;Btn("DownloadStop").IsEnabled=true;Btn("BrowseFolder").IsEnabled=false;TB("DownloadUrl").IsEnabled=false;TB("DownloadFolder").IsEnabled=false;TB("DownloadLog").Clear();
   UpdateDownload();
   var r=await Run(python,"-B "+Q(Path.Combine(root,"progress_bridge.py"))+" --no-config --progress no --twitch-supported-codecs h264,h265,av1 --ffmpeg-ffmpeg "+Q(ffmpeg)+" --output "+Q(activeDownload)+" "+Q(url)+" best",root,p=>{downloadProcess=p;if(downloadStopping)Kill(p);},s=>Dispatch(()=>DownloadLog(s)));
   downloadProcess=null;UpdateDownload();Btn("DownloadStop").IsEnabled=false;
   if(!File.Exists(activeDownload)||new FileInfo(activeDownload).Length==0)throw new Exception("Не удалось скачать поток. Проверьте доступность эфира; подробности в журнале.");
   string saved=activeDownload;
   if(mp4) {
    muxing=true;Label("DownloadStatus").Text="Упаковка в MP4 без перекодирования…";Label("DownloadPercent").Text="MP4";Bar("DownloadProgress").IsIndeterminate=true;
    string target=Path.ChangeExtension(saved,".mp4");var m=await Run(ffmpeg,"-hide_banner -loglevel warning -nostdin -n -i "+Q(saved)+" -map 0:v? -map 0:a? -c copy "+Q(target),root,null,s=>Dispatch(()=>DownloadLog(s)));
    if(m.Code==0&&File.Exists(target)&&new FileInfo(target).Length>0){File.Delete(saved);saved=target;} else DownloadLog("Упаковка MP4 не удалась. Исходный поток сохранён.");
   }
   lastDownload=saved;Btn("EditDownloaded").IsEnabled=true;
   bool full=!downloadStopping&&r.Code==0;
   Label("DownloadStatus").Text=full?"Готово! Теперь можно перейти к монтажу.":"Скачанная часть сохранена.";
   if(full){Bar("DownloadProgress").Value=100;Label("DownloadPercent").Text="100%";}
   else Label("DownloadPercent").Text=totalSeconds>0?(doneSeconds/totalSeconds*100).ToString("0.0")+"%":"СТОП";
   Label("DownloadNote").Text="Файл: "+saved;Status("Сохранено: "+Path.GetFileName(saved));
  } catch(Exception e) {Status(e.Message);Label("DownloadStatus").Text="Ошибка скачивания — проверьте журнал";}
  finally {downloading=false;muxing=false;downloadProcess=null;Bar("DownloadProgress").IsIndeterminate=false;Btn("DownloadStart").IsEnabled=true;Btn("DownloadStop").IsEnabled=false;Btn("BrowseFolder").IsEnabled=true;TB("DownloadUrl").IsEnabled=true;TB("DownloadFolder").IsEnabled=true;}
 }

 async Task Import() {var d=new OpenFileDialog{Filter="Видеофайлы|*.mp4;*.mkv;*.mov;*.ts;*.webm;*.avi;*.m4v|Все файлы|*.*",CheckFileExists=true};if(d.ShowDialog(window)==true)await OpenMedia(d.FileName);}
 async Task OpenMedia(string path) {
  if(exporting||loading||previewBuilding){Status("Дождитесь окончания текущей операции.");return;}
  if(!File.Exists(path)){Status("Видеофайл не найден: "+path);return;}
  bool opened=false;if(!selfTesting)PersistRecovery();
  try {
   loading=true;StopPlayer();frameVersion++;Status("Открываем видео…");
   var r=await Run(ffmpeg,"-hide_banner -i "+Q(path),root,null,null);
   Match d=Regex.Match(r.Error,@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
   string videoLine=r.Error.Split('\n').FirstOrDefault(l=>l.Contains("Video:"));
   Match dimensions=Regex.Match(videoLine??"",@"\b(\d{2,5})x(\d{2,5})\b");
   if(!d.Success||!dimensions.Success)throw new Exception("Не удалось определить длительность и размер видео. Файл может быть повреждён.");
   double length=double.Parse(d.Groups[1].Value)*3600+double.Parse(d.Groups[2].Value)*60+double.Parse(d.Groups[3].Value,CultureInfo.InvariantCulture);
   if(length<=0)throw new Exception("Видео не содержит доступной длительности.");
   source=Path.GetFullPath(path);duration=length;sourceWidth=int.Parse(dimensions.Groups[1].Value);sourceHeight=int.Parse(dimensions.Groups[2].Value);hasAudio=r.Error.Contains("Audio:");
   if(Regex.IsMatch(r.Error,@"rotation of -?90|rotate\s*:\s*-?90")){int swap=sourceWidth;sourceWidth=sourceHeight;sourceHeight=swap;}
   clips.Clear();RefreshClips();TB("TrimIn").Text=Time(0);TB("TrimOut").Text=Time(duration);
   Slide("Seek").Maximum=duration;Slide("Seek").Value=0;Slide("Seek").IsEnabled=true;
   Label("CursorTime").Text=Time(0)+" / "+Time(duration);Label("SourceName").Text=Path.GetFileName(source);
   Label("LibraryName").Text=Path.GetFileName(source);Label("LibraryMeta").Text=ShortTime(duration)+" • "+sourceWidth+"×"+sourceHeight;UI<Image>("LibraryThumb").Source=null;Label("ProjectState").Text="Новый проект";
   UI<StackPanel>("EmptyPreview").Visibility=Visibility.Collapsed;Btn("Export").IsEnabled=true;Btn("PlayPreview").IsEnabled=true;
   Page(true);opened=true;Status("Видео открыто: "+sourceWidth+"×"+sourceHeight+" • "+ShortTime(duration)+". Настройте фрагмент и кадр.");
  }catch(Exception e){Status(e.Message);}finally{loading=false;}
  if(opened){NewEditorSession();ResetJournal();timeline.Duration=duration;timeline.SelectionStart=0;timeline.SelectionEnd=duration;timeline.Position=0;timeline.Thumbnails=new BitmapSource[0];timeline.ResetView();Changed(false);}
 }
 static double ParseTime(string input) {
  TimeSpan t;double d;
  if(!input.Contains(":")&&double.TryParse(input.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out d)&&!double.IsNaN(d)&&!double.IsInfinity(d))return d;
  if(TimeSpan.TryParse(input,CultureInfo.InvariantCulture,out t))return t.TotalSeconds;
  throw new Exception("Время должно быть в формате 00:00:12.500 или в секундах.");
 }
 Clip Range() {
  if(source==null)throw new Exception("Сначала откройте видео.");
  double a=ParseTime(TB("TrimIn").Text),b=ParseTime(TB("TrimOut").Text);
  if(a<0||b>duration+0.002||b<=a||b-a<0.099999)throw new Exception("Укажите начало и конец внутри видео; длительность — не менее 0,1 секунды.");
  return new Clip{Start=a,End=Math.Min(duration,b)};
 }
 void SelectionInfo(){try{var c=Range();Label("SelectionDuration").Text="Фрагмент: "+Time(c.End-c.Start);}catch{if(Label("SelectionDuration")!=null)Label("SelectionDuration").Text="Проверьте границы";}SyncTimeline();QueueJournal();}
 void RefreshClips(){UI<ListBox>("ClipList").ItemsSource=null;UI<ListBox>("ClipList").ItemsSource=clips;Label("ClipSummary").Text=clips.Count==0?"Очередь пуста — экспорт текущего фрагмента.":clips.Count+" фрагм. • всего "+Time(clips.Sum(c=>c.End-c.Start));QueueJournal();}
 void MoveClip(int delta){int i=UI<ListBox>("ClipList").SelectedIndex,j=i+delta;if(i<0||j<0||j>=clips.Count)return;var c=clips[i];clips.RemoveAt(i);clips.Insert(j,c);RefreshClips();UI<ListBox>("ClipList").SelectedIndex=j;}
 async Task ImportMusic(){
  var d=new OpenFileDialog{Filter="Звук и музыка|*.mp3;*.wav;*.flac;*.ogg;*.m4a;*.aac;*.mp4;*.wma|Все файлы|*.*",CheckFileExists=true};if(d.ShowDialog(window)!=true)return;
  try {Status("Проверяем музыкальный файл…");var r=await Run(ffmpeg,"-hide_banner -i "+Q(d.FileName),root,null,null);if(!r.Error.Contains("Audio:"))throw new Exception("В выбранном файле не найдена аудиодорожка.");music=d.FileName;Label("MusicName").Text=Path.GetFileName(music);Label("LibraryMusic").Text=Path.GetFileName(music);Changed();Status("Музыка добавлена. Настройте её громкость отдельно от исходного звука.");}catch(Exception e){Status(e.Message);}
 }
 EditSettings Settings(bool validateAssets=true) {
  double font; if(!double.TryParse(TB("TextSize").Text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out font)||double.IsNaN(font)||double.IsInfinity(font)||font<16||font>160)throw new Exception("Размер текста должен быть от 16 до 160.");
  if(validateAssets&&music!=null&&!File.Exists(music))throw new Exception("Музыкальный файл перемещён. Добавьте его заново или удалите дорожку.");
  return new EditSettings{Aspect=Combo("Aspect").SelectedIndex,Fit=Combo("FitMode").SelectedIndex,X=Slide("CropX").Value,Y=Slide("CropY").Value,Caption=TB("Caption").Text,Style=Combo("TextStyle").SelectedIndex,Font=font,TextY=Slide("TextY").Value,Volume=Slide("Volume").Value,Resolution=Combo("Resolution").SelectedIndex,Subtitles=subtitle,Layout=Combo("Layout").SelectedIndex,CamX=Slide("CamX").Value,CamY=Slide("CamY").Value,CamW=Slide("CamW").Value,CamH=Slide("CamH").Value,CamPanel=Slide("CamPanel").Value,Music=music,MusicVolume=Slide("MusicVolume").Value,MusicFade=UI<CheckBox>("MusicFade").IsChecked==true,Speed=Slide("Speed").Value,Brightness=Slide("Brightness").Value,Contrast=Slide("Contrast").Value,Saturation=Slide("Saturation").Value,Mirror=UI<CheckBox>("Mirror").IsChecked==true,Fps=Combo("FrameRate").SelectedIndex==1?60:30};
 }
 void Apply(EditSettings s) {if(s==null)return;Combo("Aspect").SelectedIndex=Math.Max(0,Math.Min(2,s.Aspect));Combo("FitMode").SelectedIndex=Math.Max(0,Math.Min(1,s.Fit));Slide("CropX").Value=s.X;Slide("CropY").Value=s.Y;TB("Caption").Text=s.Caption??"";Combo("TextStyle").SelectedIndex=Math.Max(0,Math.Min(2,s.Style));TB("TextSize").Text=N(s.Font);Slide("TextY").Value=s.TextY;Slide("Volume").Value=s.Volume;Combo("Resolution").SelectedIndex=Math.Max(0,Math.Min(1,s.Resolution));subtitle=s.Subtitles;Label("SubsName").Text=subtitle==null?"":Path.GetFileName(subtitle);
  Combo("Layout").SelectedIndex=Math.Max(0,Math.Min(2,s.Layout));Slide("CamX").Value=s.CamX;Slide("CamY").Value=s.CamY;Slide("CamW").Value=s.CamW;Slide("CamH").Value=s.CamH;Slide("CamPanel").Value=s.CamPanel;
  music=s.Music;Label("MusicName").Text=music==null?"":Path.GetFileName(music);Label("LibraryMusic").Text=music==null?"Музыка не добавлена":Path.GetFileName(music);Slide("MusicVolume").Value=s.MusicVolume;UI<CheckBox>("MusicFade").IsChecked=s.MusicFade;
  Slide("Speed").Value=s.Speed;Slide("Brightness").Value=s.Brightness;Slide("Contrast").Value=s.Contrast;Slide("Saturation").Value=s.Saturation;UI<CheckBox>("Mirror").IsChecked=s.Mirror;Combo("FrameRate").SelectedIndex=s.Fps==60?1:0;
 }
 static void Dimensions(EditSettings s,bool preview,out int w,out int h) {
  int edge=preview?480:s.Resolution==1?1280:1920;
  if(s.Aspect==0){h=edge;w=edge*9/16;}else if(s.Aspect==1){h=w=preview?480:s.Resolution==1?720:1080;}else{w=edge;h=edge*9/16;}
  w=w/2*2;h=h/2*2;
 }
 string Job(EditSettings s,int width) {
  string job=Path.Combine(cacheRoot,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(job);
  string font=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"segoeuib.ttf");if(!File.Exists(font))font=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"arial.ttf");File.Copy(font,Path.Combine(job,"font.ttf"));
  File.WriteAllText(Path.Combine(job,"title.txt"),Wrap(s.Caption??"",Math.Max(6,(int)(1080/(s.Font*0.59))-2)),new UTF8Encoding(false));
  if(!String.IsNullOrEmpty(s.Subtitles)){if(!File.Exists(s.Subtitles))throw new Exception("Файл субтитров не найден.");File.Copy(s.Subtitles,Path.Combine(job,"subs"+Path.GetExtension(s.Subtitles).ToLowerInvariant()));}
  return job;
 }
 static string Wrap(string text,int max) {
  var lines=new List<string>();foreach(string paragraph in text.Replace("\r","").Split('\n')){string rest=paragraph;while(rest.Length>max){int at=rest.LastIndexOf(' ',Math.Min(max,rest.Length-1));if(at<max/3)at=max;lines.Add(rest.Substring(0,at));rest=rest.Substring(at).TrimStart();}lines.Add(rest);}return String.Join("\n",lines);
 }
 static string Fit(EditSettings s,int w,int h){return s.Fit==0 ? "scale="+w+":"+h+":force_original_aspect_ratio=increase:force_divisible_by=2,crop="+w+":"+h+":(iw-ow)*"+N(s.X/100)+":(ih-oh)*"+N(s.Y/100) : "scale="+w+":"+h+":force_original_aspect_ratio=decrease:force_divisible_by=2,pad="+w+":"+h+":(ow-iw)/2:(oh-ih)/2:color=black";}
 static string Filter(EditSettings s,int w,int h,double offset,string tag="p") {
  string f=Fit(s,w,h);
  if(s.Layout>0){
   int ch=Math.Max(2,(int)(h*s.CamPanel/100)/2*2),gh=h-ch;
   string cam="crop=trunc(iw*"+N(s.CamW/100)+"/2)*2:trunc(ih*"+N(s.CamH/100)+"/2)*2:(iw-ow)*"+N(s.CamX/100)+":(ih-oh)*"+N(s.CamY/100)+",scale="+w+":"+ch+":force_original_aspect_ratio=increase:force_divisible_by=2,crop="+w+":"+ch+",setsar=1";
   f="split=2["+tag+"g]["+tag+"c];["+tag+"g]"+Fit(s,w,gh)+",setsar=1["+tag+"game];["+tag+"c]"+cam+"["+tag+"cam];"+(s.Layout==1?"["+tag+"cam]["+tag+"game]":"["+tag+"game]["+tag+"cam]")+"vstack=inputs=2";
  }
  f+=",setsar=1,eq=brightness="+N(s.Brightness)+":contrast="+N(s.Contrast)+":saturation="+N(s.Saturation)+(s.Mirror?",hflip":"");
  if(!String.IsNullOrEmpty(s.Caption)) {
   string style=s.Style==1?":fontcolor=white:box=1:boxcolor=0x7924CC@0.88:boxborderw="+N(Math.Max(4,w*0.012)):s.Style==2?":fontcolor=0x6CFFF2:borderw="+N(Math.Max(1,w/360.0))+":bordercolor=0x18204C:shadowcolor=0xC833DF:shadowx=2:shadowy=2":":fontcolor=white:borderw="+N(Math.Max(1,w/300.0))+":bordercolor=black:shadowcolor=black@0.6:shadowx=2:shadowy=2";
   f+=",drawtext=fontfile=font.ttf:textfile=title.txt:expansion=none:fontsize="+N(s.Font*w/1080.0)+style+":x=(w-text_w)/2:y='max(0,min(h-text_h,h*"+N(s.TextY/100)+"-text_h/2))'";
  }
  if(!String.IsNullOrEmpty(s.Subtitles))f+=",setpts=PTS+"+N(offset)+"/TB,subtitles=filename=subs"+Path.GetExtension(s.Subtitles).ToLowerInvariant()+",setpts=PTS-STARTPTS";
  return f+",setpts=PTS/"+N(s.Speed)+",fps="+s.Fps;
 }
 void Changed(bool edit=true) {Label("CamRectInfo").Text=String.Format("X {0:0}%  Y {1:0}%  •  {2:0}% × {3:0}%",Slide("CamX").Value,Slide("CamY").Value,Slide("CamW").Value,Slide("CamH").Value);Label("SpeedValue").Text=Slide("Speed").Value.ToString("0.00")+"×";if(timeline!=null)timeline.FrameStep=1.0/(Combo("FrameRate").SelectedIndex==1?60:30);if(loading||selfTesting)return;if(edit)QueueJournal();frameVersion++;frameDelay.Stop();frameDelay.Start();}
 void CleanJob(string path){try{if(path!=null&&String.Equals(Path.GetDirectoryName(Path.GetFullPath(path)),Path.GetFullPath(cacheRoot),StringComparison.OrdinalIgnoreCase)&&Regex.IsMatch(Path.GetFileName(path),"^[a-f0-9]{32}$"))Directory.Delete(path,true);}catch{}}
 void StopPlayer(){playing=false;var p=UI<MediaElement>("Player");p.Stop();p.Close();p.Visibility=Visibility.Collapsed;UI<Image>("PreviewFrame").Visibility=Visibility.Visible;Btn("PlayPreview").Content="▶  Просмотр";CleanJob(previewJob);previewJob=null;}
 async Task RenderFrame() {
  if(source==null||loading||frameBusy||previewBuilding||exporting||cameraDragging)return;
  int version=frameVersion;string src=source,job=null;
  try {
   var s=Settings();int w,h;Dimensions(s,true,out w,out h);double at=Math.Min(Slide("Seek").Value,Math.Max(0,duration-0.08));
   job=Job(s,w);string path=Path.Combine(job,"frame.png");frameBusy=true;
   StopPlayer();Label("PreviewState").Text="Обновляем кадр…";
   bool original=UI<CheckBox>("ShowSource").IsChecked==true;
   string vf=original?"drawbox=x=iw*"+N((1-s.CamW/100)*s.CamX/100)+":y=ih*"+N((1-s.CamH/100)*s.CamY/100)+":w=iw*"+N(s.CamW/100)+":h=ih*"+N(s.CamH/100)+":color=0x62FFF2:t=3,scale=640:-2,setsar=1":Filter(s,w,h,at);
   var r=await Run(ffmpeg,"-hide_banner -loglevel error -nostdin -ss "+N(at)+" -i "+Q(src)+" -frames:v 1 -vf "+Q(vf)+" -update 1 "+Q(path),job,null,null);
   if(version!=frameVersion||src!=source)return;
   if(r.Code!=0||!File.Exists(path))throw new Exception(r.Error);
   UI<Image>("PreviewFrame").Source=ReadBitmap(path);if(UI<Image>("LibraryThumb").Source==null)UI<Image>("LibraryThumb").Source=UI<Image>("PreviewFrame").Source;Label("VideoInfo").Text=original?"ИСХОДНИК / ОБЛАСТЬ ВЕБКАМЕРЫ":"ПРЕДПРОСМОТР  /  "+(s.Aspect==0?"9:16":s.Aspect==1?"1:1":"16:9");
   Label("PreviewState").Text=original?"Бирюзовая рамка показывает область вебкамеры. Снимите галочку, чтобы увидеть результат.":"Кадр "+Time(at)+" • оформление совпадает с экспортом";
  }catch(Exception e){Label("PreviewState").Text="Не удалось показать кадр: "+Tail(e.Message,220);}
  finally{CleanJob(job);frameBusy=false;if(version!=frameVersion)Changed(false);}
 }
 async Task PlayPreview() {
  if(previewBuilding){previewCancelRequested=true;Kill(previewProcess);return;}
  if(playing){StopPlayer();return;}if(source==null||exporting||loading)return;
  try {
   var s=Settings();var ranges=PreviewRanges(clips.Count>0?clips:new List<Clip>{Range()},20*s.Speed);double length=ranges.Sum(c=>c.End-c.Start)/s.Speed;
   StopPlayer();string job=Job(s,270),path=Path.Combine(job,"preview.mp4");previewJob=job;
   previewBuilding=true;previewCancelRequested=false;Btn("PlayPreview").Content="Отмена";Status("Создаём лёгкий предпросмотр до 20 секунд с оформлением и звуком…");
   var r=await ExportFile(s,ranges,path,p=>{previewProcess=p;if(previewCancelRequested)Kill(p);},null,true);
   if(previewCancelRequested){Status("Предпросмотр отменён.");StopPlayer();return;}
   if(r.Code!=0)throw new Exception(Tail(r.Error,500));
   if(selfTesting){Status("PASS preview encode");return;}
   var player=UI<MediaElement>("Player");player.Source=new Uri(path);player.Visibility=Visibility.Visible;UI<Image>("PreviewFrame").Visibility=Visibility.Collapsed;player.Volume=1;player.Play();playing=true;Btn("PlayPreview").Content="■  Стоп";Status("Предпросмотр начала "+(clips.Count>0?"очереди":"фрагмента")+": "+ShortTime(length)+". Музыка включена, если она добавлена.");
  }catch(Exception e){Status("Ошибка предпросмотра: "+e.Message);StopPlayer();}finally{previewBuilding=false;previewProcess=null;Btn("PlayPreview").IsEnabled=true;if(!playing)Btn("PlayPreview").Content="▶  Просмотр";}
 }
 static string Tail(string s,int max){return s.Length>max?s.Substring(s.Length-max):s;}
 static List<Clip> PreviewRanges(List<Clip> sourceClips,double limit){var result=new List<Clip>();foreach(var c in sourceClips){double length=Math.Min(c.End-c.Start,limit);if(length>0)result.Add(new Clip{Start=c.Start,End=c.Start+length});limit-=length;if(limit<=0)break;}return result;}
 async Task Export() {
  if(montageMode){try{await ExportMontage();}catch(Exception ex){Status(ex.Message);}return;}
  if(exporting||loading||source==null)return;
  try {
   var ranges=clips.Count>0?new List<Clip>(clips):new List<Clip>{Range()};
   foreach(var c in ranges)if(c.Start<0||c.End>duration+0.002||c.End<=c.Start)throw new Exception("Проверьте границы фрагментов.");
   if(!ConfigureExport(ranges))return;var s=Settings();
   var d=new SaveFileDialog{Filter="Видео MP4|*.mp4",DefaultExt=".mp4",FileName=Path.GetFileNameWithoutExtension(source)+"_short.mp4",OverwritePrompt=true};if(d.ShowDialog(window)!=true)return;
   if(!String.Equals(Path.GetExtension(d.FileName),".mp4",StringComparison.OrdinalIgnoreCase))throw new Exception("Для экспорта выберите имя с расширением .mp4.");
   if(String.Equals(Path.GetFullPath(d.FileName),Path.GetFullPath(source),StringComparison.OrdinalIgnoreCase))throw new Exception("Выберите другое имя: исходный файл нельзя перезаписывать.");
   exporting=true;exportCancelRequested=false;StopPlayer();Btn("Export").IsEnabled=false;Btn("CancelExport").IsEnabled=true;Bar("TaskProgress").Visibility=Visibility.Visible;Bar("TaskProgress").Value=0;
   var result=await ExportFile(s,ranges,d.FileName,p=>{exportProcess=p;if(exportCancelRequested)Kill(p);},v=>Dispatch(()=>{Bar("TaskProgress").Value=v;Status("Экспорт MP4: "+v.ToString("0.0")+"% • "+ranges.Count+" фрагм.");}));
   if(result.Code!=0)throw new Exception("Экспорт не завершён. Частичный файл имеет суффикс .partial.mp4. "+Tail(result.Error,400));
   Bar("TaskProgress").Value=100;Status("Экспорт завершён: "+d.FileName);if(revealExport)Process.Start("explorer.exe","/select,"+Q(d.FileName));
  }catch(Exception e){Status(e.Message);}finally{exporting=false;exportProcess=null;Btn("Export").IsEnabled=source!=null;Btn("CancelExport").IsEnabled=false;}
 }
 bool revealExport;
 bool ConfigureExport(List<Clip> ranges){
  var dialog=new Window{Title="Экспорт ролика",Owner=window,WindowStartupLocation=WindowStartupLocation.CenterOwner,Width=450,Height=345,ResizeMode=ResizeMode.NoResize,Background=new SolidColorBrush(Color.FromRgb(25,26,31)),Foreground=new SolidColorBrush(Color.FromRgb(236,236,241)),FontFamily=new FontFamily("Segoe UI"),FontSize=13,ShowInTaskbar=false};
  var panel=new StackPanel{Margin=new Thickness(24)};dialog.Content=panel;
  panel.Children.Add(new TextBlock{Text="Сохранить готовый ролик",FontSize=21,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,14),Foreground=dialog.Foreground});
  panel.Children.Add(new TextBlock{Text=ranges.Count+" фрагм. • "+Time(ranges.Sum(c=>c.End-c.Start)/Settings().Speed)+" • H.264 / "+Settings().Fps+" fps",Margin=new Thickness(0,0,0,15),Foreground=dialog.Foreground});
  var quality=new ComboBox{Style=(Style)window.FindResource(typeof(ComboBox)),ItemContainerStyle=(Style)window.FindResource(typeof(ComboBoxItem))};quality.Items.Add("1080 — высокое качество");quality.Items.Add("720 — быстрее");quality.SelectedIndex=Combo("Resolution").SelectedIndex;panel.Children.Add(quality);
  var info=new TextBlock{Foreground=dialog.Foreground,Margin=new Thickness(0,4,0,12)};panel.Children.Add(info);
  Action update=delegate{var settings=Settings();settings.Resolution=quality.SelectedIndex;int w,h;Dimensions(settings,false,out w,out h);info.Text=w+" × "+h+" • звук AAC при наличии аудиодорожки";};quality.SelectionChanged+=delegate{update();};update();
  var open=new CheckBox{Content="Показать готовый файл в папке",Foreground=dialog.Foreground,Margin=new Thickness(0,0,0,20),IsChecked=revealExport};panel.Children.Add(open);
  var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);
  var cancel=new Button{Content="Отмена",Style=(Style)window.FindResource(typeof(Button)),Margin=new Thickness(0,0,10,0),IsCancel=true};var go=new Button{Content="Выбрать файл…",Style=(Style)window.FindResource("Primary"),IsDefault=true};buttons.Children.Add(cancel);buttons.Children.Add(go);
  go.Click+=delegate{Combo("Resolution").SelectedIndex=quality.SelectedIndex;revealExport=open.IsChecked==true;dialog.DialogResult=true;};cancel.Click+=delegate{dialog.DialogResult=false;};return dialog.ShowDialog()==true;
 }
 async Task<RunResult> ExportFile(EditSettings s,List<Clip> ranges,string output,Action<Process> started,Action<double> progress,bool preview=false) {
  bool withMusic=!String.IsNullOrEmpty(s.Music);if(withMusic&&!File.Exists(s.Music))throw new Exception("Музыкальный файл не найден.");
  int w,h;Dimensions(s,preview,out w,out h);string job=Job(s,w);string partial=Path.Combine(Path.GetDirectoryName(output),Path.GetFileNameWithoutExtension(output)+"_"+Guid.NewGuid().ToString("N").Substring(0,6)+".partial.mp4");
  double total=ranges.Sum(c=>c.End-c.Start)/s.Speed;
  var args=new StringBuilder("-hide_banner -loglevel warning -nostdin -progress pipe:1 -stats_period 0.5 ");var filters=new List<string>();string links="";
  for(int i=0;i<ranges.Count;i++) {
   var c=ranges[i];args.Append("-ss "+N(c.Start)+" -t "+N(c.End-c.Start)+" -i "+Q(source)+" ");
   filters.Add("["+i+":v]setpts=PTS-STARTPTS,"+Filter(s,w,h,c.Start,"c"+i)+"[v"+i+"]");links+="[v"+i+"]";
   if(hasAudio){filters.Add("["+i+":a:0]asetpts=PTS-STARTPTS,atempo="+N(s.Speed)+",aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,volume="+N(s.Volume/100)+"[a"+i+"]");links+="[a"+i+"]";}
  }
  filters.Add(links+"concat=n="+ranges.Count+":v=1:a="+(hasAudio?"1[vout][original]":"0[vout]"));
  if(withMusic){
   args.Append("-stream_loop -1 -i "+Q(s.Music)+" ");double fade=Math.Min(1,total/2);
   string fadeFilters=s.MusicFade?",afade=t=in:st=0:d="+N(fade)+",afade=t=out:st="+N(total-fade)+":d="+N(fade):"";
   filters.Add("["+ranges.Count+":a:0]atrim=duration="+N(total)+",asetpts=PTS-STARTPTS,aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,volume="+N(s.MusicVolume/100)+fadeFilters+"[music]");
   filters.Add((hasAudio?"[original][music]amix=inputs=2:duration=first:dropout_transition=0:normalize=0":"[music]anull")+",alimiter=limit=0.95:level=0:latency=1,atrim=duration="+N(total)+"[aout]");
  }else if(hasAudio)filters.Add("[original]alimiter=limit=0.95:level=0:latency=1[aout]");
  bool audio=hasAudio||withMusic;
  args.Append("-filter_complex "+Q(String.Join(";",filters))+" -map [vout] "+(audio?"-map [aout] ":"")+"-t "+N(total)+" -c:v libx264 -preset "+(preview?"ultrafast -crf 27":"veryfast -crf 20")+" -pix_fmt yuv420p "+(audio?"-c:a aac -b:a 192k ":"")+"-movflags +faststart "+Q(partial));
  try {
   var r=await Run(ffmpeg,args.ToString(),job,started,line=>{if(line.StartsWith("out_time_us=")){double us;if(double.TryParse(line.Substring(12),NumberStyles.Float,CultureInfo.InvariantCulture,out us)&&progress!=null)progress(Math.Max(0,Math.Min(99.9,us/1000000/total*100)));}});
   if(r.Code==0&&File.Exists(partial)) {if(File.Exists(output))File.Replace(partial,output,null);else File.Move(partial,output);}return r;
  }finally{CleanJob(job);}
 }

 void SaveProject() {
  if(montageMode){SaveMontage();return;}
  try {if(source==null)throw new Exception("Сначала откройте видео.");var c=Range();var d=new SaveFileDialog{Filter="Проект TwitchGrab|*.tgstudio",DefaultExt=".tgstudio",FileName=Path.GetFileNameWithoutExtension(source)+".tgstudio"};if(d.ShowDialog(window)!=true)return;
   if(!String.Equals(Path.GetExtension(d.FileName),".tgstudio",StringComparison.OrdinalIgnoreCase))throw new Exception("Проект нужно сохранить с расширением .tgstudio.");
   var p=new Project{Version=3,Source=source,In=c.Start,Out=c.End,Clips=new List<Clip>(clips),Settings=Settings()};AtomicProjectWrite(d.FileName,new JavaScriptSerializer().Serialize(p));Label("ProjectState").Text="Проект сохранён";Status("Проект сохранён. Видео, музыка и субтитры остаются отдельными файлами.");
  }catch(Exception e){Status(e.Message);}
 }
 async Task LoadProject() {
  var d=new OpenFileDialog{Filter="Проект TwitchGrab|*.tgstudio",CheckFileExists=true};if(d.ShowDialog(window)==true)await LoadProjectFile(d.FileName);
 }
 async Task LoadProjectFile(string path){
  try {if(exporting||loading||previewBuilding)throw new Exception("Дождитесь окончания текущей операции.");
   var p=new JavaScriptSerializer().Deserialize<Project>(File.ReadAllText(path));if(p!=null&&p.Version==4&&p.Montage!=null){await LoadMontage(p.Montage);return;}if(p==null||p.Version<1||p.Version>3||p.Settings==null||p.Clips==null||p.Clips.Count>20)throw new Exception("Неподдерживаемый или повреждённый проект.");
   if(!File.Exists(p.Source)){var sourceDialog=new OpenFileDialog{Title="Найдите исходное видео для проекта",CheckFileExists=true};if(sourceDialog.ShowDialog(window)!=true)return;p.Source=sourceDialog.FileName;}
   await OpenMedia(p.Source);if(source!=Path.GetFullPath(p.Source))return;
   p.Settings.Music=Relink(p.Settings.Music,"Найдите музыку для проекта","Звуковые файлы|*.mp3;*.wav;*.flac;*.ogg;*.m4a;*.aac;*.mp4|Все файлы|*.*");
   p.Settings.Subtitles=Relink(p.Settings.Subtitles,"Найдите субтитры для проекта","Субтитры|*.srt;*.ass");
   loading=true;Apply(p.Settings);TB("TrimIn").Text=Time(p.In);TB("TrimOut").Text=Time(p.Out);clips=p.Clips;RefreshClips();loading=false;Range();ResetJournal();SyncTimeline();Changed(false);Label("ProjectState").Text="Проект открыт";Status("Проект открыт.");
  }catch(Exception e){loading=false;Status(e.Message);}
 }
 string Relink(string path,string title,string filter){if(String.IsNullOrEmpty(path)||File.Exists(path))return path;var d=new OpenFileDialog{Title=title,Filter=filter,CheckFileExists=true};return d.ShowDialog(window)==true?d.FileName:null;}

 void Screenshot(string name,Window target=null){target=target??window;target.UpdateLayout();var image=new RenderTargetBitmap((int)target.ActualWidth,(int)target.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(target);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(root,name)))png.Save(stream);}
 static double Tone(byte[] raw,double hz){double re=0,im=0;int count=Math.Min(8000,raw.Length/2-4000);for(int i=0;i<count;i++){double v=BitConverter.ToInt16(raw,(4000+i)*2);re+=v*Math.Cos(2*Math.PI*hz*i/8000);im+=v*Math.Sin(2*Math.PI*hz*i/8000);}return Math.Sqrt(re*re+im*im)/Math.Max(1,count);}
 async Task SelfTest() {
  var report=new List<string>();string u;
  if(!Normalize("twitch.tv/sliks37",out u)||Normalize("https://twitch.tv.evil.example/foo",out u)||ParseTime("12")!=12||ParseTime("00:01:02.500")!=62.5)throw new Exception("URL or time validation failed");report.Add("PASS URL and trim time validation");
  Page(false);Screenshot("studio-downloader.png");report.Add("PASS WPF downloader render");
  string sample=Path.Combine(root,"test-input.mp4");
  var gen=await Run(ffmpeg,"-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=640x360:rate=30 -f lavfi -i sine=frequency=440:sample_rate=48000 -t 4 -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac "+Q(sample),root,null,null);if(gen.Code!=0)throw new Exception(gen.Error);
  await OpenMedia(sample);if(source!=sample||duration<3.9||!hasAudio)throw new Exception("Probe failed");report.Add("PASS media import and audio detection");
  TB("Caption").Text="Сделано sliks37\nВертикальный ролик";TB("TrimIn").Text="00:00:00.000";TB("TrimOut").Text="00:00:01.000";
  Combo("Resolution").SelectedIndex=1;Combo("TextStyle").SelectedIndex=1;
  var s=Settings();var ranges=new List<Clip>{new Clip{Start=0,End=1},new Clip{Start=2,End=3}};
  double exportProgress=0;string output=Path.Combine(root,"test-export.mp4");var exp=await ExportFile(s,ranges,output,null,v=>exportProgress=v);if(exp.Code!=0||exportProgress<=0)throw new Exception("Export/progress failed: "+exp.Error);report.Add("PASS trim + two-clip concat + 9:16 + Cyrillic title + audio + export progress");
  var probe=await Run(ffmpeg,"-hide_banner -i "+Q(output),root,null,null);if(!probe.Error.Contains("720x1280")||!probe.Error.Contains("Audio:")||!probe.Error.Contains("00:00:02."))throw new Exception("Export metadata mismatch: "+probe.Error);report.Add("PASS exported dimensions, duration, audio");
  var roundtrip=new JavaScriptSerializer().Deserialize<Project>(new JavaScriptSerializer().Serialize(new Project{Version=1,Source=source,Settings=s,Clips=ranges,In=0,Out=1}));if(roundtrip.Clips.Count!=2||roundtrip.Settings.Caption!=s.Caption)throw new Exception("Project failed");report.Add("PASS project serialization");
  string subs=Path.Combine(root,"test-subtitles.srt");File.WriteAllText(subs,"1\n00:00:00,000 --> 00:00:04,000\nПроверка субтитров\n",new UTF8Encoding(false));
  foreach(int aspect in new[]{1,2}){
   var variant=Settings();variant.Aspect=aspect;variant.Style=aspect==1?0:2;variant.Fit=aspect==1?1:0;variant.Subtitles=subs;
   int fw,fh;Dimensions(variant,true,out fw,out fh);string j=Job(variant,fw);
   try{var frame=await Run(ffmpeg,"-hide_banner -loglevel error -nostdin -ss 2 -i "+Q(source)+" -frames:v 1 -vf "+Q(Filter(variant,fw,fh,2))+" -update 1 "+Q(Path.Combine(j,"check.png")),j,null,null);if(frame.Code!=0)throw new Exception(frame.Error);}finally{CleanJob(j);}
  }report.Add("PASS square/landscape, fit/crop, all text styles and SRT subtitles");
  string musicFile=Path.Combine(root,"test-music.wav");var tone=await Run(ffmpeg,"-hide_banner -loglevel error -y -f lavfi -i sine=frequency=880:sample_rate=48000 -t 0.35 "+Q(musicFile),root,null,null);if(tone.Code!=0)throw new Exception(tone.Error);
  var advanced=Settings();advanced.Layout=1;advanced.CamX=80;advanced.CamY=20;advanced.CamW=35;advanced.CamH=45;advanced.CamPanel=35;advanced.Music=musicFile;advanced.MusicVolume=40;
  string mixed=Path.Combine(root,"test-camera-music.mp4");var combined=await ExportFile(advanced,ranges,mixed,null,null);if(combined.Code!=0)throw new Exception(combined.Error);
  var combinedProbe=await Run(ffmpeg,"-hide_banner -i "+Q(mixed),root,null,null);if(!combinedProbe.Error.Contains("720x1280")||!combinedProbe.Error.Contains("00:00:02."))throw new Exception("Camera/music export dimensions/duration failed");
  string pcm=Path.Combine(root,"test-mixed.pcm");var decode=await Run(ffmpeg,"-hide_banner -loglevel error -y -i "+Q(mixed)+" -vn -ac 1 -ar 8000 -f s16le "+Q(pcm),root,null,null);if(decode.Code!=0)throw new Exception(decode.Error);var raw=File.ReadAllBytes(pcm);if(Tone(raw,440)<100||Tone(raw,880)<100)throw new Exception("Original audio or background music is missing in the mix");report.Add("PASS camera-top layout + 2 clips + looping music + fades + both audio tones decoded");
  advanced.Layout=2;int aw,ah;Dimensions(advanced,true,out aw,out ah);string cj=Job(advanced,aw);try{var cf=await Run(ffmpeg,"-hide_banner -loglevel error -nostdin -ss 1 -i "+Q(source)+" -frames:v 1 -vf "+Q(Filter(advanced,aw,ah,1))+" -update 1 "+Q(Path.Combine(cj,"camera.png")),cj,null,null);if(cf.Code!=0)throw new Exception(cf.Error);}finally{CleanJob(cj);}report.Add("PASS camera-bottom exact preview filter");
  clips=new List<Clip>(ranges);RefreshClips();UI<ListBox>("ClipList").SelectedIndex=1;MoveClip(-1);if(clips[0].Start!=2)throw new Exception("Clip reorder failed");TB("TrimOut").Text=Time(2.8);Btn("ClipUpdate").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(clips[0].End!=2.8)throw new Exception("Clip update failed");clips.Clear();RefreshClips();report.Add("PASS queue reorder and UI trim-update action");
  var legacy=new JavaScriptSerializer().Deserialize<EditSettings>("{\"Aspect\":0,\"Font\":54}");if(legacy.CamW!=25||legacy.Layout!=0)throw new Exception("Old project compatibility failed");
  var current=new JavaScriptSerializer().Deserialize<Project>(new JavaScriptSerializer().Serialize(new Project{Version=2,Settings=advanced,Clips=ranges}));if(current.Settings.Music!=musicFile||current.Settings.Layout!=2||current.Settings.CamX!=80)throw new Exception("New settings serialization failed");report.Add("PASS V1 settings compatibility and V2 camera/music persistence");
  foreach(double speed in new[]{.5,2.0}) {
   var fast=Settings();fast.Speed=speed;fast.Fps=60;fast.Mirror=true;fast.Brightness=.1;fast.Contrast=1.2;fast.Saturation=.7;
   string motion=Path.Combine(root,"test-speed-"+N(speed)+".mp4");var rendered=await ExportFile(fast,ranges,motion,null,null);if(rendered.Code!=0)throw new Exception(rendered.Error);
   var mp=await Run(ffmpeg,"-hide_banner -i "+Q(motion),root,null,null);string expected=speed==2?"00:00:01.":"00:00:04.";
   if(!mp.Error.Contains(expected)||!mp.Error.Contains("60 fps")||!mp.Error.Contains("Audio:"))throw new Exception("Speed/FPS/audio metadata failed: "+mp.Error);
   string audio=Path.Combine(root,"test-speed.pcm");var decoded=await Run(ffmpeg,"-hide_banner -loglevel error -y -i "+Q(motion)+" -vn -ac 1 -ar 8000 -f s16le "+Q(audio),root,null,null);if(decoded.Code!=0||Tone(File.ReadAllBytes(audio),440)<100)throw new Exception("Speed change altered the original audio pitch");
  }
  if(legacy.Speed!=1||legacy.Fps!=30||legacy.Contrast!=1||legacy.Saturation!=1)throw new Exception("Legacy color/speed defaults failed");
  report.Add("PASS real 0.5x/2x two-clip exports, 60fps, color/mirror filters and preserved 440Hz audio pitch");
  string silent=Path.Combine(root,"test-silent.mp4");var silentCopy=await Run(ffmpeg,"-hide_banner -loglevel error -y -i "+Q(sample)+" -an -c:v copy "+Q(silent),root,null,null);if(silentCopy.Code!=0)throw new Exception(silentCopy.Error);
  await OpenMedia(silent);if(hasAudio)throw new Exception("Silent detection failed");TB("TrimOut").Text="00:00:01.000";await PlayPreview();if(Label("TaskStatus").Text!="PASS preview encode")throw new Exception("Silent preview failed: "+Label("TaskStatus").Text);StopPlayer();report.Add("PASS silent media + preview proxy encode");
  var onlyMusic=Settings();onlyMusic.Music=musicFile;onlyMusic.Layout=2;var sm=await ExportFile(onlyMusic,new List<Clip>{new Clip{Start=0,End=1}},Path.Combine(root,"test-silent-music.mp4"),null,null);if(sm.Code!=0)throw new Exception(sm.Error);var smp=await Run(ffmpeg,"-hide_banner -i "+Q(Path.Combine(root,"test-silent-music.mp4")),root,null,null);if(!smp.Error.Contains("Audio:"))throw new Exception("Music on silent video missing");report.Add("PASS camera layout and added music on source with no audio");
  await OpenMedia(sample);Apply(advanced);TB("TrimOut").Text="00:00:01.000";
  UI<CheckBox>("ShowSource").IsChecked=true;await RenderFrame();if(((BitmapSource)UI<Image>("PreviewFrame").Source).PixelWidth!=640)throw new Exception("Source camera-region view failed");Screenshot("studio-camera-source.png");UI<CheckBox>("ShowSource").IsChecked=false;
  await RenderFrame();if(UI<Image>("PreviewFrame").Source==null)throw new Exception("Frame render failed: "+Label("PreviewState").Text);Screenshot("studio-editor.png");report.Add("PASS camera source selection view and composite preview");
  var dialogTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};dialogTimer.Tick+=delegate{var dialog=Application.Current.Windows.Cast<Window>().FirstOrDefault(w=>w.Title=="Экспорт ролика");if(dialog!=null){dialogTimer.Stop();Screenshot("studio-export.png",dialog);dialog.DialogResult=false;}};dialogTimer.Start();if(ConfigureExport(ranges))throw new Exception("Export-options cancel failed");dialogTimer.Stop();report.Add("PASS export-options dialog and cancel");
  await TestEditing(report);
  await TestMontage(report);
  File.WriteAllLines(Path.Combine(root,"studio-test.txt"),report);Status("Self-test passed");
 }
}
