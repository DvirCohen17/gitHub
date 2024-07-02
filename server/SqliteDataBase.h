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
	// ******* CHECKS *******
	bool doesUserExist(std::string username) override;
	bool doesPermissionRequestExist(int userId, int fileId, int creatorId) override;
	bool doesPasswordMatch(std::string username, std::string password) override;
	bool hasPermission(int userId, int fileId) override;
	bool hasPermissionToProject(int projectId, int userId) override;
	bool isCreator(std::string projectName, int userId) override;

	// ******* GETTERS *******

	// ******* PROFILE *******
	bool addNewUser(std::string username, std::string password, std::string email) override;
	int getUserId(std::string username) override;
	std::string getUserName(std::string username, int id) override;
	std::string getEmail(std::string username) override;
	std::list<ClientHandler> getAllUsers() override;
	ProfileInfo getUsersInfo(int userId) override;
	std::list<std::string> searchUsers(std::string searchCommand) override;

	// ******* CHATS *******
	std::string GetChatData(const int projectId) override;

	// ******* PERMISSIONS *******
	std::list<Permission> getUserPermissions(int userId) override;
	std::list<PermissionReq> getPermissionRequests(int userId) override;
	std::map<std::string, int> getUserPermissionDetails(int userId) override;

	// ******* FILES *******
	std::string getFileName(const int fileId) override;
	FileDetail getFileDetails(const std::string& fileName, const int projectId) override;
	std::string getFileContent(const int fileId) override;

	// ******* PROJECTS *******
	std::list<Project> getAllProjects(int userId) override;
	Project getProject(std::string projectName, int projectId) override;
	std::list<FileDetail> getProjectFiles(int projectId) override;
	std::list<FriendReq> getCurrentUserReqSent(int userId) override;
	std::string getUserRoleInProject(int userId, int projectId) override;
	std::list<ProjectJoinInvite> getUserProjectInvite(int userId) override;
	ProjectJoinInvite getAUserProjectInvite(int userId, int projectId) override;
	std::list<ProjectPermission> getUserProjectPermission(int userId) override;
	int getNextAdmin(int projectId) override;
	std::map<std::string, std::string> getProjectParticipants(int projectId) override;
	// ******* FRIENDS *******
	Friends getUserFriends(int userId) override;
	std::list<FriendReq> getUserFriendReq(int userId) override;


	// ******* MODIFIRES *******

	// ******* CHATS *******
	void UpdateChat(const int projectId, const std::string& data) override;
	void createChat(const int projectId) override;
	void DeleteChat(const int projectId) override;

	// ******* PERMISSIONS *******
	void addPermissionRequest(int userId, int fileId, int creatorId) override;
	void addUserPermission(int userId, int fileId) override;
	void deletePermissionRequests(int userId, int fileId) override;
	void deletePermission(int fileId) override;
	void deleteAllPermissionReq(int fileId) override;
	void createProjectPermission(int projectId, int userId, std::string role) override;
	void createProjectJoinInvite(int projectId, int userId, std::string role) override;
	void deleteProjectJoinInvite(int projectId, int userId) override;
	void acceptProjectJoinInvite(int projectId, int userId, std::string role) override;
	void deleteAllProjectPermission(int projectId) override;
	void deleteProjectPermission(int projectId, int userId) override;
	void changeUserRoleInProject(int projectId, int userId, std::string role) override;

	// ******* FILES *******
	void addFile(int userId, const std::string& fileName, int projectId) override;
	void deleteFile(const std::string& fileName, const int projectId) override;
	void renameFile(int projectId, std::string newFileName, std::string oldFileName) override;
	void updateFile(int fileId, std::string content) override;

	// ******* PROJECTS *******
	void deleteAllProjectFiles(const int projectId) override;
	void createProject(std::string projectName, std::map<ProfileInfo, std::string> addedUsers, std::string codeLan, int creatorId, int projectId) override;
	void modifyProjectInfo(std::string oldProjectName, std::string newProjectName, std::map<ProfileInfo, std::string> addedUsers, std::string codeLan, int projectId) override;
	void deleteProject(const std::string projectName) override;
	void leaveProject(const std::string projectName, int userId) override;

	// ******* PROFILE *******
	void changePassword(std::string username, std::string opdPass, std::string newPass) override;
	void createProfile(std::string username, std::string email, std::string bio, int userId) override;
	void modifyProfile(std::string username, std::string email, std::string bio, int userId) override;

	// ******* FRIENDS *******
	void addFriend(int userId, std::string friendsList) override;
	void addFriendReq(int userId, int friendRequsetId) override;
	void removeFriend(int userId, std::string friendsList) override;
	void approveFriendReq(int userId, int friendRequsetId) override;
	void rejectFriendReq(int userId, int friendRequsetId) override;
private:
	sqlite3* _db;

	bool send(sqlite3* db, std::string msg);
	bool send_users(sqlite3* db, std::string msg, std::list<ClientHandler>* users);
	bool send_data(sqlite3* db, std::string msg, std::list<std::string>* data);
	bool send_chats(sqlite3* db, std::string msg, std::list<Chat>* data);
	bool send_Permissions(sqlite3* db, std::string msg, std::list<Permission>* data);
	bool send_PermissionReq(sqlite3* db, std::string msg, std::list<PermissionReq>* data);
	bool send_file(sqlite3* db, std::string msg, std::list<FileDetail>* data);
	bool send_fileData(sqlite3* db, std::string msg, std::list<FileDetail>* data);
	bool send_profInfo(sqlite3* db, std::string msg, std::list<ProfileInfo>* data);
	bool send_Projects(sqlite3* db, std::string msg, std::list<Project>* data);
	bool send_Friends(sqlite3* db, std::string msg, std::list<Friends>* data);
	bool send_FriendReq(sqlite3* db, std::string msg, std::list<FriendReq>* data);
	bool send_ProjectPermissions(sqlite3* db, std::string msg, std::list<ProjectPermission>* data);
	bool send_ProjectJoinInvite(sqlite3* db, std::string msg, std::list<ProjectJoinInvite>* data);
};

