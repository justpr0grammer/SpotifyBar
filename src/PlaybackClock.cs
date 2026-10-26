using System;
using System.Diagnostics;

// A monotonic local clock. Polling updates its rate instead of resetting seconds.
class PlaybackClock {
 readonly Func<double> now;
 double position, anchoredAt, duration, rate=1;
 bool playing, initialized;
 string identity;
 long stamp;
 public PlaybackClock() : this(delegate {return Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;}) {}
 public PlaybackClock(Func<double> time){now=time;}
 public double Position {get{return Clamp(position+(playing?Math.Max(0,now()-anchoredAt)*rate:0));}}
 double Clamp(double v){return Math.Max(0,duration>0?Math.Min(v,duration):v);}
 public void Sync(string key,double reported,double length,bool active,long revision){
  double current=Position, time=now();
  bool changed=!initialized||key!=identity;
  bool fresh=changed||revision!=stamp;
  duration=Math.Max(0,length);
  reported=Clamp(reported);
  if(changed){position=reported;rate=1;}
  else {
   double drift=reported-current;
   // A new timeline with a large offset is a seek. Old snapshots cannot rewind us.
   if(fresh&&Math.Abs(drift)>=2.5){position=reported;rate=1;}
   else {position=current;rate=fresh&&active?1+Math.Max(-0.1,Math.Min(0.1,drift*0.1)):rate;}
  }
  anchoredAt=time;playing=active;identity=key;stamp=revision;initialized=true;
 }
}
