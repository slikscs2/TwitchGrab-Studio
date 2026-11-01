using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Web.Script.Serialization;
public partial class Studio {
 Color MontagePixel(string path,double x,double y){var b=new FormatConvertedBitmap(ReadBitmap(path),PixelFormats.Bgra32,null,0);byte[] p=new byte[4];b.CopyPixels(new System.Windows.Int32Rect((int)(b.PixelWidth*x),(int)(b.PixelHeight*y),1,1),p,4,0);return Color.FromRgb(p[2],p[1],p[0]);}
 async Task CheckRun(string args){var r=await Run(ffmpeg,args,root,null,null);if(r.Code!=0)throw new Exception(r.Error);}
 async Task TestMontage(List<string> report){
  string blue=Path.Combine(root,"test-track-blue.mp4"),red=Path.Combine(root,"test-track-red.mp4"),tone=Path.Combine(root,"test-track-tone.wav"),picture=Path.Combine(root,"test-track-image.png");
  await CheckRun("-hide_banner -loglevel error -y -f lavfi -i color=blue:s=640x360:r=30 -f lavfi -i sine=frequency=440:sample_rate=48000 -t 4 -c:v libx264 -preset ultrafast -c:a aac "+Q(blue));
  await CheckRun("-hide_banner -loglevel error -y -f lavfi -i color=red:s=200x200:r=30 -t 3 -c:v libx264 -preset ultrafast "+Q(red));
  await CheckRun("-hide_banner -loglevel error -y -f lavfi -i sine=frequency=880:sample_rate=48000 -t 2 "+Q(tone));
  await CheckRun("-hide_banner -loglevel error -y -f lavfi -i color=yellow:s=160x90 -frames:v 1 -update 1 "+Q(picture));
  montage=new MontageDocument{Aspect=2,Resolution=1};MontageAsset baseAsset=await ProbeMontageAsset(blue),overAsset=await ProbeMontageAsset(red),soundAsset=await ProbeMontageAsset(tone),imageAsset=await ProbeMontageAsset(picture);
  if(baseAsset.Kind!="video"||!baseAsset.Audio||soundAsset.Kind!="audio"||imageAsset.Kind!="image")throw new Exception("Media classification failed");
  var bg=montage.Items[0];var overlayTrack=AddMontageTrack("video");var overlay=new MontageClip{Id=Id(),Track=overlayTrack.Id,Asset=overAsset.Id,Start=1,Length=2,Scale=25,Mask=1};montage.Items.Add(overlay);
  var soundTrack=AddMontageTrack("audio");montage.Items.Add(new MontageClip{Id=Id(),Track=soundTrack.Id,Asset=soundAsset.Id,Start=1,Length=2,Volume=70});
  AddMontageText();var title=SelectedMontage();title.Text="Сделано sliks37";title.Start=.5;title.Length=3;title.Y=12;title.Font=60;title.Style=2;
  ValidateMontage(montage,true);string frame=Path.Combine(root,"test-track-frame.png");
  var f=await RenderMontage(montage,frame,true,true,1.5,.1,null,null);if(f.Code!=0)throw new Exception(f.Error);var center=MontagePixel(frame,.5,.5);var corner=MontagePixel(frame,.38,.3);if(center.R<180||center.B>70||corner.B<180||corner.R>70)throw new Exception("Mask alpha or layer composition failed: "+center+" / "+corner);
  var early=await RenderMontage(montage,frame,true,true,0,.1,null,null);if(early.Code!=0||MontagePixel(frame,.5,.5).B<180)throw new Exception("Layer timing failed");
  report.Add("PASS video/audio/image import, independent timing, layer order and circle alpha pixels");
  foreach(int mask in new[]{0,2,3}){overlay.Mask=mask;overlay.Rotation=17;overlay.Opacity=60;overlay.Filter=2;overlay.Fade=.3;title.Style=mask;var r=await RenderMontage(montage,frame,true,true,1.5,.1,null,null);if(r.Code!=0)throw new Exception("Mask "+mask+": "+r.Error);}overlay.Mask=1;overlay.Rotation=0;overlay.Opacity=100;overlay.Filter=0;overlay.Fade=0;
  foreach(int style in new[]{1,4,5}){title.Style=style;var r=await RenderMontage(montage,frame,true,true,1.5,.1,null,null);if(r.Code!=0)throw new Exception(r.Error);}title.Style=2;
  report.Add("PASS all mask shapes, rotation, opacity, fades, color filter and six text styles in real FFmpeg frames");
  string movie=Path.Combine(root,"test-multitrack.mp4");double progress=0;var rendered=await RenderMontage(montage,movie,false,false,0,montage.Duration,null,v=>progress=v);if(rendered.Code!=0||progress<=0)throw new Exception(rendered.Error);var meta=await Run(ffmpeg,"-hide_banner -i "+Q(movie),root,null,null);if(!meta.Error.Contains("1280x720")||!meta.Error.Contains("00:00:04.")||!meta.Error.Contains("Audio:"))throw new Exception(meta.Error);
  string pcm=Path.Combine(root,"test-track-mix.pcm");await CheckRun("-hide_banner -loglevel error -y -ss 1.2 -i "+Q(movie)+" -t 1.5 -vn -ac 1 -ar 8000 -f s16le "+Q(pcm));var bytes=File.ReadAllBytes(pcm);if(Tone(bytes,440)<100||Tone(bytes,880)<100)throw new Exception("Multitrack audio did not contain both sources");
  report.Add("PASS full MP4 composition, four-second timeline, output geometry, progress and decoded 440/880Hz audio mix");
  soundTrack.Muted=true;overlayTrack.Hidden=true;var hidden=await RenderMontage(montage,frame,true,true,1.5,.1,null,null);if(hidden.Code!=0||MontagePixel(frame,.5,.5).B<180)throw new Exception("Hidden track is visible");
  string muted=Path.Combine(root,"test-track-muted.mp4");var silentMix=await RenderMontage(montage,muted,true,false,1,2,null,null);if(silentMix.Code!=0)throw new Exception(silentMix.Error);
  await CheckRun("-hide_banner -loglevel error -y -i "+Q(muted)+" -vn -ac 1 -ar 8000 -f s16le "+Q(pcm));if(Tone(File.ReadAllBytes(pcm),880)>50)throw new Exception("Muted track remained audible");
  var imageTrack=AddMontageTrack("video");var stillClip=new MontageClip{Id=Id(),Track=imageTrack.Id,Asset=imageAsset.Id,Start=0,Length=3,Speed=2,Scale=20,X=80,Y=75};montage.Items.Add(stillClip);var imageFrame=await RenderMontage(montage,frame,true,true,2.5,.1,null,null);if(imageFrame.Code!=0||MontagePixel(frame,.8,.75).R<180||MontagePixel(frame,.8,.75).G<180)throw new Exception("Image clip failed at non-unit speed");
  string transparent=Path.Combine(root,"test-track-alpha.png");await CheckRun("-hide_banner -loglevel error -y -f lavfi -i color=yellow:s=160x90,format=rgba,colorchannelmixer=aa=0.5 -frames:v 1 -update 1 "+Q(transparent));var alphaAsset=await ProbeMontageAsset(transparent);stillClip.Asset=alphaAsset.Id;stillClip.Mask=1;var alphaFrame=await RenderMontage(montage,frame,true,true,2.5,.1,null,null);var alphaPixel=MontagePixel(frame,.8,.75);if(alphaFrame.Code!=0||alphaPixel.R<70||alphaPixel.R>180||alphaPixel.B<70||alphaPixel.B>180)throw new Exception("Mask did not preserve source image alpha: "+alphaPixel);
  report.Add("PASS mask preserves the existing alpha channel of transparent PNG layers");
  montage.Items.Remove(stillClip);montage.Tracks.Remove(imageTrack);
  soundTrack.Muted=false;overlayTrack.Hidden=false;SelectMontage(bg);ResetMontageHistory();DetachMontageAudio();if(bg.Volume!=0||SelectedMontage()==null||TrackOf(SelectedMontage()).Kind!="audio")throw new Exception("Detach audio failed");MontageUndo(-1);bg=montage.Items.First(c=>c.Id==bg.Id);overlay=montage.Items.First(c=>c.Id==overlay.Id);soundTrack=montage.Tracks.First(t=>t.Id==soundTrack.Id);overlayTrack=montage.Tracks.First(t=>t.Id==overlayTrack.Id);
  report.Add("PASS audio mute verified in decoded PCM, image layer at 2x and reversible audio detachment");
  ShowMontage();SelectMontage(overlay);ResetMontageHistory();string before=MontageSnapshot();multiTimeline.Document=montage;multiTimeline.BeginClipGesture(overlay.Id,1,1);multiTimeline.MoveClipGesture(1.5,montage.Tracks.IndexOf(overlayTrack),false);multiTimeline.EndClipGesture();if(Math.Abs(overlay.Start-1.5)>.001)throw new Exception("Track drag failed");MontageUndo(-1);if(MontageSnapshot()!=before)throw new Exception("Track undo failed");MontageUndo(1);overlay=SelectedMontage();if(overlay==null||Math.Abs(overlay.Start-1.5)>.001)throw new Exception("Track redo failed");
  montageSeek.Value=2.5;int count=montage.Items.Count;SplitMontage();if(montage.Items.Count!=count+1)throw new Exception("Split failed");DuplicateMontage();if(montage.Items.Count!=count+2)throw new Exception("Duplicate failed");DeleteMontage();if(montage.Items.Count!=count+1)throw new Exception("Delete failed");
  SelectMontage(montage.Items.First(c=>c.Track==overlayTrack.Id));TrackOf(SelectedMontage()).Locked=true;int lockedCount=montage.Items.Count;DeleteMontage();if(montage.Items.Count!=lockedCount)throw new Exception("Locked track was edited");TrackOf(SelectedMontage()).Locked=false;
  report.Add("PASS actual timeline drag, undo/redo, split, duplicate, delete and track lock");
  SelectMontage(montage.Items.First(c=>c.Id==bg.Id));var trimmed=SelectedMontage();multiTimeline.BeginClipGesture(trimmed.Id,2,0);multiTimeline.MoveClipGesture(.5,montage.Tracks.IndexOf(TrackOf(trimmed)),false);multiTimeline.EndClipGesture();if(Math.Abs(trimmed.In-.5)>.002||Math.Abs(trimmed.Length-3.5)>.002)throw new Exception("Left trim did not advance source in");MontageUndo(-1);
  SelectMontage(montage.Items.First(c=>TrackOf(c).Kind=="text"));var textClip=SelectedMontage();multiTimeline.BeginClipGesture(textClip.Id,2,textClip.Start);multiTimeline.MoveClipGesture(0,montage.Tracks.IndexOf(TrackOf(textClip)),false);multiTimeline.EndClipGesture();if(textClip.In!=0||textClip.Start!=0)throw new Exception("Text extension created negative source offset");ValidateMontage(montage,true);MontageUndo(-1);
  report.Add("PASS source-aware edge trimming and extending generated text without a negative source offset");

  string project=Path.Combine(root,"test-montage.tgstudio");AtomicProjectWrite(project,new JavaScriptSerializer().Serialize(MontageProject()));string snapshot=MontageSnapshot();await LoadProjectFile(project);if(MontageSnapshot()!=snapshot||!montageMode)throw new Exception("Multitrack project did not restore");
  var invalid=new JavaScriptSerializer().Deserialize<MontageDocument>(snapshot);invalid.Items[0].Scale=double.NaN;bool rejected=false;try{ValidateMontage(invalid,true);}catch{rejected=true;}if(!rejected)throw new Exception("Non-finite project transform accepted");
  report.Add("PASS V4 project save/load and invalid transform validation");
  montageSeek.Value=1.5;var final=await RenderMontage(montage,frame,true,true,1.5,.1,null,null);if(final.Code!=0)throw new Exception(final.Error);montageImage.Source=ReadBitmap(frame);SelectMontage(montage.Items.First(c=>TrackOf(c).Kind=="video"&&c.Asset==overAsset.Id));multiTimeline.Fit();window.UpdateLayout();Screenshot("studio-tracks.png");
 }
}
