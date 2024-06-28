#pragma once
#include "IDatabase.h"
#include "Client.h"
#include <map>

class SqliteDataBase : public IDatabase
{
public:
	SqliteDataBase() = default;
	~SqliteDataBase() = default;

	bool open() override;
	bool close() override;
	bool doesUserExist(std::string username) override;
	bool doesPermissionRequestExist(int userId, int fileId, int creatorId) override;
	bool doesPasswordMatch(std::string username, std::string password) override;
	bool hasPermission(int userId, int fileId) override;
	bool hasPermissionToProject(int projectId, int userId) override;

	bool addNewUser(std::string username, std::string password, std::string email) override;
	int getUserId(std::string username) override;
	std::string getUserName(std::string username, int id) override;
	std::string getEmail(std::string username) override;
	std::list<ClientHandler> getAllUsers() override;
	std::string GetChatData(const std::string& fileName) override;
	std::list<Permission> getUserPermissions(int userId) override;
	std::list<PermissionReq> getPermissionRequests(int userId) override;
	FileDetail getFileDetails(const std::string& fileName, const int projectId) override;
	std::string getFileName(const int fileId) override;
	std::map<std::string, int> getUserPermissionDetails(int userId) override;
	ProfileInfo getUsersInfo(int userId) override;
	std::list<std::string> searchUsers(std::string searchCommand) override;
	std::list<Project> getAllProjects(int userId) override;
	Project getProject(std::string projectName) override;
	std::list<FileDetail> getProjectFiles(int projectId) override;
	Friends getUserFriends(int userId) override;
	std::list<FriendReq> getUserFriendReq(int userId) override;
	
	void UpdateChat(const std::string& fileName, const std::string& data) override;
	void createChat(const std::string& fileName) override;
	void DeleteChat(const std::string& fileName) override;
	void addPermissionRequest(int userId, int fileId, int creatorId) override;
	void addUserPermission(int userId, int fileId) override;
	void addFile(int userId, const std::string& fileName, int projectId) override;
	void deleteFile(const std::string& fileName) override;
	void deleteAllProjectFiles(const int projectId) override;
	void deletePermissionRequests(int userId, int fileId) override;
	void deletePermission(int fileId) override;
	void deleteAllPermissionReq(int fileId) override;
	void changePassword(std::string username, std::string opdPass, std::string newPass) override;
	void createProfile(std::string username, std::string email, std::string bio, int userId) override;
	void modifyProfile(std::string username, std::string email, std::string bio, int userId) override;
	void createProject(std::string projectName, std::list<ProfileInfo> addedUsers, std::string codeLan, int creatorId) override;
	void deleteProject(const std::string projectName) override;
	void createProjectPermission(int projectId, int userId) override;
	void deleteAllProjectPermission(int projectId) override;
	void deleteProjectPermission(int projectId, int userId) override;
	void addFriend(int userId, std::string friendsList) override;
	void removeFriend(int userId, std::string friendsList) override;
	void approveFriendReq(int userId, int friendRequsetId) override;
	void rejectFriendReq(int userId, int friendRequsetId) override;
	void renameFile(int projectId, std::string newFileName, std::string oldFileName) override;
private:
	sqlite3* _db;

	bool send(sqlite3* db, std::string msg);
	bool send_users(sqlite3* db, std::string msg, std::list<ClientHandler>* users);
	bool send_data(sqlite3* db, std::string msg, std::list<std::string>* data);
	bool send_chats(sqlite3* db, std::string msg, std::list<Chat>* data);
	bool send_Permissions(sqlite3* db, std::string msg, std::list<Permission>* data);
	bool send_PermissionReq(sqlite3* db, std::string msg, std::list<PermissionReq>* data);
	bool send_file(sqlite3* db, std::string msg, std::list<FileDetail>* data);
	bool send_profInfo(sqlite3* db, std::string msg, std::list<ProfileInfo>* data);
	bool send_Projects(sqlite3* db, std::string msg, std::list<Project>* data);
	bool send_Friends(sqlite3* db, std::string msg, std::list<Friends>* data);
	bool send_FriendReq(sqlite3* db, std::string msg, std::list<FriendReq>* data);
};

