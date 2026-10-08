using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;

namespace client_side
{
    // A project: its files, a shared editor, members and project chat.
    //
    // Live editing: every change in the editor becomes a TextOperation sent with
    // the last server revision this client has seen. One edit is in flight at a
    // time; later ones wait in `pendingEdits`. Edits from others are transformed
    // past our unconfirmed ones before being applied, the same way the server
    // transforms ours, so everyone ends up with the same text.
    public partial class ProjectDirectory : Window
    {
        private readonly Communicator communicator;
        private readonly string ProjectName;
        private readonly int ProjectId;
        private readonly string codeLanguage;
        private readonly bool isEditable;

        private Thread receiveServerUpdatesThread;
        private volatile bool isListeningToServer = true;
        private bool disconnect = true; // closing the window with X logs out

        public ObservableCollection<FileViewModel> Files { get; } = new ObservableCollection<FileViewModel>();
        public ObservableCollection<ChatMessage> Chat { get; } = new ObservableCollection<ChatMessage>();
        public ObservableCollection<Member> Members { get; } = new ObservableCollection<Member>();

        // ---- open file / sync state (UI thread only)
        private int? openFileId;
        private string openFileName = "";
        private string pendingOpenName;     // file requested, snapshot not yet received
        private string pendingCreateName;   // file we created; open it when the server confirms
        private int revision;
        private TextOperation inFlight;
        private readonly List<TextOperation> pendingEdits = new List<TextOperation>();
        private bool applyingRemote;

        public ProjectDirectory(Communicator communicator, string projectName, int projectId, string codeLan, bool isEditable)
        {
            InitializeComponent();
            Style = (Style)FindResource(typeof(Window));
            this.communicator = communicator;
            ProjectName = projectName;
            ProjectId = projectId;
            codeLanguage = codeLan;
            this.isEditable = isEditable;

            Title = $"{projectName} - gitHub Demo";
            txtProjectTitle.Text = projectName;
            lstFileList.ItemsSource = Files;
            lstChat.ItemsSource = Chat;
            lstMembers.ItemsSource = Members;

            txtReadOnlyBadge.Visibility = isEditable ? Visibility.Collapsed : Visibility.Visible;
            newFilePanel.Visibility = isEditable ? Visibility.Visible : Visibility.Collapsed;
            chatInputPanel.Visibility = isEditable ? Visibility.Visible : Visibility.Collapsed;
            ToDoListBtn.Visibility = isEditable ? Visibility.Visible : Visibility.Collapsed;
            fileContextMenu.IsEnabled = isEditable;
            txtNewFileName.ToolTip = $"New file name (.{FileExtension} is added if you don't type an extension)";

            txtFileContent.Document.Changed += Document_Changed;
            txtFileContent.Options.ConvertTabsToSpaces = true;
            txtFileContent.Options.IndentationSize = 4;

            communicator.ApplyTheme(this);
            txtFileContent.Foreground = communicator.AppTheme?.TextColor ?? Brushes.White;
            txtFileContent.LineNumbersForeground = Brushes.Gray;

            Closing += ProjectDirectory_CloseFile;

            receiveServerUpdatesThread = new Thread(ReceiveServerUpdates) { IsBackground = true };
            receiveServerUpdatesThread.Start();

            // Ask for everything up front; the replies are handled by the listener,
            // so nothing that arrives in between (chat, edits, heartbeats) is lost.
            Send(MessageCodes.MC_GET_PROJECT_FILES_REQUEST, ProjectId.ToString());
            Send(MessageCodes.MC_GET_CHAT_MESSAGES_REQUEST, ProjectId.ToString());
            Send(MessageCodes.MC_GET_PROJECT_PATICIPANTS_REQUEST, ProjectId.ToString());
            SetStatus(isEditable ? "Ready." : "You are viewing this project. Join it to edit files and chat.");
        }

