using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;
class Track {
 public string Title="Music", Artist="Open Spotify or SoundCloud", Identity="", Source=""; public bool Playing; public double Position,Duration; public long TimelineStamp; public byte[] Cover;
}
class Media {
 static async Task<T> Wait<T>(Windows.Foundation.IAsyncOperation<T> op) { try { var until=DateTime.UtcNow.AddSeconds(3); while(op.Status==Windows.Foundation.AsyncStatus.Started) { if(DateTime.UtcNow>until){op.Cancel();throw new TimeoutException();} await Task.Delay(40); } return op.GetResults(); } finally { if(op.Status!=Windows.Foundation.AsyncStatus.Started)op.Close(); } }
 GlobalSystemMediaTransportControlsSessionManager manager; GlobalSystemMediaTransportControlsSession session; string key; byte[] cover;
 public PlayerPreference Preference=PlayerPreference.Auto;
 public async Task<Track> Read() {
  if(manager==null)manager=await Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
  var current=manager.GetCurrentSession();
  var available=manager.GetSessions().ToList();
  var candidates=available.Select(s=>new PlayerCandidate{Id=s.SourceAppUserModelId,Playing=s.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,Current=current!=null&&s.SourceAppUserModelId==current.SourceAppUserModelId,Previous=session!=null&&s.SourceAppUserModelId==session.SourceAppUserModelId}).ToList();
  var selected=MediaSelection.Pick(candidates,Preference);
  session=selected==null?null:available[candidates.IndexOf(selected)];
  if(session==null){key=null;cover=null;return new Track();}
  var p=await Wait(session.TryGetMediaPropertiesAsync());var timeline=session.GetTimelineProperties();string k=session.SourceAppUserModelId+"\n"+p.Title+"\n"+p.Artist;
  if(key!=k){cover=null;if(p.Thumbnail!=null){using(var stream=await Wait(p.Thumbnail.OpenReadAsync())){using(var reader=new DataReader(stream)){uint size=(uint)Math.Min(stream.Size,4000000UL);await Wait((Windows.Foundation.IAsyncOperation<uint>)reader.LoadAsync(size));cover=new byte[size];reader.ReadBytes(cover);}}}key=k;}
  bool playing=session.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
  double position=(timeline.Position-timeline.StartTime).TotalSeconds;
  double age=(DateTimeOffset.UtcNow-timeline.LastUpdatedTime).TotalSeconds;
  if(playing&&age>=0&&age<86400)position+=age;
  string source=MediaSelection.Source(session.SourceAppUserModelId);
  if(source=="Browser"&&((p.AlbumTitle??"").IndexOf("soundcloud",StringComparison.OrdinalIgnoreCase)>=0||Preference==PlayerPreference.SoundCloud))source="SoundCloud";
  return new Track{Title=p.Title,Artist=p.Artist,Identity=k,Source=source,TimelineStamp=timeline.LastUpdatedTime.UtcDateTime.Ticks,Playing=playing,Position=position,Duration=(timeline.EndTime-timeline.StartTime).TotalSeconds,Cover=cover};
 }
 public async Task Action(int action){if(session==null)return;if(action<0)await Wait(session.TrySkipPreviousAsync());else if(action>0)await Wait(session.TrySkipNextAsync());else await Wait(session.TryTogglePlayPauseAsync());}
}
