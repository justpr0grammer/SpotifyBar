using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

class Bar : Form {
 [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h,int id,uint mod,uint key);
 [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h,int id);
 [DllImport("user32.dll")] static extern bool ReleaseCapture();
 [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
 readonly Color yellow=Color.FromArgb(234,222,90), muted=Color.FromArgb(143,145,134), surface=Color.FromArgb(29,31,29);
 readonly Font title=new Font("Segoe UI",16,FontStyle.Bold), body=new Font("Segoe UI",9), small=new Font("Segoe UI",8), big=new Font("Segoe UI",10,FontStyle.Bold), symbols=new Font("Segoe MDL2 Assets",14);
 readonly Media media=new Media(); readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer(); readonly NotifyIcon tray=new NotifyIcon();
 readonly System.Windows.Forms.Timer animation=new System.Windows.Forms.Timer();
 readonly System.Diagnostics.Stopwatch animationClock=System.Diagnostics.Stopwatch.StartNew();
 double recordAngle, lastFrame;
 List<AudioChannel> channels=new List<AudioChannel>(); readonly Dictionary<string,Image> icons=new Dictionary<string,Image>();
 Track track=new Track(); Image cover;byte[] coverRef;readonly PlaybackClock playbackClock=new PlaybackClock();bool polling,uk=true,cleaned;int ticks,offset,drag=-1;string error;
 string SettingsPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SpotifyBar","language.txt");}}
 public Bar(){
  Text="Spotify Bar";FormBorderStyle=FormBorderStyle.None;ClientSize=new Size(440,520);BackColor=Color.FromArgb(18,20,19);DoubleBuffered=true;TopMost=true;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(Screen.PrimaryScreen.WorkingArea.Right-Width-24,Screen.PrimaryScreen.WorkingArea.Top+24);
  try{uk=File.ReadAllText(SettingsPath).Trim()!="en";}catch{}
  var menu=new ContextMenuStrip();menu.Items.Add("Показати / Hide · Alt+X",null,delegate{Toggle();});menu.Items.Add("Українська / English",null,delegate{Language();});var players=new ToolStripMenuItem("Джерело / Player");
  players.DropDownItems.Add("Auto",null,async delegate{media.Preference=PlayerPreference.Auto;if(!polling)await RefreshData(false);});
  players.DropDownItems.Add("Spotify",null,async delegate{media.Preference=PlayerPreference.Spotify;if(!polling)await RefreshData(false);});
  players.DropDownItems.Add("SoundCloud (browser)",null,async delegate{media.Preference=PlayerPreference.SoundCloud;if(!polling)await RefreshData(false);});
  menu.Items.Add(players);menu.Items.Add("Вийти / Exit",null,delegate{Close();});
  tray.Icon=SystemIcons.Application;tray.Text="Spotify Bar · Alt+X";tray.ContextMenuStrip=menu;tray.MouseClick+=delegate(object s,MouseEventArgs e){if(e.Button==MouseButtons.Left)Toggle();};tray.Visible=true;
  timer.Interval=100;timer.Tick+=async delegate{ticks++;if(Visible){Invalidate(new Rectangle(0,180,Width,340));if(ticks%10==0&&!polling)await RefreshData(ticks%50==0);} };timer.Start();
  animation.Interval=33;animation.Tick+=delegate {double now=animationClock.Elapsed.TotalSeconds;double delta=now-lastFrame;lastFrame=now;if(Visible&&track.Playing){recordAngle=(recordAngle+delta*90)%360;Invalidate(new Rectangle(20,52,124,124));}};animation.Start();
  Shown+=async delegate{await RefreshData(true);};MouseWheel+=delegate(object s,MouseEventArgs e){offset=Math.Max(0,Math.Min(Math.Max(0,channels.Count-6),offset-(e.Delta>0?1:-1)));Invalidate();};
 }
 protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);RegisterHotKey(Handle,1,0x4001,0x58);}
 protected override void WndProc(ref Message m){if(m.Msg==0x312&&m.WParam.ToInt32()==1)Toggle();base.WndProc(ref m);}
 void Toggle(){if(Visible)Hide();else{Show();Activate();ticks=49;}}
 string T(string ua,string en){return uk?ua:en;}
 void Language(){uk=!uk;try{Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));File.WriteAllText(SettingsPath,uk?"uk":"en");}catch{}Invalidate();}
 async Task RefreshData(bool rebuild){
  polling=true;try{
   if(rebuild&&drag<0){var fresh=Audio.Read();foreach(var c in channels)c.Dispose();channels=fresh.OrderBy(c=>c.Endpoint!=null?0:1).ToList();offset=Math.Min(offset,Math.Max(0,channels.Count-6));}
   var task=media.Read();if(await Task.WhenAny(task,Task.Delay(3500))==task){track=await task;playbackClock.Sync(track.Identity,track.Position,track.Duration,track.Playing,track.TimelineStamp);if(!Object.ReferenceEquals(track.Cover,coverRef)){coverRef=track.Cover;if(cover!=null)cover.Dispose();cover=null;if(track.Cover!=null)using(var stream=new MemoryStream(track.Cover))using(var image=Image.FromStream(stream))cover=new Bitmap(image); }error=null;}
   else {error=T("Плеєр не відповідає","Player is not responding"); task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
  }catch{error=T("Не вдалося оновити дані","Could not refresh audio");}finally{polling=false;if(!IsDisposed)Invalidate();}
 }
 void TextAt(Graphics g,string text,Font font,Color color,Rectangle r,StringAlignment align){using(var b=new SolidBrush(color))using(var f=new StringFormat{Alignment=align,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(text,font,b,r,f);}
 GraphicsPath Round(RectangleF r,float radius){var p=new GraphicsPath();float d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
 void Box(Graphics g,Rectangle r,Color c,int radius){using(var p=Round(r,radius))using(var b=new SolidBrush(c))g.FillPath(b,p);}
 string TimeText(double s){var t=TimeSpan.FromSeconds(Math.Max(0,Math.Min(86400,s)));return ((int)t.TotalMinutes)+":"+t.Seconds.ToString("00");}
 void DrawRecord(Graphics g){
  using(var shadow=new SolidBrush(Color.FromArgb(9,10,9)))g.FillEllipse(shadow,21,55,122,122);
  using(var vinyl=new LinearGradientBrush(new Rectangle(21,53,122,122),Color.FromArgb(48,50,47),Color.FromArgb(10,12,11),45))g.FillEllipse(vinyl,21,53,122,122);
  using(var edge=new Pen(Color.FromArgb(76,78,67)))g.DrawEllipse(edge,21,53,122,122);
  for(int r=33;r<=57;r+=3)using(var groove=new Pen(Color.FromArgb(r%2==0?42:34,45,39),0.7f))g.DrawEllipse(groove,82-r,114-r,r*2,r*2);
  using(var sheen=new Pen(Color.FromArgb(65,96,98,84),3))g.DrawArc(sheen,29,61,106,106,220,40);
  var state=g.Save();g.TranslateTransform(82,114);g.RotateTransform((float)recordAngle);
  if(cover!=null){using(var clip=new GraphicsPath()){clip.AddEllipse(-29,-29,58,58);g.SetClip(clip,CombineMode.Intersect);}float scale=Math.Max(58f/cover.Width,58f/cover.Height);float w=cover.Width*scale,h=cover.Height*scale;g.DrawImage(cover,-w/2,-h/2,w,h);}
  else {using(var label=new SolidBrush(yellow))g.FillEllipse(label,-29,-29,58,58);using(var ink=new Pen(Color.FromArgb(110,100,34),1.2f)){g.DrawEllipse(ink,-23,-23,46,46);g.DrawLine(ink,-15,-12,15,-12);g.DrawLine(ink,-10,14,10,14);}using(var dot=new SolidBrush(Color.FromArgb(83,77,28)))g.FillEllipse(dot,15,-4,5,5);}
  g.Restore(state);
  using(var rim=new Pen(Color.FromArgb(133,135,110)))g.DrawEllipse(rim,53,85,58,58);
  using(var hub=new SolidBrush(Color.FromArgb(17,19,17)))g.FillEllipse(hub,78,110,8,8);
  using(var spindle=new SolidBrush(Color.FromArgb(166,168,145)))g.FillEllipse(spindle,80.5f,112.5f,3,3);
 }
 protected override void OnPaint(PaintEventArgs e){
  base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
  using(var p=new Pen(Color.FromArgb(68,70,52)))g.DrawRectangle(p,0,0,Width-1,Height-1);
  TextAt(g,"●  "+(track.Source=="SoundCloud"?"SOUNDCLOUD":track.Source=="Browser"?"BROWSER":track.Source=="Spotify"?"SPOTIFY BAR":"MUSIC BAR"),small,yellow,new Rectangle(20,12,200,22),StringAlignment.Near);
  TextAt(g,uk?"UA":"EN",small,muted,new Rectangle(321,12,35,24),StringAlignment.Center);TextAt(g,"−",big,muted,new Rectangle(363,12,24,24),StringAlignment.Center);TextAt(g,"×",big,muted,new Rectangle(398,12,24,24),StringAlignment.Center);
  DrawRecord(g);
  TextAt(g,track.Title,title,Color.FromArgb(244,245,230),new Rectangle(162,54,254,42),StringAlignment.Near);
  TextAt(g,track.Identity==""?T("Увімкніть Spotify або SoundCloud","Open Spotify or SoundCloud"):track.Artist,body,muted,new Rectangle(164,95,249,24),StringAlignment.Near);
  int[] x={164,222,280};string[] glyph={"\uE892",track.Playing?"\uE769":"\uE768","\uE893"};for(int i=0;i<3;i++){Box(g,new Rectangle(x[i],135,46,38),i==1?yellow:surface,10);TextAt(g,glyph[i],symbols,i==1?BackColor:Color.White,new Rectangle(x[i],135,46,38),StringAlignment.Center);}
  double pos=playbackClock.Position;
  TextAt(g,TimeText(pos),small,muted,new Rectangle(20,190,80,18),StringAlignment.Near);TextAt(g,TimeText(track.Duration),small,muted,new Rectangle(340,190,80,18),StringAlignment.Far);
  Box(g,new Rectangle(20,216,400,4),surface,2);if(track.Duration>0)Box(g,new Rectangle(20,216,Math.Max(4,(int)(400*pos/track.Duration)),4),yellow,2);
  TextAt(g,T("МІКШЕР","VOLUME MIXER"),small,muted,new Rectangle(20,237,210,22),StringAlignment.Near);TextAt(g,"Alt + X",small,muted,new Rectangle(315,237,105,22),StringAlignment.Far);
  for(int j=0;e.ClipRectangle.Bottom>=274&&j<6&&offset+j<channels.Count;j++){
   var c=channels[offset+j];int cx=22+j*67;float volume=0,peak=0;bool mute=false;try{volume=c.Volume;peak=c.Peak;mute=c.Muted;}catch{}
   Box(g,new Rectangle(cx,274,59,204),surface,10);
   Image icon=null;if(c.Path!=null){if(!icons.TryGetValue(c.Path,out icon)){try{using(var ico=Icon.ExtractAssociatedIcon(c.Path))icon=ico.ToBitmap();}catch{}icons[c.Path]=icon;}}
   if(icon!=null)g.DrawImage(icon,cx+17,285,26,26);else TextAt(g,c.Endpoint!=null?(c.Name=="Microphone"?"\uE720":"\uE767"):"\uE8A5",symbols,yellow,new Rectangle(cx,282,59,32),StringAlignment.Center);
   TextAt(g,c.Name=="Speakers"?T("Звук","Output"):c.Name=="Microphone"?T("Мікр.","Mic"):c.Name,small,muted,new Rectangle(cx+2,316,55,18),StringAlignment.Center);
   TextAt(g,Math.Round(volume*100)+"%",small,mute?muted:yellow,new Rectangle(cx,337,59,20),StringAlignment.Center);
   Box(g,new Rectangle(cx+26,367,6,75),Color.FromArgb(54,57,48),3);int fill=(int)(75*volume);if(fill>0)Box(g,new Rectangle(cx+26,442-fill,6,Math.Max(6,fill)),mute?muted:yellow,3);
   using(var b=new SolidBrush(Color.FromArgb(92,153,111)))g.FillRectangle(b,cx+39,442-(int)(75*peak),3,Math.Max(1,(int)(75*peak)));
   Box(g,new Rectangle(cx+20,438-fill,18,7),mute?muted:Color.FromArgb(250,242,161),3);
   TextAt(g,mute?"\uE74F":"\uE767",symbols,mute?muted:yellow,new Rectangle(cx+12,450,35,23),StringAlignment.Center);
  }
  TextAt(g,error??(channels.Count>6?T("Прокрутіть для інших програм","Scroll for more apps"):T("Перетягніть верх панелі, щоб перемістити","Drag the header to move")),small,muted,new Rectangle(20,489,400,20),StringAlignment.Center);
 }
 protected override async void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
  if(e.Y<40){if(e.X>394){Hide();return;}if(e.X>360){Hide();return;}if(e.X>316){Language();return;}ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);return;}
  if(e.Y>=135&&e.Y<=173){int a=e.X>=164&&e.X<=210?-1:e.X>=222&&e.X<=268?0:e.X>=280&&e.X<=326?1:99;if(a!=99){try{await media.Action(a);}catch{}if(!polling)await RefreshData(false);}return;}
  int j=(e.X-22)/67;if(e.X<22||j<0||j>=6||offset+j>=channels.Count)return;
  if(e.Y>=450&&e.Y<=477){try{var c=channels[offset+j];c.Muted=!c.Muted;}catch{}Invalidate();return;}
  if(e.Y>=360&&e.Y<=447){drag=offset+j;Capture=true;SetSlider(e.Y);}
 }
 void SetSlider(int y){if(drag<0||drag>=channels.Count)return;try{channels[drag].Volume=Math.Max(0,Math.Min(1,(442-y)/75f));}catch{}Invalidate(new Rectangle(0,270,Width,210));}
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(drag>=0)SetSlider(e.Y);}
 protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);drag=-1;Capture=false;}
 protected override bool ProcessCmdKey(ref Message msg,Keys key){if(key==Keys.Escape){Hide();return true;}return base.ProcessCmdKey(ref msg,key);}
 protected override void Dispose(bool disposing){if(disposing&&!cleaned){cleaned=true;animation.Stop();animation.Dispose();timer.Stop();timer.Dispose();if(IsHandleCreated)UnregisterHotKey(Handle,1);tray.Visible=false;tray.Dispose();foreach(var c in channels)c.Dispose();foreach(var i in icons.Values)if(i!=null)i.Dispose();if(cover!=null)cover.Dispose();title.Dispose();body.Dispose();small.Dispose();big.Dispose();symbols.Dispose();}base.Dispose(disposing);}
 public async Task Test(string file){await RefreshData(true);using(var bmp=new Bitmap(Width,Height)){DrawToBitmap(bmp,new Rectangle(0,0,Width,Height));bmp.Save(file,ImageFormat.Png);}File.WriteAllText(file+".txt","Audio channels: "+channels.Count+Environment.NewLine+String.Join(Environment.NewLine,channels.Select(c=>c.Name+" "+c.Volume))+Environment.NewLine+"Media: "+track.Title+Environment.NewLine+"Status: "+(error??"OK"));}
 static void Log(Exception e){File.WriteAllText(Path.Combine(Path.GetTempPath(),"SpotifyBar-crash.txt"),e.ToString());}
 [STAThread] static void Main(string[] args){AppDomain.CurrentDomain.UnhandledException+=delegate(object s,UnhandledExceptionEventArgs e){Log((Exception)e.ExceptionObject);};Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);Application.ThreadException+=delegate(object s,ThreadExceptionEventArgs e){Log(e.Exception);Application.Exit();};try{Run(args);}catch(Exception e){Log(e);}}
 static void Run(string[] args){bool created;using(var mutex=new Mutex(true,args.Length>0&&args[0]=="--test"?"SpotifyBar.Native.Test":"SpotifyBar.Native.v1",out created)){if(!created)return;Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);using(var bar=new Bar()){if(args.Length==2&&args[0]=="--test"){bar.Shown+=async delegate{await bar.Test(args[1]);bar.Close();};}Application.Run(bar);}}}
}
