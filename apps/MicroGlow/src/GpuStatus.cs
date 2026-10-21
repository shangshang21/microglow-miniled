// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 shangshang21 and contributors
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MicroGlow {
 public sealed class GpuInfo {
  public string FreeSyncStatus="尚未读取", FreeSyncDetail="等待显卡回复", HdrStatus="尚未读取", HdrDetail="等待显卡回复", Mode="等待屏幕连接";
  public bool FreeSyncAvailable;
 }
 // Read-only AMD driver information. No display, HDR, or FreeSync setters are imported.
 public static class GpuStatus {
  static readonly object Gate=new object();
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct DisplayDevice {
   public int Size;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Name;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Description;
   public uint Flags;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Id;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Key;
  }
  [StructLayout(LayoutKind.Explicit,Size=220,CharSet=CharSet.Unicode)] struct DisplayMode {
   [FieldOffset(68)] public ushort Size;
   [FieldOffset(172)] public uint Width;
   [FieldOffset(176)] public uint Height;
   [FieldOffset(184)] public uint Frequency;
  }
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Ansi)] struct AdapterInfo {
   public int Size,Index;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Udid;
   public int Bus,Device,Function,Vendor;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Name;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string DisplayName;
   public int Present,Exist;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string DriverPath;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string DriverPathExt;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Pnp;
   public int OsDisplayIndex;
  }
  [StructLayout(LayoutKind.Sequential)] struct DisplayId {public int Logical,Physical,LogicalAdapter,PhysicalAdapter;}
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Ansi)] struct AdlDisplay {
   public DisplayId Id; public int Controller;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Name;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Manufacturer;
   public int Type,OutputType,Connector,Mask,Value;
  }
  [StructLayout(LayoutKind.Sequential,Size=32)] struct FreeSyncCap {
   public int Caps,Min,Max; public byte Label,Reserved0,Reserved1,Reserved2; public int Reserved3,Reserved4,Reserved5,Reserved6;
  }
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool EnumDisplayDevices(string device,uint index,ref DisplayDevice result,uint flags);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool EnumDisplaySettingsEx(string device,int mode,ref DisplayMode result,uint flags);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibrary(string path);
  [DllImport("kernel32.dll",CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr library,string name);
  [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr library);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Alloc(int bytes);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Create(Alloc allocate,int enumerateConnected,out IntPtr context);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Destroy(IntPtr context);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Count(IntPtr context,out int count);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Adapters(IntPtr context,IntPtr data,int bytes);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Displays(IntPtr context,int adapter,out int count,out IntPtr data,int forceDetect);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Cap(IntPtr context,int adapter,int display,out FreeSyncCap cap);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int State(IntPtr context,int adapter,int display,out int current,out int defaults,out int minimum,out int maximum);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Hdr(IntPtr context,int adapter,DisplayId display,out int supported,out int enabled);
  static T Function<T>(IntPtr dll,string name) where T:class {
   var ptr=GetProcAddress(dll,name); if(ptr==IntPtr.Zero)throw new InvalidOperationException("驱动未开放 "+name);
   return Marshal.GetDelegateForFunctionPointer(ptr,typeof(T)) as T;
  }
  public static GpuInfo Read() {lock(Gate){return ReadLocked();}}
  static GpuInfo ReadLocked() {
   var result=new GpuInfo(); string target=null;int matches=0;
   for(uint i=0;i<64;i++) {
    var adapter=new DisplayDevice();adapter.Size=Marshal.SizeOf(typeof(DisplayDevice));
    if(!EnumDisplayDevices(null,i,ref adapter,0))break;
    if((adapter.Flags&1)==0)continue;
    for(uint j=0;j<16;j++) {
     var monitor=new DisplayDevice();monitor.Size=Marshal.SizeOf(typeof(DisplayDevice));
     if(!EnumDisplayDevices(adapter.Name,j,ref monitor,0))break;
     if((monitor.Id??"").IndexOf("RTK2775",StringComparison.OrdinalIgnoreCase)<0)continue;
     target=adapter.Name;matches++;
    }
   }
   if(matches!=1){result.FreeSyncStatus=result.HdrStatus="未连接";result.FreeSyncDetail=result.HdrDetail=matches==0?"未找到小太阳":"同型号屏幕不唯一";return result;}
   var mode=new DisplayMode();mode.Size=220;
   if(EnumDisplaySettingsEx(target,-1,ref mode,0))result.Mode=mode.Width+" × "+mode.Height+" · "+mode.Frequency+" Hz";
   else result.Mode="当前显示模式暂不可读";
   IntPtr dll=IntPtr.Zero,context=IntPtr.Zero,adapters=IntPtr.Zero,displays=IntPtr.Zero;
   Destroy destroy=null; Alloc allocator=delegate(int bytes){return Marshal.AllocHGlobal(bytes);};
   try {
    dll=LoadLibrary(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"atiadlxx.dll"));
    if(dll==IntPtr.Zero)throw new InvalidOperationException("未找到 AMD 显示驱动接口");
    destroy=Function<Destroy>(dll,"ADL2_Main_Control_Destroy");
    int rc=Function<Create>(dll,"ADL2_Main_Control_Create")(allocator,1,out context);
    if(rc!=0||context==IntPtr.Zero)throw new InvalidOperationException("AMD 接口初始化失败 ("+rc+")");
    int count;rc=Function<Count>(dll,"ADL2_Adapter_NumberOfAdapters_Get")(context,out count);
    if(rc!=0||count<=0||count>128)throw new InvalidOperationException("AMD 显卡枚举失败 ("+rc+")");
    int size=Marshal.SizeOf(typeof(AdapterInfo));adapters=Marshal.AllocHGlobal(size*count);
    for(int i=0;i<count;i++)Marshal.StructureToPtr(new AdapterInfo{Size=size},IntPtr.Add(adapters,i*size),false);
    rc=Function<Adapters>(dll,"ADL2_Adapter_AdapterInfo_Get")(context,adapters,size*count);
    if(rc!=0)throw new InvalidOperationException("AMD 显卡信息不可读 ("+rc+")");
    int selected=-1;
    for(int i=0;i<count;i++){
     var adapter=(AdapterInfo)Marshal.PtrToStructure(IntPtr.Add(adapters,i*size),typeof(AdapterInfo));
     if((adapter.Vendor==1002||adapter.Vendor==0x1002)&&string.Equals(adapter.DisplayName,target,StringComparison.OrdinalIgnoreCase))selected=adapter.Index;
    }
    if(selected<0)throw new InvalidOperationException("当前屏幕未映射到 AMD 显卡");
    int displayCount;rc=Function<Displays>(dll,"ADL2_Display_DisplayInfo_Get")(context,selected,out displayCount,out displays,0);
    if(rc!=0||displayCount<=0||displayCount>128||displays==IntPtr.Zero)throw new InvalidOperationException("AMD 屏幕信息不可读 ("+rc+")");
    AdlDisplay chosen=new AdlDisplay();int connected=0;size=Marshal.SizeOf(typeof(AdlDisplay));
    for(int i=0;i<displayCount;i++){
     var item=(AdlDisplay)Marshal.PtrToStructure(IntPtr.Add(displays,i*size),typeof(AdlDisplay));
     if((item.Value&1)!=0){chosen=item;connected++;}
    }
    // ADL's legacy adapter entries can report the same full GPU topology. Refuse ambiguity.
    if(connected!=1)throw new InvalidOperationException("AMD 连接目标不唯一，暂不读取状态");
    int logicalAdapter=chosen.Id.LogicalAdapter;
    FreeSyncCap cap;int capRc=Function<Cap>(dll,"ADL2_Display_FreeSync_Cap")(context,logicalAdapter,chosen.Id.Logical,out cap);
    int current,defaults,minimum,maximum;int stateRc=Function<State>(dll,"ADL2_Display_FreeSyncState_Get")(context,logicalAdapter,chosen.Id.Logical,out current,out defaults,out minimum,out maximum);
    result.FreeSyncAvailable=capRc==0&&stateRc==0&&(cap.Caps&1)!=0;
    if(result.FreeSyncAvailable){
     result.FreeSyncStatus=(current&4)!=0?"已开启":"已关闭";
     result.FreeSyncDetail=cap.Min>0&&cap.Max>=cap.Min?String.Format("可变刷新率 {0:0.#}–{1:0.#} Hz",cap.Min/1000000.0,cap.Max/1000000.0):"AMD 已识别可变刷新率";
    } else {
     result.FreeSyncStatus="当前连接不可用";
     result.FreeSyncDetail=capRc==-8||stateRc==-8?"AMD 当前未识别 FreeSync；检查屏幕开关和连接模式":"AMD 查询未成功 ("+capRc+" / "+stateRc+")";
    }
    int support,enabled;int hdrRc=Function<Hdr>(dll,"ADL2_Display_HDRState_Get")(context,logicalAdapter,chosen.Id,out support,out enabled);
    result.HdrStatus=hdrRc!=0?"暂不可读":support==0?"当前连接不支持":enabled!=0?"已开启":"已关闭";
    result.HdrDetail=hdrRc!=0?"AMD HDR 查询未成功 ("+hdrRc+")":support!=0?"Windows HDR · 由系统显示设置控制":"当前显示链路未报告 HDR 支持";
   } catch(Exception ex) {
    result.FreeSyncStatus=result.HdrStatus="暂不可读";result.FreeSyncDetail=result.HdrDetail=ex.Message;
   } finally {
    if(displays!=IntPtr.Zero)Marshal.FreeHGlobal(displays);
    if(adapters!=IntPtr.Zero)Marshal.FreeHGlobal(adapters);
    if(context!=IntPtr.Zero&&destroy!=null)destroy(context);
    GC.KeepAlive(allocator);
    if(dll!=IntPtr.Zero)FreeLibrary(dll);
   }
   return result;
  }
 }
}
