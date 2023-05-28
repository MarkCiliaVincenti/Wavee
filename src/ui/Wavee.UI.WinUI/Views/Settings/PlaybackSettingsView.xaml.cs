using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Wavee.UI.Infrastructure.Live;
using Wavee.UI.ViewModels;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Wavee.UI.WinUI.Views.Settings
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class PlaybackSettingsView : Page
    {
        public PlaybackSettingsView()
        {
            this.InitializeComponent();
        }
        public SettingsViewModel<WaveeUIRuntime> ViewModel => SettingsViewModel<WaveeUIRuntime>.Instance;

        public string GetCrossfadeStr(int i)
        {
            //0 seconds (off)
            return i switch
            {
                0 => "off",
                1 => "1 second",
                _ => $"{i} seconds",
            };
        }
    }
}