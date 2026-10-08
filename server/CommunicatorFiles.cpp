#include "Communicator.h"

// Project files: create/delete/rename, opening and closing files, file permissions.

void Communicator::createFile(SOCKET client_sock, std::string fileName, int projectId)
{
	// Check if the file with the specified name exists
	//if (fileOperationHandler.fileExists(".\\files\\" + reqDetail.data + ".txt"))
	Project project = m_database->getProject("", projectId);

	std::string currUserName = m_clients[client_sock]->getUsername();

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

		std::string msg = currUserName + " created file - " + fileName + ", inside project - " + project.name;

		notifyAllclientsOnProject(repCode, client_sock, msg);
	}
}

void Communicator::deleteFile(SOCKET client_sock, std::string fileName, int projectId)
{
	Project project = m_database->getProject("", projectId);

	std::string currUserName = m_clients[client_sock]->getUsername();

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
		std::string msg = currUserName + " deleted file - " + fileName + ", inside project - " + project.name;

		notifyAllclientsOnProject(repCode, client_sock, msg);
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

	notifyAllclientsOnProject(repCode, client_sock, "");
}
