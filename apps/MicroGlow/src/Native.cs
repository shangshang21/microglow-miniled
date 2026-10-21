// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 shangshang21 and contributors
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace MicroGlow {
 public static class Store {
  public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MicroGlowOSD");
  static readonly object Gate = new object();
  public static void Log(string text) { lock(Gate) { try { Directory.CreateDirectory(DirectoryPath); File.AppendAllText(Path.Combine(DirectoryPath,"activity.log"), DateTime.Now.ToString("s")+" "+text+Environment.NewLine, Encoding.UTF8); } catch {} } }
  public static void Json(string file, object value) { Directory.CreateDirectory(DirectoryPath); using(var stream = File.Create(Path.Combine(DirectoryPath,file))) new DataContractJsonSerializer(value.GetType()).WriteObject(stream,value); }
 }
 [DataContract] public class Feature {
  [DataMember] public int Code;
  [DataMember] public bool Supported;
  [DataMember] public uint Current;
  [DataMember] public uint Maximum;
  [DataMember] public string Error;
 }
 [DataContract] public class DisplayState {
  [DataMember] public bool Connected;
  [DataMember] public string Name;
  [DataMember] public string Identity;
  [DataMember] public string DeviceName;
  [DataMember] public int Width;
  [DataMember] public int Height;
  [DataMember] public string Error;
  [DataMember] public List<Feature> Features = new List<Feature>();
 }
 public sealed partial class MonitorDevice : IDisposable {
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Physical { public IntPtr Handle; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Description; }
  [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Device; }
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct DisplayDevice { public int Size; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Name; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Description; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Id; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Key; }
  delegate bool EnumProc(IntPtr monitor,IntPtr dc,IntPtr rectangle,IntPtr data);
  [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,EnumProc callback,IntPtr data);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool EnumDisplayDevices(string device,uint index,ref DisplayDevice result,uint flags);
  [DllImport("dxva2.dll",SetLastError=true)] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor,out uint count);
  [DllImport("dxva2.dll",SetLastError=true)] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor,uint count,[Out] Physical[] results);
  [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitor(IntPtr monitor);
  [DllImport("dxva2.dll",SetLastError=true)] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor,byte code,out uint kind,out uint current,out uint maximum);
  [DllImport("dxva2.dll",SetLastError=true)] static extern bool SetVCPFeature(IntPtr monitor,byte code,uint value);
  IntPtr handle;bool opened;
  public DisplayState State = new DisplayState();
  static readonly byte[] Codes = {0x10,0x12,0x62,0x16,0x18,0x1a,0x14,0x87};
  public DisplayState Connect() {
   State=new DisplayState();if(!AcquireTarget())return State;
   foreach(byte code in Codes) State.Features.Add(Read(code));
   State.Connected=Find(0x10).Supported;
   if(!State.Connected)State.Error="屏幕暂未响应亮度控制，请稍后重连";
   Store.Log("monitor-connect "+State.Connected+" "+State.Identity);return State;
  }
  bool AcquireTarget(){
   Dispose();var matches=new List<Tuple<IntPtr,MonitorInfo,string>>();
   EnumProc cb=delegate(IntPtr monitor,IntPtr dc,IntPtr rectangle,IntPtr data) {
    var info=new MonitorInfo();info.Size=Marshal.SizeOf(typeof(MonitorInfo));
    if(!GetMonitorInfo(monitor,ref info))return true;
    var device=new DisplayDevice();device.Size=Marshal.SizeOf(typeof(DisplayDevice));
    if(EnumDisplayDevices(info.Device,0,ref device,0) && (device.Id??"").IndexOf("RTK2775",StringComparison.OrdinalIgnoreCase)>=0)
     matches.Add(Tuple.Create(monitor,info,device.Id));
    return true;
   };
   EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,cb,IntPtr.Zero);GC.KeepAlive(cb);
   if(matches.Count!=1) {State.Error=matches.Count==0?"没有找到便携屏，请检查连接后重连":"找到多块同型号屏幕，请先保留一块连接";return false;}
   var match=matches[0]; uint count;
   if(!GetNumberOfPhysicalMonitorsFromHMONITOR(match.Item1,out count)||count!=1) {State.Error="暂时无法识别屏幕控制接口";return false;}
   var physical=new Physical[count];
   if(!GetPhysicalMonitorsFromHMONITOR(match.Item1,count,physical)) {State.Error="无法打开屏幕控制接口";return false;}
   handle=physical[0].Handle;opened=true;State.Name="小太阳 · Mini LED";State.Identity=match.Item3;State.DeviceName=match.Item2.Device;State.Width=match.Item2.Monitor.Right-match.Item2.Monitor.Left;State.Height=match.Item2.Monitor.Bottom-match.Item2.Monitor.Top;
   return true;
  }
  public Feature Find(int code) { foreach(var value in State.Features)if(value.Code==code)return value;return new Feature{Code=code,Error="未查询"}; }
  public Feature Read(byte code) {
   uint kind,current,maximum;var result=new Feature{Code=code};
   if(!opened){result.Error="未连接";return result;}
   for(int attempt=0;attempt<6;attempt++){
    bool ok=GetVCPFeatureAndVCPFeatureReply(handle,code,out kind,out current,out maximum);
    if(ok&&code==0x14)maximum=11;
    if(ok&&code==0x87)maximum=4;
    if(ok&&maximum>0&&current<=maximum){result.Supported=true;result.Current=current;result.Maximum=maximum;result.Error=null;Thread.Sleep(50);break;}
    int error=Marshal.GetLastWin32Error();result.Error="读取未成功 ("+error+")";
    if(error==unchecked((int)0xC0262589)&&attempt<5){Store.Log("monitor-handle-reacquire code="+code.ToString("X2"));if(!AcquireTarget())break;}
    if(attempt<5)Thread.Sleep(180);
   }
   if(!result.Supported)Store.Log("vcp-read-failed code="+code.ToString("X2")+" "+result.Error);
   return result;
  }
  public Feature Set(byte code,int percent) {
   if(code!=0x10 && code!=0x12 && code!=0x62 && code!=0x16 && code!=0x18 && code!=0x1a)throw new InvalidOperationException("控制项目不在允许列表");
   var saved=Find(code);if(!saved.Supported)throw new InvalidOperationException("此屏幕不支持这项控制");
   uint value=(uint)Math.Round(Math.Max(0,Math.Min(100,percent))*saved.Maximum/100.0);
   return WriteChecked(code,value);
  }
  public Feature SetDiscrete(byte code,uint value){
   if(code==0x14){if(value!=1&&value!=4&&value!=5&&value!=6&&value!=8&&value!=11)throw new InvalidOperationException("未知色彩预设");}
   else if(code==0x87){if(value>4)throw new InvalidOperationException("锐度范围为 0～4");}
   else throw new InvalidOperationException("离散控制项目不在允许列表");
   if(!Find(code).Supported)throw new InvalidOperationException("屏幕未确认支持这项控制");
   return WriteChecked(code,value);
  }
  Feature WriteChecked(byte code,uint value){
   if(!SetVCPFeature(handle,code,value)){
    int error=Marshal.GetLastWin32Error();
    if(error!=unchecked((int)0xC0262589)||!AcquireTarget()||!SetVCPFeature(handle,code,value))throw new InvalidOperationException("屏幕没有接受设置，请重连后再试");
    Store.Log("monitor-write-handle-reacquired code="+code.ToString("X2"));
   }
   Feature read=null;
   for(int attempt=0;attempt<6;attempt++){
    Thread.Sleep(attempt==0?120:180);read=Read(code);
    Store.Log("vcp-readback code="+code.ToString("X2")+" requested="+value+" supported="+read.Supported+" read="+read.Current+" attempt="+attempt);
    if(read.Supported&&read.Current==value)break;
   }
   if(!read.Supported || read.Current!=value)throw new InvalidOperationException("设置尚未读回确认，请重连刷新");
   State.Features.RemoveAll(x=>x.Code==code);State.Features.Add(read);
   Store.Log("vcp-verified code="+code.ToString("X2")+" current="+read.Current+" max="+read.Maximum);return read;
  }
  public void Dispose(){if(opened){DestroyPhysicalMonitor(handle);handle=IntPtr.Zero;opened=false;}}
 }
 [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom {}
 [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDeviceEnumerator {
  [PreserveSig]int EnumAudioEndpoints(int flow,uint mask,out IntPtr devices);
  [PreserveSig]int GetDefaultAudioEndpoint(int flow,int role,out IMMDevice device);
  [PreserveSig]int GetDevice([MarshalAs(UnmanagedType.LPWStr)]string id,out IMMDevice device);
  [PreserveSig]int RegisterEndpointNotificationCallback(IntPtr client);
  [PreserveSig]int UnregisterEndpointNotificationCallback(IntPtr client);
 }
 [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IMMDevice {
  [PreserveSig]int Activate(ref Guid id,uint context,IntPtr activation,[MarshalAs(UnmanagedType.IUnknown)]out object result);
  [PreserveSig]int OpenPropertyStore(uint access,out IPropertyStore properties);
  [PreserveSig]int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);
  [PreserveSig]int GetState(out uint state);
 }
 [StructLayout(LayoutKind.Sequential)] struct PropertyKey {public Guid Id;public uint Index; public PropertyKey(Guid id,uint index){Id=id;Index=index;} }
 [StructLayout(LayoutKind.Explicit,Size=24)] struct PropertyVariant { [FieldOffset(0)] public ushort Type;[FieldOffset(8)] public IntPtr Pointer; }
 [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IPropertyStore {
  [PreserveSig]int GetCount(out uint count);
  [PreserveSig]int GetAt(uint index,out PropertyKey key);
  [PreserveSig]int GetValue(ref PropertyKey key,out PropertyVariant value);
  [PreserveSig]int SetValue(ref PropertyKey key,ref PropertyVariant value);
  [PreserveSig]int Commit();
 }
 [ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IAudioEndpointVolume {
  [PreserveSig]int RegisterControlChangeNotify(IntPtr callback);
  [PreserveSig]int UnregisterControlChangeNotify(IntPtr callback);
  [PreserveSig]int GetChannelCount(out uint count);
  [PreserveSig]int SetMasterVolumeLevel(float value,ref Guid context);
  [PreserveSig]int SetMasterVolumeLevelScalar(float value,ref Guid context);
  [PreserveSig]int GetMasterVolumeLevel(out float value);
  [PreserveSig]int GetMasterVolumeLevelScalar(out float value);
  [PreserveSig]int SetChannelVolumeLevel(uint channel,float value,ref Guid context);
  [PreserveSig]int SetChannelVolumeLevelScalar(uint channel,float value,ref Guid context);
  [PreserveSig]int GetChannelVolumeLevel(uint channel,out float value);
  [PreserveSig]int GetChannelVolumeLevelScalar(uint channel,out float value);
  [PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool value,ref Guid context);
  [PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool value);
 }
 [DataContract] public class AudioState {
  [DataMember]public bool Available;
  [DataMember]public int Volume;
  [DataMember]public bool Muted;
  [DataMember]public string Name;
  [DataMember]public string DeviceId;
  [DataMember]public string Error;
 }
 public static class Audio {
  [DllImport("ole32.dll")]static extern int PropVariantClear(ref PropertyVariant value);
  static Guid context=new Guid("671D509B-F5F3-493A-9B6C-120B7E96222D");
  public static AudioState Access(int? setVolume,bool? setMute,string expectedDevice=null) {
   object enumerator=null,activated=null;IMMDevice device=null;IPropertyStore properties=null;
   var result=new AudioState();
   try {
    enumerator=new MMDeviceEnumeratorCom();Marshal.ThrowExceptionForHR(((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0,0,out device));
    string endpointId;Marshal.ThrowExceptionForHR(device.GetId(out endpointId));result.DeviceId=endpointId;if(expectedDevice!=null&&endpointId!=expectedDevice)throw new InvalidOperationException("播放设备已切换，未向其他设备写入");
    var iid=new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");Marshal.ThrowExceptionForHR(device.Activate(ref iid,23,IntPtr.Zero,out activated));var volume=(IAudioEndpointVolume)activated;
    if(setVolume.HasValue)Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar(Math.Max(0,Math.Min(100,setVolume.Value))/100f,ref context));
    if(setMute.HasValue)Marshal.ThrowExceptionForHR(volume.SetMute(setMute.Value,ref context));
    float scalar;bool muted;Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out scalar));Marshal.ThrowExceptionForHR(volume.GetMute(out muted));
    result.Available=true;result.Volume=(int)Math.Round(scalar*100);result.Muted=muted;result.Name="当前播放设备";
    if(device.OpenPropertyStore(0,out properties)>=0) {
     var key=new PropertyKey(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),14);PropertyVariant value;
     if(properties.GetValue(ref key,out value)>=0){try{if(value.Type==31)result.Name=Marshal.PtrToStringUni(value.Pointer);}finally{PropVariantClear(ref value);}}
    }
   }catch(Exception ex){result.Error=ex.Message;}
   finally {foreach(object obj in new object[]{properties,activated,device,enumerator})if(obj!=null && Marshal.IsComObject(obj))Marshal.ReleaseComObject(obj);}
   return result;
  }
 }
 public static class Native {
  [DllImport("kernel32.dll")]public static extern uint SetThreadExecutionState(uint flags);
  [DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr window,int command);
  [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr window);
  [DllImport("user32.dll")]public static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
  [DllImport("user32.dll")]public static extern bool UnregisterHotKey(IntPtr window,int id);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern uint RegisterWindowMessage(string name);
  [DllImport("user32.dll")]public static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
  [DllImport("user32.dll")]public static extern bool DestroyIcon(IntPtr icon);
 }
}
