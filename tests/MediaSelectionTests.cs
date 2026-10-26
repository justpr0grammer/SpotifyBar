using System;
class MediaSelectionTests {
 static void Check(bool value,string name){if(!value)throw new Exception(name);}
 public static void Run(){
  var spotify=new PlayerCandidate{Id="Spotify.exe",Playing=false};
  var cloud=new PlayerCandidate{Id="SoundCloud.App",Playing=true};
  var browser=new PlayerCandidate{Id="Chrome",Playing=true,Current=true};
  Check(MediaSelection.Pick(new[]{spotify,cloud},PlayerPreference.Auto)==cloud,"Playing SoundCloud beats paused Spotify");
  Check(MediaSelection.Pick(new[]{spotify,browser},PlayerPreference.Auto)==browser,"SoundCloud browser session is eligible");
  Check(MediaSelection.Pick(new[]{spotify,browser},PlayerPreference.Spotify)==spotify,"Explicit Spotify selection");
  spotify.Playing=true;
  Check(MediaSelection.Pick(new[]{spotify,browser},PlayerPreference.Auto)==browser,"Current playing session wins");
  Check(MediaSelection.Pick(new[]{spotify,browser},PlayerPreference.SoundCloud)==browser,"Explicit SoundCloud browser selection");
  Check(MediaSelection.Pick(new[]{spotify},PlayerPreference.SoundCloud)==null,"SoundCloud mode cannot control Spotify");
  Check(MediaSelection.Source("MSEdge")=="Browser","Edge support");
  Check(MediaSelection.Source("org.mozilla.firefox")=="Browser","Firefox support");
  Check(MediaSelection.Pick(new PlayerCandidate[0],PlayerPreference.Auto)==null,"No sessions");
  Console.WriteLine("PASS: SoundCloud, browsers, Spotify, current session and explicit player selection.");
 }
}
