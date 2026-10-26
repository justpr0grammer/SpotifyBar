using System;
using System.Collections.Generic;
using System.Linq;

enum PlayerPreference { Auto, Spotify, SoundCloud }
class PlayerCandidate {
 public string Id; public bool Playing, Current, Previous;
}
static class MediaSelection {
 public static string Source(string id) {
  string s=(id??"").ToLowerInvariant();
  if(s.Contains("soundcloud"))return "SoundCloud";
  if(s.Contains("spotify"))return "Spotify";
  if(new[]{"chrome","msedge","firefox","opera","brave","vivaldi","browser","thorium","chromium"}.Any(s.Contains))return "Browser";
  return "";
 }
 public static PlayerCandidate Pick(IEnumerable<PlayerCandidate> candidates,PlayerPreference preference) {
  return candidates.Where(c=>{
   string source=Source(c.Id);
   return preference==PlayerPreference.Spotify?source=="Spotify":preference==PlayerPreference.SoundCloud?(source=="SoundCloud"||source=="Browser"):source!="";
  }).OrderByDescending(c=>c.Playing).ThenByDescending(c=>c.Current).ThenByDescending(c=>c.Previous).FirstOrDefault();
 }
}
