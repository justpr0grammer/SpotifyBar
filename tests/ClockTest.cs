using System;
class ClockTest {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Main(){
  MediaSelectionTests.Run();
  double now=0;var c=new PlaybackClock(()=>now);
  c.Sync("song",56,200,true,1);now=1;Check(c.Position==57,"local tick");
  c.Sync("song",56,200,true,1);Check(c.Position==57,"stale rewind");
  double last=c.Position;
  for(int i=0;i<100;i++){now+=0.1;if(i%10==0)c.Sync("song",Math.Floor(56+now)-0.7,200,true,i+2);Check(c.Position>=last,"jitter rewind");last=c.Position;}
  c.Sync("song",c.Position,200,false,200);double paused=c.Position;now+=10;Check(c.Position==paused,"pause");
  c.Sync("song",paused,200,true,201);now+=1;Check(c.Position>paused,"resume");
  c.Sync("song",20,200,true,202);Check(Math.Abs(c.Position-20)<0.001,"backward seek");
  c.Sync("song",120,200,true,203);Check(Math.Abs(c.Position-120)<0.001,"forward seek");
  c.Sync("new song",0,30,true,1);Check(c.Position==0,"track reset");now+=100;Check(c.Position==30,"duration clamp");
  Console.WriteLine("PASS: local tick, stale snapshots, jitter, pause/resume, seeks, track change, duration.");
 }
}
