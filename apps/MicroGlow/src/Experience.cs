// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 shangshang21 and contributors
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MicroGlow {
 [DataContract]public class SceneValues {
  [DataMember]public int Brightness;
  [DataMember]public int Contrast;
  [DataMember]public uint Color;
  [DataMember]public uint Sharpness;
 }
 [DataContract]public class ExperiencePreferences {[DataMember]public bool Motion=true;[DataMember]public string Theme="auto";}
 public sealed partial class Controller {
  bool extraUpdating,sceneBusy,gpuBusy,motion=true;SceneValues sceneUndo,savedScene;
  readonly Dictionary<string,FrameworkElement> pages=new Dictionary<string,FrameworkElement>();
  readonly Dictionary<string,Button> navigation=new Dictionary<string,Button>();
  readonly Dictionary<Button,Brush> sceneBrushes=new Dictionary<Button,Brush>();
  string currentPage="home";
  T ReadFile<T>(string name)where T:class {try{using(var file=File.OpenRead(Path.Combine(Store.DirectoryPath,name)))return new DataContractJsonSerializer(typeof(T)).ReadObject(file)as T;}catch{return null;}}
  void InitializeExperience(){
   string[] keys={"home","scenes","display","gaming","settings"};string[] names={"Home","Scenes","Display","Gaming","Settings"};
   for(int i=0;i<keys.Length;i++){string key=keys[i];pages[key]=Get<FrameworkElement>(names[i]+"Page");var button=Get<Button>("Nav"+names[i]);navigation[key]=button;button.Click+=delegate{Navigate(key);};}
   var prefs=ReadFile<ExperiencePreferences>("preferences.json");if(prefs!=null)motion=prefs.Motion;var motionCheck=Get<CheckBox>("MotionCheck");motionCheck.IsChecked=motion;
   RoutedEventHandler motionChanged=delegate{motion=motionCheck.IsChecked==true;SavePreferences();UpdateMotion();};motionCheck.Checked+=motionChanged;motionCheck.Unchecked+=motionChanged;
   savedScene=ReadFile<SceneValues>("my-scene.json");Get<Button>("SceneSaved").IsEnabled=savedScene!=null;if(savedScene!=null)Get<TextBlock>("SavedSceneHint").Text="已经保存，点击应用\n亮度 "+savedScene.Brightness+" · 对比 "+savedScene.Contrast;
   WireScene("SceneStandard","标准",20,50,5,1);WireScene("SceneOffice","办公",25,50,5,1);WireScene("SceneNight","夜读",8,50,4,0);WireScene("SceneFps","FPS",45,55,5,2);WireScene("SceneCinema","电影",35,50,5,0);
   var saved=Get<Button>("SceneSaved");sceneBrushes[saved]=saved.Background;saved.Click+=delegate{if(savedScene!=null)ApplyScene("我的配方",savedScene,saved,true);};
   Get<Button>("SaveSceneButton").Click+=delegate{if(!connected||sceneBusy)return;Queue(delegate{try{var values=ReadScene();Store.Json("my-scene.json",values);Ui(delegate{savedScene=values;saved.IsEnabled=true;Get<TextBlock>("SavedSceneHint").Text="已经保存，点击应用\n亮度 "+values.Brightness+" · 对比 "+values.Contrast;SyncScene(monitor.State);SceneFeedback("已保存当前参数，下次仍可使用");});}catch(Exception e){Ui(delegate{SceneFeedback(e.Message);});}});};
   Get<Button>("UndoSceneButton").Click+=delegate{if(sceneUndo!=null){var restore=sceneUndo;ApplyScene("切换前参数",restore,null,false);}};
   Get<Button>("OpenScenesButton").Click+=delegate{Navigate("scenes");};Get<Button>("RefreshGpuButton").Click+=delegate{RefreshGpu();};
   Get<Button>("OpenAmdButton").Click+=delegate{string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),@"AMD\CNext\CNext\RadeonSoftware.exe");if(File.Exists(path))System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true});else Status("本机没有找到 AMD 软件入口",true);};
   Get<Button>("OpenHdrButton").Click+=delegate{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:display"){UseShellExecute=true});};
   Get<ComboBox>("ColorPresetCombo").SelectionChanged+=delegate{ChangeDiscrete(0x14,"ColorPresetCombo");};Get<ComboBox>("SharpnessCombo").SelectionChanged+=delegate{ChangeDiscrete(0x87,"SharpnessCombo");};
   foreach(var slider in sliders.Values)slider.ValueChanged+=delegate{if(!updating&&!sceneBusy)MarkSceneCustom();};
   window.Loaded+=delegate{AddHoverMotion(window);};Navigate("home");RefreshGpu();
  }
  void AddHoverMotion(DependencyObject parent){for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);var button=child as Button;if(button!=null){var scale=new ScaleTransform(1,1);button.RenderTransformOrigin=new Point(.5,.5);button.RenderTransform=scale;button.PreviewMouseLeftButtonDown+=delegate{if(MotionEnabled){scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(.97,TimeSpan.FromMilliseconds(180)));scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(.97,TimeSpan.FromMilliseconds(180)));}};button.PreviewMouseLeftButtonUp+=delegate{scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(MotionEnabled?180:0)));scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(MotionEnabled?180:0)));};button.MouseLeave+=delegate{scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);scale.ScaleX=scale.ScaleY=1;};}AddHoverMotion(child);}}
  void Navigate(string key){if(!pages.ContainsKey(key))return;currentPage=key;foreach(var item in pages)item.Value.Visibility=item.Key==key?Visibility.Visible:Visibility.Collapsed;foreach(var item in navigation)item.Value.Tag=item.Key==key?"selected":null;Get<ScrollViewer>("BodyScroll").VerticalScrollBarVisibility=key=="home"?ScrollBarVisibility.Disabled:ScrollBarVisibility.Auto;
   string title="刚刚好的光与声音",subtitle="照顾此刻的画面，也照顾你的使用习惯。";
   if(key=="scenes"){title="切换此刻的氛围";subtitle="办公、夜读、电影，或是你的下一场游戏。";}
   if(key=="display"){title="把画面调到喜欢的样子";subtitle="色彩、轮廓与细节，都有自己的分寸。";}
   if(key=="gaming"){title="让游戏的每一帧更清楚";subtitle="实际信号、HDR 与可变刷新率状态。";RefreshGpu();}
   if(key=="settings"){title="按你的习惯来";subtitle="托盘、快捷键与轻盈的界面动效。";}
   Get<TextBlock>("PageTitle").Text=title;Get<TextBlock>("PageSubtitle").Text=subtitle;UpdatePageHeading();Get<ScrollViewer>("BodyScroll").ScrollToTop();
   var page=pages[key];page.BeginAnimation(UIElement.OpacityProperty,null);var move=new TranslateTransform();page.RenderTransform=move;if(MotionEnabled){var ease=new CubicEase{EasingMode=EasingMode.EaseOut};page.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(700)));move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(12,0,TimeSpan.FromMilliseconds(700)){EasingFunction=ease});}else page.Opacity=1;AnimateCards(page);
  }
  static Brush BrushColor(string hex){return(Brush)new BrushConverter().ConvertFromString(hex);}
  void PaintExtras(DisplayState state){extraUpdating=true;try{foreach(var pair in new[]{Tuple.Create((byte)0x14,"ColorPresetCombo"),Tuple.Create((byte)0x87,"SharpnessCombo")}){var combo=Get<ComboBox>(pair.Item2);var feature=monitor.Find(pair.Item1);combo.IsEnabled=state.Connected&&feature.Supported;combo.SelectedIndex=-1;if(feature.Supported)foreach(ComboBoxItem item in combo.Items)if(Convert.ToUInt32(item.Tag)==feature.Current){combo.SelectedItem=item;break;}}
   Get<TextBlock>("ExtraStatus").Text=!state.Connected?"屏幕未连接，参数不可用":monitor.Find(0x14).Supported&&monitor.Find(0x87).Supported?"原机色彩预设与锐度已读取；色温为预设名称，未经仪器校准。":"部分原机项目暂未读到，请重连后再试。";
   bool ready=state.Connected&&monitor.Find(0x12).Supported&&monitor.Find(0x14).Supported&&monitor.Find(0x87).Supported;foreach(string id in new[]{"SceneStandard","SceneOffice","SceneNight","SceneFps","SceneCinema","SaveSceneButton"}){Get<Button>(id).IsEnabled=ready;var quick=Get<Button>(id.Replace("Scene","Quick"));if(quick!=null)quick.IsEnabled=ready;}Get<Button>("SceneSaved").IsEnabled=ready&&savedScene!=null;
   Get<TextBlock>("SideConnection").Text=state.Connected?"已连接 · 硬件读回":"未连接";SyncScene(state);
   }finally{extraUpdating=false;}}
  void ChangeDiscrete(byte code,string name){if(extraUpdating||sceneBusy||dimmingBusy||!connected)return;var item=Get<ComboBox>(name).SelectedItem as ComboBoxItem;if(item==null)return;uint value=Convert.ToUInt32(item.Tag);lastInput=DateTime.Now;sceneBusy=true;Get<Grid>("PageHost").IsEnabled=false;Get<TextBlock>("ExtraStatus").Text="正在写入并读回确认…";
   Queue(delegate{string outcome=null;var before=monitor.Read(code);try{monitor.SetDiscrete(code,value);}catch(Exception e){outcome=e.Message;if(before.Supported)try{monitor.SetDiscrete(code,before.Current);}catch(Exception restore){outcome+="；恢复未确认："+restore.Message;}}var state=monitor.Connect();Ui(delegate{sceneBusy=false;Get<Grid>("PageHost").IsEnabled=true;foreach(var feature in state.Features)SetFeature(feature);PaintExtras(state);Get<TextBlock>("ExtraStatus").Text=outcome??"已从屏幕读回确认";Status(outcome??"原机设置已确认",outcome!=null);});});
  }
  void WireScene(string name,string title,int brightness,int contrast,uint color,uint sharpness){var button=Get<Button>(name);sceneBrushes[button]=button.Background;var values=new SceneValues{Brightness=brightness,Contrast=contrast,Color=color,Sharpness=sharpness};button.Click+=delegate{ApplyScene(title,values,button,true);};var quick=Get<Button>(name.Replace("Scene","Quick"));if(quick!=null)quick.Click+=delegate{ApplyScene(title,values,button,true);};}
  SceneValues ReadScene(){for(int attempt=0;attempt<3;attempt++){var state=monitor.Connect();var values=SceneOf(state);if(values!=null)return values;Store.Log("scene-snapshot-retry "+attempt);System.Threading.Thread.Sleep(250);}throw new InvalidOperationException("场景所需的屏幕状态未读全，请先重连");}
  void WriteScene(SceneValues values){monitor.SetDiscrete(0x14,values.Color);monitor.SetDiscrete(0x87,values.Sharpness);monitor.Set(0x12,values.Contrast);monitor.Set(0x10,values.Brightness);}
  void ApplyScene(string name,SceneValues values,Button chosen,bool saveUndo){if(sceneBusy||dimmingBusy||!connected)return;sceneBusy=true;foreach(var timer in pending.Values)timer.Stop();lock(versionsLock)foreach(byte code in sliders.Keys)versions[code]++;Get<Grid>("PageHost").IsEnabled=false;SceneFeedback("正在切换到「"+name+"」…");lastInput=DateTime.Now;
   Queue(delegate{SceneValues before=null;string error=null;bool restored=false;try{before=ReadScene();Store.Json("scene-before.json",before);WriteScene(values);if(!SameScene(ReadScene(),values))throw new InvalidOperationException("场景最终参数与配方不一致");}catch(Exception e){error=e.Message;if(before!=null)try{WriteScene(before);restored=true;}catch(Exception restore){error+="；恢复未确认："+restore.Message;}}
    var state=monitor.Connect();Ui(delegate{sceneBusy=false;Get<Grid>("PageHost").IsEnabled=true;connected=state.Connected;foreach(var feature in state.Features)SetFeature(feature);PaintExtras(state);if(error==null){if(saveUndo)sceneUndo=before;else sceneUndo=null;Get<Button>("UndoSceneButton").IsEnabled=sceneUndo!=null;SyncScene(state);SceneFeedback("「"+name+"」已应用，全部参数读回确认");Status("场景已同步",false);Get<TextBlock>("BrightnessStatus").Text="已同步";}else{SceneFeedback("切换未完成"+(restored?"，已恢复切换前参数":"")+"："+error);Status(error,true);}});
   });
  }
  void SceneFeedback(string message){var label=Get<TextBlock>("SceneStatus");label.Text=message;if(MotionEnabled)label.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.35,1,TimeSpan.FromMilliseconds(300)));}
  void MarkSceneCustom(){Get<TextBlock>("ActiveSceneText").Text="参数调整中，待读回";Get<TextBlock>("CurrentSceneName").Text="自定参数";foreach(var pair in sceneBrushes){pair.Key.Tag=null;var quick=Get<Button>(pair.Key.Name.Replace("Scene","Quick"));if(quick!=null)quick.Tag=null;}}
  static SceneValues SceneOf(DisplayState state){if(state==null||!state.Connected)return null;Feature b=null,c=null,col=null,sh=null;foreach(var f in state.Features){if(f.Code==0x10)b=f;if(f.Code==0x12)c=f;if(f.Code==0x14)col=f;if(f.Code==0x87)sh=f;}if(b==null||c==null||col==null||sh==null||!b.Supported||!c.Supported||!col.Supported||!sh.Supported)return null;return new SceneValues{Brightness=(int)Math.Round(b.Current*100.0/b.Maximum),Contrast=(int)Math.Round(c.Current*100.0/c.Maximum),Color=col.Current,Sharpness=sh.Current};}
  public static string MatchScene(SceneValues v){if(v==null)return "未读取";if(v.Color==5&&v.Contrast==50&&v.Sharpness==1&&v.Brightness==20)return "标准";if(v.Color==5&&v.Contrast==50&&v.Sharpness==1&&v.Brightness==25)return "办公";if(v.Color==4&&v.Contrast==50&&v.Sharpness==0&&v.Brightness==8)return "夜读";if(v.Color==5&&v.Contrast==55&&v.Sharpness==2&&v.Brightness==45)return "FPS";if(v.Color==5&&v.Contrast==50&&v.Sharpness==0&&v.Brightness==35)return "电影";return "自定参数";}
  static bool SameScene(SceneValues a,SceneValues b){return a!=null&&b!=null&&a.Brightness==b.Brightness&&a.Contrast==b.Contrast&&a.Color==b.Color&&a.Sharpness==b.Sharpness;}
  void SyncScene(DisplayState state){var values=SceneOf(state);string name=MatchScene(values);if(name=="自定参数"&&SameScene(values,savedScene))name="我的配方";Get<TextBlock>("CurrentSceneName").Text=name;Get<TextBlock>("ActiveSceneText").Text=values==null?"屏幕参数未确认":"亮度 "+values.Brightness+" · 对比 "+values.Contrast+" · 锐度 "+values.Sharpness;string selected=name=="标准"?"SceneStandard":name=="办公"?"SceneOffice":name=="夜读"?"SceneNight":name=="FPS"?"SceneFps":name=="电影"?"SceneCinema":name=="我的配方"?"SceneSaved":null;foreach(var pair in sceneBrushes){pair.Key.Tag=pair.Key.Name==selected?"selected":null;var quick=Get<Button>(pair.Key.Name.Replace("Scene","Quick"));if(quick!=null)quick.Tag=pair.Key.Name==selected?"selected":null;}Store.Json("scene-state.json",new SceneState{Scene=name,Values=values,Confirmed=values!=null});}
  void RefreshGpu(){if(gpuBusy)return;gpuBusy=true;Get<Button>("RefreshGpuButton").IsEnabled=false;Queue(delegate{var info=GpuStatus.Read();Store.Json("gpu-state.json",info);Ui(delegate{gpuBusy=false;Get<Button>("RefreshGpuButton").IsEnabled=true;Get<TextBlock>("FreeSyncStatus").Text=info.FreeSyncStatus;Get<TextBlock>("FreeSyncDetail").Text=info.FreeSyncDetail;Get<TextBlock>("HdrStatus").Text=info.HdrStatus;Get<TextBlock>("HdrDetail").Text=info.HdrDetail;Get<TextBlock>("SignalMode").Text=info.Mode;});});}
 }
 [DataContract]public class SceneState {[DataMember]public string Scene;[DataMember]public SceneValues Values;[DataMember]public bool Confirmed;}
 [DataContract]public class ExtraTestResult {[DataMember]public bool ColorPresetPass;[DataMember]public bool SharpnessPass;[DataMember]public bool Restored;[DataMember]public string Error;[DataMember]public DisplayState After;}
 public static class ExperienceTests {
  public static int Run(){using(var monitor=new MonitorDevice()){monitor.Connect();var savedColor=monitor.Read(0x14);var savedSharp=monitor.Read(0x87);var brightness=monitor.Read(0x10);Store.Json("extras-original.json",monitor.State);bool colorPass=false,sharpPass=false,restored=false;string error=null;
   try{if(!savedColor.Supported||!savedSharp.Supported)throw new InvalidOperationException("Extra controls not readable");monitor.SetDiscrete(0x14,savedColor.Current==4?5u:4u);colorPass=true;monitor.SetDiscrete(0x14,savedColor.Current);monitor.SetDiscrete(0x87,savedSharp.Current<4?savedSharp.Current+1:savedSharp.Current-1);sharpPass=true;}
   catch(Exception e){error=e.Message;}
   finally{try{if(savedColor.Supported)monitor.SetDiscrete(0x14,savedColor.Current);if(savedSharp.Supported)monitor.SetDiscrete(0x87,savedSharp.Current);var color=monitor.Read(0x14);var sharp=monitor.Read(0x87);var after=monitor.Read(0x10);restored=color.Supported&&sharp.Supported&&color.Current==savedColor.Current&&sharp.Current==savedSharp.Current&&after.Supported&&after.Current==brightness.Current;}catch(Exception e){error=(error??"")+" Restore: "+e.Message;}}
   Store.Json("extras-test-result.json",new ExtraTestResult{ColorPresetPass=colorPass,SharpnessPass=sharpPass,Restored=restored,Error=error,After=monitor.Connect()});return colorPass&&sharpPass&&restored&&error==null?0:1;
  }}
 }
}
