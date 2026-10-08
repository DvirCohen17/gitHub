#include "Communicator.h"
#include <cstdio> // For popen and pclose

extern std::unordered_map<std::string, std::mutex> m_fileMutexes;

std::string Communicator::executeCommand(const std::string& command) {
	std::string result;
	FILE* pipe = _popen(command.c_str(), "r");
	if (!pipe) throw std::runtime_error("popen() failed!");
	char buffer[128];
	while (!feof(pipe)) {
		if (fgets(buffer, 128, pipe) != nullptr)
			result += buffer;
	}
	_pclose(pipe);
	return result;
}

Communicator::Communicator()
{
	// this server use TCP. that why SOCK_STREAM & IPPROTO_TCP
	// if the server use UDP we will use: SOCK_DGRAM & IPPROTO_UDP
	m_serverSocket = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
	if (m_serverSocket == INVALID_SOCKET)
		throw std::exception("Failed to initialize server socket.");
}

// Destructor
Communicator::~Communicator() {
	try {
		closesocket(m_serverSocket);
	}
	catch (...) {}
}

void Communicator::setDB(IDatabase* db)
{
	m_database = db;
}

void Communicator::bindAndListen()
{
	struct sockaddr_in sa = { 0 };

	sa.sin_port = htons(PORT); // port that server will listen for
	sa.sin_family = AF_INET;   // must be AF_INET
	sa.sin_addr.s_addr = INADDR_ANY;    // when there are few ip's for the machine. We will use always "INADDR_ANY"

	// Connects between the socket and the configuration (port and etc..)
	if (bind(m_serverSocket, (struct sockaddr*)&sa, sizeof(sa)) == SOCKET_ERROR)
		throw std::exception("Failed to bind onto the requested port");

	// Start listening for incoming requests of clients
	if (listen(m_serverSocket, SOMAXCONN) == SOCKET_ERROR)
		throw std::exception("Failed listening to requests.");
}

bool Communicator::isSocketConnected(SOCKET socket) {
	// Try sending a small piece of data

	// Heartbeat (MC_HEARTBEAT_REQUEST), framed like every other message so it
	// can't get glued onto a real reply.
	const std::string testMessage = Helper::frame(std::to_string(MC_HEARTBEAT_REQUEST));

	int result = send(socket, testMessage.data(), (int)testMessage.size(), 0);

	if (result == SOCKET_ERROR) {
		int error = WSAGetLastError();
		if (error == WSAECONNRESET || error == WSAENOTCONN || error == WSAENOTSOCK) {
			// Connection reset by peer, not connected, or not a socket
			return false;
		}
		// Other errors, could be due to non-blocking operation or temporary issues
		std::cerr << "send failed with error: " << error << std::endl;
		return false;
	}

	// If the send operation was successful, assume the socket is connected
	return true;
}

