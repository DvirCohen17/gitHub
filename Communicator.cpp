#include "Communicator.h"
#include <cstdio> // For popen and pclose

extern std::unordered_map<std::string, std::mutex> m_fileMutexes;
const std::unordered_map<std::string, std::string> codeStyles = {
	{"C++-Mode", ".\\codeStyles\\C++-Mode.xshd"},
	{"Python-Mode", ".\\codeStyles\\Python-Mode.xshd"},
	{"C#-Mode", ".\\codeStyles\\C#-Mode.xshd"},
	{"Java-Mode", ".\\codeStyles\\Java-Mode.xshd"},
	{"JavaScript-Mode", ".\\codeStyles\\JavaScript-Mode.xshd"}
};

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

void Communicator::login(SOCKET client_sock,
	std::string username, std::string pass, std::string mail)
{
	bool check = false;
	for (auto it = m_clients.begin(); it != m_clients.end(); ++it)
	{
		if (it->second->getUsername() == username || it->second->getEmail() == mail)
		{
			throw std::exception("User already logged in");
			check = true;  // Indicate that the response has been sent
			break;  // Exit the loop
		}
	}

	// If the response has been sent, don't proceed to the second condition
	if (!check)
	{
		if (m_database->doesUserExist(username) && m_database->doesPasswordMatch(username, pass))
		{
			std::string repCode = std::to_string(MC_LOGIN_RESP);
			username = m_database->getUserName(mail, -1);
			mail = m_database->getEmail(username);
			ClientHandler* client_handler = new ClientHandler(m_database->getUserId(username), username, mail);
			client_handler->setWindow("HomePage");
			m_clients[client_sock] = client_handler;

			std::string lengthString = std::to_string(username.length());
			lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
			repCode += lengthString + username + std::to_string(client_handler->getId());

			Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

			repCode = std::to_string(MC_LOGIN_RESP) + username;
			
			std::string friendsList = m_database->getUserFriends(m_database->getUserId(username)).fiendsList;
			int currentIndex = 0;
			while (currentIndex < friendsList.length())
			{
				// Extract data length for each message
				int dataLength = std::stoi(friendsList.substr(currentIndex, 5));
				currentIndex += 5;

				// Extract data from the response
				std::string name = friendsList.substr(currentIndex, dataLength);
				currentIndex += dataLength;

				m_friends[username].push_back(name);
			}

			notifyAllfriends(repCode, client_sock);

			std::string dirName = username +"'s projects";
			_dirName = dirName;
		}
		else
		{
			throw std::exception("invalid username or password.");
		}
	}
}

void Communicator::logout(SOCKET client_sock)
{
	std::string repCode = std::to_string(MC_LOGOUT_RESP);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
	handleClientDisconnect(client_sock);
}

void Communicator::signUp(SOCKET client_sock, 
	std::string username, std::string pass, std::string mail)
{
	if (!m_database->doesUserExist(username) && !m_database->doesUserExist(mail))
	{
		m_database->addNewUser(username, pass, mail);
		int userId = m_database->getUserId(username);
		m_database->createProfile(username, mail, "", userId);
		std::string repCode = std::to_string(MC_SIGNUP_RESP);
		ClientHandler* client_handler = new ClientHandler(userId, username, mail);
		client_handler->setWindow("HomePage");
		m_clients[client_sock] = client_handler;
		std::string initialFileContent = repCode + std::to_string(client_handler->getId());
		Helper::sendData(client_sock, BUFFER(initialFileContent.begin(), initialFileContent.end()));

		repCode += username;

		std::string friendsList = m_database->getUserFriends(m_database->getUserId(username)).fiendsList;
		int currentIndex = 0;
		while (currentIndex < friendsList.length())
		{
			// Extract data length for each message
			int dataLength = std::stoi(friendsList.substr(currentIndex, 5));
			currentIndex += 5;

			// Extract data from the response
			std::string name = friendsList.substr(currentIndex, dataLength);
			currentIndex += dataLength;

			m_friends[username].push_back(name);
		}

		notifyAllfriends(repCode, client_sock);

		std::string dirName = username + "'s projects";
		_dirName = dirName;
	}
	else
	{
		throw std::exception("Invalid name or email");
	}
}

void Communicator::forgotPassword(SOCKET client_sock,
	std::string username, std::string pass, std::string oldPass, std::string mail)
{
	bool check = false;
	for (auto it = m_clients.begin(); it != m_clients.end(); ++it)
	{
		if (it->second->getUsername() == username || it->second->getEmail() == mail)
		{
			throw std::exception("User logged in, cant change password");
			check = true;  // Indicate that the response has been sent
			break;  // Exit the loop
		}
	}

	// If the response has been sent, don't proceed to the second condition
	if (!check)
	{
		if (m_database->doesUserExist(username) && m_database->doesPasswordMatch(username, oldPass))
		{
			std::string repCode = std::to_string(MC_FORGOT_PASSW_RESP);
			username = m_database->getUserName(mail, -1);
			mail = m_database->getEmail(username);
			m_database->changePassword(username, oldPass, pass);
			ClientHandler* client_handler = new ClientHandler(m_database->getUserId(username), username, mail);
			m_clients[client_sock] = client_handler;

			std::string lengthString = std::to_string(username.length());
			lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
			repCode += lengthString + username + std::to_string(client_handler->getId());

			Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

			repCode = std::to_string(MC_LOGIN_RESP) + username;
			notifyAllClients(repCode, client_sock, false);
		}
		else
		{
			throw std::exception("invalid username or password.");
		}
	}
}

void Communicator::createFile(SOCKET client_sock, std::string fileName, int projectId)
{
	// Check if the file with the specified name exists
	//if (fileOperationHandler.fileExists(".\\files\\" + reqDetail.data + ".txt"))
	Project project = m_database->getProject("", projectId);
	if (m_database->getFileDetails(fileName, projectId).fileName != "")
	{
		// File already exists, send an appropriate response code
		throw std::exception("file already exists");
	}
	else
	{
		//fileOperationHandler.createFile(".\\" + _dirName + "\\\\" + projectName + "\\" + fileName, true);

		Action emptyAction;
		// Create the mutex for the new file
		m_database->addFile(m_clients[client_sock]->getId(), fileName, project.projectId);

		FileDetail fileList = m_database->getFileDetails(fileName, project.projectId);
		std::map<std::string, int>& filesMap = m_projects[project.projectId];
		filesMap[fileName] = fileList.fileId;

		m_fileMutexes[fileList.fileId];
		m_filesData[fileList.fileId] = "";

		//m_database->addUserPermission(m_clients[client_sock]->getId(), m_fileNames[fileName + ".txt"]);

		std::string repCode = std::to_string(MC_ADD_FILE_RESP) + fileName;
		m_clients[client_sock]->setFile(fileList.fileName, "", fileList.fileId);
		Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
		notifyAllclientsOnProject(repCode, client_sock);
	}
}