        private string FileExtension => codeLanguage switch
        {
            "cpp" => "cpp",
            "python" or "py" => "py",
            "cs" => "cs",
            "java" => "java",
            "JavaScript" or "js" => "js",
            _ => "txt",
        };

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

        // =====================================================================
        // Server messages
        // =====================================================================

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
                if (isListeningToServer)
                {
                    communicator.HandleListenerError(ex);
                }
            }
        }

        private void HandleMessage(string update)
        {
            if (update.Length < 3)
            {
                return;
            }
            try
            {
                switch (update.Substring(0, 3))
                {
                    case "230": HandleFileList(update); break;            // MC_GET_PROJECT_FILES_RESP
                    case "209": HandleChatHistory(update); break;         // MC_GET_CHAT_MESSAGES_RESP
                    case "264": HandleMembers(update); break;             // MC_GET_PROJECT_PATICIPANTS_RESP
                    case "211": HandleChatMessage(update); break;         // MC_POST_MSG_RESP
                    case "201": HandleSnapshot(update, false); break;     // MC_INITIAL_RESP
                    case "275": HandleSnapshot(update, true); break;      // MC_FILE_RESYNC
                    case "273": HandleRemoteEdit(update); break;          // MC_FILE_OP_RESP
                    case "274": HandleEditAck(update); break;             // MC_FILE_OP_ACK
                    case "207": HandleFileAdded(update.Substring(3)); break;   // MC_ADD_FILE_RESP
                    case "214": HandleFileDeleted(update.Substring(3)); break; // MC_DELETE_FILE_RESP
                    case "233": HandleFileRenamed(update); break;         // MC_RENAME_FILE_RESP
                    case "200": HandleError(update.Substring(3)); break;  // MC_ERROR_RESP
                    case "231": OpenOtherWindow(() => new HomePage(communicator)); break;            // back to home
                    case "268": OpenOtherWindow(() => new ToDoListWindow(communicator, ProjectId)); break; // to-do list
                    default: break; // heartbeats and messages for other windows
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Couldn't process a message from the server ({ex.Message}).", true);
            }
        }

        private void HandleError(string message)
        {
            if (pendingCreateName != null)
            {
                lblCreteErr.Text = message;
                pendingCreateName = null;
            }
            else if (pendingOpenName != null)
            {
                pendingOpenName = null;
                SetStatus(message, true);
            }
            else
            {
                SetStatus(message, true);
            }
        }

        // ---- files list

        private void HandleFileList(string update)
        {
            var reader = new WireReader(update);
            var names = new List<string>();
            while (!reader.AtEnd)
            {
                names.Add(reader.Field());
            }
            Files.Clear();
            foreach (string name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                Files.Add(new FileViewModel { FileName = name });
            }
        }

        private void HandleFileAdded(string name)
        {
            if (!Files.Any(f => f.FileName == name))
            {
                InsertSorted(name);
            }
            if (pendingCreateName == name)
            {
                pendingCreateName = null;
                txtNewFileName.Clear();
                lblCreteErr.Text = "";
                lstFileList.SelectedItem = Files.First(f => f.FileName == name); // opens it
            }
        }

        private void InsertSorted(string name)
        {
            int index = 0;
            while (index < Files.Count && string.Compare(Files[index].FileName, name, StringComparison.OrdinalIgnoreCase) < 0)
            {
                index++;
            }
            Files.Insert(index, new FileViewModel { FileName = name });
        }

        private void HandleFileDeleted(string name)
        {
            var file = Files.FirstOrDefault(f => f.FileName == name);
            if (file != null)
            {
                Files.Remove(file);
            }
            if (openFileName == name)
            {
                ResetEditor("This file was deleted.");
            }
        }

        private void HandleFileRenamed(string update)
        {
            var reader = new WireReader(update);
            string newName = reader.Field();
            string oldName = reader.Field();
            var file = Files.FirstOrDefault(f => f.FileName == oldName);
            if (file != null)
            {
                Files.Remove(file);
            }
            if (!Files.Any(f => f.FileName == newName))
            {
                InsertSorted(newName);
            }
            if (openFileName == oldName)
            {
                openFileName = newName;
                txtFileName.Text = newName;
                SetHighlighting(newName);
            }
            SetStatus($"Renamed {oldName} to {newName}.");
        }

        private void AddFile_Click(object sender, RoutedEventArgs e) => CreateFile();

        private void TxtNewFileName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CreateFile();
                e.Handled = true;
            }
        }

        private void CreateFile()
        {
            string name = txtNewFileName.Text.Trim();
            if (name.Length > 0 && !Path.HasExtension(name))
            {
                name += "." + FileExtension;
            }
            string problem = ValidateFileName(name);
            if (problem != null)
            {
                lblCreteErr.Text = problem;
                return;
            }
            lblCreteErr.Text = "";
            pendingCreateName = name;
            Send(MessageCodes.MC_CREATE_FILE_REQUEST, Wire.Field(ProjectId.ToString()) + Wire.Field(name));
        }

        private string ValidateFileName(string name)
        {
            if (name.Length == 0 || name.Length > 100)
                return "Enter a file name (up to 100 characters).";
            if (name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
                return "File names can't contain / \\ : * ? \" < > |";
            if (Files.Any(f => string.Equals(f.FileName, name, StringComparison.OrdinalIgnoreCase)))
                return "A file with that name already exists.";
            return null;
        }

        private void RenameFileMenu_Click(object sender, RoutedEventArgs e)
        {
            if (lstFileList.SelectedItem is not FileViewModel file)
            {
                return;
            }
            string newName = TextPrompt.Show(this, "Rename file", "New name:", file.FileName);
            if (newName == null || newName.Trim() == file.FileName)
            {
                return;
            }
            newName = newName.Trim();
            string problem = ValidateFileName(newName);
            if (problem != null)
            {
                SetStatus(problem, true);
                return;
            }
            Send(MessageCodes.MC_RENAME_FILE_REQUEST, Wire.Field(newName) + Wire.Field(file.FileName) + Wire.Field(ProjectId.ToString()));
        }

        private void DeleteFileMenu_Click(object sender, RoutedEventArgs e) => DeleteSelectedFile();

        private void LstFiles_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && isEditable)
            {
                DeleteSelectedFile();
                e.Handled = true;
            }
            else if (e.Key == Key.F2 && isEditable)
            {
                RenameFileMenu_Click(sender, e);
                e.Handled = true;
            }
        }

        private void DeleteSelectedFile()
        {
            if (lstFileList.SelectedItem is not FileViewModel file)
            {
                return;
            }
            if (MessageBox.Show(this, $"Delete {file.FileName}? This can't be undone.", "Delete file",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }
            if (openFileName == file.FileName)
            {
                CloseOpenFile(); // the server refuses to delete a file someone is in
            }
            Send(MessageCodes.MC_DELETE_FILE_REQUEST, Wire.Field(ProjectId.ToString()) + Wire.Field(file.FileName));
        }

        // ---- opening / closing a file

        private void LstFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstFileList.SelectedItem is FileViewModel file && file.FileName != openFileName && file.FileName != pendingOpenName)
            {
                OpenFile(file.FileName);
            }
        }

        private void OpenFile(string name)
        {
            if (openFileId != null)
            {
                Send(MessageCodes.MC_EXIT_FILE_REQUEST);
            }
            ResetSyncState();
            openFileId = null;
            pendingOpenName = name;
            txtFileName.Text = name;
            SetStatus($"Opening {name}...");
            Send(MessageCodes.MC_ENTER_FILE_REQUEST, Wire.Field(name) + communicator.UserId);
            Send(MessageCodes.MC_INITIAL_REQUEST, Wire.Field(ProjectId.ToString()) + Wire.Field(name));
        }

        // "201/275<fileId>|<revision>|<text>": the file as the server has it right now.
        private void HandleSnapshot(string update, bool isResync)
        {
            string[] parts = update.Substring(3).Split('|', 3);
            int fileId = int.Parse(parts[0]);
            if (!isResync)
            {
                if (pendingOpenName == null)
                {
                    return; // a reply for a file we already moved away from
                }
                openFileName = pendingOpenName;
                pendingOpenName = null;
            }
            else if (fileId != openFileId)
            {
                return;
            }

            openFileId = fileId;
            ResetSyncState();
            revision = int.Parse(parts[1]);

            applyingRemote = true;
            try
            {
                txtFileContent.Document.Text = parts[2];
                txtFileContent.Document.UndoStack.ClearAll();
            }
            finally
            {
                applyingRemote = false;
            }

            txtFileName.Text = openFileName;
            SetHighlighting(openFileName);
            txtFileContent.IsReadOnly = !isEditable;
            txtFileContent.Visibility = Visibility.Visible;
            txtEditorPlaceholder.Visibility = Visibility.Collapsed;
            closeFileBtn.Visibility = Visibility.Visible;
            if (isEditable)
            {
                txtFileContent.Focus();
            }
            SetStatus(isResync ? "Your copy was out of date and has been reloaded." :
                      isEditable ? "Live - everyone in this file sees your changes as you type." : "Viewing (read-only).");
        }

        private void CloseFile_Click(object sender, RoutedEventArgs e) => CloseOpenFile();

        private void CloseOpenFile()
        {
            if (openFileId != null || pendingOpenName != null)
            {
                Send(MessageCodes.MC_EXIT_FILE_REQUEST);
            }
            ResetEditor("Ready.");
            lstFileList.SelectedItem = null;
        }

        private void ResetEditor(string status)
        {
            openFileId = null;
            openFileName = "";
            pendingOpenName = null;
            ResetSyncState();
            applyingRemote = true;
            try
            {
                txtFileContent.Document.Text = "";
                txtFileContent.Document.UndoStack.ClearAll();
            }
            finally
            {
                applyingRemote = false;
            }
            txtFileContent.IsReadOnly = true;
            txtFileContent.Visibility = Visibility.Hidden;
            txtEditorPlaceholder.Visibility = Visibility.Visible;
            closeFileBtn.Visibility = Visibility.Collapsed;
            txtFileName.Text = "";
            SetStatus(status);
        }

        private void ResetSyncState()
        {
            inFlight = null;
            pendingEdits.Clear();
        }

        private void SetHighlighting(string fileName)
        {
            txtFileContent.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(fileName));
        }

        // ---- live editing

        private void Document_Changed(object sender, DocumentChangeEventArgs e)
        {
            if (applyingRemote || openFileId == null || !isEditable)
            {
                return;
            }
            int lengthBefore = txtFileContent.Document.TextLength - e.InsertionLength + e.RemovalLength;
            var op = TextOperation.FromChange(lengthBefore, e.Offset, e.RemovalLength, e.InsertedText.Text);
            if (inFlight == null)
            {
                SendEdit(op);
            }
            else
            {
                pendingEdits.Add(op);
            }
        }

        private void SendEdit(TextOperation op)
        {
            inFlight = op;
            Send(MessageCodes.MC_FILE_OP_REQUEST, $"{openFileId}|{revision}|{op.Encode()}");
            txtStatus.Text = "Syncing...";
        }

        // "274<fileId>|<revision>": our edit is in.
        private void HandleEditAck(string update)
        {
            string[] parts = update.Substring(3).Split('|');
            if (int.Parse(parts[0]) != openFileId || inFlight == null)
            {
                return;
            }
            revision = int.Parse(parts[1]);
            inFlight = null;
            if (pendingEdits.Count > 0)
            {
                TextOperation next = pendingEdits[0];
                pendingEdits.RemoveAt(0);
                SendEdit(next);
            }
            else
            {
                SetStatus("All changes saved.");
            }
        }

        // "273<fileId>|<revision>|<operation>": someone else's edit.
        private void HandleRemoteEdit(string update)
        {
            string[] parts = update.Substring(3).Split('|', 3);
            if (int.Parse(parts[0]) != openFileId)
            {
                return;
            }
            revision = int.Parse(parts[1]);
            TextOperation op = TextOperation.Decode(parts[2]);

            // Our unconfirmed edits happened "after" this one on the server.
            if (inFlight != null)
            {
                (inFlight, op) = TextOperation.Transform(inFlight, op);
                for (int k = 0; k < pendingEdits.Count; k++)
                {
                    (pendingEdits[k], op) = TextOperation.Transform(pendingEdits[k], op);
                }
            }

            applyingRemote = true;
            try
            {
                op.ApplyTo(txtFileContent.Document);
            }
            catch (InvalidOperationException)
            {
                // Out of step (shouldn't happen): start over from the server's copy.
                OpenFile(openFileName);
            }
            finally
            {
                applyingRemote = false;
            }
        }

        // ---- members and chat

        private void HandleMembers(string update)
        {
            var reader = new WireReader(update);
            Members.Clear();
            var names = new List<string>();
            while (!reader.AtEnd)
            {
                names.Add(reader.Field());
            }
            foreach (string name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                Members.Add(new Member { Name = name, RoleLabel = name == communicator.UserName ? " (you)" : "" });
            }
        }

        private void HandleChatHistory(string update)
        {
            var reader = new WireReader(update);
            Chat.Clear();
            while (!reader.AtEnd)
            {
                string text = reader.Field();
                string sender = reader.Field();
                Chat.Add(new ChatMessage { Sender = sender == communicator.UserName ? "You" : sender, Text = text });
            }
            chatScroll.ScrollToEnd();
        }

        private void HandleChatMessage(string update)
        {
            var reader = new WireReader(update);
            string text = reader.Field();
            string sender = reader.Field();
            Chat.Add(new ChatMessage { Sender = sender, Text = text });
            chatScroll.ScrollToEnd();
        }

        private void SendChat_Click(object sender, RoutedEventArgs e) => SendChatMessage();

        private void TxtChatInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                SendChatMessage();
                e.Handled = true;
            }
        }

        private void SendChatMessage()
        {
            string message = txtChatInput.Text.Trim();
            if (message.Length == 0)
            {
                return;
            }
            lblErr.Text = "";
            Send(MessageCodes.MC_POST_MSG_REQUEST, Wire.Field(ProjectId.ToString()) + Wire.Field(message));
            Chat.Add(new ChatMessage { Sender = "You", Text = message });
            chatScroll.ScrollToEnd();
            txtChatInput.Clear();
        }

        // ---- navigation

        private void ReturnToMainPage_Click(object sender, RoutedEventArgs e)
        {
            if (openFileId != null)
            {
                Send(MessageCodes.MC_EXIT_FILE_REQUEST);
            }
            Send(MessageCodes.MC_EXIT_PROJECT_REQUEST);
        }

        private void ToDoList_Click(object sender, RoutedEventArgs e)
        {
            if (openFileId != null)
            {
                Send(MessageCodes.MC_EXIT_FILE_REQUEST);
            }
            Send(MessageCodes.MC_MOVE_TO_TO_DO_LIST_REQUEST);
        }

        private void OpenOtherWindow(Func<Window> create)
        {
            disconnect = false;
            isListeningToServer = false; // this message was the last one for this window
            Window next = create();
            next.Show();
            Close();
        }

        private void ProjectDirectory_CloseFile(object sender, EventArgs e)
        {
            isListeningToServer = false;
            if (disconnect)
            {
                Send(MessageCodes.MC_DISCONNECT, $"{communicator.UserId:D5}");
            }
        }

        // ---- view models

        public class FileViewModel
        {
            public string FileName { get; set; }
        }

        public class ChatMessage
        {
            public string Sender { get; set; }
            public string Text { get; set; }
        }

        public class Member
        {
            public string Name { get; set; }
            public string RoleLabel { get; set; }
        }
    }
}
