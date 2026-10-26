using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class DeviceEnumerator {}
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IDevices {
 [PreserveSig] int Enum(int flow,int state,out IntPtr p); [PreserveSig] int Default(int flow,int role,out IDevice d);
}
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IDevice {
 [PreserveSig] int Activate(ref Guid id,int ctx,IntPtr args,[MarshalAs(UnmanagedType.IUnknown)] out object obj);
}
[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IEndpoint {
 [PreserveSig] int Register(IntPtr p); [PreserveSig] int Unregister(IntPtr p); [PreserveSig] int Channels(out uint n); [PreserveSig] int SetDb(float n,IntPtr c); [PreserveSig] int Set(float n,IntPtr c); [PreserveSig] int GetDb(out float n); [PreserveSig] int Get(out float n); [PreserveSig] int SetChannel(uint c,float v,IntPtr x); [PreserveSig] int SetChannelScalar(uint c,float v,IntPtr x); [PreserveSig] int GetChannel(uint c,out float v); [PreserveSig] int GetChannelScalar(uint c,out float v); [PreserveSig] int Mute([MarshalAs(UnmanagedType.Bool)] bool b,IntPtr c); [PreserveSig] int IsMute([MarshalAs(UnmanagedType.Bool)] out bool b);
}
[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IManager {
 [PreserveSig] int Session(ref Guid g,uint f,out IntPtr p); [PreserveSig] int Volume(ref Guid g,uint f,out IntPtr p); [PreserveSig] int Sessions(out ISessions s);
}
[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ISessions {
 [PreserveSig] int Count(out int n); [PreserveSig] int Item(int i,out IControl c);
}
[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IControl {
 [PreserveSig] int State(out int n); [PreserveSig] int Name(out IntPtr s); [PreserveSig] int SetName(IntPtr s,IntPtr c); [PreserveSig] int Icon(out IntPtr s); [PreserveSig] int SetIcon(IntPtr s,IntPtr c); [PreserveSig] int Group(out Guid g); [PreserveSig] int SetGroup(ref Guid g,IntPtr c); [PreserveSig] int Register(IntPtr p); [PreserveSig] int Unregister(IntPtr p); [PreserveSig] int Id(out IntPtr s); [PreserveSig] int Instance(out IntPtr s); [PreserveSig] int Pid(out uint p); [PreserveSig] int SystemSounds(); [PreserveSig] int Duck(bool b);
}
[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ISimple {
 [PreserveSig] int Set(float v,IntPtr c); [PreserveSig] int Get(out float v); [PreserveSig] int Mute([MarshalAs(UnmanagedType.Bool)] bool b,IntPtr c); [PreserveSig] int IsMute([MarshalAs(UnmanagedType.Bool)] out bool b);
}
[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMeter { [PreserveSig] int Peak(out float v); }
class AudioChannel : IDisposable {
 public string Name, Path; public uint Pid; public object Owner; public IEndpoint Endpoint; public ISimple Simple; public IMeter Meter;
 public float Volume { get {float v=0; if(Endpoint!=null) Marshal.ThrowExceptionForHR(Endpoint.Get(out v)); else Marshal.ThrowExceptionForHR(Simple.Get(out v)); return v;} set {if(Endpoint!=null) Marshal.ThrowExceptionForHR(Endpoint.Set(value,IntPtr.Zero)); else Marshal.ThrowExceptionForHR(Simple.Set(value,IntPtr.Zero));} }
 public bool Muted {get {bool v=false; if(Endpoint!=null) Endpoint.IsMute(out v); else Simple.IsMute(out v); return v;} set {if(Endpoint!=null) Endpoint.Mute(value,IntPtr.Zero); else Simple.Mute(value,IntPtr.Zero);} }
 public float Peak {get {float v=0; if(Meter!=null) Meter.Peak(out v); return v;} }
 public void Dispose(){ if(Owner!=null && Marshal.IsComObject(Owner)) Marshal.ReleaseComObject(Owner); Owner=null; }
}
static class Audio {
 static object Activate(IDevice d,Type t) {object o; Guid g=t.GUID; Marshal.ThrowExceptionForHR(d.Activate(ref g,23,IntPtr.Zero,out o)); return o;}
 public static List<AudioChannel> Read() {
  var list=new List<AudioChannel>(); var enumerator=(IDevices)new DeviceEnumerator();
  try { for(int flow=0;flow<2;flow++) { IDevice dev=null;
   try {Marshal.ThrowExceptionForHR(enumerator.Default(flow,1,out dev)); var o=Activate(dev,typeof(IEndpoint)); IMeter meter=null; try {meter=(IMeter)Activate(dev,typeof(IMeter));}catch{}
    list.Add(new AudioChannel{Name=flow==0?"Speakers":"Microphone",Endpoint=(IEndpoint)o,Owner=o,Meter=meter,Pid=(uint)(flow+1)});
    if(flow==0) {var manager=(IManager)Activate(dev,typeof(IManager)); ISessions sessions; manager.Sessions(out sessions); try {int n;sessions.Count(out n); for(int i=0;i<n;i++){IControl control;sessions.Item(i,out control); uint pid;control.Pid(out pid);int state;control.State(out state); if(pid==0||state==2){Marshal.ReleaseComObject(control);continue;} try {var p=Process.GetProcessById((int)pid); string path=null;try{path=p.MainModule.FileName;}catch{} IMeter sm=null;try{sm=(IMeter)control;}catch{} list.Add(new AudioChannel{Name=p.ProcessName,Path=path,Pid=pid,Owner=control,Simple=(ISimple)control,Meter=sm});}catch {Marshal.ReleaseComObject(control);} }}finally{Marshal.ReleaseComObject(sessions);Marshal.ReleaseComObject(manager);}}
   }catch{} finally{if(dev!=null)Marshal.ReleaseComObject(dev);}
  }}finally{Marshal.ReleaseComObject(enumerator);} return list;
 }
}
