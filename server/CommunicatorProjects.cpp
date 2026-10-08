#include "Communicator.h"

// Projects: create/modify/delete, invites, entering and leaving.

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

	/*
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
	*/

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

void Communicator::createProject(SOCKET client_sock, std::string projectName, std::string friendList, std::string codeLan, int mode)
{
	std::string repCode;

	int userId = m_database->getUserId(m_clients[client_sock]->getUsername());
	std::map<ProfileInfo, std::string> friends; // info, role

	int index = 0;

	m_database->createProject(projectName, friends, codeLan, userId, -1);
	Project project = m_database->getProject(projectName, -1);

	int projectId = project.projectId;
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

		std::string msg = " Added you to Project - " + projectName;
		m_database->AddMsg(userId, m_database->getUserId(name), msg, 2, projectId);

		auto it = std::find_if(m_clients.begin(), m_clients.end(), [&name](const auto& pair) {
			return pair.second->getUsername() == name; // Assuming pair.first is the SOCKET identifier
			});

		if (it != m_clients.end() && it->second->getWindow() == HOME_PAGE) {
			// Client handler found, client is online

			std::string repCode = std::to_string(MC_SEND_MSG_RESP);
			Helper::sendData(it->first, BUFFER(repCode.begin(), repCode.end()));

			repCode = std::to_string(MC_UPDATE_PROJECT_LIST_REQUEST);
			Helper::sendData(it->first, BUFFER(repCode.begin(), repCode.end()));
		}
	}

	m_database->createProjectPermission(projectId, userId, "creator");
	m_database->createChat(projectId);

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
		m_database->DeleteProjectIssues(projectId);
		m_database->DeleteAllIssue(projectId);

		for (auto user : projectPermissions)
		{
			std::string currUserName = user.first;
			int currUserId = m_database->getUserId(user.first);

			std::string msg = " Deleted project - " + projectName;
			m_database->AddMsg(userId, currUserId, msg, 0, -1);

			auto userIr = std::find_if(m_clients.begin(), m_clients.end(), [&currUserName](const auto& pair) {
				return pair.second->getUsername() == currUserName; // Assuming pair.first is the SOCKET identifier
				});

			if (userIr != m_clients.end() && userIr->second->getWindow() == HOME_PAGE) {

				std::string repCode = std::to_string(MC_SEND_MSG_RESP);
				Helper::sendData(userIr->first, BUFFER(repCode.begin(), repCode.end()));

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
	std::string currUserName = m_clients[client_sock]->getUsername();
	int currUserId = m_database->getUserId(currUserName);
	if (!m_database->hasPermissionToProject(projectId, currUserId))
	{
		throw std::runtime_error("You dont longer have permission to this project");

	}

	std::map<ProfileInfo, std::string> friends; // info, role

	int index = 0;
	Project project = m_database->getProject("", projectId);
	std::string oldProjectName = project.name;
	std::map<std::string, std::string> oldFriendList;
	std::map<std::string, std::string> newFriendList;

	std::string msg;

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

	// check if all reemoved users not working on the project
	for (auto user : m_database->getProjectParticipants(projectId))
	{
		oldFriendList[user.first] = user.second;

		if (user.first == currUserName) continue;

		std::string userName = user.first;
		auto it = std::find_if(newFriendList.begin(), newFriendList.end(), [&userName](const auto& pair) {
			return pair.first == userName; // Assuming pair.first is the SOCKET identifier
			});

		if (it == newFriendList.end())
		{
			auto userIt = std::find_if(m_clients.begin(), m_clients.end(), [&userName](const auto& pair) {
				return pair.second->getUsername() == userName; // Assuming pair.first is the SOCKET identifier
				});

			if (userIt != m_clients.end() && userIt->second->getWindow() == PROJECT_PAGE && userIt->second->getProjectName() == oldProjectName)
			{
				throw std::runtime_error("Cant remove user that inside the project - " + userIt->second->getUsername());
			}
		}

	}

	for (auto user : m_database->getProjectParticipants(projectId))
	{
		oldFriendList[user.first] = user.second;

		if (user.first == currUserName) continue;

		std::string userName = user.first;
		auto it = std::find_if(newFriendList.begin(), newFriendList.end(), [&userName](const auto& pair) {
			return pair.first == userName; // Assuming pair.first is the SOCKET identifier
			});

		if (it == newFriendList.end())
		{
			auto userIt = std::find_if(m_clients.begin(), m_clients.end(), [&userName](const auto& pair) {
				return pair.second->getUsername() == userName; // Assuming pair.first is the SOCKET identifier
				});

			msg = "You had been removed from " + oldProjectName + " by - " + currUserName;
			m_database->AddMsg(currUserId, m_database->getUserId(userName), msg, 0, -1);
			m_database->deleteProjectPermission(projectId, m_database->getUserId(userName));

			if (userIt != m_clients.end() && userIt->second->getWindow() == HOME_PAGE) {
				// Client handler found, client is online
				repCode = std::to_string(MC_SEND_MSG_RESP);
				Helper::sendData(userIt->first, BUFFER(repCode.begin(), repCode.end()));

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

		msg = currUserName + " had changed project info - " + oldProjectName;
		m_database->AddMsg(currUserId, m_database->getUserId(name), msg, 0, -1);

		auto userIr = std::find_if(m_clients.begin(), m_clients.end(), [&name](const auto& pair) {
			return pair.second->getUsername() == name; // Assuming pair.first is the SOCKET identifier
			});

		if (userIr != m_clients.end() && userIr->second->getWindow() == HOME_PAGE) {
			// Client handler found, client is online
			repCode = std::to_string(MC_SEND_MSG_RESP);
			Helper::sendData(userIr->first, BUFFER(repCode.begin(), repCode.end()));

			repCode = std::to_string(MC_UPDATE_PROJECT_LIST_REQUEST);
			Helper::sendData(userIr->first, BUFFER(repCode.begin(), repCode.end()));
		}
		else if (userIr != m_clients.end() && userIr->second->getWindow() == PROJECT_PAGE)
		{
			m_clients[userIr->first]->setProject(projectName, projectId);
		}
	}

	m_database->modifyProjectInfo(projectId, projectName, friends, codeLan);
	repCode = std::to_string(MC_BACK_TO_HOME_PAGE_RESP);
	// Send to each friend the invite if they are online
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::acceptProjectInvite(SOCKET client_sock, int ProjectId, std::string userName)
{
	std::string repCode = std::to_string(MC_UPDATE_PROJECT_LIST_REQUEST);

	int userId = m_database->getUserId(m_clients[client_sock]->getUsername());

	m_database->DeleteMessage(ProjectId, userId);

	std::string newRole = m_database->getAUserProjectInvite(userId, ProjectId).role;
	m_database->acceptProjectJoinInvite(ProjectId, userId, newRole);

	std::string msg = userName + " Joined Project - " + m_database->getProject("", ProjectId).name;
	notifyAllclientsOnProject("", client_sock, "");
	//Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

}

void Communicator::declineProjectInvite(SOCKET client_sock, int ProjectId, std::string userName)
{
	std::string repCode = std::to_string(MC_UPDATE_PROJECT_LIST_REQUEST);
	//Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

	int userId = m_database->getUserId(m_clients[client_sock]->getUsername());
	m_database->DeleteMessage(ProjectId, userId);
	m_database->deleteProjectJoinInvite(ProjectId, userId);
}

void Communicator::moveToCreateWindow(SOCKET client_sock)
{
	std::string repCode = std::to_string(MC_MOVE_TO_CREATE_PROJ_WINDOW_RESP) + "00000create";

	m_clients[client_sock]->setWindow(CREATE_PROJECT_PAGE);
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
	m_clients[client_sock]->setWindow(PROJECT_PAGE);

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
	m_clients[client_sock]->setWindow(HOME_PAGE);
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
			
			for (auto issue : m_database->getAllProjectIssues(projectId))
			{
				int i = 0;
				bool inIssue = false;
				std::string newList = "";
				while (i < issue.usersAssigment.length()) {
					// Extract the length of the name
					std::string lengthStr = issue.usersAssigment.substr(i, 5);
					int length = std::stoi(lengthStr);
					i += 5;

					// Extract the name itself
					std::string extractedName = issue.usersAssigment.substr(i, length);
					i += length;

					// Compare the extracted name with the name to search
					if (extractedName == userName) {
						inIssue = true;
						continue;
					}
					else
					{
						newList += lengthStr + extractedName;
					}
				}
				if (inIssue)
				{
					m_database->UpdateIssue(issue.id, issue.data, issue.date, newList);
				}
			}

			std::string replacerName = m_database->getUserName("", replacerId);
			auto it = std::find_if(m_clients.begin(), m_clients.end(), [&replacerName](const auto& pair) {
				return pair.second->getUsername() == replacerName; // Assuming pair.first is the SOCKET identifier
				});

			if (it != m_clients.end()) {
				// Client handler found, client is online
				repCode = std::to_string(MC_UPDATE_PROJECT_LIST_REQUEST);
				Helper::sendData(it->first, BUFFER(repCode.begin(), repCode.end()));
			}

			std::string msg = userName + " Left Project - " + project.name;
			notifyAllclientsOnProject("", client_sock, msg);
		}
	}
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
		m_clients[client_sock]->setWindow(CREATE_PROJECT_PAGE);

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
	m_clients[client_sock]->setWindow(CREATE_PROJECT_PAGE);
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

void Communicator::getProjectParticipants(SOCKET client_sock, int projectId)
{
	std::string lengthString;
	std::string repCode = std::to_string(MC_GET_PROJECT_PATICIPANTS_RESP);
	std::map<std::string, std::string> projectParticipants = m_database->getProjectParticipants(projectId);

	for (auto user : projectParticipants)
	{
		lengthString = std::to_string(user.first.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + user.first;
	}

	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}
