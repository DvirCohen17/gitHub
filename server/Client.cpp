#include "Client.h"

ClientHandler::ClientHandler(int clientId, std::string userName, std::string email)
    : _id(clientId), _userName(userName), _email(email), window(""), _fileName(""), _projectName(""), _projectId(-1), _fileId(-1)
{
}

ClientHandler::ClientHandler()
    : _id(0), _userName(""), _email(""), window(""), _fileName(""), _projectName(""), _projectId(-1), _fileId(-1)
{
}

ClientHandler::~ClientHandler()
{
}

int ClientHandler::getId() const {
    return _id;
}

std::string ClientHandler::getFileName() const
{
    return _fileName;
}

std::string ClientHandler::getFileContent() const
{
    return _fileContent;
}

int ClientHandler::getFileId() const
{
    return _fileId;
}

std::string ClientHandler::getProjectName() const
{
    return _projectName;
}

int ClientHandler::getProjectId() const
{
    return _projectId;
}

int ClientHandler::getIssueId() const
{
    return _issueId;

}

std::string ClientHandler::getUsername() const
{
    return _userName;
}

std::string ClientHandler::getPass() const
{
    return _pass;
}

std::string ClientHandler::getEmail() const
{
    return _email;
}

std::string ClientHandler::getWindow() const
{
    return window;
}

void ClientHandler::setFile(const std::string& newFileName, const std::string& content, int fileId)
{
    _fileName = newFileName;
    _fileId = fileId;
    _fileContent = content;
}

void ClientHandler::setProject(const std::string& newProjectName, int projectId)
{
    _projectName = newProjectName;
    _projectId = projectId;
}

void ClientHandler::setUsername(const std::string& newName)
{
    _userName = newName;
}

void ClientHandler::setEmail(const std::string& newEmail)
{
    _email = newEmail;
}

void ClientHandler::setWindow(const std::string& newWindow)
{
    window = newWindow;
}

void ClientHandler::setPass(const std::string& newPass)
{
    _pass = newPass;
}

void ClientHandler::setId(int id)
{
    _id = id;
}

void ClientHandler::setIssue(int id)
{
    _issueId = id;
}
