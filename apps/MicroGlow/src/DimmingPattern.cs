// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 shangshang21 and contributors
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MicroGlow {
 [DataContract]public class PatternState {
  [DataMember]public string Pattern;
  [DataMember]public bool Moving;
  [DataMember]public double X;
  [DataMember]public double Y;
  [DataMember]public long Frames;
  [DataMember]public int CheckerPhase;
  [DataMember]public bool DisplayAwakeLock;
  [DataMember]public double RenderEventsPerSecond;
  [DataMember]public string Timestamp=DateTime.Now.ToString("o");
 }
 public sealed class DimmingPattern:Window {
  readonly Canvas canvas;readonly Rectangle block;readonly List<Rectangle> checkers=new List<Rectangle>();readonly TextBlock label;readonly Border help;readonly DispatcherTimer hide;
  readonly Stopwatch watch=new Stopwatch();bool moving=true;int mode;double x,y,lastTelemetry;long frames;uint previousExecutionState;
  [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
  public DimmingPattern(){Title="微光 · 动态背光测试";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;Background=Brushes.Black;Topmost=true;ShowInTaskbar=true;
   canvas=new Canvas{Background=Brushes.Black};for(int i=0;i<48;i++){var square=new Rectangle{Fill=Brushes.White,Visibility=Visibility.Collapsed,IsHitTestVisible=false};checkers.Add(square);canvas.Children.Add(square);}block=new Rectangle{Width=110,Height=110,Fill=Brushes.White,IsHitTestVisible=false};canvas.Children.Add(block);
   label=new TextBlock{Foreground=Brushes.LightGray,FontSize=13,TextWrapping=TextWrapping.Wrap};help=new Border{Background=new SolidColorBrush(Color.FromArgb(210,18,18,18)),CornerRadius=new CornerRadius(10),Padding=new Thickness(16),Child=label,MaxWidth=850};Canvas.SetLeft(help,24);Canvas.SetTop(help,24);canvas.Children.Add(help);Content=canvas;
   hide=new DispatcherTimer{Interval=TimeSpan.FromSeconds(4)};hide.Tick+=delegate{hide.Stop();help.Visibility=Visibility.Collapsed;};
   Loaded+=delegate{previousExecutionState=Native.SetThreadExecutionState(0x80000002);watch.Start();CompositionTarget.Rendering+=Render;ShowStatus("白块自动移动 · Space 暂停 · 点击定位 · Tab 切图案 · Esc 返回");};SizeChanged+=delegate{LayoutChecker();};
   MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){Point point=e.GetPosition(canvas);moving=false;x=Math.Max(0,Math.Min(canvas.ActualWidth-block.Width,point.X-block.Width/2));y=Math.Max(0,Math.Min(canvas.ActualHeight-block.Height,point.Y-block.Height/2));PlaceBlock();ShowStatus("已暂停并定位 · Space 继续移动 · Tab 切图案 · Esc 返回");};
   PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){Close();e.Handled=true;}else if(e.Key==Key.Space){moving=!moving;ShowStatus(moving?"自动移动中 · Space 暂停":"已暂停 · Space 继续");e.Handled=true;}else if(e.Key==Key.Tab){mode=(mode+1)%3;LayoutChecker();ShowStatus(new[]{"移动白块","交替棋盘","移动细条"}[mode]+" · Space 暂停 · Tab 切图案 · Esc 返回");e.Handled=true;}};
   Closed+=delegate{CompositionTarget.Rendering-=Render;watch.Stop();hide.Stop();if(previousExecutionState!=0){Native.SetThreadExecutionState(previousExecutionState|0x80000000);previousExecutionState=0;}WriteTelemetry();};
  }
  void LayoutChecker(){double w=canvas.ActualWidth/8,h=canvas.ActualHeight/6;for(int i=0;i<checkers.Count;i++){checkers[i].Width=w+1;checkers[i].Height=h+1;Canvas.SetLeft(checkers[i],i%8*w);Canvas.SetTop(checkers[i],i/8*h);checkers[i].Opacity=((i%8+i/8)%2)==0?1:0;checkers[i].Visibility=mode==1?Visibility.Visible:Visibility.Collapsed;}block.Visibility=mode==1?Visibility.Collapsed:Visibility.Visible;block.Width=mode==2?32:110;block.Height=mode==2?Math.Max(110,canvas.ActualHeight*.6):110;x=Math.Max(0,Math.Min(x,canvas.ActualWidth-block.Width));y=Math.Max(0,Math.Min(y,canvas.ActualHeight-block.Height));PlaceBlock();}
  void PlaceBlock(){Canvas.SetLeft(block,x);Canvas.SetTop(block,y);}
  void Render(object sender,EventArgs e){frames++;double t=watch.Elapsed.TotalSeconds;if(moving){if(mode==1){int phase=(int)(t/1.5)%2;for(int i=0;i<checkers.Count;i++)checkers[i].Opacity=((i%8+i/8+phase)%2)==0?1:0;}else{x=(Math.Sin(t*Math.PI/5)+1)*.5*Math.Max(0,canvas.ActualWidth-block.Width);y=(Math.Cos(t*Math.PI/7)+1)*.5*Math.Max(0,canvas.ActualHeight-block.Height);PlaceBlock();}}if(t-lastTelemetry>=1){lastTelemetry=t;WriteTelemetry();}}
  void WriteTelemetry(){try{Store.Json("test-pattern-state.json",new PatternState{Pattern=new[]{"moving-square","alternating-checker","moving-bar"}[mode],Moving=moving,DisplayAwakeLock=previousExecutionState!=0,CheckerPhase=checkers[0].Opacity>0?0:1,X=Math.Round(x,2),Y=Math.Round(y,2),Frames=frames,RenderEventsPerSecond=watch.Elapsed.TotalSeconds>0?Math.Round(frames/watch.Elapsed.TotalSeconds,2):0});}catch(Exception ex){Store.Log("pattern-telemetry "+ex.Message);}}
  public void ShowStatus(string message){label.Text=message;help.Visibility=Visibility.Visible;hide.Stop();hide.Start();}
  public void OpenOn(DisplayState state){System.Windows.Forms.Screen target=null;foreach(var screen in System.Windows.Forms.Screen.AllScreens)if(screen.DeviceName==state.DeviceName)target=screen;if(target==null)throw new InvalidOperationException("屏幕位置已改变，请重连后再试");var bounds=target.Bounds;Show();if(!SetWindowPos(new WindowInteropHelper(this).Handle,new IntPtr(-1),bounds.X,bounds.Y,bounds.Width,bounds.Height,0x0040))throw new InvalidOperationException("无法定位测试窗口");LayoutChecker();Activate();}
 }
}