void Communicator::getVersion(SOCKET client_sock)
{
	std::string repCode = std::to_string(MC_VERSION_RESP);
	repCode += m_database->getLatestVersion();
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::handleNewClient(SOCKET client_sock)
{
	bool run = true;
	bool pass = true;
	std::string msg;
	std::string repCode;
	FileDetail fileList;
	BUFFER buf;

	/*fileOperationHandler.getFilesInDirectory(".\\files", m_fileNames);
	for (const auto& fileName : m_fileNames) 
	{
		fileList = m_database->getFileDetails(fileName.first);
		m_fileNames[fileName.first] = fileList.fileId;
	}
	*/
	while (run)
	{
		try
		{
			bool legacyUnframed = false;
			buf = Helper::receiveMessage(client_sock, legacyUnframed);
			if (buf.size() == 0)
			{
				closesocket(client_sock);
				run = false;
				// Handle disconnection
				handleClientDisconnect(client_sock);
				continue;
			}

			if (legacyUnframed)
			{
				// A client/installer from before message framing. Answer only what
				// it needs to find out it must update, in the format it understands.
				std::string legacyRequest(buf.begin(), buf.end());
				if (legacyRequest.rfind(std::to_string(MC_VERSION_REQUEST), 0) == 0)
				{
					std::lock_guard<std::recursive_mutex> stateLock(m_stateMutex);
					Helper::sendRaw(client_sock, std::to_string(MC_VERSION_RESP) + m_database->getLatestVersion());
				}
				else if (legacyRequest.rfind(std::to_string(MC_RECEIVE_FILES_REQUEST), 0) == 0)
				{
					sendFile(client_sock, ".\\gitHubDemo.zip", false);
					run = false;
				}
				else
				{
					run = false; // anything else from an old client can't be parsed reliably
				}
				continue;
			}

			if (std::string(buf.begin(), buf.end()) == std::to_string(MC_RECEIVE_FILES_REQUEST))
			{
				// Installer download: streamed outside the state lock so a slow
				// download doesn't stall every other client.
				sendFile(client_sock, ".\\gitHubDemo.zip", true);
				run = false;
				continue;
			}

			std::lock_guard<std::recursive_mutex> stateLock(m_stateMutex);
			std::string newRequest(buf.begin(), buf.end());
			Action reqDetail = deconstructReq(newRequest);
			int msgCode = std::stoi(newRequest.substr(0, 3));

			// Everything except the pre-login handshake needs a logged-in client;
			// without this, m_clients[client_sock] below is a null pointer and the
			// whole server crashes on a single unauthenticated request.
			bool preLoginRequest = msgCode == MC_LOGIN_REQUEST || msgCode == MC_SIGNUP_REQUEST ||
				msgCode == MC_FORGOT_PASSW_REQUEST || msgCode == MC_DISCONNECT ||
				msgCode == MC_VERSION_REQUEST || msgCode == MC_RECEIVE_FILES_REQUEST;
			if (!preLoginRequest && m_clients.find(client_sock) == m_clients.end())
			{
				throw std::runtime_error("Not logged in");
			}

			pass = false;
			switch (msgCode)
			{
			case MC_INSERT_REQUEST:
				pass = true;
				repCode = std::to_string(MC_INSERT_RESP);
				break;
			case MC_DELETE_REQUEST:
				pass = true;
				repCode = std::to_string(MC_DELETE_RESP);
				break;
			case MC_REPLACE_REQUEST:
				pass = true;
				repCode = std::to_string(MC_REPLACE_RESP);
				break;
			case MC_LOGIN_REQUEST:
				login(client_sock, reqDetail.userName, reqDetail.pass, reqDetail.email);
				break;
			case MC_LOGOUT_REQUEST:
				logout(client_sock);
				break;
			case MC_SIGNUP_REQUEST:
				signUp(client_sock, reqDetail.userName, reqDetail.pass, reqDetail.email);
				break;
			case MC_FORGOT_PASSW_REQUEST:
				forgotPassword(client_sock, reqDetail.userName, reqDetail.pass, reqDetail.oldPass, reqDetail.email);
				break;
			case MC_INITIAL_REQUEST:
				getInitialContent(client_sock, reqDetail.fileName, reqDetail.projectId);
				break;
			case MC_CREATE_FILE_REQUEST:
				createFile(client_sock, reqDetail.fileName, reqDetail.projectId);
				break;
			case MC_DELETE_FILE_REQUEST:
				deleteFile(client_sock, reqDetail.fileName, reqDetail.projectId);
				break;
			case MC_GET_FILES_REQUEST:
				getFiles(client_sock);
				break;
			case MC_GET_CHAT_MESSAGES_REQUEST:
				getChatMesegges(client_sock, reqDetail.data);
				break;
			case MC_GET_USERS_ON_FILE_REQUEST:
				getUsersOnFile(client_sock, reqDetail.data);
				break;
			case MC_GET_USERS_REQUEST:
				getUsers(client_sock);
				break;
			case MC_GET_USERS_PERMISSIONS_REQ_REQUEST:
				getUserPermissionReq(client_sock);
				break;
			case MC_POST_MSG_REQUEST:
				postChatMsg(client_sock, reqDetail.projectId, reqDetail.data, reqDetail.dataLength);
				break;
			case MC_APPROVE_PERMISSION_REQUEST:
				approvePermissionReq(client_sock, reqDetail.userName, reqDetail.fileName);
				break;
			case MC_REJECT_PERMISSION_REQUEST:
				rejectPermissionReq(client_sock, reqDetail.userName, reqDetail.fileName);
				break;
			case MC_PERMISSION_FILE_REQ_REQUEST:
				permissionFileReq(client_sock, reqDetail.userName, reqDetail.fileName, reqDetail.fileNameLength);
				break;
			case MC_ENTER_FILE_REQUEST:
				enterFile(client_sock, reqDetail.data, reqDetail.dataLength);
				break;
			case MC_EXIT_FILE_REQUEST:
				exitFile(client_sock);
				break;
			case MC_PROFILE_INFO_REQUEST:
				getProfileInfo(client_sock, reqDetail.userName);
				break;
			case MC_GET_PROFILE_IMAGE_REQUEST:
				getProfileImage(client_sock, reqDetail.userName);
				break;
			case MC_EDIT_PROFILE_INFO_REQUEST:
				editInfo(client_sock, reqDetail.bio);
				break;
			case MC_FRIENDS_LIST_REQUEST:
				getUserFriends(client_sock, reqDetail.userName);
				break;
			case MC_ADD_FRIEND_REQUEST:
				addFriend(client_sock, reqDetail.userName);
				break;
			case MC_APPROVE_FRIEND_REQ_REQUEST:
				approveFriendReq(client_sock, reqDetail.userId);
				break;
			case MC_REJECT_FRIEND_REQ_REQUEST:
				rejectFriendReq(client_sock, reqDetail.userId);
				break;
			case MC_SEARCH_REQUEST:
				searchUsers(client_sock, reqDetail.searchCommand);
				break;
			case MC_SEARCH_FRIENDS_REQUEST:
				searchFriends(client_sock, reqDetail.searchCommand);
				break;
			case MC_REMOVE_FRIEND_REQUEST:
				removeFriend(client_sock, reqDetail.userName, reqDetail.userNameToRemove);
				break;
			case MC_PROJECTS_LIST_REQUEST:
				getProjectsList(client_sock, reqDetail.userName);
				break;
			case MC_USER_PROJECTS_LIST_REQUEST:
				getUserProjectsList(client_sock, reqDetail.userName);
				break;
			case MC_MOVE_TO_CREATE_PROJ_WINDOW_REQUEST:
				moveToCreateWindow(client_sock);
				break;
			case MC_CREATE_PROJECT_REQUEST:
				createProject(client_sock, reqDetail.projectName, reqDetail.firendsList, reqDetail.codeLaneguage, reqDetail.mode);
				break;
			case MC_BACK_TO_HOME_PAGE_REQUEST:
				backToMainPage(client_sock);
				break;
			case MC_BACK_TO_PROJECT_PAGE_REQUEST:
				backToProjectPage(client_sock);
				break;
			case MC_DELETE_PROJECT_REQUEST:
				deleteProject(client_sock, reqDetail.projectId);
				break;
			case MC_ENTER_PROJECT_REQUEST:
				enterProject(client_sock, reqDetail.projectId);
				break;
			case MC_EXIT_PROJECT_REQUEST:
				exitProject(client_sock);
				break;
			case MC_LEAVE_PROJECT_REQUEST:
				leaveProject(client_sock, reqDetail.projectId, reqDetail.userName);
				break;
			case MC_ACCEPT_PROJECT_INVITE_REQUEST:
				acceptProjectInvite(client_sock, reqDetail.projectId, reqDetail.userName);
				break;
			case MC_DECLINE_PROJECT_INVITE_REQUEST:
				declineProjectInvite(client_sock, reqDetail.projectId, reqDetail.userName);
				break;
			case MC_GET_PROJECT_FILES_REQUEST:
				getProjectFiles(client_sock, reqDetail.projectId);
				break;
			case MC_GET_PROJECT_INFO_REQUEST:
				getProjectInfo(client_sock, reqDetail.projectName);
				break;
			case MC_EDIT_PROJECT_INFO_REQUEST:
				editProjectInfo(client_sock, reqDetail.projectId);
				break;
			case MC_VIEW_PROJECT_INFO_REQUEST:
				viewProjectInfo(client_sock, reqDetail.projectId);
				break;
			case MC_MODIFY_PROJECT_INFO_REQUEST:
				modifyProjectInfo(client_sock, reqDetail.projectId, reqDetail.projectName, reqDetail.firendsList, reqDetail.codeLaneguage);
				break;
			case MC_RENAME_FILE_REQUEST:
				renameFile(client_sock, reqDetail.fileName,reqDetail.oldFileName, reqDetail.projectId);
				break;
			case MC_GET_MESSAGES_REQUEST:
				getMessages(client_sock, reqDetail.userName);
				break;
			case MC_MARK_AS_READ_REQUEST:
				markMessageAsRead(client_sock, reqDetail.messageId);
				break;
			case MC_MARK_ALL_AS_READ_REQUEST:
				markAllMessageAsRead(client_sock);
				break;
			case MC_GET_MSG_COUNT_REQUEST:
				getMessagesCount(client_sock, reqDetail.userName);
				break;
			case MC_GET_CODE_STYLES_REQUEST:
				getCodeStyles(client_sock);
				break;
			case MC_GET_CURRENT_PROJECT_ISSUES_REQUEST:
				loadCurrentIssues(client_sock, reqDetail.projectId);
				break;
			case MC_GET_COMPLETED_PROJECT_ISSUES_REQUEST:
				loadCompletedIssues(client_sock, reqDetail.projectId);
				break;
			case MC_GET_ISSUE_REQUEST:
				getIssue(client_sock, reqDetail.issueId);;
				break;
			case MC_MARK_TASK_AS_COMPLETED_REQUEST:
				markIssueAsComplete(client_sock, reqDetail.issueId);
				break;
			case MC_MARK_TASK_AS_NOT_COMPLETED_REQUEST:
				markIssueAsNotComplete(client_sock, reqDetail.issueId);
				break;
			case MC_ADD_TASK_REQUEST:
				addTask(client_sock, reqDetail.issueData, reqDetail.assignedPeople, reqDetail.issueDate);
				break;
			case MC_MODIFY_ISSUE_REQUEST:
				modifyIssue(client_sock, reqDetail.issueData, reqDetail.assignedPeople, reqDetail.issueDate, reqDetail.issueId);
				break;
			case MC_DELETE_TASK_REQUEST:
				deleteTask(client_sock, reqDetail.issueId);
				break;
			case MC_GET_PROJECT_PATICIPANTS_REQUEST:
				getProjectParticipants(client_sock, reqDetail.projectId);
				break;
			case MC_GET_MAIL_IMAGES_REQUEST:
				getMailImages(client_sock);
				break;
			case MC_SETTINGS_REQUEST:
				moveToSettings(client_sock);
				break;
			case MC_MOVE_TO_MESSAGES_REQUEST:
				moveToMessages(client_sock);
				break;
			case MC_MOVE_TO_TO_DO_LIST_REQUEST:
				moveToToDoWindow(client_sock);
				break;
			case MC_MOVE_TO_ISSUE_DATA_WINDOW_REQUEST:
				moveToIssueData(client_sock, reqDetail.issueId);
				break;
			case MC_MOVE_TO_PROJECT_PAGE_REQUEST:
				moveToProjectWindow(client_sock, reqDetail.projectId);
				break;
			case MC_DISCONNECT: // Handle disconnect request
				run = false;
				handleClientDisconnect(client_sock);
				continue;
			case MC_RECEIVE_FILES_REQUEST:
				sendFile(client_sock, ".\\gitHubDemo.zip", true);
				run = false;
				break;
			case MC_VERSION_REQUEST:
				getVersion(client_sock);
				break;
			default:
				// Handle the default case or throw an error
				throw std::runtime_error("Unknown action code: " + reqDetail.msg);
			}

			if (pass)
			{
				{
					int fileId = m_clients[client_sock]->getFileId();
					int projectId = m_clients[client_sock]->getProjectId();
					// Lock the mutex before updating the file
					std::lock_guard<std::mutex> lock(m_fileMutexes[fileId]);

					reqDetail = adjustIndexForSync(fileId, projectId, reqDetail);

					updateFileOnServer(fileId, reqDetail);
					notifyAllClients(repCode + reqDetail.msg, client_sock, true);
					
					reqDetail.timestamp = getCurrentTimestamp();
					m_lastActionMap[fileId].push_back(reqDetail);
					m_FileUpdate[fileId] = true;
					needUpdate = true;
				}// lock goes out of scope, releasing the lock
			}
		}
		catch (const std::exception& e)
		{
			// Check if it's a connection error
			if (Helper::IsConnectionError(e))
			{
				run = false;
				// Handle connection error
				handleClientDisconnect(client_sock);
			}
			else
			{
				handleError(client_sock, e);
			}
		}
	}
	closesocket(client_sock);
}

Action Communicator::adjustIndexForSync(const int fileId, int projectId, Action reqDetail)
{
	std::string lengthString;
	std::string selectionLengthString;
	std::string indexString;

	int selectionLength;
	int length;
	std::string data;
	int newIndex;

	int newCode = reqDetail.code;
	// Check if there is a last action recorded for this file
	if (m_lastActionMap.find(fileId) != m_lastActionMap.end())
	{
		std::vector<Action>& lastActions = m_lastActionMap[fileId];

		// Use an iterator to iterate over the vector
		auto it = lastActions.begin();

		// Iterate over all last actions for the file
		while (it != lastActions.end())
		{
			const Action& action = *it;

			// Check if the new action was created before the current last action and by a different user
			if (reqDetail.timestamp < action.timestamp && reqDetail.userId != action.userId
				&& action.code != MC_INITIAL_REQUEST && action.code != MC_CREATE_FILE_REQUEST)
			{
				int lastActionCode = action.code;
				int size = action.size;
				int lastIndex = std::stoi(action.index);

				std::string newAction = reqDetail.msg;

				std::string adjustedIndex = reqDetail.index;
				std::string updatedAction = newAction;

				newIndex = std::stoi(reqDetail.index);

				//reqDetail.timestamp = getCurrentTimestamp();

				// uodate the index
				switch (lastActionCode) {
				case MC_INSERT_REQUEST:
					if (newIndex > lastIndex)
					{
						newIndex += size;
						adjustedIndex = std::to_string(newIndex);
						adjustedIndex = std::string(5 - adjustedIndex.length(), '0') + adjustedIndex;
						updatedAction = reqDetail.dataLength + reqDetail.data + adjustedIndex;
					}
					break;
				case MC_DELETE_REQUEST:
					if (newIndex > lastIndex)
					{
						newIndex -= size;
						adjustedIndex = std::to_string(newIndex);
						adjustedIndex = std::string(5 - adjustedIndex.length(), '0') + adjustedIndex;
						updatedAction = reqDetail.dataLength + adjustedIndex;
					}
					break;
				case MC_REPLACE_REQUEST:
					if (newIndex > lastIndex)
					{
						newIndex = newIndex - std::stoi(reqDetail.selectionLength) + std::stoi(reqDetail.dataLength);
						adjustedIndex = std::to_string(newIndex);
						adjustedIndex = std::string(5 - adjustedIndex.length(), '0') + adjustedIndex;
						updatedAction = reqDetail.selectionLength + reqDetail.dataLength + reqDetail.data + adjustedIndex;
					}
					break;
				}
				reqDetail.index = adjustedIndex;
				reqDetail.msg = updatedAction;
			}
			else if (reqDetail.timestamp > action.timestamp + 5)
			{
				it = lastActions.erase(it);
			}
			if (!lastActions.empty())
			{
				++it;
			}
		}
	}
	return reqDetail;

}

void Communicator::handleError(SOCKET client_sock, std::exception a)
{
	std::lock_guard<std::recursive_mutex> stateLock(m_stateMutex);
	try
	{
		// Check if the client is associated with a file
		if (m_clients.find(client_sock) != m_clients.end())
		{
			ClientHandler* client = m_clients[client_sock];

			// Check if the client is currently working on a file
			if (client->getFileName() != "")
			{
				// Notify the client about the error
				std::string response = std::to_string(MC_ERROR_RESP);

				std::string fileContent = m_filesData[client->getFileId()];
				std::string lengthString = std::to_string(fileContent.length());
				lengthString = std::string(5 - lengthString.length(), '0') + lengthString;

				response += lengthString + fileContent;

				Helper::sendData(client_sock, BUFFER(response.begin(), response.end()));

				/*
				// If necessary, adjust and commit the client's request
				reqDetail = adjustIndexForSync(fileName, reqDetail);
				reqDetail.fileName = fileName;
				updateFileOnServer(fileName, reqDetail);

				// Notify all clients about the adjusted request
				std::string repCode = std::to_string(MC_ERR_ADJUSTED_RESP);
				notifyAllClients(repCode + reqDetail.msg, client_sock, true);

				// Update the last action map
				reqDetail.timestamp = getCurrentTimestamp();
				m_lastActionMap[fileName].push_back(reqDetail);
				*/
			}
			else
			{
				std::string initialFileContent = std::to_string(MC_ERROR_RESP) + a.what();
				Helper::sendData(client_sock, BUFFER(initialFileContent.begin(), initialFileContent.end()));
			}
		}
		else
		{
			std::string initialFileContent = std::to_string(MC_ERROR_RESP) + a.what();
			Helper::sendData(client_sock, BUFFER(initialFileContent.begin(), initialFileContent.end()));
		}
	}
	catch (const std::exception& e)
	{
	}
}

void Communicator::handleClientDisconnect(SOCKET client_sock)
{
	std::lock_guard<std::recursive_mutex> stateLock(m_stateMutex);
	// Check if the client is associated with a file
	if (m_clients.find(client_sock) != m_clients.end())
	{
		ClientHandler* disconnectedClient = m_clients[client_sock];
		std::string repCode = std::to_string(MC_DISCONNECT) + disconnectedClient->getUsername();

		// Check if the client is inside a file
		if (disconnectedClient->getFileName() != "")
		{
			int file = disconnectedClient->getFileId();
			std::string fileName = disconnectedClient->getFileName();
			std::string projectName = disconnectedClient->getProjectName();

			// Remove the client from the file's user list
			auto it = m_usersOnFile.find(file);
			if (it != m_usersOnFile.end())
			{
				it->second.erase(std::remove_if(it->second.begin(), it->second.end(),
					[disconnectedClient](const ClientHandler& client) {
						return client.getId() == disconnectedClient->getId();
					}), it->second.end());

				if (!it->second.empty()) {
					notifyAllfriends(repCode, client_sock);
				}

				if (m_usersOnFile[file].empty()) {
					// Delete the mutex and remove the file from m_usersOnFile
					m_fileMutexes.erase(file);
					m_usersOnFile.erase(file);
					m_lastActionMap.erase(file);
				}
			}
		}
		notifyAllfriends(repCode, client_sock);

		// Clean up resources and remove the client from the map
		delete disconnectedClient;
		m_clients.erase(client_sock);
	}
}

Action Communicator::deconstructReq(const std::string& req) {
	std::string msgCode = req.substr(0, 3);
	std::string action = req.substr(3);

	Action newAction;
	std::string indexString;

	switch (std::stoi(msgCode))
	{
	case MC_INITIAL_REQUEST:
		newAction.projectIdLength = std::stoi(action.substr(0, 5));
		newAction.projectId = std::stoi(action.substr(5, newAction.projectIdLength));

		newAction.fileNameLength = action.substr(5 + newAction.projectIdLength, 5);
		newAction.fileName = action.substr(10 + newAction.projectIdLength, std::stoi(newAction.fileNameLength));
		break;
	case MC_INSERT_REQUEST:
		newAction.dataLength = action.substr(0, 5);
		newAction.size = std::stoi(newAction.dataLength);

		newAction.data = action.substr(5, newAction.size);
		newAction.index = action.substr(5 + newAction.size, 5);
		newAction.newLineCount = action.substr(10 + newAction.size, 5);
		newAction.size += std::stoi(newAction.newLineCount);
		break;

	case MC_DELETE_REQUEST:
		newAction.dataLength = action.substr(0, 5);
		indexString = action.substr(5, 5);

		newAction.size = std::stoi(newAction.dataLength);
		newAction.index = indexString;
		newAction.newLineCount = action.substr(10, 5);
		break;

	case MC_REPLACE_REQUEST:
		newAction.selectionLength = action.substr(0, 5);
		newAction.dataLength = action.substr(5, 5);
		newAction.size = std::stoi(newAction.dataLength);
		newAction.data = action.substr(10, newAction.size);
		indexString = action.substr(10 + newAction.size, 5);
		newAction.index = indexString;
		newAction.newLineCount = action.substr(15 + newAction.size, 5);
		break;
	case MC_CREATE_FILE_REQUEST:
		newAction.projectIdLength = std::stoi(action.substr(0, 5));
		newAction.projectId = std::stoi(action.substr(5, newAction.projectIdLength));

		newAction.fileNameLength = action.substr(5 + newAction.projectIdLength, 5);
		newAction.fileName = action.substr(10 + newAction.projectIdLength, std::stoi(newAction.fileNameLength));
		break;
	case MC_DELETE_FILE_REQUEST:
		newAction.projectIdLength = std::stoi(action.substr(0, 5));
		newAction.projectId = std::stoi(action.substr(5, newAction.projectIdLength));

		newAction.fileNameLength = action.substr(5 + newAction.projectIdLength, 5);
		newAction.fileName = action.substr(10 + newAction.projectIdLength, std::stoi(newAction.fileNameLength));
		break;
	case MC_GET_FILES_REQUEST:
		//newAction.data = action;
		break;
	case MC_GET_CODE_STYLES_REQUEST:
		//newAction.data = action;
		break;
	case MC_GET_MAIL_IMAGES_REQUEST:
		//newAction.data = action;
		break;
	case MC_GET_CHAT_MESSAGES_REQUEST:
		newAction.data = action;
		break;
	case MC_GET_USERS_ON_FILE_REQUEST:
		newAction.data = action;
		break;
	case MC_GET_USERS_REQUEST:
		newAction.data = action;
	case MC_GET_USERS_PERMISSIONS_REQ_REQUEST:
		newAction.data = action;
		break;
	case MC_POST_MSG_REQUEST:
		newAction.projectIdLength = std::stoi(action.substr(0, 5));
		newAction.projectId = std::stoi(action.substr(5, newAction.projectIdLength));

		newAction.dataLength = action.substr(5 + newAction.projectIdLength, 5);
		newAction.data = action.substr(10 + newAction.projectIdLength, std::stoi(newAction.dataLength));
		break;
	case MC_APPROVE_PERMISSION_REQUEST:
		newAction.fileNameLength = action.substr(0, 5);
		newAction.size = std::stoi(newAction.fileNameLength);
		newAction.fileName = action.substr(5, newAction.size);
		newAction.userNameLength = std::stoi(action.substr(5 + newAction.size, 5));
		newAction.userName = action.substr(10 + newAction.size, newAction.userNameLength);
		break;
	case MC_REJECT_PERMISSION_REQUEST:
		newAction.fileNameLength = action.substr(0, 5);
		newAction.size = std::stoi(newAction.fileNameLength);
		newAction.fileName = action.substr(5, newAction.size);
		newAction.userNameLength = std::stoi(action.substr(5 + newAction.size, 5));
		newAction.userName = action.substr(10 + newAction.size, newAction.userNameLength);
		break;
	case MC_PERMISSION_FILE_REQ_REQUEST:
		newAction.fileNameLength = action.substr(0, 5);
		newAction.size = std::stoi(newAction.fileNameLength);
		newAction.fileName = action.substr(5, newAction.size);
		newAction.userNameLength = std::stoi(action.substr(5 + newAction.size, 5));
		newAction.userName = action.substr(10 + newAction.size, newAction.userNameLength);
	case MC_ENTER_FILE_REQUEST:
		newAction.dataLength = action.substr(0, 5);
		newAction.data = action.substr(5, std::stoi(newAction.dataLength));
		break;
	case MC_EXIT_FILE_REQUEST:
		newAction.data = action.substr(0, 5);
		break;
	case MC_LOGIN_REQUEST:
		newAction.userNameLength = std::stoi(action.substr(0, 5));
		newAction.userName = action.substr(5, newAction.userNameLength);
		newAction.email = newAction.userName;

		newAction.passLength = std::stoi(action.substr(5 + newAction.userNameLength, 5));
		newAction.pass = action.substr(10 + newAction.userNameLength, newAction.passLength);
		break;

	case MC_SIGNUP_REQUEST:
		newAction.userNameLength = std::stoi(action.substr(0, 5));
		newAction.userName = action.substr(5, newAction.userNameLength);

		newAction.passLength = std::stoi(action.substr(5 + newAction.userNameLength, 5));
		newAction.pass = action.substr(10 + newAction.userNameLength, newAction.passLength);

		newAction.emailLength = std::stoi(action.substr(10 + newAction.userNameLength + newAction.passLength, 5));
		newAction.email = action.substr(15 + newAction.userNameLength + newAction.passLength, newAction.emailLength);
		break;
	case MC_FORGOT_PASSW_REQUEST:
		newAction.userNameLength = std::stoi(action.substr(0, 5));
		newAction.userName = action.substr(5, newAction.userNameLength);
		newAction.email = newAction.userName;

		newAction.oldPassLength = std::stoi(action.substr(5 + newAction.userNameLength, 5));
		newAction.oldPass = action.substr(10 + newAction.userNameLength, newAction.oldPassLength);

		newAction.passLength = std::stoi(action.substr(10 + newAction.userNameLength + newAction.oldPassLength, 5));
		newAction.pass = action.substr(15 + newAction.userNameLength + newAction.oldPassLength, newAction.passLength);
		break;
	case MC_PROFILE_INFO_REQUEST:
		newAction.userNameLength = std::stoi(action.substr(0, 5));
		newAction.userName = action.substr(5, newAction.userNameLength);
		break;
	case MC_GET_PROFILE_IMAGE_REQUEST:
		newAction.userNameLength = std::stoi(action.substr(0, 5));
		newAction.userName = action.substr(5, newAction.userNameLength);
		break;
	case MC_FRIENDS_LIST_REQUEST:
		newAction.userNameLength = std::stoi(action.substr(0, 5));
		newAction.userName = action.substr(5, newAction.userNameLength);
		break;
	case MC_PROJECTS_LIST_REQUEST:
		newAction.userNameLength = std::stoi(action.substr(0, 5));
		newAction.userName = action.substr(5, newAction.userNameLength);
		break;
	case MC_REMOVE_FRIEND_REQUEST:
		newAction.userNameLength = std::stoi(action.substr(0, 5));
		newAction.userName = action.substr(5, newAction.userNameLength);

		newAction.userNameToRemoveLength = std::stoi(action.substr(5 + newAction.userNameLength, 5));
		newAction.userNameToRemove = action.substr(10 + newAction.userNameLength, newAction.userNameToRemoveLength);
		break;
	case MC_EDIT_PROFILE_INFO_REQUEST:
		newAction.bioLength = std::stoi(action.substr(0, 5));
		newAction.bio = action.substr(5, newAction.bioLength);
		break;
	case MC_CREATE_PROJECT_REQUEST:
		newAction.projectNameLength = std::stoi(action.substr(0, 5));
		newAction.projectName = action.substr(5, newAction.projectNameLength);

		newAction.firendsListLength = std::stoi(action.substr(5 + newAction.projectNameLength, 5));
		newAction.firendsList = action.substr(10 + newAction.projectNameLength, newAction.firendsListLength);

		newAction.codeLaneguageLength = std::stoi(action.substr(10 + newAction.projectNameLength + newAction.firendsListLength, 5));
		newAction.codeLaneguage = action.substr(15 + newAction.projectNameLength + newAction.firendsListLength, newAction.codeLaneguageLength);

		newAction.modeStr = action.substr(15 + newAction.projectNameLength + newAction.firendsListLength + newAction.codeLaneguageLength);
		if (newAction.modeStr == "True")
		{
			newAction.mode = true;
		}
		else
		{
			newAction.mode = false;
		}
		break;
	case MC_DELETE_PROJECT_REQUEST:
		newAction.projectIdLength = std::stoi(action.substr(0, 5));
		newAction.projectId = std::stoi(action.substr(5, newAction.projectIdLength));
		break;
	case MC_MOVE_TO_CREATE_PROJ_WINDOW_REQUEST:
		//newAction.data = action;
		break;
	case MC_MOVE_TO_MESSAGES_REQUEST:
		//newAction.data = action;
		break;
	case MC_MOVE_TO_TO_DO_LIST_REQUEST:
		//newAction.data = action;
		break;
	case MC_MOVE_TO_PROJECT_PAGE_REQUEST:
		newAction.projectId = std::stoi(action.substr());
		break;
	case MC_MOVE_TO_ISSUE_DATA_WINDOW_REQUEST:
		newAction.issueId = std::stoi(action.substr());
		break;
	case MC_ENTER_PROJECT_REQUEST:
		newAction.projectIdLength = std::stoi(action.substr(0, 5));
		newAction.projectId = std::stoi(action.substr(5, newAction.projectIdLength));
		break;
	case MC_GET_PROJECT_FILES_REQUEST:
		newAction.projectId = std::stoi(action.substr(0));
		break;
	case MC_USER_PROJECTS_LIST_REQUEST:
		newAction.userNameLength = std::stoi(action.substr(0, 5));
		newAction.userName = action.substr(5, newAction.userNameLength);
		break;
	case MC_EXIT_PROJECT_REQUEST:
		//
		break;
	case MC_LEAVE_PROJECT_REQUEST:
		newAction.projectIdLength = std::stoi(action.substr(0, 5));
		newAction.projectId = std::stoi(action.substr(5, newAction.projectIdLength));

		newAction.userNameLength = std::stoi(action.substr(5 + newAction.projectIdLength, 5));
		newAction.userName = action.substr(10 + newAction.projectIdLength, newAction.userNameLength);
		break;
	case MC_ACCEPT_PROJECT_INVITE_REQUEST:
		newAction.projectIdLength = std::stoi(action.substr(0, 5));
		newAction.projectId = std::stoi(action.substr(5, newAction.projectIdLength));

		newAction.userNameLength = std::stoi(action.substr(5 + newAction.projectIdLength, 5));
		newAction.userName = action.substr(10 + newAction.projectIdLength, newAction.userNameLength);

		break;
	case MC_DECLINE_PROJECT_INVITE_REQUEST:
		newAction.projectIdLength = std::stoi(action.substr(0, 5));
		newAction.projectId = std::stoi(action.substr(5, newAction.projectIdLength));

		newAction.userNameLength = std::stoi(action.substr(5 + newAction.projectIdLength, 5));
		newAction.userName = action.substr(10 + newAction.projectIdLength, newAction.userNameLength);
		break;
	case MC_RENAME_FILE_REQUEST:
		newAction.fileNameLength = action.substr(0, 5);
		newAction.size = std::stoi(newAction.fileNameLength);
		newAction.fileName = action.substr(5, newAction.size);

		newAction.oldFileNameLength = std::stoi(action.substr(5 + newAction.size, 5));
		newAction.oldFileName = action.substr(10 + newAction.size, newAction.oldFileNameLength);

		newAction.projectIdLength = std::stoi(action.substr(10 + newAction.size + newAction.oldFileNameLength, 5));
		newAction.projectName = action.substr(15 + newAction.size + newAction.oldFileNameLength, newAction.projectIdLength);
		break;
	case MC_SEARCH_REQUEST:
		newAction.searchCommandLength = std::stoi(action.substr(0, 5));
		newAction.searchCommand = action.substr(5, newAction.searchCommandLength);
		break;
	case MC_SEARCH_FRIENDS_REQUEST:
		newAction.searchCommandLength = std::stoi(action.substr(0, 5));
		newAction.searchCommand = action.substr(5, newAction.searchCommandLength);
		break;
	case MC_APPROVE_FRIEND_REQ_REQUEST:
		newAction.userIdLen = std::stoi(action.substr(0, 5));
		newAction.userId = std::stoi(action.substr(5, newAction.userIdLen));
		break;
	case MC_REJECT_FRIEND_REQ_REQUEST:
		newAction.userIdLen = std::stoi(action.substr(0, 5));
		newAction.userId = std::stoi(action.substr(5, newAction.userIdLen));
		break;
	case MC_ADD_FRIEND_REQUEST:
		newAction.userNameLength = std::stoi(action.substr(0, 5));
		newAction.userName = action.substr(5, newAction.userNameLength);
		break;
	case MC_BACK_TO_HOME_PAGE_REQUEST:
		break;
	case MC_BACK_TO_PROJECT_PAGE_REQUEST:
		break;
	case MC_EDIT_PROJECT_INFO_REQUEST:
		newAction.projectIdLength = std::stoi(action.substr(0, 5));
		newAction.projectId = std::stoi(action.substr(5, newAction.projectIdLength));
		break;
	case MC_VIEW_PROJECT_INFO_REQUEST:
		newAction.projectIdLength = std::stoi(action.substr(0, 5));
		newAction.projectId = std::stoi(action.substr(5, newAction.projectIdLength));
		break;
	case MC_MODIFY_PROJECT_INFO_REQUEST:
		newAction.projectIdLength = std::stoi(action.substr(0, 5));
		newAction.projectId = std::stoi(action.substr(5, newAction.projectIdLength));

		newAction.projectNameLength = std::stoi(action.substr(5 + newAction.projectIdLength, 5));
		newAction.projectName = action.substr(10 + newAction.projectIdLength, newAction.projectNameLength);

		newAction.firendsListLength = std::stoi(action.substr(10 + newAction.projectNameLength + newAction.projectIdLength, 5));
		newAction.firendsList = action.substr(15 + newAction.projectIdLength + newAction.projectNameLength, newAction.firendsListLength);

		newAction.codeLaneguageLength = std::stoi(action.substr(15 + newAction.projectIdLength + newAction.projectNameLength + newAction.firendsListLength, 5));
		newAction.codeLaneguage = action.substr(20 + newAction.projectIdLength + newAction.projectNameLength + newAction.firendsListLength, newAction.codeLaneguageLength);

		newAction.modeStr = action.substr(20 + newAction.projectIdLength + newAction.projectNameLength + newAction.firendsListLength + newAction.codeLaneguageLength);
		if (newAction.modeStr == "True")
		{
			newAction.mode = true;
		}
		else
		{
			newAction.mode = false;
		}
		break;
	case MC_GET_PROJECT_INFO_REQUEST:
		newAction.projectNameLength = std::stoi(action.substr(0, 5));
		newAction.projectName = action.substr(5, newAction.projectNameLength);
		break;
	case MC_GET_MESSAGES_REQUEST:
		newAction.userNameLength = std::stoi(action.substr(0, 5));
		newAction.userName = action.substr(5, newAction.userNameLength);
		break;
	case MC_GET_MSG_COUNT_REQUEST:
		newAction.userNameLength = std::stoi(action.substr(0, 5));
		newAction.userName = action.substr(5, newAction.userNameLength);
		break;
	case MC_MARK_AS_READ_REQUEST:
		newAction.messageId = std::stoi(action.substr(0, 5));
		break;
	case MC_MARK_ALL_AS_READ_REQUEST:
		break;
	case MC_GET_CURRENT_PROJECT_ISSUES_REQUEST:
		newAction.projectId = std::stoi(action.substr());
		break;
	case MC_GET_COMPLETED_PROJECT_ISSUES_REQUEST:
		newAction.projectId = std::stoi(action.substr());
		break;
	case MC_GET_ISSUE_REQUEST:
		newAction.issueId = std::stoi(action.substr());
		break;
	case MC_MARK_TASK_AS_COMPLETED_REQUEST:
		newAction.issueId = std::stoi(action.substr());
		break;
	case MC_MARK_TASK_AS_NOT_COMPLETED_REQUEST:
		newAction.issueId = std::stoi(action.substr());
		break;
	case MC_ADD_TASK_REQUEST:
		newAction.issueDataLength = std::stoi(action.substr(0, 5));
		newAction.issueData = action.substr(5, newAction.issueDataLength);

		newAction.assignedPeopleLength = std::stoi(action.substr(5 + newAction.issueDataLength, 5));
		newAction.assignedPeople = action.substr(10 + newAction.issueDataLength, newAction.assignedPeopleLength);

		newAction.issueDateLength = std::stoi(action.substr(10 + newAction.issueDataLength + newAction.assignedPeopleLength, 5));
		newAction.issueDate = action.substr(15 + newAction.issueDataLength + newAction.assignedPeopleLength, newAction.issueDateLength);
		break;
	case MC_MODIFY_ISSUE_REQUEST:
		newAction.issueDataLength = std::stoi(action.substr(0, 5));
		newAction.issueData = action.substr(5, newAction.issueDataLength);

		newAction.assignedPeopleLength = std::stoi(action.substr(5 + newAction.issueDataLength, 5));
		newAction.assignedPeople = action.substr(10 + newAction.issueDataLength, newAction.assignedPeopleLength);

		newAction.issueDateLength = std::stoi(action.substr(10 + newAction.issueDataLength + newAction.assignedPeopleLength, 5));
		newAction.issueDate = action.substr(15 + newAction.issueDataLength + newAction.assignedPeopleLength, newAction.issueDateLength);
		newAction.issueId = std::stoi(action.substr(15 + newAction.issueDataLength + newAction.assignedPeopleLength + newAction.issueDateLength));
		break;
	case MC_DELETE_TASK_REQUEST:
		newAction.issueId = std::stoi(action.substr());
		break;
	case MC_GET_PROJECT_PATICIPANTS_REQUEST:
		newAction.projectId = std::stoi(action.substr());
		break;
	case MC_SETTINGS_REQUEST:
		break;
	case MC_RECEIVE_FILES_REQUEST:
		break;
	case MC_VERSION_REQUEST:
		break;
	}
	newAction.timestamp = getCurrentTimestamp();
	newAction.code = std::stoi(msgCode);
	newAction.msg = action;
	return newAction;
}

void Communicator::updateFileOnServer(const int fileId, const Action& reqDetail)
{
	auto it = m_filesData.find(fileId);

	if (it != m_filesData.end()) {
		int index;
		int length;
		switch (reqDetail.code) {
		case MC_INSERT_REQUEST:
			// Insert operation
			if (std::stoi(reqDetail.index) <= it->second.size()) {
				it->second.insert(std::stoi(reqDetail.index), reqDetail.data);
			}
			else {
				throw std::runtime_error("Insert index out of range");
			}
			break;

		case MC_DELETE_REQUEST:
			// Delete operation
			if (std::stoi(reqDetail.index) < it->second.size()) {
				it->second.erase(std::stoi(reqDetail.index), reqDetail.size);
			}
			else {
				throw std::runtime_error("Delete index out of range");
			}
			break;

		case MC_REPLACE_REQUEST:
			// Replace operation
			index = std::stoi(reqDetail.index);
			length = std::stoi(reqDetail.selectionLength);
			if (index < it->second.size() &&
				index + length <= it->second.size()) {
				it->second.erase(index, length);
				it->second.insert(index, reqDetail.data);
			}
			else {
				throw std::runtime_error("Replace index or selection length out of range");
			}
			break;

		default:
			throw std::runtime_error("Unknown action code: " + reqDetail.code);
		}
	}
	else {
		throw std::runtime_error("File not found: " + std::to_string(fileId) );
	}



}

void Communicator::updateFileOnServerold(const std::string& filePath, const Action& reqDetail)
{
	std::fstream file(filePath, std::ios::in | std::ios::out);
	if (!file.is_open()) {
		throw std::runtime_error("Failed to open file for reading/writing: " + filePath);
	}
	else {
		switch (reqDetail.code) {
		case MC_INSERT_REQUEST:
			// Insert operation
			operationHandler.insert(file, reqDetail.data, (std::stoi(reqDetail.index) + std::stoi(reqDetail.newLineCount)));
			break;

		case MC_DELETE_REQUEST:
			// Delete operation
			operationHandler.deleteContent(file, std::stoi(reqDetail.dataLength), (std::stoi(reqDetail.index) + std::stoi(reqDetail.newLineCount)),
				reqDetail.fileName);
			break;

		case MC_REPLACE_REQUEST:
			// Replace operation
			operationHandler.replace(file, std::stoi(reqDetail.selectionLength), reqDetail.data,
				(std::stoi(reqDetail.index) + std::stoi(reqDetail.newLineCount)), reqDetail.fileName);
			break;

		default:
			throw std::runtime_error("Unknown action code: " + reqDetail.code);
		}

		file.close();
	}
}

void Communicator::notifyAllClients(const std::string& updatedContent, SOCKET client_sock, const bool isOnFile)
{
	// Iterate through all connected clients and send them the updated content
	for (auto& sock : m_clients)
	{
		if (sock.first != client_sock)
		{
			if (isOnFile && m_clients[client_sock]->getFileName() == m_clients[sock.first]->getFileName())
			{
				SOCKET client_sock = sock.first;
				Helper::sendData(client_sock, BUFFER(updatedContent.begin(), updatedContent.end()));
			}
			else if (!isOnFile && m_clients[sock.first]->getFileName() == "")
			{
				SOCKET client_sock = sock.first;
				Helper::sendData(client_sock, BUFFER(updatedContent.begin(), updatedContent.end()));
			}
		}
	}
}

void Communicator::notifyAllfriends(const std::string& updatedContent, SOCKET client_sock)
{
	// Get the username of the client who triggered the notification
	std::string name = m_clients[client_sock]->getUsername();

	// Find the client's friends list
	auto friendsList = m_friends.find(name);
	if (friendsList == m_friends.end()) {
		// If client has no friends, do nothing
		return;
	}

	// Iterate through all connected clients
	for (auto& pair : m_clients)
	{
		SOCKET friend_sock = pair.first;
		ClientHandler* client = pair.second;

		// Skip sending to the client who triggered the update
		if (friend_sock == client_sock) continue;

		if (m_clients[friend_sock]->getWindow() == CREATE_PROJECT_PAGE) continue;
		if (m_clients[friend_sock]->getWindow() == PROJECT_PAGE) continue;
		if (m_clients[friend_sock]->getWindow() == CREATE_PROJECT_PAGE) continue;
		if (m_clients[friend_sock]->getWindow() == TO_TO_LIST_PAGE) continue;
		if (m_clients[friend_sock]->getWindow() == ISSUE_DATA_PAGE) continue;
		if (m_clients[friend_sock]->getWindow() == MESSAGES_PAGE) continue;
		if (m_clients[friend_sock]->getWindow() == SETTINGS_PAGE) continue;
		if (m_clients[friend_sock]->getWindow() == SEARCH_PAGE) continue;

		// Check if the current client is in the friend list of the client who triggered the update
		if (friendsList->second.end() != std::find(friendsList->second.begin(), friendsList->second.end(), client->getUsername()))
		{
			// Send updated content to the friend
			Helper::sendData(friend_sock, BUFFER(updatedContent.begin(), updatedContent.end()));
		}
	}
}

void Communicator::notifyAllclientsOnProject(const std::string& updatedContent, SOCKET client_sock, std::string msg)
{
	// Get the username of the client who triggered the notification
	std::string name = m_clients[client_sock]->getUsername();
	std::string projectName = m_clients[client_sock]->getProjectName();
	Project project = m_database->getProject(projectName, -1);
	int currUserId = m_database->getUserId(name);


	// Find the client's friends list
	auto friendsList = m_friends.find(name);
	if (friendsList == m_friends.end()) {
		// If client has no friends, do nothing
		return;
	}

	// Iterate through all connected clients
	for (auto& pair : m_clients)
	{
		SOCKET friend_sock = pair.first;
		ClientHandler* client = pair.second;

		// Skip sending to the client who triggered the update
		if (friend_sock == client_sock) continue;

		int reciverId = m_database->getUserId(m_clients[friend_sock]->getUsername());

		if (updatedContent.length() > 0)
		{
			if (m_clients[friend_sock]->getWindow() == PROJECT_PAGE
				&& m_clients[friend_sock]->getProjectName() != projectName)
			{
				Helper::sendData(friend_sock, BUFFER(updatedContent.begin(), updatedContent.end()));
				continue;
			}
		}

		if (msg.length() > 0)
		{
			if (m_clients[friend_sock]->getWindow() == HOME_PAGE
				&& m_database->hasPermissionToProject(project.projectId, reciverId))
			{
				std::string repCode = std::to_string(MC_SEND_MSG_RESP);
				Helper::sendData(friend_sock, BUFFER(repCode.begin(), repCode.end()));
				continue;
			}
			m_database->AddMsg(currUserId, reciverId, msg, 0, -1);
		}
	}
}

void Communicator::notifyAllclientsOnIssueWindow(const std::string& updatedContent, SOCKET client_sock, std::string msg)
{
	// Get the username of the client who triggered the notification
	std::string name = m_clients[client_sock]->getUsername();
	std::string projectName = m_clients[client_sock]->getProjectName();
	Project project = m_database->getProject(projectName, -1);
	int currUserId = m_database->getUserId(name);
	int issueId = m_clients[client_sock]->getIssueId();

	// Iterate through all connected clients
	for (auto& pair : m_clients)
	{
		SOCKET friend_sock = pair.first;
		ClientHandler* client = pair.second;

		// Skip sending to the client who triggered the update
		if (friend_sock == client_sock) continue;

		int reciverId = m_database->getUserId(m_clients[friend_sock]->getUsername());
		std::string reciverName = pair.second->getUsername();

		if (updatedContent.length() > 0)
		{
			if (m_clients[friend_sock]->getWindow() == TO_TO_LIST_PAGE
				&& m_clients[friend_sock]->getProjectName() == projectName)
			{
				Helper::sendData(friend_sock, BUFFER(updatedContent.begin(), updatedContent.end()));
				continue;
			}
		}

		if (msg.length() > 0)
		{
			if (m_clients[friend_sock]->getWindow() == HOME_PAGE
				&& m_database->hasPermissionToProject(project.projectId, reciverId) && m_database->inIssue(issueId, pair.second->getUsername()))
			{
				m_database->AddMsg(currUserId, reciverId, msg, 0, -1);
				std::string repCode = std::to_string(MC_SEND_MSG_RESP);
				Helper::sendData(friend_sock, BUFFER(repCode.begin(), repCode.end()));
				continue;
			}
			m_database->AddMsg(currUserId, reciverId, msg, 0, -1);
		}
	}
}

long long Communicator::getCurrentTimestamp() {
	auto currentTime = std::chrono::system_clock::now();
	auto duration = currentTime.time_since_epoch();

	// Convert duration to milliseconds
	auto milliseconds = std::chrono::duration_cast<std::chrono::milliseconds>(duration);

	// Convert milliseconds to a long long value
	return milliseconds.count();
}

void Communicator::startHandleRequests()
{
	SOCKET client_socket;
	bindAndListen();
	while (true)
	{
		client_socket = accept(m_serverSocket, NULL, NULL);
		if (client_socket == INVALID_SOCKET)
			throw std::exception("Recieved an invalid socket.");
		std::thread t(&Communicator::handleNewClient, this, client_socket);
		t.detach();
	}
}

void Communicator::saveFiles(/* Parameters for communication */) {
	try {
		while (true) {
			std::unique_lock<std::recursive_mutex> stateLock(m_stateMutex);
			if (needUpdate) {
				for (auto it = m_FileUpdate.begin(); it != m_FileUpdate.end(); ) {
					const int& fileId = it->first;
					bool updated = it->second;

					if (updated) {
						{
							std::lock_guard<std::mutex> lock(m_fileMutexes[fileId]);
							m_database->updateFile(fileId, m_filesData[fileId]);
						}
						m_FileUpdate[fileId] = false; // Reset the change flag
					}

					if (m_usersOnFile[fileId].empty()) {
						m_filesData.erase(fileId);
						m_fileMutexes.erase(fileId);
						it = m_FileUpdate.erase(it);
						m_usersOnFile.erase(fileId);
					}
					else {
						++it;
					}

					if (m_FileUpdate.empty()) {
						needUpdate = false;
						break;
					}
				}
			}
			stateLock.unlock();
			std::this_thread::sleep_for(std::chrono::seconds(60));
		}
	}
	catch (const std::exception& e) {
		// Handle the exception appropriately, e.g., logging
		std::cerr << "Exception in saveFiles: " << e.what() << std::endl;
	}
}

void Communicator::checkClientsConnection(/* Parameters for communication */) {
	try {
		while (true) {
			
			std::this_thread::sleep_for(std::chrono::seconds(20));

			std::lock_guard<std::recursive_mutex> stateLock(m_stateMutex);

			for (auto it = m_clients.begin(); it != m_clients.end(); ++it) {
				if (!isSocketConnected(it->first)) {
					// Don't delete the handler here: its client thread may still be using it.
					// Shutting the socket down makes that thread's recv() fail, and it then
					// cleans up through handleClientDisconnect().
					shutdown(it->first, SD_BOTH);
				}
			}
		}
	}
	catch (const std::exception& e) {
		// Handle the exception appropriately, e.g., logging
		std::cerr << "Exception in check connection: " << e.what() << std::endl;
	}
}

// Streams a file to the installer.
// withHeader: first send one framed message "500<size>" (or "200<error>") so the
// installer can show progress and detect a truncated download. Installers from
// before framing get the raw bytes only.
void Communicator::sendFile(SOCKET clientSocket, const std::string& filePath, bool withHeader) {
	std::ifstream file(filePath, std::ios::binary | std::ios::ate);
	if (!file.is_open()) {
		std::cerr << "Failed to open file: " << filePath << std::endl;
		if (withHeader) {
			std::string error = std::to_string(MC_ERROR_RESP) + "The installation package is not available on the server.";
			Helper::sendData(clientSocket, BUFFER(error.begin(), error.end()));
		}
		return;
	}

	std::streamsize fileSize = file.tellg();
	file.seekg(0, std::ios::beg);

	if (withHeader) {
		std::string header = std::to_string(MC_RECEIVE_FILES_REQUEST) + std::to_string(fileSize);
		Helper::sendData(clientSocket, BUFFER(header.begin(), header.end()));
	}

	char buffer[64 * 1024];
	while (fileSize > 0) {
		file.read(buffer, sizeof(buffer));
		std::streamsize bytesRead = file.gcount();
		if (bytesRead <= 0) {
			break;
		}
		try {
			Helper::sendRaw(clientSocket, std::string(buffer, static_cast<size_t>(bytesRead)));
		}
		catch (const std::exception&) {
			std::cerr << "Send failed with error: " << WSAGetLastError() << std::endl;
			break;
		}
		fileSize -= bytesRead;
	}

	file.close();
}
