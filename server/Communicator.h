#pragma once
#include <iostream>
#include <map>
#include <stdexcept>
#include <thread>
#include <exception>
#include <unordered_map>
#include <sys/stat.h>
#include <winsock2.h>
#include <ws2tcpip.h>
#include <filesystem>
#include <mutex>
#include <chrono>
#include <cstdlib> // For system function
#include <cstdio> // For popen and pclose
#include <string>
#include <unordered_set>
#include "Client.h"
#include "helper.h"
#include "Operations.h"
#include "FileOperation.h"
#include "IDatabase.h"

#pragma comment(lib, "ws2_32.lib")  // Add this lin

#define PORT 12345
#define CLOUD_PORT 5555
#define BUFFER_SIZE 4096

#define HOME_PAGE "HomePage"
#define PROJECT_PAGE "ProjectPage"
#define CREATE_PROJECT_PAGE "CreateProjectPage"
#define TO_TO_LIST_PAGE "ToDoListPage"
#define ISSUE_DATA_PAGE "IssueDataPage"
#define MESSAGES_PAGE "MessagesPage"
#define SETTINGS_PAGE "SettingsPage"
#define SEARCH_PAGE "SearchPage"

struct Action
{
    int code;
    std::string dataLength;
    std::string data;
    std::string index;
    std::string newLineCount;
    std::string selectionLength;

    long long timestamp; // Timestamp indicating when the action was created

    std::string fileName;
    std::string fileNameLength;
    int fileId;

    std::string oldFileName;
    int oldFileNameLength;

    std::string msg;

    int userNameLength; // login/ signup
    std::string userName;

    int userNameToRemoveLength; // login/ signup
    std::string userNameToRemove;

    std::string pass;
    int passLength;

    std::string oldPass; // forgot password
    int oldPassLength;

    std::string email;
    int emailLength;

    std::string bio;
    int bioLength;
    
    std::string projectName;
    int projectNameLength;
    std::string projectNameLengthStr;
    int projectId;
    int projectIdLength;

    std::string oldProjectName;
    int oldProjectNameLength;

    std::string role;
    int roleLength;

    std::string codeLaneguage;
    int codeLaneguageLength;

    std::string firendsList;
    int firendsListLength;

    std::string searchCommand;
    int searchCommandLength;

    std::string modeStr;
    bool mode;

    int messageId;

    int issueId;
    std::string issueData;
    int issueDataLength;
    
    std::string issueDate;
    int issueDateLength;

    std::string assignedPeople;
    int assignedPeopleLength;

    int size;
    int userIdLen;
    int userId;
};

class Communicator {
private:
    SOCKET m_serverSocket;
    std::map<SOCKET, ClientHandler*> m_clients;
    std::map<int, std::vector<Action>> m_lastActionMap; // fileId : <lastAction, index>
    std::map<int, std::vector<ClientHandler>> m_usersOnFile; // fileId : users
    std::map<int, std::vector<ClientHandler>> m_usersOnProject; // projectName : users

    std::map<int, std::map<std::string, int>> m_projects; // name, id
    std::map<std::string, std::vector<std::string>> m_friends; // name, id
    std::map<int, std::string> m_filesData; // id, data
    std::map<int, bool> m_FileUpdate; // id, there was a change since last update

    std::unordered_map<int, std::mutex> m_fileMutexes;

    Operations operationHandler;
    FileOperation fileOperationHandler;
    IDatabase* m_database;

    std::string executeCommand(const std::string& command);
    std::string _dirName;

    bool needUpdate;
public:
    // Constructor
    Communicator();

    // Destructor
    ~Communicator();

    void sendFile(SOCKET clientSocket, const std::string& filePath);

    void setDB(IDatabase* db);
    void startHandleRequests();
    void bindAndListen();
    void handleNewClient(SOCKET client_sock);
    bool isSocketConnected(SOCKET socket);

    void updateFileOnServerold(const std::string& filePath, const Action& reqDetail);
    void updateFileOnServer(const int fileId, const Action& reqDetail);
    void saveFiles();
    void checkClientsConnection();
    void getVersion(SOCKET client_sock);

    void handleClientDisconnect(SOCKET client_sock);
    void handleError(SOCKET client_sock, std::exception a);
    long long getCurrentTimestamp();

    void notifyAllClients(const std::string& updatedContent, SOCKET client_sock, const bool isOnFile);
    void notifyAllfriends(const std::string& updatedContent, SOCKET client_sock);
    void notifyAllclientsOnProject(const std::string& updatedContent, SOCKET client_sock, std::string msg);
    void notifyAllclientsOnIssueWindow(const std::string& updatedContent, SOCKET client_sock, std::string msg);

    Action deconstructReq(const std::string& req);
    Action adjustIndexForSync(const int fileId, int projectId, Action reqDetail);

