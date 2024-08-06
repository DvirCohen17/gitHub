using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using static client_side.HomePage;

namespace client_side
{
    public class MultiVisibilityConverter2 : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 &&
                values[0] is bool type &&
                values[1] is bool isRead)
            {
                // Example logic: Button is visible if Type is true and IsRead is false
                return (!type && !isRead) ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed; // Default fallback
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MultiVisibilityConverter1 : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 &&
                values[0] is bool type &&
                values[1] is bool isRead)
            {
                // Example logic: Button is visible if Type is true and IsRead is false
                return (type && !isRead) ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed; // Default fallback
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }


    public partial class MessagesWindow : Window, INotifyPropertyChanged
    {
        private Communicator communicator;

        public ObservableCollection<Message> Messages { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;

        public MessagesWindow(Communicator communicator)
        {
            InitializeComponent();
            Style = (Style)FindResource(typeof(Window)); // Specified full namespace for Window
            this.communicator = communicator;
            DataContext = this;

            communicator.ApplyTheme(this);

            Messages = new ObservableCollection<Message>();
            MessagesListView.ItemsSource = Messages;

            HandleLoadMessagesList();
        }

        private void HandleLoadMessagesList()
        {
            string messageCode = ((int)MessageCodes.MC_GET_MESSAGES_REQUEST).ToString();
            communicator.SendData($"{messageCode}{communicator.UserName.Length:D5}{communicator.UserName}");

            string update = communicator.ReceiveData();
            string messageRepCode = update.Substring(0, 3);

            if (messageRepCode == ((int)MessageCodes.MC_HEARTBEAT_REQUEST).ToString())
            {
                update = communicator.ReceiveData();
                messageRepCode = update.Substring(0, 3);
            }
            if (messageRepCode == ((int)MessageCodes.MC_GET_MESSAGES_RESP).ToString())
            {
                int index = 3;
                ObservableCollection<Message> updatedMessages = new ObservableCollection<Message>();

                while (index < update.Length)
                {
                    int mode = int.Parse(update.Substring(index, 1));
                    index++;

                    int userNameLen = int.Parse(update.Substring(index, 5));
                    index += 5;

                    string userName = update.Substring(index, userNameLen);
                    index += userNameLen;

                    int msgDataLen = int.Parse(update.Substring(index, 5));
                    index += 5;

                    string msgData = update.Substring(index, msgDataLen);
                    index += msgDataLen;

                    int itemIdLen = int.Parse(update.Substring(index, 5));
                    index += 5;

                    int itemId;
                    if (itemIdLen > 0)
                    {
                        itemId = int.Parse(update.Substring(index, itemIdLen));
                        index += itemIdLen;
                    }
                    else
                    {
                        itemId = 0;
                    }

                    int idLen = int.Parse(update.Substring(index, 5));
                    index += 5;

                    int id = int.Parse(update.Substring(index, idLen));
                    index += idLen;

                    bool state = mode != 0;

                    updatedMessages.Add(new Message
                    {
                        Text = $"{userName} - {msgData}",
                        IsRead = false,
                        Type = state,
                        Mode = mode,
                        ItemId = itemId,
                        Id = id
                    });
                }

                Dispatcher.Invoke(() =>
                {
                    Messages.Clear();
                    foreach (var message in updatedMessages)
                    {
                        Messages.Add(message);
                    }

                    SortMessageList(Messages);
                });
            }
        }

        private void SortMessageList(ObservableCollection<Message> messages)
        {
            var sortedMessages = new ObservableCollection<Message>(messages.OrderBy(u => u.Id));
            MessagesListView.ItemsSource = sortedMessages;
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            // Call the method to reload messages
            HandleLoadMessagesList();
        }

        private void MarkAsRead_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is Message message)
            {
                message.IsRead = true;
                string chatMessageCode = ((int)MessageCodes.MC_MARK_AS_READ_REQUEST).ToString();
                string fullMessage = $"{chatMessageCode}{message.Id:D5}";
                communicator.SendData(fullMessage);

            }
        }

        private void MarkAllAsRead_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is Message message)
            {
                message.IsRead = true;
                string chatMessageCode = ((int)MessageCodes.MC_MARK_ALL_AS_READ_REQUEST).ToString();
                string fullMessage = $"{chatMessageCode}";
                HandleLoadMessagesList();
            }
        }

        private void approveRequest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is Message message)
            {
                if (message.Mode == 2)
                {
                    string addFriendCode = ((int)MessageCodes.MC_ACCEPT_PROJECT_INVITE_REQUEST).ToString();
                    communicator.SendData($"{addFriendCode}{message.ItemId.ToString().Length:D5}{message.ItemId}{communicator.UserName.Length:D5}{communicator.UserName}");
                }
                else if (message.Mode == 1)
                {
                    string addFriendCode = ((int)MessageCodes.MC_APPROVE_FRIEND_REQ_REQUEST).ToString();
                    communicator.SendData($"{addFriendCode}{message.ItemId:D5}{message.ItemId}");
                }
                message.IsRead = true;
            }
        }

        private void declineRequest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is Message message)
            {
                if (message.Mode == 2)
                {
                    string addFriendCode = ((int)MessageCodes.MC_DECLINE_PROJECT_INVITE_REQUEST).ToString();
                    communicator.SendData($"{addFriendCode}{message.ItemId.ToString().Length:D5}{message.ItemId}{communicator.UserName.Length:D5}{communicator.UserName}");
                }
                else if (message.Mode == 1)
                {
                    string rejectFriendCode = ((int)MessageCodes.MC_REJECT_FRIEND_REQ_REQUEST).ToString();
                    communicator.SendData($"{rejectFriendCode}{message.ItemId:D5}{message.ItemId}");
                }

                // Hide all buttons in the same item
                message.IsRead = true;
            }
        }

        public class Message : INotifyPropertyChanged
        {
            private string text;
            private bool isRead;
            private bool type;
            private int mode;
            private int id;
            private int itemId;


            public string Text
            {
                get => text;
                set
                {
                    text = value;
                    OnPropertyChanged();
                }
            }

            public bool IsRead
            {
                get => isRead;
                set
                {
                    isRead = value;
                    OnPropertyChanged();
                }
            }

            public bool Type
            {
                get => type;
                set
                {
                    type = value;
                    OnPropertyChanged();
                }
            }

            public int Mode
            {
                get => mode;
                set
                {
                    mode = value;
                    OnPropertyChanged();
                }
            }

            public int Id
            {
                get => id;
                set
                {
                    id = value;
                    OnPropertyChanged();
                }
            }

            public int ItemId
            {
                get => itemId;
                set
                {
                    itemId = value;
                    OnPropertyChanged();
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