void Communicator::deleteFile(SOCKET client_sock, std::string fileName, int projectId)
{
	FileDetail file = m_database->getFileDetails(fileName, projectId);
	if (m_usersOnFile.find(file.fileId) != m_usersOnFile.end()
		&& !m_usersOnFile[file.fileId].empty())
	{
		throw std::exception("cannot delete. Someone is inside");
	}
	else if (!m_database->hasPermissionToProject( projectId, m_clients[client_sock]->getId()))
	{
		throw std::exception("dont have permission for this file");
	}
	else
	{
		std::string repCode = std::to_string(MC_DELETE_FILE_RESP) + fileName;

		//fileOperationHandler.deleteFile(".\\" + _dirName + "\\\\" + projectName + "\\" + fileName); // decide if needs to be removed later

		m_database->deleteFile(fileName, projectId);
		//m_database->deletePermission(m_fileNames[fileName + ".txt"]);
		//m_database->deleteAllPermissionReq(m_fileNames[fileName + ".txt"]);
		//m_fileNames.erase(fileName + ".txt");
		auto projectIt = m_projects.find(projectId);
		if (projectIt != m_projects.end()) {
			// Find the file in the inner map
			std::map<std::string, int>& filesMap = projectIt->second;
			auto fileIt = filesMap.find(fileName);
			if (fileIt != filesMap.end()) {
				// Erase the file from the inner map
				filesMap.erase(fileIt);
				}
			}

		Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
		notifyAllclientsOnProject(repCode, client_sock);
	}
}