    void login(SOCKET client_sock, std::string username, std::string pass, std::string mail);
    void logout(SOCKET client_sock);
    void signUp(SOCKET client_sock, std::string username, std::string pass, std::string mail);
    void forgotPassword(SOCKET client_sock, std::string username, std::string pass, std::string oldPass, std::string mail);
    void createFile(SOCKET client_sock, std::string fileNamem, int projectId);
    void deleteFile(SOCKET client_sock, std::string fileName, int projectId);
    void getFiles(SOCKET client_sock);
    void getInitialContent(SOCKET client_sock, std::string fileName, int projectId);
    void enterFile(SOCKET client_sock, std::string fileName, std::string fileNameLen);
    void enterProject(SOCKET client_sock, int projectId);
    void exitFile(SOCKET client_sock);
    void exitProject(SOCKET client_sock);
    void leaveProject(SOCKET client_sock, int projectId, std::string userName);
    void acceptProjectInvite(SOCKET client_sock, int ProjectId, std::string userName);
    void declineProjectInvite(SOCKET client_sock, int ProjectId, std::string userName);
    void getChatMesegges(SOCKET client_sock, std::string projectName);
    void getUsersOnFile(SOCKET client_sock, std::string fileName);
    void getUsers(SOCKET client_sock);
    void moveToCreateWindow(SOCKET client_sock);
    void getUserPermissionReq(SOCKET client_sock);
    void postChatMsg( SOCKET client_sock, int projectId, std::string data, std::string dataLen);
    void approvePermissionReq(SOCKET client_sock, std::string username, std::string filename);
    void rejectPermissionReq(SOCKET client_sock, std::string username, std::string filename);
    void permissionFileReq(SOCKET client_sock, std::string username, std::string filename, std::string fileNameLen);
    void getProfileInfo(SOCKET client_sock, std::string userName);
    void getProfileImage(SOCKET client_sock, std::string userName);
    void getUserFriends(SOCKET client_sock, std::string userName);
    void approveFriendReq(SOCKET client_sock, int userId);
    void rejectFriendReq(SOCKET client_sock, int userId);
    void addFriend(SOCKET client_sock, std::string userName);
    void getProjectsList(SOCKET client_sock, std::string userName);
    void getUserProjectsList(SOCKET client_sock, std::string userName);
    void getProjectFiles(SOCKET client_sock, int projectId);
    void removeFriend(SOCKET client_sock, std::string userName, std::string userNameToRemove);
    void editInfo(SOCKET client_sock, std::string bio);
    void createProject(SOCKET client_sock, std::string projectName, std::string friendList, std::string codeLan, int mode);
    void deleteProject(SOCKET client_sock,int projectId);
    void renameFile(SOCKET client_sock, std::string fileName, std::string oldFileName, int projectId);
    void searchUsers(SOCKET client_sock, std::string searchCommand);
    void searchFriends(SOCKET client_sock, std::string searchCommand);
    void editProjectInfo(SOCKET client_sock, int projectId);
    void viewProjectInfo(SOCKET client_sock, int projectId);
    void modifyProjectInfo(SOCKET client_sock, int projectId, std::string newProjectName, std::string friendList, std::string codeLan);
    void getProjectInfo(SOCKET client_sock, std::string projectName);
    void getMessages(SOCKET client_sock, std::string userName);
    void markMessageAsRead(SOCKET client_sock, int messageId);
    void markAllMessageAsRead(SOCKET client_sock);
    void getMessagesCount(SOCKET client_sock, std::string userName);
    void loadCurrentIssues(SOCKET client_sock, int projectId);
    void loadCompletedIssues(SOCKET client_sock, int projectId);
    void getIssue(SOCKET client_sock, int issueId);
    void markIssueAsComplete(SOCKET client_sock, int issueId);
    void markIssueAsNotComplete(SOCKET client_sock, int issueId);
    void addTask(SOCKET client_sock, std::string issueData, std::string assignedPeople, std::string issueDate);
    void modifyIssue(SOCKET client_sock, std::string issueData, std::string assignedPeople, std::string issueDate, int issueId);
    void deleteTask(SOCKET client_sock, int issueId);
    void getProjectParticipants(SOCKET client_sock, int projectId);
    void getCodeStyles(SOCKET client_sock);
    void getMailImages(SOCKET client_sock);
    void moveToSettings(SOCKET client_sock);
    void moveToMessages(SOCKET client_sock);
    void moveToToDoWindow(SOCKET client_sock);
    void moveToProjectWindow(SOCKET client_sock, int projectId);
    void moveToIssueData(SOCKET client_sock, int issueId);
    void backToMainPage(SOCKET client_sock);
    void backToProjectPage(SOCKET client_sock);

};