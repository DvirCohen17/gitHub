#include "Communicator.h"

// Profiles, friends, user search, in-app messages and project chat.

void Communicator::getChatMesegges(SOCKET client_sock, std::string projectName)
{
	std::string repCode = std::to_string(MC_GET_FILES_RESP);
	int projectId = std::stoi(projectName);

	// Handle get messages request
	repCode = std::to_string(MC_GET_CHAT_MESSAGES_RESP);
	//std::string chatContent = executeCommand("main.exe decrypt \'" + m_database->GetChatData(projectId) + "\'");
	std::string chatContent = m_database->GetChatData(projectId);
	chatContent = chatContent.substr(0, chatContent.length());
	repCode += chatContent;
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::postChatMsg(SOCKET client_sock, int projectId, std::string data, std::string dataLen)
{
	std::string repCode = std::to_string(MC_POST_MSG_RESP);
	
	std::string userName = m_clients[client_sock]->getUsername();
	// Handle post message request
	std::string lengthString = std::to_string(userName.length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;

	//std::string chatMsg = executeCommand("main.exe decrypt \'" + m_database->GetChatData(projectId) + "\'");
	std::string chatMsg = m_database->GetChatData(projectId);
	chatMsg = chatMsg.substr(0, chatMsg.length());
	chatMsg += dataLen + data +
		lengthString + userName;
	//m_database->UpdateChat(projectId, executeCommand("main.exe encrypt \'" + chatMsg + "\'"));
	m_database->UpdateChat(projectId, chatMsg);

	repCode += dataLen + data + lengthString + userName;
	notifyAllclientsOnProject(repCode, client_sock, "");
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
	//std::list<FriendReq> friendsReq = m_database->getUserFriendReq(userId);

	std::string lengthString;

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

	if (m_clients[client_sock]->getWindow() != CREATE_PROJECT_PAGE)
	{
		if (m_clients[client_sock]->getWindow() != PROJECT_PAGE)
		{
			m_clients[client_sock]->setWindow(HOME_PAGE);
		}
	}

	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::approveFriendReq(SOCKET client_sock, int userId)
{
	std::string repCode = std::to_string(MC_APPROVE_FRIEND_REQ_RESP);
	std::string lengthString;
	
	std::string currentUserName = m_clients[client_sock]->getUsername();
	std::string userName = m_database->getUserName("", userId);

	m_friends[userName].push_back(currentUserName);
	m_friends[currentUserName].push_back(userName);

	int currUserId = m_database->getUserId(currentUserName);

	m_database->approveFriendReq(userId, currUserId);

	m_database->DeleteMessage(userId, currUserId);
	std::string msg = " Approved friend request";
	m_database->AddMsg(currUserId, userId, msg, 0, -1);

	auto it = std::find_if(m_clients.begin(), m_clients.end(), [&userName](const auto& pair) {
		return pair.second->getUsername() == userName; // Assuming pair.first is the SOCKET identifier
		});

	if (it != m_clients.end()) {
		// Client handler found, client is online
		if (it->second->getWindow() == HOME_PAGE || it->second->getWindow() == PROJECT_PAGE)
		{
			if (it->second->getWindow() == HOME_PAGE)
			{
				std::string repCode = std::to_string(MC_SEND_MSG_RESP);
				Helper::sendData(it->first, BUFFER(repCode.begin(), repCode.end()));
			}
			
			repCode = std::to_string(MC_ADD_FRIEND_RESP);
			lengthString = std::to_string(currentUserName.length());
			lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
			repCode += lengthString + currentUserName + "1"; // Online
			Helper::sendData(it->first, BUFFER(repCode.begin(), repCode.end()));


			repCode = std::to_string(MC_APPROVE_FRIEND_REQ_RESP);
			lengthString = std::to_string(userName.length());
			lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
			repCode += lengthString + userName + "1"; // Online
		}
	}
	else {
		// Client handler not found, client is offline
		lengthString = std::to_string(userName.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + userName + "0"; // Offline
	}
	//Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

}

void Communicator::rejectFriendReq(SOCKET client_sock, int userId)
{
	std::string currentUserName = m_clients[client_sock]->getUsername();
	std::string userName = m_database->getUserName("", userId);
	std::string repCode;

	
	int currUserId = m_database->getUserId(currentUserName);

	m_database->rejectFriendReq(userId, currUserId);

	m_database->DeleteMessage(userId, currUserId);
	std::string msg = " Rejected friend request";
	m_database->AddMsg(currUserId, userId, msg, 0, -1);

	auto it = std::find_if(m_clients.begin(), m_clients.end(), [&userName](const auto& pair) {
		return pair.second->getUsername() == userName; // Assuming pair.first is the SOCKET identifier
		});

	if (it != m_clients.end()) {
		// Client handler found, client is online
		if (it->second->getWindow() == HOME_PAGE)
		{
			std::string repCode = std::to_string(MC_SEND_MSG_RESP);
			Helper::sendData(it->first, BUFFER(repCode.begin(), repCode.end()));
		}
	}

	repCode = std::to_string(MC_REJECT_FRIEND_REQ_RESP);
	std::string lengthString = std::to_string(userName.length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	repCode += lengthString + userName;

	//Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

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
		int recieverUserId = m_database->getUserId(userName);

		m_database->addFriendReq(m_database->getUserId(currUserName), recieverUserId);

		std::string msg = " Send friend request";
		m_database->AddMsg(m_database->getUserId(currUserName), recieverUserId, msg, 1, recieverUserId);


		auto it = std::find_if(m_clients.begin(), m_clients.end(), [&userName](const auto& pair) {
			return pair.second->getUsername() == userName; // Assuming pair.first is the SOCKET identifier
			});

		if (it != m_clients.end()) {
			// Client handler found, client is online
			if (it->second->getWindow() == HOME_PAGE || it->second->getWindow() == PROJECT_PAGE)
			{
				if (it->second->getWindow() == HOME_PAGE)
				{
					std::string repCode = std::to_string(MC_SEND_MSG_RESP);
					Helper::sendData(it->first, BUFFER(repCode.begin(), repCode.end()));
				}

				std::string repCode = std::to_string(MC_FRIEND_REQ_RESP);
				std::string lengthString = std::to_string(currUserName.length());
				lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
				repCode += lengthString + currUserName + "3"; // defualt
				Helper::sendData(it->first, BUFFER(repCode.begin(), repCode.end()));
			}
		}
	}
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

	std::string msg = " Removed you from friends";
	m_database->AddMsg(currUserId, removedUserId, msg, 0, -1);

	auto it = std::find_if(m_clients.begin(), m_clients.end(), [&userNameToRemove](const auto& pair) {
		return pair.second->getUsername() == userNameToRemove; // Assuming pair.first is the SOCKET identifier
		});
	if (it != m_clients.end() && (it->second->getWindow() == HOME_PAGE || it->second->getWindow() == PROJECT_PAGE))
	{
		if (it->second->getWindow() == HOME_PAGE)
		{
			std::string repCode = std::to_string(MC_SEND_MSG_RESP);
			Helper::sendData(it->first, BUFFER(repCode.begin(), repCode.end()));
		}
		repCode = std::to_string(MC_REMOVE_FRIEND_RESP) + userName;
		Helper::sendData(it->first, BUFFER(repCode.begin(), repCode.end()));
	}

	repCode = std::to_string(MC_REMOVE_FRIEND_RESP) + userNameToRemove;
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

	for (auto project : m_database->getUserProjectPermission(currUserId))
	{
		if (m_database->hasPermissionToProject(project.projectId, removedUserId))
		{
			Project projectDetail = m_database->getProject("", project.projectId);

			msg = "You had been removed from " + projectDetail.name + " by - " + userName;
			m_database->AddMsg(currUserId, removedUserId, msg, 0, -1);
			m_database->deleteProjectPermission(project.projectId, removedUserId);
			auto userIt = std::find_if(m_clients.begin(), m_clients.end(), [&userNameToRemove](const auto& pair) {
				return pair.second->getUsername() == userNameToRemove; // Assuming pair.first is the SOCKET identifier
				});

			if (userIt != m_clients.end() && userIt->second->getWindow() == HOME_PAGE) {
				// Client handler found, client is online
				repCode = std::to_string(MC_SEND_MSG_RESP);
				Helper::sendData(userIt->first, BUFFER(repCode.begin(), repCode.end()));

				repCode = std::to_string(MC_UPDATE_PROJECT_LIST_REQUEST);
				Helper::sendData(userIt->first, BUFFER(repCode.begin(), repCode.end()));
			}

			msg = userNameToRemove + " had been removed from " + projectDetail.name + " by - " + userName;
			notifyAllclientsOnProject("",client_sock, msg);
		}
	}

	auto senderUserIt = std::find_if(m_clients.begin(), m_clients.end(), [&userNameToRemove](const auto& pair) {
		return pair.second->getUsername() == userNameToRemove; // Assuming pair.first is the SOCKET identifier
		});

	for (auto project : m_database->getUserProjectPermission(removedUserId))
	{
		if (m_database->hasPermissionToProject(project.projectId, currUserId))
		{
			Project projectDetail = m_database->getProject("", project.projectId);

			msg = "You had been removed from " + projectDetail.name + " by - " + userNameToRemove;
			m_database->AddMsg(removedUserId, currUserId, msg, 0, -1);
			m_database->deleteProjectPermission(project.projectId, currUserId);
			auto userIt = std::find_if(m_clients.begin(), m_clients.end(), [&userName](const auto& pair) {
				return pair.second->getUsername() == userName; // Assuming pair.first is the SOCKET identifier
				});

			if (userIt != m_clients.end() && userIt->second->getWindow() == HOME_PAGE) {
				// Client handler found, client is online
				repCode = std::to_string(MC_SEND_MSG_RESP);
				Helper::sendData(userIt->first, BUFFER(repCode.begin(), repCode.end()));

				repCode = std::to_string(MC_UPDATE_PROJECT_LIST_REQUEST);
				Helper::sendData(userIt->first, BUFFER(repCode.begin(), repCode.end()));
			}
			msg = userName + " had been removed from " + projectDetail.name + " by - " + userNameToRemove;
			notifyAllclientsOnProject("", senderUserIt->first, msg);

		}
	}
}

void Communicator::editInfo(SOCKET client_sock, std::string bio)
{
	std::string repCode = std::to_string(MC_PROJECTS_LIST_RESP);;

	int userId = m_database->getUserId(m_clients[client_sock]->getUsername());
	ProfileInfo profile = m_database->getUsersInfo(userId);

	m_database->modifyProfile(profile.name, profile.email, bio, userId);

	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
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

void Communicator::getMessages(SOCKET client_sock, std::string userName)
{
	std::string lengthString;
	std::string repCode = std::to_string(MC_GET_MESSAGES_RESP);
	int userId = m_database->getUserId(m_clients[client_sock]->getUsername());

	for (auto msg : m_database->getUserMessages(userId))
	{
		std::string name = m_database->getUserName("", msg.senderId);
		lengthString = std::to_string(name.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += std::to_string(msg.mode) + lengthString + name;

		lengthString = std::to_string(msg.data.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + msg.data;

		if (msg.mode != 0)
		{
			lengthString = std::to_string(std::to_string(msg.itemId).length());
			lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
			repCode += lengthString + std::to_string(msg.itemId);
		}

		else
		{
			repCode += "00000";
		}

		lengthString = std::to_string(std::to_string(msg.id).length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + std::to_string(msg.id);
	}

	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::markMessageAsRead(SOCKET client_sock, int messageId)
{
	m_database->MarkAsRead(messageId);
}

void Communicator::markAllMessageAsRead(SOCKET client_sock)
{
	m_database->MarkAllAsRead(m_database->getUserId(m_clients[client_sock]->getUsername()));
}

void Communicator::getMessagesCount(SOCKET client_sock, std::string userName)
{
	std::string lengthString;
	std::string repCode = std::to_string(MC_GET_MESSAGES_RESP);
	int userId = m_database->getUserId(userName);
	int count = 0;

	for (auto msg : m_database->getUserMessages(userId))
	{
		count++;
	}
	lengthString = std::to_string(count);
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	repCode += lengthString;

	m_clients[client_sock]->setWindow(HOME_PAGE);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}