void Communicator::getFiles(SOCKET client_sock)
{
	std::string repCode = std::to_string(MC_GET_FILES_RESP);
	std::string msg = "";
	// Access the correct inner map using the project name
	int project = m_clients[client_sock]->getProjectId();
	std::map<std::string, int>& projectFiles = m_projects[project];

	//fileOperationHandler.getFilesInDirectory(".\\files", projectFiles);

	//std::map<std::string, int> filesPermissions = m_database->getUserPermissionDetails(m_clients[client_sock]->getId());

	for (const auto& file : m_database->getProjectFiles(project))
	{
		// for file permissions
		/*
		if (filesPermissions.find(fileName.first) != filesPermissions.end())
		{
			std::string lengthString = std::to_string(fileName.first.length());
			lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
			repCode += lengthString + fileName.first + std::to_string(filesPermissions[fileName.first]);
		}
		else
		{
			std::string lengthString = std::to_string(fileName.first.length());
			lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
			repCode += lengthString + fileName.first + std::to_string(2); // don't have
		}
		*/ 

		std::string lengthString = std::to_string(file.fileName.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + file.fileName;
		//FileDetail fileList = m_database->getFileDetails(fileName.first);
		projectFiles[file.fileName] = file.fileId;
	}
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::getInitialContent(SOCKET client_sock, std::string fileName, int projectId)
{
	std::string fileContent;
	Action emptyAction;

	std::string repCode = std::to_string(MC_INITIAL_RESP);
	Project project = m_database->getProject("", projectId);
	FileDetail fileList = m_database->getFileDetails(fileName, project.projectId);

	if (m_FileUpdate[fileList.fileId])
	{
		fileContent = m_filesData[fileList.fileId];
	}
	else
	{
		//fileContent = fileOperationHandler.readFromFile(".\\" + _dirName + "\\\\" + projectName + "\\" + fileName);
		fileContent = m_database->getFileContent(fileList.fileId);
		m_filesData[fileList.fileId] = fileContent;
		std::map<std::string, int>& filesMap = m_projects[project.projectId];
		filesMap[fileName] = fileList.fileId;
	}
	
	// Convert the length to a string with exactly 5 digits
	std::string lengthString = std::to_string(fileContent.length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;

	emptyAction.code = MC_INITIAL_REQUEST;
	m_lastActionMap[fileList.fileId].push_back(emptyAction);
	m_clients[client_sock]->setFile(fileName, fileContent, fileList.fileId);
	// Create the initialFileContent string
	std::string initialFileContent = repCode + lengthString + fileContent;
	Helper::sendData(client_sock, BUFFER(initialFileContent.begin(), initialFileContent.end()));

}

void Communicator::enterFile(SOCKET client_sock, std::string fileName, std::string fileNameLen)
{
	std::string projectName = m_clients[client_sock]->getProjectName();
	Project project = m_database->getProject(projectName, -1);

	FileDetail file = m_database->getFileDetails(fileName, project.projectId);
	std::string repCode;

	/*
	if (!m_database->hasPermission(m_clients[client_sock]->getId(), m_database->getFileDetails(fileName).fileId)) {
		// Send an error response indicating lack of permission
		std::string errMsg = "You are not allowed to join this file" + fileNameLen+ fileName;
		throw std::exception(errMsg.c_str());
	}
	*/
	repCode = std::to_string(MC_ENTER_FILE_RESP);
	

	m_clients[client_sock]->setFile(file.fileName, "", file.fileId);
	// Create the mutex for the file if it doesn't exist
	m_fileMutexes.try_emplace(file.fileId);

	m_usersOnFile[file.fileId].push_back(*m_clients[client_sock]);
}

void Communicator::exitFile(SOCKET client_sock)
{
	std::string repCode = std::to_string(MC_EXIT_FILE_RESP);
	int file = m_clients[client_sock]->getFileId();

	for (auto it = m_usersOnFile.begin(); it != m_usersOnFile.end(); ++it) {
		// Iterate over the array of clients for each file
		for (auto clientIt = it->second.begin(); clientIt != it->second.end(); ) {
			if (clientIt->getId() == m_clients[client_sock]->getId()) {
				clientIt = it->second.erase(clientIt);
			}
			else {
				++clientIt;
			}
		}
	}
	
	// Check if the user leaving was the last one
	if (m_usersOnFile[file].empty()) {
		// Delete the mutex and remove the file from m_usersOnFile
		m_usersOnFile.erase(file);
		m_lastActionMap.erase(file);
		m_fileMutexes.erase(file);
	}
	m_clients[client_sock]->setFile("", "", -1);

}

void Communicator::getMesegges(SOCKET client_sock, std::string projectName)
{
	std::string repCode = std::to_string(MC_GET_FILES_RESP);
	int projectId = std::stoi(projectName);

	// Handle get messages request
	repCode = std::to_string(MC_GET_MESSAGES_RESP);
	std::string chatContent = executeCommand("main.exe decrypt \'" + m_database->GetChatData(projectId) + "\'");
	chatContent = chatContent.substr(0, chatContent.length() - 1);
	repCode += chatContent;
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::getUsersOnFile(SOCKET client_sock, std::string fileName)
{
	std::string repCode = std::to_string(MC_GET_USERS_ON_FILE_RESP);
	std::string lengthString;

	/*
	// Get the list of users logged into the file
	for (const auto& user : m_usersOnFile[".\\files\\" + fileName]) {
		lengthString = std::to_string((user.getUsername().length()));
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + user.getUsername();
	}
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
	*/
}

void Communicator::getUsers(SOCKET client_sock)
{
	// Handle get users request
	std::string repCode = std::to_string(MC_GET_USERS_RESP);
	std::string lengthString;
	for (auto& sock : m_clients)
	{
		lengthString = std::to_string(m_clients[sock.first]->getUsername().length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + m_clients[sock.first]->getUsername();

		// Add file name length and file name to the response
		std::string fileName = m_clients[sock.first]->getFileName();
		lengthString = std::to_string(fileName.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + fileName;
	}
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::getUserPermissionReq(SOCKET client_sock)
{
	// Handle get users request
	std::string repCode = std::to_string(MC_APPROVE_REQ_RESP);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

	repCode = std::to_string(MC_GET_USERS_PERMISSIONS_REQ_RESP);
	std::string lengthString;
	for (auto& req : m_database->getPermissionRequests(m_clients[client_sock]->getId()))
	{
		lengthString = std::to_string(m_database->getUserName("", req.userId).length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + m_database->getUserName("", req.userId);

		// Add file name length and file name to the response
		lengthString = std::to_string(m_database->getFileName(req.fileId).length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + m_database->getFileName(req.fileId);
	}
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::postMsg(SOCKET client_sock, int projectId, std::string data, std::string dataLen)
{
	std::string repCode = std::to_string(MC_POST_MSG_RESP);
	
	std::string userName = m_clients[client_sock]->getUsername();
	// Handle post message request
	std::string lengthString = std::to_string(userName.length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;

	std::string chatMsg = executeCommand("main.exe decrypt \'" + m_database->GetChatData(projectId) + "\'");
	chatMsg = chatMsg.substr(0, chatMsg.length() - 1);
	chatMsg += dataLen + data +
		lengthString + userName;
	m_database->UpdateChat(projectId, executeCommand("main.exe encrypt \'" + chatMsg + "\'"));

	repCode += dataLen + data + lengthString + userName;
	notifyAllclientsOnProject(repCode, client_sock);
}

void Communicator::approvePermissionReq(SOCKET client_sock, std::string username, std::string filename)
{	std::string repCode = std::to_string(MC_APPROVE_PERMISSION_RESP);

	//m_database->deletePermissionRequests(m_database->getUserId(username),
		//m_database->getFileDetails(filename).fileId);
	//m_database->addUserPermission(m_database->getUserId(username),
		//m_database->getFileDetails(filename).fileId);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

	for (auto& sock : m_clients)
	{
		if (sock.second->getUsername() == username)
		{
			repCode += filename;
			Helper::sendData(sock.first, BUFFER(repCode.begin(), repCode.end()));

		}
	}
}

void Communicator::rejectPermissionReq(SOCKET client_sock, std::string username, std::string filename)
{
	std::string repCode = std::to_string(MC_REJECT_PERMISSION_RESP);

	//m_database->deletePermissionRequests(m_database->getUserId(username), m_database->getFileDetails(filename).fileId);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

	for (auto& sock : m_clients)
	{
		if (sock.second->getUsername() == username)
		{
			repCode += filename;
			Helper::sendData(sock.first, BUFFER(repCode.begin(), repCode.end()));

		}
	}
}

void Communicator::permissionFileReq(SOCKET client_sock, std::string username, 
	std::string filename, std::string fileNameLen)
{
	std::string repCode;

	//FileDetail fileList = m_database->getFileDetails(filename);
	
	/*if (!m_database->doesPermissionRequestExist(m_database->getUserId(username), fileList.fileId, fileList.creatorId))
	{
		repCode = std::to_string(MC_PERMISSION_FILE_REQ_RESP);
		m_database->addPermissionRequest(m_database->getUserId(username), fileList.fileId, fileList.creatorId);
		repCode += fileNameLen + filename;
	}
	else
	{
		repCode = std::to_string(MC_ERROR_RESP) + "Request already exist, waiting for the owner of the file to approve";
	}
	*/
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::getProfileInfo(SOCKET client_sock, std::string userName)
{
	std::string repCode = std::to_string(MC_PROFILE_INFO_RESP);;
	ProfileInfo info = m_database->getUsersInfo(m_database->getUserId(userName));
	
	std::string lengthString = std::to_string(userName.length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	repCode += lengthString + userName;

	lengthString = std::to_string(info.email.length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	repCode += lengthString + info.email;

	lengthString = std::to_string(info.bio.length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	repCode += lengthString + info.bio;

	std::string currentUserName = m_clients[client_sock]->getUsername();
	if (userName != currentUserName)
	{
		Friends friends = m_database->getUserFriends(m_database->getUserId(currentUserName));
		std::unordered_set<std::string> FriendsList;
		int index = 0;
		while (index < friends.fiendsList.length())
		{
			std::string nameLenStr = friends.fiendsList.substr(index, 5);
			int nameLen = std::stoi(nameLenStr);
			index += 5;
			std::string friendName = friends.fiendsList.substr(index, nameLen);
			index += nameLen;
			FriendsList.insert(friendName);
		}
		if (FriendsList.find(userName) != FriendsList.end())
		{
			repCode += "0"; // is friend
		}
	}
	else
	{
		repCode += "1"; // not friend
	}

	/*
	try
	{
		size_t imageSize = fileOperationHandler.getImageSize(".\\ProfileImages\\" + userName + ".jpg");
		lengthString = std::to_string(imageSize);
		lengthString = std::string(6 - lengthString.length(), '0') + lengthString;
		repCode += lengthString;

		std::vector<unsigned char> image = fileOperationHandler.ReadImageFile(".\\ProfileImages\\" + userName + ".jpg");
		const char* imageData = reinterpret_cast<const char*>(image.data());

		repCode += std::string(image.begin(), image.end()); // Append image data to repCode
	}
	catch (const std::exception&)
	{
		repCode += "000000"; 
	}
	*/

	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::getProfileImage(SOCKET client_sock, std::string userName)
{
	std::string repCode;// = std::to_string(MC_GET_PROFILE_IMAGE_RESP);;
	
	std::vector<unsigned char> image = fileOperationHandler.ReadImageFile(".\\ProfileImages\\" + userName + ".jpg");
	const char* imageData = reinterpret_cast<const char*>(image.data());

	repCode += std::string(image.begin(), image.end()); // Append image data to repCode
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::getUserFriends(SOCKET client_sock, std::string userName)
{
	std::string repCode = std::to_string(MC_FRIENDS_LIST_RESP);;

	int userId = m_database->getUserId(userName);
	std::string friendsList = m_database->getUserFriends(userId).fiendsList;
	std::list<FriendReq> friendsReq = m_database->getUserFriendReq(userId);

	std::string lengthString;

	for (auto req : friendsReq)
	{
		std::string userName = m_database->getUserName("", req.userId);
		lengthString = std::to_string(userName.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + userName + "3"; // request
	}

	int currentIndex = 0;
	while (currentIndex < friendsList.length())
	{
		// Extract data length for each message
		int dataLength = std::stoi(friendsList.substr(currentIndex, 5));
		currentIndex += 5;

		// Extract data from the response
		std::string name = friendsList.substr(currentIndex, dataLength);
		currentIndex += dataLength;
		
		auto it = std::find_if(m_clients.begin(), m_clients.end(), [&name](const auto& pair) {
			return pair.second->getUsername() == name; // Assuming pair.first is the SOCKET identifier
			});

		if (it != m_clients.end()) {
			// Client handler found, client is online
			lengthString = std::to_string(name.length());
			lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
			repCode += lengthString + name + "1"; // Online
		}
		else {
			// Client handler not found, client is offline
			lengthString = std::to_string(name.length());
			lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
			repCode += lengthString + name + "0"; // Offline
		}
	}

	if (m_clients[client_sock]->getWindow() != "project")
	{
		m_clients[client_sock]->setWindow("HomePage");
	}
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::approveFriendReq(SOCKET client_sock, std::string userName)
{
	std::string repCode = std::to_string(MC_APPROVE_FRIEND_REQ_RESP);
	std::string lengthString;
	m_database->approveFriendReq(m_database->getUserId(userName), m_clients[client_sock]->getId());
	
	std::string currentUserName = m_clients[client_sock]->getUsername();

	m_friends[userName].push_back(currentUserName);
	m_friends[currentUserName].push_back(userName);

	auto it = std::find_if(m_clients.begin(), m_clients.end(), [&userName](const auto& pair) {
		return pair.second->getUsername() == userName; // Assuming pair.first is the SOCKET identifier
		});

	if (it != m_clients.end()) {
		// Client handler found, client is online
		if (it->second->getWindow() == "HomePage" || it->second->getWindow() == "project" || it->second->getWindow() == "searchUsers")
		{
			repCode = std::to_string(MC_ADD_FRIEND_RESP);
			lengthString = std::to_string(currentUserName.length());
			lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
			repCode += lengthString + currentUserName + "1"; // Online
			Helper::sendData(it->first, BUFFER(repCode.begin(), repCode.end()));
		}

		repCode = std::to_string(MC_APPROVE_FRIEND_REQ_RESP);
		lengthString = std::to_string(userName.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + userName + "1"; // Online
	}
	else {
		// Client handler not found, client is offline
		lengthString = std::to_string(userName.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + userName + "0"; // Offline
	}
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

}

void Communicator::rejectFriendReq(SOCKET client_sock, std::string userName)
{
	std::string repCode = std::to_string(MC_REJECT_FRIEND_REQ_RESP);

	m_database->rejectFriendReq(m_database->getUserId(userName), m_clients[client_sock]->getId());
	
	std::string lengthString = std::to_string(userName.length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	repCode += lengthString + userName;
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

}

void Communicator::addFriend(SOCKET client_sock, std::string userName)
{
	std::string currUserName = m_clients[client_sock]->getUsername();
	Friends friends = m_database->getUserFriends(m_database->getUserId(currUserName));
	std::list<FriendReq> friendReq = m_database->getUserFriendReq(m_database->getUserId(userName));
	
	std::unordered_set<std::string> FriendsReqList;

	std::string friendName;

	int index = 0;
	while(index < friends.fiendsList.length())
	{
		int lengthName = std::stoi(friends.fiendsList.substr(index, 5));
		index += 5;
		std::string name = friends.fiendsList.substr(index, lengthName);
		index += lengthName;
		FriendsReqList.insert(name);
	}

	for (auto req : friendReq)
	{
		friendName = m_database->getUserName("", req.friendReqId);
		FriendsReqList.insert(friendName);
	}

	if (FriendsReqList.find(userName) == FriendsReqList.end())
	{
		m_database->addFriendReq(m_database->getUserId(currUserName), m_database->getUserId(userName));

		auto it = std::find_if(m_clients.begin(), m_clients.end(), [&userName](const auto& pair) {
			return pair.second->getUsername() == userName; // Assuming pair.first is the SOCKET identifier
			});

		if (it != m_clients.end()) {
			// Client handler found, client is online
			if (it->second->getWindow() == "HomePage" || it->second->getWindow() == "project")
			{
				std::string repCode = std::to_string(MC_FRIEND_REQ_RESP);
				std::string lengthString = std::to_string(currUserName.length());
				lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
				repCode += lengthString + currUserName + "3"; // defualt
				Helper::sendData(it->first, BUFFER(repCode.begin(), repCode.end()));
			}
		}
	}
}

void Communicator::getProjectsList(SOCKET client_sock, std::string userName)
{
	std::string repCode = std::to_string(MC_PROJECTS_LIST_RESP);;
	int userId = m_database->getUserId(userName);

	std::string lengthString;
	std::string role;

	for (auto detail : m_database->getUserProjectPermission(userId))
	{
		role = m_database->getUserRoleInProject(userId, detail.projectId);
		Project project = m_database->getProject("", detail.projectId);

		lengthString = std::to_string(project.name.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + project.name;

		lengthString = std::to_string(role.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + role ; // has access

		lengthString = std::to_string(std::to_string(project.projectId).length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + std::to_string(project.projectId);
	}

	for (auto invite : m_database->getUserProjectInvite(userId))
	{
		Project project = m_database->getProject("", invite.projectId);
		lengthString = std::to_string(project.name.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + project.name;

		repCode += "00006invite"; // invite
		lengthString = std::to_string(std::to_string(project.projectId).length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + std::to_string(project.projectId);
	}

	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::getUserProjectsList(SOCKET client_sock, std::string userName)
{
	std::string repCode = std::to_string(MC_USER_PROJECTS_LIST_RESP);;
	int userId = m_database->getUserId(userName);

	std::string lengthString;

	for (auto detail : m_database->getUserProjectPermission(userId))
	{
		Project project = m_database->getProject("", detail.projectId);

		lengthString = std::to_string(project.name.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + project.name;

		std::string id = std::to_string(project.projectId);
		lengthString = std::to_string(id.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + id;
	}

	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::removeFriend(SOCKET client_sock, std::string userName, std::string userNameToRemove)
{
	std::string repCode = std::to_string(MC_REMOVE_FRIEND_RESP);

	std::string friendsListCurrUser = m_database->getUserFriends(m_database->getUserId(userName)).fiendsList;
	std::string friendsListRemovedUser = m_database->getUserFriends(m_database->getUserId(userNameToRemove)).fiendsList;

	int currUserId = m_database->getUserId(userName);
	int removedUserId = m_database->getUserId(userNameToRemove);

	std::string updateFriendList;
	int currentIndex = 0;

	while (currentIndex < friendsListRemovedUser.length())
	{
		// Extract data length for each message
		std::string dataLenString = friendsListRemovedUser.substr(currentIndex, 5);
		int dataLength = std::stoi(dataLenString);
		currentIndex += 5;

		// Extract data from the response
		std::string name = friendsListRemovedUser.substr(currentIndex, dataLength);
		currentIndex += dataLength;

		if (name != userName)
		{
			updateFriendList += dataLenString + name;
		}
	}
	m_database->removeFriend(removedUserId, updateFriendList);

	updateFriendList = "";
	currentIndex = 0;
	while (currentIndex < friendsListCurrUser.length())
	{
		// Extract data length for each message
		std::string dataLenString = friendsListCurrUser.substr(currentIndex, 5);
		int dataLength = std::stoi(dataLenString);
		currentIndex += 5;

		// Extract data from the response
		std::string name = friendsListCurrUser.substr(currentIndex, dataLength);
		currentIndex += dataLength;
		
		if (name != userNameToRemove)
		{
			updateFriendList += dataLenString + name;
		}
	}
	m_database->removeFriend(currUserId, updateFriendList);

	auto firstUserFriends = m_friends.find(userName);
	if (firstUserFriends != m_friends.end()) {
		// Get reference to the vector of friends
		std::vector<std::string>& friendsList = firstUserFriends->second;

		// Remove the specific friend using std::remove and erase idiom
		friendsList.erase(std::remove(friendsList.begin(), friendsList.end(), userNameToRemove), friendsList.end());
	}
	auto secondUserFriends = m_friends.find(userNameToRemove);
	if (secondUserFriends != m_friends.end()) {
		// Get reference to the vector of friends
		std::vector<std::string>& friendsList = secondUserFriends->second;

		// Remove the specific friend using std::remove and erase idiom
		friendsList.erase(std::remove(friendsList.begin(), friendsList.end(), userName), friendsList.end());
	}

	
	auto it = std::find_if(m_clients.begin(), m_clients.end(), [&userNameToRemove](const auto& pair) {
		return pair.second->getUsername() == userNameToRemove; // Assuming pair.first is the SOCKET identifier
		});
	if (it != m_clients.end() && (it->second->getWindow() == "HomePage" || it->second->getWindow() == "project"))
	{
		repCode = std::to_string(MC_REMOVE_FRIEND_RESP) + userName;
		Helper::sendData(it->first, BUFFER(repCode.begin(), repCode.end()));
	}
	repCode = std::to_string(MC_REMOVE_FRIEND_RESP) + userNameToRemove;
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::editInfo(SOCKET client_sock, std::string bio)
{
	std::string repCode = std::to_string(MC_PROJECTS_LIST_RESP);;

	int userId = m_database->getUserId(m_clients[client_sock]->getUsername());
	ProfileInfo profile = m_database->getUsersInfo(userId);

	m_database->modifyProfile(profile.name, profile.email, bio, userId);

	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::createProject(SOCKET client_sock, std::string projectName, std::string friendList, std::string codeLan, int mode)
{
	std::string repCode;

	int userId = m_database->getUserId(m_clients[client_sock]->getUsername());
	std::map<ProfileInfo, std::string> friends; // info, role

	int index = 0;

	while (index < friendList.length())
	{
		int nameLengeth = std::stoi(friendList.substr(index, 5));
		index += 5;

		std::string name = friendList.substr(index, nameLengeth);
		index += nameLengeth;

		std::string roleMsg = friendList.substr(index, 1);
		index += 1;
		std::string role;
		if (roleMsg == "1")
		{
			role = "admin";
		}
		else
		{
			role = "participant";
		}

		ProfileInfo profile = m_database->getUsersInfo(m_database->getUserId(name));
		friends[profile] = role;

		auto it = std::find_if(m_clients.begin(), m_clients.end(), [&name](const auto& pair) {
			return pair.second->getUsername() == name; // Assuming pair.first is the SOCKET identifier
			});

		if (it != m_clients.end() && it->second->getWindow() == "HomePage") {
			// Client handler found, client is online
			repCode = std::to_string(MC_UPDATE_PROJECT_LIST_REQUEST);

			Helper::sendData(it->first, BUFFER(repCode.begin(), repCode.end()));
		}
	}

	m_database->createProject(projectName, friends, codeLan, userId, -1);
	Project project = m_database->getProject(projectName, -1);
	m_database->createProjectPermission(project.projectId, userId, "creator");
	m_database->createChat(project.projectId);

	// Send to each friend the invite if they are online
	repCode = std::to_string(MC_BACK_TO_HOME_PAGE_RESP);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::deleteProject(SOCKET client_sock, int projectId)
{
	Project project = m_database->getProject("", projectId);
	std::string projectName = project.name;
	int userId = m_database->getUserId(m_clients[client_sock]->getUsername());
	if (m_usersOnProject.find(project.projectId) != m_usersOnProject.end())
	{
		throw std::exception("cannot delete. Someone is inside");
	}
	else if (!m_database->hasPermissionToProject(project.projectId, userId))
	{
		throw std::exception("dont have permission for this file");
	}
	else
	{
		std::string repCode;

		//fileOperationHandler.deleteDirectory(".\\" + _dirName + "\\\\" + projectName); // decide if needs to be removed later

		std::list<FileDetail> files = m_database->getProjectFiles(project.projectId);

		m_usersOnProject.erase(project.projectId);
		for (const auto& file : files) {
			std::string fileName = file.fileName;

			// Delete fileName from m_lastActionMap
			auto it1 = m_lastActionMap.find(file.fileId);
			if (it1 != m_lastActionMap.end()) {
				m_lastActionMap.erase(it1);
			}

			// Delete fileName from m_usersOnFile
			auto it2 = m_usersOnFile.find(file.fileId);
			if (it2 != m_usersOnFile.end()) {
				m_usersOnFile.erase(it2);
			}

			// Delete fileName from m_projects
			auto it3 = m_projects.find(file.fileId);
			if (it3 != m_projects.end()) {
				m_projects.erase(it3);
			}

			// Delete fileName from m_filesData
			auto it4 = m_filesData.find(file.fileId);
			if (it4 != m_filesData.end()) {
				m_filesData.erase(it4);
			}

			// Delete fileName from m_FileUpdate
			auto it5 = m_FileUpdate.find(file.fileId);
			if (it5 != m_FileUpdate.end()) {
				m_FileUpdate.erase(it5);
			}

			// Delete fileName from m_fileMutexes
			auto it6 = m_fileMutexes.find(file.fileId);
			if (it6 != m_fileMutexes.end()) {
				m_fileMutexes.erase(it6);
			}
			m_database->deleteFile(fileName, project.projectId);
		}

		std::map<std::string, std::string> projectPermissions = m_database->getProjectParticipants(project.projectId);
		m_database->DeleteChat(project.projectId);
		m_database->deleteAllProjectPermission(project.projectId);

		for (auto user : projectPermissions)
		{
			std::string currUserName = user.first;
			auto userIr = std::find_if(m_clients.begin(), m_clients.end(), [&currUserName](const auto& pair) {
				return pair.second->getUsername() == currUserName; // Assuming pair.first is the SOCKET identifier
				});

			if (userIr != m_clients.end() && userIr->second->getWindow() == "HomePage") {
				int currUserId = m_database->getUserId(user.first);
				if (m_database->hasPermissionToProject(project.projectId, currUserId))
				{
					repCode = std::to_string(MC_UPDATE_PROJECT_LIST_REQUEST);
					Helper::sendData(userIr->first, BUFFER(repCode.begin(), repCode.end()));
				}
			}
		}

		m_database->deleteProject(projectName);
		//m_database->deletePermission(m_fileNames[fileName + ".txt"]);
		//m_database->deleteAllPermissionReq(m_fileNames[fileName + ".txt"]);
		//m_fileNames.erase(fileName + ".txt");
		repCode = std::to_string(MC_UPDATE_PROJECT_LIST_REQUEST);
		Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
	}
}

void Communicator::modifyProjectInfo(SOCKET client_sock, int projectId, std::string projectName, std::string friendList, std::string codeLan)
{
	std::string repCode;

	int userId = m_database->getUserId(m_clients[client_sock]->getUsername());
	std::map<ProfileInfo, std::string> friends; // info, role

	int index = 0;
	Project project = m_database->getProject("", projectId);
	std::map<std::string, std::string> oldFriendList;
	std::map<std::string, std::string> newFriendList;

	while (index < friendList.length())
	{
		int nameLengeth = std::stoi(friendList.substr(index, 5));
		index += 5;

		std::string name = friendList.substr(index, nameLengeth);
		index += nameLengeth;

		std::string roleMsg = friendList.substr(index, 1);
		index += 1;
		std::string role;
		if (roleMsg == "1")
		{
			role = "admin";
		}
		else if (roleMsg == "2")
		{
			role = "creator";
		}
		else if(roleMsg == "0")
		{
			role = "participant";
		}
		newFriendList[name] = role;
	}

	for (auto user : m_database->getProjectParticipants(projectId))
	{
		oldFriendList[user.first] = user.second;

		if (user.first == m_clients[client_sock]->getUsername()) continue;

		std::string userName = user.first;
		auto it = std::find_if(newFriendList.begin(), newFriendList.end(), [&userName](const auto& pair) {
			return pair.first == userName; // Assuming pair.first is the SOCKET identifier
			});

		if (it == newFriendList.end())
		{
			m_database->deleteProjectPermission(projectId, m_database->getUserId(userName));
			auto userIt = std::find_if(m_clients.begin(), m_clients.end(), [&userName](const auto& pair) {
				return pair.second->getUsername() == userName; // Assuming pair.first is the SOCKET identifier
				});

			if (userIt != m_clients.end() && userIt->second->getWindow() == "HomePage") {
				// Client handler found, client is online
				repCode = std::to_string(MC_UPDATE_PROJECT_LIST_REQUEST);
				Helper::sendData(userIt->first, BUFFER(repCode.begin(), repCode.end()));
			}
			continue;
		}

	}
	
	for (auto item : newFriendList)
	{
		std::string name = item.first;
		ProfileInfo profile = m_database->getUsersInfo(m_database->getUserId(name));
		friends[profile] = item.second;

		auto userIr = std::find_if(m_clients.begin(), m_clients.end(), [&name](const auto& pair) {
			return pair.second->getUsername() == name; // Assuming pair.first is the SOCKET identifier
			});

		if (userIr != m_clients.end() && userIr->second->getWindow() == "HomePage") {
			// Client handler found, client is online
			repCode = std::to_string(MC_UPDATE_PROJECT_LIST_REQUEST);
			Helper::sendData(userIr->first, BUFFER(repCode.begin(), repCode.end()));
		}
	}

	m_database->modifyProjectInfo(projectId, projectName, friends, codeLan);
	repCode = std::to_string(MC_BACK_TO_HOME_PAGE_RESP);
	// Send to each friend the invite if they are online
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::acceptProjectInvite(SOCKET client_sock, int ProjectId, std::string userName, std::string role)
{
	std::string repCode = std::to_string(MC_UPDATE_PROJECT_LIST_REQUEST);

	int userId = m_database->getUserId(m_clients[client_sock]->getUsername());

	std::string newRole = m_database->getAUserProjectInvite(userId, ProjectId).role;
	m_database->acceptProjectJoinInvite(ProjectId, userId, newRole);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

}

void Communicator::declineProjectInvite(SOCKET client_sock, int ProjectId, std::string userName)
{
	std::string repCode = std::to_string(MC_UPDATE_PROJECT_LIST_REQUEST);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

	int userId = m_database->getUserId(m_clients[client_sock]->getUsername());
	m_database->deleteProjectJoinInvite(ProjectId, userId);
}

void Communicator::moveToCreateWindow(SOCKET client_sock)
{
	std::string repCode = std::to_string(MC_MOVE_TO_CREATE_PROJ_WINDOW_RESP) + "00000create";

	m_clients[client_sock]->setWindow("createProjectWindow");
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::enterProject(SOCKET client_sock, int projectId)
{
	Project project = m_database->getProject("", projectId);
	std::string repCode;

	/*
	if (!m_database->hasPermissionToProject(projectId, m_database->getUserId(m_clients[client_sock]->getUsername()))) {
		// Send an error response indicating lack of permission
		std::string errMsg = "You are not allowed to join this project" + projectNameLen + projectName;
		throw std::exception(errMsg.c_str());
	}
	*/

	std::string lengthString = std::to_string((project.codeLan.length()));
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;

	bool mode = m_database->hasPermissionToProject(projectId, m_database->getUserId(m_clients[client_sock]->getUsername()));
	std::string isEditable = mode ? "true" : "false";

	std::string projectIdStr = std::to_string(projectId);
	std::string IdlengthString = std::to_string(projectIdStr.length());
	IdlengthString = std::string(5 - IdlengthString.length(), '0') + IdlengthString;

	std::string projectNameLen = std::to_string(project.name.length());
	projectNameLen = std::string(5 - projectNameLen.length(), '0') + projectNameLen;

	repCode = std::to_string(MC_APPROVE_JOIN_RESP) + projectNameLen + project.name + IdlengthString + projectIdStr + lengthString + project.codeLan + isEditable;
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

	repCode = std::to_string(MC_ENTER_PROJECT_RESP);
	m_clients[client_sock]->setProject(project.name, projectId);
	m_clients[client_sock]->setWindow("project");

	m_usersOnProject[projectId].push_back(*m_clients[client_sock]);

	lengthString = std::to_string((m_clients[client_sock]->getUsername().length()));
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	repCode += lengthString + m_clients[client_sock]->getUsername();

	lengthString = std::to_string((project.name.length()));
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	repCode += lengthString + project.name;

	//notifyAllClients(repCode, client_sock, true);
	//notifyAllClients(repCode, client_sock, false);
}

void Communicator::exitProject(SOCKET client_sock)
{
	std::string repCode = std::to_string(MC_EXIT_PROJECT_RESP);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

	for (auto it = m_usersOnProject.begin(); it != m_usersOnProject.end(); ++it) {
		// Iterate over the array of clients for each file
		for (auto clientIt = it->second.begin(); clientIt != it->second.end(); ) {
			if (clientIt->getId() == m_clients[client_sock]->getId()) {
				clientIt = it->second.erase(clientIt);
			}
			else {
				++clientIt;
			}
		}
	}

	std::string projectName = m_clients[client_sock]->getProjectName();
	int projectId = m_clients[client_sock]->getProjectId();
	std::string fileName = m_clients[client_sock]->getFileName();

	// Check if the user leaving was the last one
	if (m_usersOnProject[projectId].empty()) {
		// Delete the mutex and remove the file from m_usersOnFile
		m_usersOnProject.erase(projectId);
	}

	m_clients[client_sock]->setProject("", -1);
	m_clients[client_sock]->setWindow("HomePage");
}

void Communicator::leaveProject(SOCKET client_sock, int projectId, std::string userName)
{
	std::string repCode = std::to_string(MC_LEAVE_PROJECT_RESP);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

	int userId = m_database->getUserId(userName);
	Project project = m_database->getProject("", projectId);
	m_database->leaveProject(project.name, userId);
	
	if (m_database->isCreator(project.name, userId))
	{
		int replacerId = m_database->getNextAdmin(project.projectId);

		if (replacerId != -1)
		{
			std::map<ProfileInfo, std::string> friends; // info, role
			m_database->createProject(project.name, friends, project.codeLan, replacerId, project.projectId);
			m_database->changeUserRoleInProject(project.projectId, replacerId, "creator");
			std::string replacerName = m_database->getUserName("", replacerId);
			auto it = std::find_if(m_clients.begin(), m_clients.end(), [&replacerName](const auto& pair) {
				return pair.second->getUsername() == replacerName; // Assuming pair.first is the SOCKET identifier
				});

			if (it != m_clients.end()) {
				// Client handler found, client is online
				repCode = std::to_string(MC_UPDATE_PROJECT_LIST_REQUEST);
				Helper::sendData(it->first, BUFFER(repCode.begin(), repCode.end()));
			}
		}
	}
}

void Communicator::getProjectFiles(SOCKET client_sock, int projectId)
{
	Project project = m_database->getProject("", projectId);
	std::string repCode = std::to_string(MC_GET_PROJECT_FILES_RESP);
	
	std::list<FileDetail> files = m_database->getProjectFiles(projectId);

	for (auto file : files)
	{
		std::string lengthString = std::to_string((file.fileName.length()));
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + file.fileName;
	}

	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::renameFile(SOCKET client_sock, std::string newFileName, std::string oldFileName, int projectId)
{
	m_database->renameFile(projectId, newFileName, oldFileName);

	std::string repCode = std::to_string(MC_RENAME_FILE_RESP);
	
	std::string lengthString = std::to_string((newFileName.length()));
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	repCode += lengthString + newFileName;

	lengthString = std::to_string((oldFileName.length()));
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	repCode += lengthString + oldFileName;

	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
	notifyAllclientsOnProject(repCode, client_sock);
}

void Communicator::searchUsers(SOCKET client_sock, std::string nsearchCommand)
{
	std::string repCode = std::to_string(MC_SEARCH_RESP);

	std::list<std::string> userList = m_database->searchUsers(nsearchCommand);
	m_clients[client_sock]->setWindow("searchUsers");
	std::string currentUser = m_clients[client_sock]->getUsername();
	Friends friends = m_database->getUserFriends(m_database->getUserId(currentUser));
	std::unordered_set<std::string> FriendsList;
	
	std::list<FriendReq> req1 = m_database->getCurrentUserReqSent(m_database->getUserId(currentUser));
	std::list<FriendReq> req2 = m_database->getUserFriendReq(m_database->getUserId(currentUser));
	std::unordered_set<std::string> FriendsReqList;

	std::string friendName;
	std::string lengthString;

	for (auto friendReq : req1)
	{
		friendName = m_database->getUserName("", friendReq.friendReqId);
		FriendsReqList.insert(friendName);
	}

	for (auto friendReq : req2)
	{
		friendName = m_database->getUserName("", friendReq.userId);
		FriendsReqList.insert(friendName);
	}

	int index = 0;
	while(index < friends.fiendsList.length())
	{
		std::string nameLenStr = friends.fiendsList.substr(index, 5);
		int nameLen = std::stoi(nameLenStr);
		index += 5;
		friendName = friends.fiendsList.substr(index, nameLen);
		index += nameLen;
		FriendsList.insert(friendName);
	}

	for (auto userName : userList)
	{
		if (userName == currentUser) continue;
		if (FriendsReqList.find(userName) != FriendsReqList.end()) continue;
		if (FriendsList.find(userName) != FriendsList.end())
		{
			auto it = std::find_if(m_clients.begin(), m_clients.end(), [&userName](const auto& pair) {
				return pair.second->getUsername() == userName; // Assuming pair.first is the SOCKET identifier
				});

			if (it != m_clients.end()) {
				// Client handler found, client is online
				lengthString = std::to_string(userName.length());
				lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
				repCode += lengthString + userName + "1"; // Online
			}
			else {
				// Client handler not found, client is offline
				lengthString = std::to_string(userName.length());
				lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
				repCode += lengthString + userName + "0"; // Offline
			}
		}
		else
		{
			lengthString = std::to_string(userName.length());
			lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
			repCode += lengthString + userName + "2"; // defualt
		}
	}

	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::searchFriends(SOCKET client_sock, std::string nsearchCommand)
{
	std::string repCode = std::to_string(MC_SEARCH_FRIENDS_RESP);

	std::list<std::string> userList = m_database->searchUsers(nsearchCommand);
	std::string currentUser = m_clients[client_sock]->getUsername();
	Friends friends = m_database->getUserFriends(m_database->getUserId(currentUser));
	std::unordered_set<std::string> FriendsList;

	std::string friendName;
	std::string lengthString;

	int index = 0;
	while(index < friends.fiendsList.length())
	{
		std::string nameLenStr = friends.fiendsList.substr(index, 5);
		int nameLen = std::stoi(nameLenStr);
		index += 5;
		friendName = friends.fiendsList.substr(index, nameLen);
		index += nameLen;
		FriendsList.insert(friendName);
	}

	for (auto userName : userList)
	{
		if (userName == currentUser) continue;
		if (FriendsList.find(userName) != FriendsList.end())
		{
			
			lengthString = std::to_string(userName.length());
			lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
			repCode += lengthString + userName;
		}
	}

	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::editProjectInfo(SOCKET client_sock, int projectId)
{
	Project project = m_database->getProject("", projectId);
	std::string projectName = project.name;
	if (m_usersOnProject.find(project.projectId) != m_usersOnProject.end()
		&& !m_usersOnProject[project.projectId].empty())
	{
		throw std::exception("cannot edit. Someone is inside");
	}
	else
	{
		m_clients[client_sock]->setWindow("createProjectWindow");

		std::string repCode = std::to_string(MC_MOVE_TO_CREATE_PROJ_WINDOW_RESP);
		std::string lengthString;

		lengthString = std::to_string(projectName.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + projectName;

		lengthString = std::to_string(std::to_string(projectId).length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + std::to_string(projectId);

		bool isCretor = m_database->isCreator(projectName, m_database->getUserId(m_clients[client_sock]->getUsername()));
		repCode += isCretor ? "creator" : "editor";
		Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
	}
}

void Communicator::viewProjectInfo(SOCKET client_sock, int projectId)
{
	std::string repCode = std::to_string(MC_MOVE_TO_CREATE_PROJ_WINDOW_RESP);
	std::string lengthString;
	Project project = m_database->getProject("", projectId);

	lengthString = std::to_string(project.name.length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	repCode += lengthString + project.name;

	lengthString = std::to_string(std::to_string(projectId).length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	repCode += lengthString + std::to_string(projectId);

	repCode += "viewer";
	m_clients[client_sock]->setWindow("createProjectWindow");
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::getProjectInfo(SOCKET client_sock, std::string projectName)
{
	std::string lengthString;
	std::string repCode = std::to_string(MC_GET_PROJECT_INFO_RESP);
	Project project = m_database->getProject(projectName, -1);
	
	lengthString  = std::to_string(projectName.length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;

	repCode += lengthString + projectName;

	std::string currentUser = m_clients[client_sock]->getUsername();
	std::string userList;
	for (auto user : m_database->getProjectParticipants(project.projectId))
	{
		if (user.first == currentUser) continue;
		lengthString = std::to_string(user.first.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		userList += lengthString + user.first;
		std::string role;
		if (user.second == "admin")
		{
			role = "1";
		}
		else if (user.second == "creator")
		{
			role = "2";
		}
		else if(user.second == "participant")
		{
			role = "0";
		}

		userList += role;
	}
	
	lengthString = std::to_string(userList.length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	repCode += lengthString + userList;

	lengthString = std::to_string(project.codeLan.length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;

	repCode += lengthString + project.codeLan;
	repCode += "0"; // privacy - '1':private, '0':public
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::backToMainPage(SOCKET client_sock)
{
	std::string repCode = std::to_string(MC_BACK_TO_HOME_PAGE_RESP);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
	m_clients[client_sock]->setWindow("HomePage");
}

void Communicator::getCodeStyles(SOCKET client_sock)
{
	std::string repCode = std::to_string(MC_GET_CODE_STYLES_RESP);
	std::string path;
	std::string lengthString;
	for (auto file : codeStyles)
	{
		path = file.second;
		std::string data = fileOperationHandler.readFromFile(path);

		lengthString = std::to_string(file.first.length());
		lengthString = std::string(3 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + file.first;

		lengthString = std::to_string(data.length());
		lengthString = std::string(7 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + data;
	}

	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::moveToSettings(SOCKET client_sock)
{
	std::string repCode = std::to_string(MC_SETTINGS_RESP);
	m_clients[client_sock]->setWindow("settings");
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
			buf = Helper::getPartFromSocket(client_sock, 512);
			if (buf.size() == 0)
			{
				closesocket(client_sock);
				run = false;
				// Handle disconnection
				handleClientDisconnect(client_sock);
				continue;
			}

			std::string newRequest(buf.begin(), buf.end());
			Action reqDetail = deconstructReq(newRequest);
			int msgCode = std::stoi(newRequest.substr(0, 3));
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
			case MC_GET_MESSAGES_REQUEST:
				getMesegges(client_sock, reqDetail.data);
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
				postMsg(client_sock, reqDetail.projectId, reqDetail.data, reqDetail.dataLength);
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
				approveFriendReq(client_sock, reqDetail.userName);
				break;
			case MC_REJECT_FRIEND_REQ_REQUEST:
				rejectFriendReq(client_sock, reqDetail.userName);
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
				acceptProjectInvite(client_sock, reqDetail.projectId, reqDetail.userName, reqDetail.role);
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
			case MC_GET_CODE_STYLES_REQUEST:
				getCodeStyles(client_sock);
				break;
			case MC_SETTINGS_REQUEST:
				moveToSettings(client_sock);
				break;
			case MC_DISCONNECT: // Handle disconnect request
				run = false;
				handleClientDisconnect(client_sock);
				continue;
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
	case MC_GET_MESSAGES_REQUEST:
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

		newAction.roleLength = std::stoi(action.substr(10 + newAction.projectIdLength + newAction.userNameLength, 5));
		newAction.role = action.substr(15 + newAction.projectIdLength + newAction.userNameLength, newAction.roleLength);

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
		newAction.userNameLength = std::stoi(action.substr(0, 5));
		newAction.userName = action.substr(5, newAction.userNameLength);
		break;
	case MC_REJECT_FRIEND_REQ_REQUEST:
		newAction.userNameLength = std::stoi(action.substr(0, 5));
		newAction.userName = action.substr(5, newAction.userNameLength);
		break;
	case MC_ADD_FRIEND_REQUEST:
		newAction.userNameLength = std::stoi(action.substr(0, 5));
		newAction.userName = action.substr(5, newAction.userNameLength);
		break;
	case MC_BACK_TO_HOME_PAGE_REQUEST:
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
	case MC_SETTINGS_REQUEST:
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

		if (m_clients[friend_sock]->getWindow() == "createProjectWindow") continue;
		if (m_clients[friend_sock]->getWindow() == "searchUsers") continue;

		// Check if the current client is in the friend list of the client who triggered the update
		if (friendsList->second.end() != std::find(friendsList->second.begin(), friendsList->second.end(), client->getUsername()))
		{
			// Send updated content to the friend
			Helper::sendData(friend_sock, BUFFER(updatedContent.begin(), updatedContent.end()));
		}
	}
}

void Communicator::notifyAllclientsOnProject(const std::string& updatedContent, SOCKET client_sock)
{
	// Get the username of the client who triggered the notification
	std::string name = m_clients[client_sock]->getUsername();
	std::string projectName = m_clients[client_sock]->getProjectName();

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

		if (m_clients[friend_sock]->getWindow() != "project" 
			&& m_clients[friend_sock]->getProjectName() != projectName) continue;

		Helper::sendData(friend_sock, BUFFER(updatedContent.begin(), updatedContent.end()));
		
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
			std::this_thread::sleep_for(std::chrono::seconds(60));
		}
	}
	catch (const std::exception& e) {
		// Handle the exception appropriately, e.g., logging
		std::cerr << "Exception in saveFiles: " << e.what() << std::endl;
	}
}