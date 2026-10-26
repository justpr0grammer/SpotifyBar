using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;
class Track {
 public string Title="Spotify", Artist="Open Spotify to play music", Identity=""; public bool Playing; public double Position,Duration; public long TimelineStamp; public byte[] Cover;
}
class Media {
 static async Task<T> Wait<T>(Windows.Foundation.IAsyncOperation<T> op) { try { var until=DateTime.UtcNow.AddSeconds(3); while(op.Status==Windows.Foundation.AsyncStatus.Started) { if(DateTime.UtcNow>until){op.Cancel();throw new TimeoutException();} await Task.Delay(40); } return op.GetResults(); } finally { if(op.Status!=Windows.Foundation.AsyncStatus.Started)op.Close(); } }
 GlobalSystemMediaTransportControlsSessionManager manager; GlobalSystemMediaTransportControlsSession session; string key; byte[] cover;
 public async Task<Track> Read() {
  if(manager==null)manager=await Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
  session=manager.GetSessions().FirstOrDefault(s=>s.SourceAppUserModelId.IndexOf("spotify",StringComparison.OrdinalIgnoreCase)>=0);
  if(session==null){key=null;cover=null;return new Track();}
  var p=await Wait(session.TryGetMediaPropertiesAsync());var timeline=session.GetTimelineProperties();string k=p.Title+"\n"+p.Artist;
  if(key!=k){cover=null;if(p.Thumbnail!=null){using(var stream=await Wait(p.Thumbnail.OpenReadAsync())){using(var reader=new DataReader(stream)){uint size=(uint)Math.Min(stream.Size,4000000UL);await Wait((Windows.Foundation.IAsyncOperation<uint>)reader.LoadAsync(size));cover=new byte[size];reader.ReadBytes(cover);}}}key=k;}
  bool playing=session.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
  double position=(timeline.Position-timeline.StartTime).TotalSeconds;
  double age=(DateTimeOffset.UtcNow-timeline.LastUpdatedTime).TotalSeconds;
  if(playing&&age>=0&&age<86400)position+=age;
  return new Track{Title=p.Title,Artist=p.Artist,Identity=session.SourceAppUserModelId+"\n"+k,TimelineStamp=timeline.LastUpdatedTime.UtcDateTime.Ticks,Playing=playing,Position=position,Duration=(timeline.EndTime-timeline.StartTime).TotalSeconds,Cover=cover};
 }
 public async Task Action(int action){if(session==null)return;if(action<0)await Wait(session.TrySkipPreviousAsync());else if(action>0)await Wait(session.TrySkipNextAsync());else await Wait(session.TryTogglePlayPauseAsync());}
}


