// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 shangshang21 and contributors
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Drawing=System.Drawing;
using Forms=System.Windows.Forms;

namespace MicroGlow {
 [DataContract]public class Snapshot {
  [DataMember]public DisplayState Display;
  [DataMember]public AudioState Audio;
  [DataMember]public string Timestamp=DateTime.Now.ToString("o");
 }
 [DataContract]public class TestResult {
  [DataMember]public Snapshot Before;
  [DataMember]public Snapshot After;
  [DataMember]public List<string> Passed=new List<string>();
  [DataMember]public string Error;
  [DataMember]public bool Restored;
 }
 public static class Program {
  public static uint ShowMessage=Native.RegisterWindowMessage("MicroGlowOSD.Show.20261020");
  public static uint ExitMessage=Native.RegisterWindowMessage("MicroGlowOSD.Exit.20261020");
  public static uint CaptureMessage=Native.RegisterWindowMessage("MicroGlowOSD.Capture.20261020");
  [STAThread]public static int Main(string[] arguments) {
   try {
    if(Array.IndexOf(arguments,"--layout-check")>=0){try{using(var reader=File.OpenRead(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MainWindow.xaml")))XamlReader.Load(reader);Store.Log("layout-check PASS");return 0;}catch(Exception layout){Store.Log("layout-check FAIL "+layout);return 1;}}
    if(Array.IndexOf(arguments,"--quit")>=0){Native.PostMessage(new IntPtr(0xffff),ExitMessage,IntPtr.Zero,IntPtr.Zero);return 0;}
    if(Array.IndexOf(arguments,"--snapshot")>=0){Native.PostMessage(new IntPtr(0xffff),CaptureMessage,IntPtr.Zero,IntPtr.Zero);return 0;}
    if(Array.IndexOf(arguments,"--probe")>=0){using(var m=new MonitorDevice()){Store.Json("probe.json",new Snapshot{Display=m.Connect(),Audio=Audio.Access(null,null)});}return 0;}
    if(Array.IndexOf(arguments,"--self-test")>=0)return SelfTest();
    if(Array.IndexOf(arguments,"--comprehensive-test")>=0)return Regression.Run();
    if(Array.IndexOf(arguments,"--dimming-test")>=0){Store.Log("public-build: optical toggle not accepted; no E8 write");return 2;}
    if(Array.IndexOf(arguments,"--extras-test")>=0)return ExperienceTests.Run();
    bool created;using(var instance=new Mutex(true,"Local\\MicroGlowOSD.Desktop",out created)) {
     if(!created){Native.PostMessage(new IntPtr(0xffff),ShowMessage,IntPtr.Zero,IntPtr.Zero);return 0;}
     var app=new Application();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
     app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e){Store.Log("ui-error "+e.Exception);e.Handled=true;};
     var controller=new Controller(app);
     app.Exit+=delegate{controller.Dispose();};
     if(Array.IndexOf(arguments,"--tray")<0)controller.Open();
     app.Run();instance.ReleaseMutex();return 0;
    }
   }catch(Exception e){Store.Log("fatal "+e);MessageBox.Show(e.Message,"微光启动失败",MessageBoxButton.OK,MessageBoxImage.Information);return 1;}
  }
  static Snapshot Capture(MonitorDevice m){return new Snapshot{Display=m.State,Audio=Audio.Access(null,null)};}
  static int Percent(Feature f){return f.Supported?(int)Math.Round(f.Current*100.0/f.Maximum):0;}
  static int SelfTest(){
   var result=new TestResult();using(var m=new MonitorDevice()) {
    m.Connect();result.Before=Capture(m);Store.Json("self-test-original.json",result.Before);
    var brightness=m.Find(0x10);var contrast=m.Find(0x12);var audio=result.Before.Audio;
    try {
     if(!m.State.Connected)throw new InvalidOperationException("目标便携屏未连接");
     int b=Percent(brightness);m.Set(0x10,b<100?b+1:b-1);result.Passed.Add("brightness-write-readback");m.Set(0x10,b);
     if(contrast.Supported){int c=Percent(contrast);m.Set(0x12,c<100?c+1:c-1);result.Passed.Add("contrast-write-readback");m.Set(0x12,c);}
     foreach(byte code in new byte[]{0x62,0x16,0x18,0x1a}){var feature=m.Find(code);if(!feature.Supported)continue;int saved=Percent(feature);try{m.Set(code,saved<100?saved+1:saved-1);result.Passed.Add("vcp-"+code.ToString("X2")+"-write-readback");}finally{m.Set(code,saved);}}
     if(audio.Available){int v=audio.Volume<100?audio.Volume+1:audio.Volume-1;var after=Audio.Access(v,null);if(!after.Available||after.Volume!=v)throw new InvalidOperationException("系统音量读回失败");result.Passed.Add("audio-write-readback");Audio.Access(audio.Volume,null);}
    }catch(Exception e){result.Error=e.Message;}
    finally {
     try{if(brightness.Supported)m.Set(0x10,Percent(brightness));if(contrast.Supported)m.Set(0x12,Percent(contrast));if(audio.Available)Audio.Access(audio.Volume,audio.Muted);
      var reread=m.Read(0x10);result.After=Capture(m);result.Restored=reread.Supported&&reread.Current==brightness.Current&&(!audio.Available||result.After.Audio.Volume==audio.Volume&&result.After.Audio.Muted==audio.Muted);
     }catch(Exception e){result.Error=(result.Error??"")+" Restore: "+e.Message;}
     Store.Json("self-test-result.json",result);
    }
   }return result.Error==null&&result.Restored?0:1;
  }
 }
 public sealed partial class Controller:IDisposable {
  readonly Application app;readonly Window window;readonly MonitorDevice monitor=new MonitorDevice();
  readonly BlockingCollection<Action> work=new BlockingCollection<Action>();readonly Thread worker;
  readonly Dictionary<byte,int> versions=new Dictionary<byte,int>();readonly object versionsLock=new object();
  readonly Dictionary<byte,Slider> sliders=new Dictionary<byte,Slider>();readonly Dictionary<byte,TextBlock> numbers=new Dictionary<byte,TextBlock>();
  readonly Dictionary<byte,DispatcherTimer> pending=new Dictionary<byte,DispatcherTimer>();readonly Dictionary<byte,bool> supported=new Dictionary<byte,bool>();
  readonly Forms.NotifyIcon tray;readonly DispatcherTimer audioTimer,monitorTimer,volumeDelay,wakeTimer;readonly Toast toast;
  int lastAudioVolume=-1;AudioState audio=new AudioState();bool updating,exiting,connecting,connected,brightnessToast;DateTime lastInput=DateTime.MinValue;IntPtr hwnd;HwndSource source;
  T Get<T>(string name)where T:class{return window.FindName(name)as T;}
  public Controller(Application application) {
   app=application;Palette.Install(app.Resources);
   using(var reader=File.OpenRead(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MainWindow.xaml")))window=(Window)XamlReader.Load(reader);
   window.Width=Math.Min(1100,SystemParameters.WorkArea.Width-24);window.Height=Math.Min(700,SystemParameters.WorkArea.Height-24);window.MaxHeight=SystemParameters.WorkArea.Height-20;
   var iconFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MicroGlow.ico");if(File.Exists(iconFile))window.Icon=BitmapFrame.Create(new Uri(iconFile));
   window.Left=SystemParameters.WorkArea.Left+(SystemParameters.WorkArea.Width-window.Width)/2;window.Top=Math.Max(SystemParameters.WorkArea.Top+10,SystemParameters.WorkArea.Top+35);
   worker=new Thread(delegate(){foreach(var action in work.GetConsumingEnumerable())try{action();}catch(Exception e){Store.Log("worker-error "+e);Ui(delegate{sceneBusy=dimmingBusy=connecting=false;Get<Grid>("PageHost").IsEnabled=true;Disconnect("连接暂不可用，请点击重连");});}monitor.Dispose();});worker.Name="MicroGlow-DDC";worker.IsBackground=true;worker.Start();
   toast=new Toast();
   AddSlider(0x10,"BrightnessSlider","BrightnessNumber");AddSlider(0x12,"ContrastSlider","ContrastNumber");AddSlider(0x62,"HardwareVolumeSlider","HardwareVolumeNumber");AddSlider(0x16,"RedSlider","RedNumber");AddSlider(0x18,"GreenSlider","GreenNumber");AddSlider(0x1a,"BlueSlider","BlueNumber");
   Get<Button>("HideButton").Click+=delegate{window.Hide();};
   Get<Button>("PinButton").Click+=delegate{window.Topmost=!window.Topmost;Get<Button>("PinButton").Content=window.Topmost?"置顶：开":"置顶：关";};Get<Button>("PinButton").Content="置顶：开";
   Get<Grid>("DragHeader").MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e){if(e.OriginalSource is Button)return;try{window.DragMove();}catch{}};
   Get<Button>("ReconnectButton").Click+=delegate{Connect();};
   Get<Button>("NightButton").Click+=delegate{Brightness(8);};Get<Button>("DayButton").Click+=delegate{Brightness(35);};Get<Button>("BrightButton").Click+=delegate{Brightness(70);};
   Get<Button>("HelpButton").Click+=delegate{Navigate("settings");};
   Get<Button>("MuteButton").Click+=delegate{Mute();};
   volumeDelay=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(90)};volumeDelay.Tick+=delegate{volumeDelay.Stop();ApplyAudio((int)Math.Round(Get<Slider>("VolumeSlider").Value),null,false);};
   Get<Slider>("VolumeSlider").ValueChanged+=delegate{if(updating)return;Get<TextBlock>("VolumeNumber").Text=((int)Get<Slider>("VolumeSlider").Value).ToString("00");volumeDelay.Stop();volumeDelay.Start();};
   Get<Slider>("VolumeSlider").PreviewMouseWheel+=delegate(object s,MouseWheelEventArgs e){var slider=(Slider)s;slider.Value=Math.Max(0,Math.Min(100,slider.Value+(e.Delta>0?2:-2)));e.Handled=true;};
   var startup=Get<CheckBox>("StartupCheck");updating=true;using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))startup.IsChecked=key!=null&&key.GetValue("MicroGlowOSD")!=null;updating=false;
   RoutedEventHandler startupChanged=delegate{if(updating)return;try{using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(startup.IsChecked==true)key.SetValue("MicroGlowOSD","\""+Assembly.GetExecutingAssembly().Location+"\" --tray");else key.DeleteValue("MicroGlowOSD",false);}Status(startup.IsChecked==true?"已设置登录时启动":"已关闭登录时启动",false);}catch(Exception e){Status("启动设置未成功："+e.Message,true);}};startup.Checked+=startupChanged;startup.Unchecked+=startupChanged;
   window.Closing+=delegate(object s,CancelEventArgs e){if(!exiting){e.Cancel=true;window.Hide();}};
   window.PreviewKeyDown+=delegate(object s,KeyEventArgs e){if(e.Key==Key.Escape){window.Hide();e.Handled=true;}};
   window.SourceInitialized+=delegate{AttachHotkeys();};
   tray=new Forms.NotifyIcon();tray.Text="微光 · 屏幕与声音";tray.Icon=MakeIcon();tray.Visible=true;
   tray.MouseClick+=delegate(object s,Forms.MouseEventArgs e){if(e.Button==Forms.MouseButtons.Left)Ui(delegate{if(window.IsVisible)window.Hide();else Open();});};
   var menu=new Forms.ContextMenuStrip();menu.Items.Add("打开微光",null,delegate{Ui(Open);});menu.Items.Add("重新连接屏幕",null,delegate{Ui(Connect);});menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("退出",null,delegate{Ui(delegate{exiting=true;app.Shutdown();});});tray.ContextMenuStrip=menu;
   audioTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};audioTimer.Tick+=delegate{if(window.IsVisible&&!volumeDelay.IsEnabled)ReadAudio();};audioTimer.Start();
   monitorTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(18)};monitorTimer.Tick+=delegate{if(window.IsVisible&&!connecting&&!sceneBusy&&!dimmingBusy&&DateTime.Now-lastInput>TimeSpan.FromSeconds(3))Connect();};monitorTimer.Start();
   wakeTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};wakeTimer.Tick+=delegate{wakeTimer.Stop();if(window.IsVisible)Connect();};
   // Create the handle even for tray startup, so global hotkeys always exist.
   InitializeExperience();InitializeDimming();InitializeArtDirection();new WindowInteropHelper(window).EnsureHandle();AttachHotkeys();ReadAudio();Connect();
  }
  void AttachHotkeys(){if(source!=null)return;hwnd=new WindowInteropHelper(window).Handle;if(hwnd==IntPtr.Zero)return;source=HwndSource.FromHwnd(hwnd);source.AddHook(Hook);int fail=0;foreach(var key in new[]{new[]{1,0x4f},new[]{2,0x26},new[]{3,0x28},new[]{4,0x27},new[]{5,0x25},new[]{6,0x4d},new[]{7,0x4c}}){bool ok=Native.RegisterHotKey(hwnd,key[0],0x4007,(uint)key[1]);Store.Log("hotkey-register id="+key[0]+" success="+ok);if(!ok)fail++;}if(fail>0)Status("部分快捷键被占用，面板和托盘仍可使用",true);}
  void AddSlider(byte code,string name,string number){
   var slider=Get<Slider>(name);sliders[code]=slider;numbers[code]=Get<TextBlock>(number);supported[code]=false;versions[code]=0;
   var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};pending[code]=timer;
   timer.Tick+=delegate{timer.Stop();int version;lock(versionsLock)version=versions[code];int requested=(int)Math.Round(slider.Value);
    Queue(delegate{lock(versionsLock)if(version!=versions[code])return;try{var result=monitor.Set(code,requested);Ui(delegate{lock(versionsLock)if(version!=versions[code])return;SetFeature(result);SyncScene(monitor.State);if(code==0x10){Get<TextBlock>("BrightnessStatus").Text="已同步";if(brightnessToast){toast.Show("屏幕亮度",(int)Math.Round(result.Current*100.0/result.Maximum),"已同步 · 便携屏");brightnessToast=false;}}Status("设置已确认",false);});}catch(Exception e){Ui(delegate{lock(versionsLock)if(version!=versions[code])return;numbers[code].Text="—";slider.IsEnabled=false;supported[code]=false;if(code==0x10)Get<TextBlock>("BrightnessStatus").Text="未确认";Status(e.Message,true);});}});
   };
   slider.ValueChanged+=delegate{if(updating)return;lastInput=DateTime.Now;lock(versionsLock)versions[code]++;numbers[code].Text=((int)Math.Round(slider.Value)).ToString(code==0x10?"00":"0");if(code==0x10)Get<TextBlock>("BrightnessStatus").Text="正在调整…";timer.Stop();timer.Start();};
   slider.PreviewMouseWheel+=delegate(object s,MouseWheelEventArgs e){if(!slider.IsEnabled)return;slider.Value=Math.Max(0,Math.Min(100,slider.Value+(e.Delta>0?2:-2)));e.Handled=true;};
  }
  void Ui(Action action){if(!app.Dispatcher.HasShutdownStarted)app.Dispatcher.BeginInvoke(action);}
  void Queue(Action action){if(!work.IsAddingCompleted)try{work.Add(action);}catch(InvalidOperationException){}}
  void Connect(){if(connecting||sceneBusy||dimmingBusy)return;connecting=true;Get<Button>("ReconnectButton").Content="连接中";Get<TextBlock>("BrightnessStatus").Text="读取中";DateTime started=DateTime.Now;
   Queue(delegate{var state=monitor.Connect();Store.Json("display-state.json",state);Ui(delegate{connecting=false;if(lastInput>started){Get<Button>("ReconnectButton").Content=connected?"已连接":"重连";Store.Log("monitor-snapshot-skipped-new-input");return;}connected=state.Connected;if(!connected){Disconnect(state.Error);return;}Get<TextBlock>("DisplayName").Text=state.Name;Get<TextBlock>("DisplayDetails").Text=state.Width+" × "+state.Height+"  ·  BOE NM4";Get<TextBlock>("DisplayDetails").ToolTip="面板：BOE NE160QDM-NM4 V8.5（拆机标签核实）";Get<Button>("ReconnectButton").Content="已连接";foreach(var feature in state.Features)SetFeature(feature);PaintExtras(state);RefreshDimming();Get<TextBlock>("BrightnessStatus").Text="已同步";Get<StackPanel>("HardwareVolumePanel").Visibility=supported[0x62]?Visibility.Visible:Visibility.Collapsed;Get<StackPanel>("RgbPanel").Visibility=supported[0x16]&&supported[0x18]&&supported[0x1a]?Visibility.Visible:Visibility.Collapsed;Status("Ctrl + Shift + Alt + O，随时唤出微光",false);});});
  }
  void Disconnect(string message){connected=false;PaintExtras(new DisplayState());Get<RollingDigits>("BrightnessDigits").SetValue(null,false);PaintDimming(new DimmingState{Error="小太阳未连接"});Get<TextBlock>("SideConnection").Text="未连接";Get<ComboBox>("ColorPresetCombo").IsEnabled=false;Get<ComboBox>("SharpnessCombo").IsEnabled=false;Get<TextBlock>("DisplayName").Text="便携屏未连接";Get<TextBlock>("DisplayDetails").Text="检查连接后，点击右侧重连";Get<Button>("ReconnectButton").Content="重连";Get<TextBlock>("BrightnessStatus").Text="未连接";foreach(byte code in sliders.Keys){supported[code]=false;sliders[code].IsEnabled=false;numbers[code].Text="—";}Status(message??"连接暂不可用",true);}
  void SetFeature(Feature value){byte code=(byte)value.Code;if(!sliders.ContainsKey(code))return;supported[code]=value.Supported;updating=true;try{sliders[code].IsEnabled=value.Supported;if(value.Supported){int percent=(int)Math.Round(value.Current*100.0/value.Maximum);sliders[code].Value=percent;numbers[code].Text=percent.ToString(code==0x10?"00":"0");}else numbers[code].Text="—";}finally{updating=false;}if(code==0x10)UpdateArtBrightness();}
  void Status(string message,bool error){Get<TextBlock>("FooterStatus").Text=message;Get<TextBlock>("FooterStatus").SetResourceReference(TextBlock.ForegroundProperty,error?"Brush.AccentInk":"Brush.Muted");}
  void ReadAudio(){audio=Audio.Access(null,null);PaintAudio();}
  void PaintAudio(){updating=true;try{Get<Slider>("VolumeSlider").IsEnabled=audio.Available;Get<Button>("MuteButton").IsEnabled=audio.Available;Get<TextBlock>("VolumeNumber").Text=audio.Available?audio.Volume.ToString("00"):"—";Get<RollingDigits>("VolumeDigits").SetValue(audio.Available?(int?)audio.Volume:null,MotionEnabled&&audio.Available&&lastAudioVolume>=0&&lastAudioVolume!=audio.Volume);lastAudioVolume=audio.Available?audio.Volume:-1;Get<TextBlock>("AudioName").Text=audio.Available?audio.Name:"未找到播放设备";Get<TextBlock>("AudioName").ToolTip=audio.Available?audio.Name:audio.Error;Get<TextBlock>("MuteState").Text=audio.Muted?"已静音":"";Get<Button>("MuteButton").Content=audio.Muted?"恢复声音":"静音";if(audio.Available)Get<Slider>("VolumeSlider").Value=audio.Volume;}finally{updating=false;}}
  void ApplyAudio(int? value,bool? mute,bool feedback){audio=Audio.Access(value,mute,audio.DeviceId);PaintAudio();if(!audio.Available){Status("播放设备暂不可用",true);return;}if(feedback)toast.Show("系统音量",audio.Volume,audio.Muted?"已静音":audio.Name);}
  void Mute(){ReadAudio();if(audio.Available)ApplyAudio(null,!audio.Muted,true);}
  void Brightness(int value){if(!supported[0x10]){Status("请先连接便携屏",true);return;}sliders[0x10].Value=Math.Max(0,Math.Min(100,value));}
  IntPtr Hook(IntPtr handle,int message,IntPtr w,IntPtr l,ref bool handled){
   if((uint)message==Program.ExitMessage){exiting=true;app.Shutdown();handled=true;return IntPtr.Zero;}
   if((uint)message==Program.CaptureMessage){if(window.IsVisible){window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*2),(int)Math.Ceiling(window.ActualHeight*2),192,192,PixelFormats.Pbgra32);bitmap.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory(Store.DirectoryPath);using(var file=File.Create(Path.Combine(Store.DirectoryPath,"preview.png")))png.Save(file);}handled=true;return IntPtr.Zero;}
   if((uint)message==Program.ShowMessage){Open();handled=true;}
   if(message==0x0312){Store.Log("hotkey "+w.ToInt32());handled=true;int id=w.ToInt32();if((sceneBusy||dimmingBusy)&&id!=1){Status("正在确认控制状态，请稍候",false);return IntPtr.Zero;}if(id==1){if(window.IsVisible)window.Hide();else Open();}else if(id==2||id==3){if(!supported[0x10]){Open();return IntPtr.Zero;}int value=(int)sliders[0x10].Value+(id==2?5:-5);brightnessToast=true;Brightness(value);toast.Show("屏幕亮度",(int)sliders[0x10].Value,"正在同步…");}else if(id==4||id==5){ReadAudio();if(audio.Available)ApplyAudio(audio.Volume+(id==4?5:-5),null,true);}else if(id==6)Mute();else if(id==7)ToggleManualDimming();}
   return IntPtr.Zero;
  }
  public void Open(){Native.SetThreadExecutionState(2);if(wakeTimer!=null){wakeTimer.Stop();wakeTimer.Start();}if(!window.IsVisible){window.Opacity=0;window.Show();window.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(MotionEnabled?700:0)));}Native.ShowWindow(new WindowInteropHelper(window).Handle,5);window.Activate();Native.SetForegroundWindow(new WindowInteropHelper(window).Handle);ReadAudio();}
  public void Dispose(){exiting=true;if(themeTimer!=null)themeTimer.Stop();if(dimmingPattern!=null)dimmingPattern.Close();audioTimer.Stop();monitorTimer.Stop();wakeTimer.Stop();volumeDelay.Stop();foreach(var timer in pending.Values)timer.Stop();for(int id=1;id<=7;id++)Native.UnregisterHotKey(hwnd,id);tray.Visible=false;tray.Dispose();toast.Dispose();work.CompleteAdding();worker.Join(1000);}
  static Drawing.Icon MakeIcon(){using(var bmp=new Drawing.Bitmap(32,32)){using(var g=Drawing.Graphics.FromImage(bmp)){g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.Clear(Drawing.Color.Transparent);using(var bg=new Drawing.SolidBrush(Drawing.Color.FromArgb(221,233,214)))g.FillEllipse(bg,1,1,30,30);using(var pen=new Drawing.Pen(Drawing.Color.FromArgb(70,101,73),2)){g.DrawEllipse(pen,10,10,12,12);for(int i=0;i<8;i++){double a=i*Math.PI/4;g.DrawLine(pen,(float)(16+9*Math.Cos(a)),(float)(16+9*Math.Sin(a)),(float)(16+12*Math.Cos(a)),(float)(16+12*Math.Sin(a)));}}}var native=bmp.GetHicon();var icon=(Drawing.Icon)Drawing.Icon.FromHandle(native).Clone();Native.DestroyIcon(native);return icon;}}
 }
 public sealed class Toast:IDisposable {
  readonly Window window;readonly TextBlock title,value,detail;readonly ProgressBar bar;readonly DispatcherTimer timer;public bool Motion=true;bool hiding;
  public Toast(){window=new Window{Width=340,Height=112,WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Topmost=true,IsHitTestVisible=false};
   var card=new Border{Margin=new Thickness(10),CornerRadius=new CornerRadius(16),Background=Palette.Brush("Surface"),Padding=new Thickness(18,11,18,11)};card.Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=15,ShadowDepth=3,Opacity=.2};
   var stack=new StackPanel();var row=new Grid();title=new TextBlock{FontSize=13,Foreground=Palette.Brush("Ink"),VerticalAlignment=VerticalAlignment.Center};value=new TextBlock{FontSize=23,FontFamily=new FontFamily("Segoe UI"),Foreground=Palette.Brush("Ink"),HorizontalAlignment=HorizontalAlignment.Right};row.Children.Add(title);row.Children.Add(value);stack.Children.Add(row);bar=new ProgressBar{Height=5,Minimum=0,Maximum=100,BorderThickness=new Thickness(0),Margin=new Thickness(0,9,0,6),Foreground=Palette.Brush("Accent"),Background=Palette.Brush("Chip")};stack.Children.Add(bar);detail=new TextBlock{FontSize=11,Foreground=Palette.Brush("Muted"),TextTrimming=TextTrimming.CharacterEllipsis};stack.Children.Add(detail);card.Child=stack;window.Content=card;timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1.3)};timer.Tick+=delegate{timer.Stop();if(Motion){hiding=true;var fade=new DoubleAnimation(0,TimeSpan.FromMilliseconds(350));fade.Completed+=delegate{if(hiding)window.Hide();};window.BeginAnimation(UIElement.OpacityProperty,fade);}else window.Hide();};}
  public void Show(string name,int percent,string description){title.Text=name;value.Text=Math.Max(0,Math.Min(100,percent))+"%";detail.Text=description;bar.Value=percent;window.Left=SystemParameters.WorkArea.Left+(SystemParameters.WorkArea.Width-window.Width)/2;window.Top=SystemParameters.WorkArea.Bottom-window.Height-28;hiding=false;if(!window.IsVisible){window.Opacity=1;window.Show();if(Motion)window.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(450)){EasingFunction=new BackEase{EasingMode=EasingMode.EaseOut,Amplitude=.3}});}else if(window.HasAnimatedProperties){window.BeginAnimation(UIElement.OpacityProperty,null);window.Opacity=1;}timer.Stop();timer.Start();}
  public void Dispose(){timer.Stop();window.Close();}
 }
}
