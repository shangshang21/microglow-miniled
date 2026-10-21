// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 shangshang21 and contributors
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MicroGlow {
 [DataContract]public sealed class DimmingState {
  [DataMember]public bool Supported;
  [DataMember]public bool Enabled;
  [DataMember]public uint Signature;
  [DataMember]public uint RawState;
  [DataMember]public string Error;
  [DataMember]public string ReadAt=DateTime.Now.ToString("o");
 }
 public sealed partial class MonitorDevice {
  public DimmingState ReadDimming(){
   var result=new DimmingState();
   if(!opened&&!AcquireTarget()){result.Error="小太阳未连接";return result;}
   for(int attempt=0;attempt<6;attempt++){
    uint kind,current,maximum;bool ok=GetVCPFeatureAndVCPFeatureReply(handle,0xe8,out kind,out current,out maximum);
    if(ok&&((maximum==0x4c44&&current<=1)||(maximum==0x4c45&&current<=5))){result.Supported=true;result.Enabled=current==1;result.RawState=current;result.Signature=maximum;Thread.Sleep(50);return result;}
    int error=Marshal.GetLastWin32Error();
    Store.Log("dimming-read-failed ok="+ok+" error=0x"+error.ToString("X8")+" current="+current+" max="+maximum+" attempt="+attempt);
    result.Error=ok?"分区接口签名或状态未确认":"面板暂未响应，唤醒屏幕后刷新";
    if(ok)break;
    if(error==unchecked((int)0xC0262589)&&attempt<5){if(!AcquireTarget())break;}
    if(attempt<5)Thread.Sleep(200);
   }
   return result;
  }
 }
 public sealed partial class Controller {
  bool dimmingBusy;DimmingPattern dimmingPattern;
  void InitializeDimming(){
   Get<Button>("DimmingOnButton").IsEnabled=false;Get<Button>("DimmingOffButton").IsEnabled=false;
   Get<Button>("DimmingRefreshButton").Click+=delegate{RefreshDimming();};Get<Button>("DimmingPatternButton").Click+=delegate{OpenDimmingPattern();};
  }
  void RefreshDimming(){if(dimmingBusy||sceneBusy||!connected)return;dimmingBusy=true;Get<TextBlock>("DimmingStatus").Text="读取中…";Queue(delegate{var state=monitor.ReadDimming();Store.Json("dimming-state.json",state);Ui(delegate{dimmingBusy=false;PaintDimming(state);});});}
  void PaintDimming(DimmingState state){Get<Button>("DimmingOnButton").IsEnabled=false;Get<Button>("DimmingOffButton").IsEnabled=false;
   Get<TextBlock>("DimmingStatus").Text=state.Supported?(state.RawState>1?"控制状态不一致":"控制位可读 · 切换无可见差异"):"分区控制状态未确认";
   Get<TextBlock>("DimmingDetail").Text=state.Supported?"本案例分区效果有效；开关无可见差异，公开版停用切换。":"未安装研究用状态桥时此处不可读，不代表分区背光失效。";Get<TextBlock>("DimmingRegister").Text=state.Supported?"E8 v"+(state.Signature==0x4c45?"2":"1")+" · state "+state.RawState+" · 读回":"E8 — · 尚未确认";
   Get<Button>("DimmingOnButton").Tag=state.Supported&&state.RawState==1?"selected":null;Get<Button>("DimmingOffButton").Tag=state.Supported&&state.RawState==0?"selected":null;PaintZones(MotionEnabled);
  }
  void ToggleManualDimming(){Status("本案例开关没有可见效果，公开版不发送分区切换命令",false);}
  void OpenDimmingPattern(){if(dimmingPattern!=null){dimmingPattern.Activate();return;}if(!connected){Status("请先连接小太阳",true);return;}
   dimmingPattern=new DimmingPattern();dimmingPattern.Closed+=delegate{dimmingPattern=null;if(!exiting)Open();};try{dimmingPattern.OpenOn(monitor.State);window.Hide();}catch(Exception e){dimmingPattern.Close();Status(e.Message,true);}}
 }
}
