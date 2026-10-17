using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace client_side
{
    // Inbox: notifications, project invites (mode 2) and friend requests (mode 1).
    // Server replies arrive through the home page's listener (IServerMessageHandler).
    public partial class MessagesWindow : Window, IServerMessageHandler
    {
        private readonly Communicator communicator;
        public ObservableCollection<Message> Messages { get; } = new ObservableCollection<Message>();

        public MessagesWindow(Communicator communicator)
        {
            InitializeComponent();
            Style = (Style)FindResource(typeof(Window));
            this.communicator = communicator;
            MessagesListView.ItemsSource = Messages;
            communicator.ApplyTheme(this);
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
            txtStatus.Foreground = isError ? Brushes.IndianRed : (communicator.AppTheme?.TextColor ?? Brushes.White);
            txtStatus.Opacity = isError ? 1 : 0.65;
        }

        private void Load() => Send(MessageCodes.MC_GET_MESSAGES_REQUEST, Wire.Field(communicator.UserName));

        public bool HandleServerMessage(string update)
        {
            switch (update.Substring(0, 3))
            {
                case "253":
                    if (update.Length == 8) return false; // a message *count* for the home page
                    ShowMessages(update);
                    return true;
                case "236": SetStatus($"You're now friends with {new WireReader(update).Field()}."); Load(); return true;
                case "237": SetStatus("Friend request declined."); Load(); return true;
                case "256": Load(); return true; // new message arrived
                case "200": SetStatus(update.Substring(3), true); Load(); return true;
                default: return false;
            }
        }

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

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        public class Message
        {
            public int Mode { get; set; }        // 0 notification, 1 friend request, 2 project invite
            public string Sender { get; set; }
            public string Text { get; set; }
            public int ItemId { get; set; }      // project id (invite) or sender id (friend request)
            public int Id { get; set; }

            public bool IsRequest => Mode == 1 || Mode == 2;
            public string Kind => Mode switch { 1 => "Friend request", 2 => "Project invitation", _ => "Notification" };
        }
    }
}
