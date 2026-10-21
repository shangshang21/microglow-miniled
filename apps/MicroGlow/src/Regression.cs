// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 shangshang21 and contributors
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MicroGlow {
 [DataContract]public class RegressionResult {
  [DataMember]public DisplayState Before;
  [DataMember]public DisplayState After;
  [DataMember]public AudioState AudioBefore;
  [DataMember]public AudioState AudioAfter;
  [DataMember]public List<string> Passed=new List<string>();
  [DataMember]public List<SceneState> Scenes=new List<SceneState>();
  [DataMember]public bool Restored;
  [DataMember]public string Error;
 }
 public static class Regression {
  static DisplayState Copy(DisplayState state){var copy=new DisplayState{Connected=state.Connected,Name=state.Name,Identity=state.Identity,DeviceName=state.DeviceName,Width=state.Width,Height=state.Height,Error=state.Error};foreach(var f in state.Features)copy.Features.Add(new Feature{Code=f.Code,Supported=f.Supported,Current=f.Current,Maximum=f.Maximum,Error=f.Error});return copy;}
  public static int Run(){var result=new RegressionResult();using(var m=new MonitorDevice()){
   result.Before=Copy(m.Connect());result.AudioBefore=Audio.Access(null,null);Store.Json("comprehensive-before.json",result);
   try{if(!result.Before.Connected)throw new InvalidOperationException("目标屏幕未连接");
    foreach(var f in result.Before.Features)if(f.Supported){if(f.Code==0x14){m.SetDiscrete(0x14,f.Current==4?5u:4u);m.SetDiscrete(0x14,f.Current);}else if(f.Code==0x87){m.SetDiscrete(0x87,f.Current<4?f.Current+1:f.Current-1);m.SetDiscrete(0x87,f.Current);}else{int value=(int)Math.Round(f.Current*100.0/f.Maximum);m.Set((byte)f.Code,value<100?value+1:value-1);m.Set((byte)f.Code,value);}result.Passed.Add("VCP-"+f.Code.ToString("X2")+"-write-read-restore");}
    string[] names={"标准","办公","夜读","FPS","电影"};int[,] values={{20,50,5,1},{25,50,5,1},{8,50,4,0},{45,55,5,2},{35,50,5,0}};
    for(int i=0;i<5;i++){m.SetDiscrete(0x14,(uint)values[i,2]);m.SetDiscrete(0x87,(uint)values[i,3]);m.Set(0x12,values[i,1]);m.Set(0x10,values[i,0]);var actual=new SceneValues{Brightness=(int)m.Read(0x10).Current,Contrast=(int)m.Read(0x12).Current,Color=m.Read(0x14).Current,Sharpness=m.Read(0x87).Current};string identified=Controller.MatchScene(actual);if(identified!=names[i])throw new InvalidOperationException("场景状态不一致："+names[i]+" / "+identified);result.Scenes.Add(new SceneState{Scene=identified,Values=actual,Confirmed=true});result.Passed.Add("scene-"+identified);}
    if(result.AudioBefore.Available){var a=result.AudioBefore;var volume=Audio.Access(a.Volume<100?a.Volume+1:a.Volume-1,null,a.DeviceId);if(!volume.Available||volume.Volume!=(a.Volume<100?a.Volume+1:a.Volume-1))throw new InvalidOperationException("系统音量读回不一致");var mute=Audio.Access(a.Volume,a.Muted,a.DeviceId);if(!mute.Available||mute.Volume!=a.Volume||mute.Muted!=a.Muted)throw new InvalidOperationException("音量/静音恢复失败");result.Passed.Add("system-volume-and-same-mute-readback");}
   }catch(Exception e){result.Error=e.Message;}
   finally{try{
    foreach(var f in result.Before.Features)if(f.Supported&&f.Code!=0x14&&f.Code!=0x87)m.Set((byte)f.Code,(int)Math.Round(f.Current*100.0/f.Maximum));
    foreach(var f in result.Before.Features)if(f.Supported&&(f.Code==0x14||f.Code==0x87))m.SetDiscrete((byte)f.Code,f.Current);
    if(result.AudioBefore.Available)result.AudioAfter=Audio.Access(result.AudioBefore.Volume,result.AudioBefore.Muted,result.AudioBefore.DeviceId);else result.AudioAfter=Audio.Access(null,null);
    result.After=m.Connect();result.Restored=result.After.Connected;
    foreach(var f in result.Before.Features)if(f.Supported){var after=m.Find(f.Code);if(!after.Supported||after.Current!=f.Current)result.Restored=false;}
    if(result.AudioBefore.Available&&(!result.AudioAfter.Available||result.AudioAfter.DeviceId!=result.AudioBefore.DeviceId||result.AudioAfter.Volume!=result.AudioBefore.Volume||result.AudioAfter.Muted!=result.AudioBefore.Muted))result.Restored=false;
   }catch(Exception e){result.Error=(result.Error??"")+" Restore: "+e.Message;result.Restored=false;}
   Store.Json("comprehensive-result.json",result);}
  }return result.Error==null&&result.Restored?0:1;}
 }
}
