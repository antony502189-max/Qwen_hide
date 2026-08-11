using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace QwenWorkOverlay;
public sealed class SettingsWindow : Window
{
    public SettingsWindow(SettingsService service, AudioDeviceService devices)
    {
        Title="Qwen Work Overlay Settings";Width=520;Height=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel { Margin=new Thickness(16) }; Content=panel; var s=service.Current;
        var opacity=new Slider{Minimum=.35,Maximum=1,Value=s.Opacity,TickFrequency=.05,IsSnapToTickEnabled=true}; var mic=new ComboBox{ItemsSource=devices.Inputs(),DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=s.MicrophoneDeviceId}; var loop=new ComboBox{ItemsSource=devices.Outputs(),DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=s.LoopbackDeviceId}; var virtualMix=new ComboBox{ItemsSource=devices.Outputs(),DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=s.VirtualMixOutputDeviceId};
        Add(panel,"Opacity",opacity); Add(panel,"Physical microphone",mic); Add(panel,"System playback/loopback",loop); Add(panel,"Virtual mix output (optional; virtual cable only)",virtualMix);
        var top=new CheckBox{Content="Always on top",IsChecked=s.TopMost};var privacy=new CheckBox{Content="Capture protection",IsChecked=s.CaptureProtection};var right=new CheckBox{Content="Right Ctrl enables capture session",IsChecked=s.RightCtrlAudioEnabled}; panel.Children.Add(top);panel.Children.Add(privacy);panel.Children.Add(right);
        var gains=new StackPanel{Orientation=Orientation.Horizontal};var mg=new TextBox{Text=s.MicGain.ToString("0.00"),Width=70};var sg=new TextBox{Text=s.SystemGain.ToString("0.00"),Width=70};gains.Children.Add(new TextBlock{Text="Mic gain",Width=90});gains.Children.Add(mg);gains.Children.Add(new TextBlock{Text="System gain",Width=100,Margin=new Thickness(12,0,0,0)});gains.Children.Add(sg);panel.Children.Add(gains);
        var save=new Button{Content="Save",Width=80,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};save.Click+=(_,_)=>{s.Opacity=opacity.Value;s.MicrophoneDeviceId=mic.SelectedValue as string;s.LoopbackDeviceId=loop.SelectedValue as string;s.VirtualMixOutputDeviceId=virtualMix.SelectedValue as string;s.TopMost=top.IsChecked==true;s.CaptureProtection=privacy.IsChecked==true;s.RightCtrlAudioEnabled=right.IsChecked==true;float.TryParse(mg.Text,out var x);float.TryParse(sg.Text,out var y);s.MicGain=x>0?x:1;s.SystemGain=y>0?y:1;service.Save();Close();};panel.Children.Add(save);
    }
    static void Add(System.Windows.Controls.Panel p,string label,FrameworkElement control){p.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,9,0,2)});p.Children.Add(control);}
}
