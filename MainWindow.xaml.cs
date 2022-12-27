using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using Windows.ApplicationModel.DataTransfer;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Windows.ApplicationModel.Core;
using Windows.UI.ViewManagement;

namespace TwitchChatFrequencyMapper
{
    public class TwitchMessage : IComparable<TwitchMessage>, INotifyPropertyChanged
    {
        private int _frequency { get; set; }
        public string Message { get; set; }
        public int Frequency
        {
            get
            {
                return _frequency;
            }
            set
            {
                _frequency = value;
                NotifyPropertyChanged(propertyName: nameof(Frequency));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public TwitchMessage(){}

        public int CompareTo(TwitchMessage other)
        {
            int compare = other.Frequency.CompareTo(Frequency);
            if (compare == 0)
            {
                return Message.CompareTo(other.Message);
            }

            return compare;
        }

        private void NotifyPropertyChanged(String propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    public sealed partial class MainWindow : Window
    {
        private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        private readonly TwitchClient _client = new();
        private readonly Dictionary<string, TwitchMessage> _frequencyDict = new();
        private readonly ObservableCollection<TwitchMessage> _filteredListViewElements = new();
        private string _streamerName = " ";

        private AppWindow _apw;
        private OverlappedPresenter _presenter;

        public void GetAppWindowAndPresenter()
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId myWndId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _apw = AppWindow.GetFromWindowId(myWndId);
            _presenter = _apw.Presenter as OverlappedPresenter;
        }

        public MainWindow()
        {
            this.InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            GetAppWindowAndPresenter();
            _presenter.IsResizable = false;

            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindowName);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            appWindow.Resize(new Windows.Graphics.SizeInt32(400, 600));

            SetupTwitchClient();
        }

        // -----------------------------------------------------
        // Twitch

        private void SetupTwitchClient()
        {
            var creds = new ConnectionCredentials("justinfan1234567", "");
            _client.Initialize(creds, _streamerName);

            _client.OnConnected += Client_OnConnected;
            _client.OnMessageReceived += Client_OnMessageReceived;
            _client.Connect();
        }
        private void Client_OnConnected(object sender, OnConnectedArgs e)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                BottomInfoBar.Title = "Connected!";
                BottomInfoBar.Content = "";
                BottomInfoBar.Severity = InfoBarSeverity.Success;
                HideBarAfterDelay(3, BottomInfoBar);
            });

            _client.JoinChannel(_streamerName);
        }
        private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {

            var msg = e.ChatMessage.Message.ToLower().Trim();
            msg = Regex.Replace(msg, @"\s+", " ");
            msg = Regex.Replace(msg, @"\u0020\udb40\udc00+", "");
            
            
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (PausePlayButton.IsChecked == true) return;
                
                if (_frequencyDict.ContainsKey(msg))
                {
                    // remove from view, update freq, then binary search insert back in.
                    _filteredListViewElements.Remove(_frequencyDict[msg]);

                    ++_frequencyDict[msg].Frequency;
                }
                else
                {
                    _frequencyDict[msg] = new() { Frequency = 1, Message = msg };
                }

                if (msg.Contains(Filter.Text))
                {
                    InsertOrdered(_filteredListViewElements, _frequencyDict[msg]);
                }
            });
        }

        // -----------------------------------------------------
        private async void HideBarAfterDelay(int seconds, InfoBar infoBar)
        {
            await Task.Delay(seconds * 1000);
            infoBar.IsOpen= false;
        }

        private void OnFilterChanged(object sender, TextChangedEventArgs args)
        {
            // removing elements that dont fit
            foreach (var item in _filteredListViewElements.ToList())
            {
                if (!item.Message.Contains(Filter.Text))
                {
                    _filteredListViewElements.Remove(item);
                }
            }

            // adding to filter
            foreach (var item in _frequencyDict)
            {
                if (item.Key.Contains(Filter.Text) && !_filteredListViewElements.Contains(item.Value))
                {
                    InsertOrdered(_filteredListViewElements, item.Value);
                    //filteredListViewElements.Add(item.Value);
                }
            }
        }

        private void InsertOrdered(ObservableCollection<TwitchMessage> twitchMessages, TwitchMessage messageToInsert)
        {
            int index = twitchMessages.ToList().BinarySearch(messageToInsert, Comparer<TwitchMessage>.Create((x, y) =>
            {
                return x.CompareTo(y);
            }));

            if (index < 0)
            {
                twitchMessages.Insert(~index, messageToInsert);
            } else
            {
                twitchMessages.Insert(index, messageToInsert);
            }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_client.IsConnected)
            {
                _client.Connect();
                return;
            }

            if (_client.JoinedChannels.Count != 0)
                _client.LeaveChannel(_streamerName);

            _streamerName = ChannelName.Text;

            if (_streamerName.Length > 0 && _client.IsConnected )
                _client.JoinChannel(_streamerName);
            
            ClearButton_Click(sender, e);
        }

        private void ChannelName_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
                ConfirmButton_Click((object)sender, e);
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _frequencyDict.Clear();
            _filteredListViewElements.Clear();
        }

        private void PausePlayButton_Click(object sender, RoutedEventArgs e)
        {
            //Shorthand name for the button
            var btn = PausePlayButton;

            //Set proper name on button
            btn.Content = (bool)btn.IsChecked ? "Play" : "Pause";
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = ((FrameworkElement)sender).DataContext;  
            var twitchMessage = selectedItem as TwitchMessage;
            Debug.WriteLine(twitchMessage);
            if (twitchMessage != null)
            {
                DataPackage dataPackage = new()
                {
                    RequestedOperation = DataPackageOperation.Copy
                };
                dataPackage.SetText(twitchMessage.Message);
                Clipboard.SetContent(dataPackage);
            }
        }
    }
}
