using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace client_side
{
    // Inbox screen: notifications, project invites (mode 2) and friend requests (mode 1).
    public partial class MessagesWindow : Window
    {
        private readonly Communicator communicator;
        private volatile bool isListeningToServer = true;
        private bool disconnect = true;
        public ObservableCollection<Message> Messages { get; } = new ObservableCollection<Message>();

        public MessagesWindow(Communicator communicator)
        {
            InitializeComponent();
            MainFrame.Attach(this); // same size and place as the previous screen
            Style = (Style)FindResource(typeof(Window));
            this.communicator = communicator;
            MessagesListView.ItemsSource = Messages;
            communicator.ApplyTheme(this);
            Closing += (_, _) =>
            {
                isListeningToServer = false;
                if (disconnect) Send(MessageCodes.MC_DISCONNECT, $"{communicator.UserId:D5}");
            };
            new Thread(ReceiveServerUpdates) { IsBackground = true }.Start();
            Load();
        }

        private void Send(MessageCodes code, string body = "")
        {
            try
            {
                communicator.SendData($"{(int)code}{body}");
            }
            catch (Exception ex)
            {
                communicator.HandleListenerError(ex);
            }
        }

        private void SetStatus(string text, bool isError = false)
        {
            txtStatus.Text = text;
            txtStatus.SetResourceReference(TextBlock.ForegroundProperty, isError ? "Theme.Danger" : "Theme.Muted");
        }

        private void ReceiveServerUpdates()
        {
            try
            {
                while (isListeningToServer)
                {
                    string update = communicator.ReceiveData();
                    Dispatcher.Invoke(() => HandleMessage(update));
                }
            }
            catch (Exception ex)
            {
                if (isListeningToServer) communicator.HandleListenerError(ex);
            }
        }

        private void HandleMessage(string update)
        {
            if (update.Length < 3) return;
            try
            {
                switch (update.Substring(0, 3))
                {
                    case "253": if (update.Length != 8) ShowMessages(update); break;
                    case "236": SetStatus($"You're now friends with {new WireReader(update).Field()}."); Load(); break;
                    case "237": SetStatus("Friend request declined."); Load(); break;
                    case "240": GoHome(); break;                      // back-to-home acknowledged
                    case "200": SetStatus(update.Substring(3), true); Load(); break;
                    default: break;
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Couldn't process a message from the server ({ex.Message}).", true);
            }
        }

        private void Load() => Send(MessageCodes.MC_GET_MESSAGES_REQUEST, Wire.Field(communicator.UserName));

        // "253" then per message: <mode digit><sender><text><itemId><id>
        private void ShowMessages(string update)
        {
            var reader = new WireReader(update);
            var list = new List<Message>();
            while (!reader.AtEnd)
            {
                int mode = reader.Take(1)[0] - '0';
                string sender = reader.Field();
                string text = reader.Field();
                string itemId = reader.Field();
                int id = int.Parse(reader.Field());
                list.Add(new Message
                {
                    Mode = mode,
                    Sender = sender,
                    Text = text.Trim(),
                    ItemId = int.TryParse(itemId, out int item) ? item : 0,
                    Id = id,
                });
            }
            Messages.Clear();
            foreach (var message in list.OrderByDescending(m => m.Id))
            {
                Messages.Add(message);
            }
            txtEmpty.Visibility = Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            markAllBtn.IsEnabled = Messages.Any(m => !m.IsRequest);
        }

        private static Message MessageOf(object sender) => (sender as FrameworkElement)?.Tag as Message;

        private void approveRequest_Click(object sender, RoutedEventArgs e)
        {
            if (MessageOf(sender) is not Message message) return;
            if (message.Mode == 2)
            {
                Send(MessageCodes.MC_ACCEPT_PROJECT_INVITE_REQUEST, Wire.Field(message.ItemId.ToString()) + Wire.Field(communicator.UserName));
                SetStatus("Joined the project. It's now in your project list.");
            }
            else
            {
                Send(MessageCodes.MC_APPROVE_FRIEND_REQ_REQUEST, Wire.Field(message.ItemId.ToString()));
            }
            Load();
        }

        private void declineRequest_Click(object sender, RoutedEventArgs e)
        {
            if (MessageOf(sender) is not Message message) return;
            if (message.Mode == 2)
            {
                Send(MessageCodes.MC_DECLINE_PROJECT_INVITE_REQUEST, Wire.Field(message.ItemId.ToString()) + Wire.Field(communicator.UserName));
                SetStatus("Invitation declined.");
            }
            else
            {
                Send(MessageCodes.MC_REJECT_FRIEND_REQ_REQUEST, Wire.Field(message.ItemId.ToString()));
            }
            Load();
        }

        private void MarkAsRead_Click(object sender, RoutedEventArgs e)
        {
            if (MessageOf(sender) is Message message)
            {
                Send(MessageCodes.MC_MARK_AS_READ_REQUEST, message.Id.ToString());
                Messages.Remove(message);
                txtEmpty.Visibility = Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void MarkAllAsRead_Click(object sender, RoutedEventArgs e)
        {
            Send(MessageCodes.MC_MARK_ALL_AS_READ_REQUEST);
            Load();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => Load();

        // The reply (240) ends this screen's listener before the home screen starts its own.
        private void Back_Click(object sender, RoutedEventArgs e) => Send(MessageCodes.MC_BACK_TO_HOME_PAGE_REQUEST);

        private void GoHome()
        {
            disconnect = false;
            isListeningToServer = false;
            new HomePage(communicator).Show();
            Close();
        }

        public class Message
        {
            public int Mode { get; set; }        // 0 notification, 1 friend request, 2 project invite
            public string Sender { get; set; }
            public string Text { get; set; }
            public int ItemId { get; set; }      // project id (invite) or sender id (friend request)
            public int Id { get; set; }

            public bool IsRequest => Mode == 1 || Mode == 2;
            public string Kind => Mode switch { 1 => "Friend request", 2 => "Project invitation", _ => "Notification" };
            public string Icon => Mode switch { 1 => "", 2 => "", _ => "" };
        }
    }
}
