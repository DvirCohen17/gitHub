#pragma once
#include <string>
#include "sqlite3.h"
#include <io.h>
#include <list>
#include <vector>
#include <map>
#include "Client.h"

struct Chat
{
	std::string projectName;
	std::string data;
};

struct PermissionReq
{
	int fileId;
	int userId;
	int creatorId;
};

struct Permission
{
	int fileId;
	int projectId;
};


struct ProjectPermission
{
	std::string role;
	int projectId;
};

struct FileDetail
{
	int creatorId;
	int fileId;
	int projectId;
	std::string fileName;
};

struct ProfileInfo
{
	std::string name;
	std::string email;
	std::string bio;
	int userId;

	bool operator<(const ProfileInfo& other) const {
		return name < other.name; // Compare based on unique id, adjust if necessary
	}
};

struct Project
{
	std::string name;
	std::string codeLan;
	int userId;
	int projectId;
};

struct ProjectJoinInvite
{
	std::string role;
	int userId;
	int projectId;
};


struct Friends
{
	std::string fiendsList;
	int userId;
};

struct FriendReq
{
	int friendReqId;
	int userId;
};

class IDatabase
{
public:
	virtual ~IDatabase() = default;
	virtual bool open() = 0;
	virtual bool close() = 0;
	virtual bool doesUserExist(std::string username) = 0;
	virtual bool doesPermissionRequestExist(int userId, int fileId, int creatorId) = 0;
	virtual bool doesPasswordMatch(std::string username, std::string password) = 0;
	virtual bool hasPermission(int userId, int fileId) = 0;
	virtual bool hasPermissionToProject(int projectId, int userId) = 0;

	virtual bool addNewUser(std::string username, std::string password, std::string email) = 0;
	virtual int getUserId(std::string username) = 0;
	virtual std::string getUserName(std::string username, int id) = 0;
	virtual std::string getEmail(std::string username) = 0;
	virtual std::list<ClientHandler> getAllUsers() = 0;
	virtual std::string GetChatData(const std::string& fileName) = 0;
	virtual std::list<Permission> getUserPermissions(int userId) = 0;
	virtual std::list<PermissionReq> getPermissionRequests(int userId) = 0;
	virtual std::string getFileName(const int fileId) = 0;
	virtual FileDetail getFileDetails(const std::string& fileName, const int projectId) = 0;
	virtual std::map<std::string, int> getUserPermissionDetails(int userId) = 0;
	virtual ProfileInfo getUsersInfo(int userId) = 0;
	virtual std::list<std::string> searchUsers(std::string searchCommand) = 0;
	virtual std::list<Project> getAllProjects(int userId) = 0;
	virtual Project getProject(std::string projectName, int projectId) = 0;
	virtual std::list<FileDetail> getProjectFiles(int projectId) = 0;
	virtual Friends getUserFriends(int userId) = 0;
	virtual std::list<FriendReq> getUserFriendReq(int userId) = 0;
	virtual std::list<FriendReq> getCurrentUserReqSent(int userId) = 0;
	virtual std::string getUserRoleInProject(int userId, int projectId) = 0;
	virtual std::list<ProjectJoinInvite> getUserProjectInvite(int userId) = 0;
	virtual std::list<ProjectPermission> getUserProjectPermission(int userId) = 0;

	virtual void UpdateChat(const std::string& fileName, const std::string& data) = 0;
	virtual void createChat(const std::string& ProjectName) = 0;
	virtual void DeleteChat(const std::string& ProjectName) = 0;
	virtual void addPermissionRequest(int userId, int fileId, int creatorId) = 0;
	virtual void addUserPermission(int userId, int fileId) = 0;
	virtual void addFile(int userId, const std::string& fileName, int projectId) = 0;
	virtual void deleteFile(const std::string& fileName) = 0;
	virtual void deleteAllProjectFiles(const int projectId) = 0;
	virtual void deletePermissionRequests(int userId, int fileId) = 0;
	virtual void deletePermission(int fileId) = 0;
	virtual void deleteAllPermissionReq(int fileId) = 0;
	virtual void changePassword(std::string username, std::string opdPass, std::string newPass) = 0;
	virtual void createProfile(std::string username, std::string email , std::string bio, int userId) = 0;
	virtual void modifyProfile(std::string username, std::string email , std::string bio, int userId) = 0;
	virtual void createProject(std::string projectName, std::map<ProfileInfo, std::string> addedUsers, std::string codeLan, int creatorId) = 0;
	virtual void deleteProject(const std::string projectName) = 0;
	virtual void createProjectPermission(int projectId, int userId, std::string role) = 0;
	virtual void createProjectJoinInvite(int projectId, int userId, std::string role) = 0;
	virtual void deleteAllProjectPermission(int projectId) = 0;
	virtual void deleteProjectPermission(int projectId, int userId) = 0;
	virtual void addFriend(int userId, std::string friendsList) = 0;
	virtual void addFriendReq(int userId, int friendRequsetId) = 0;
	virtual void removeFriend(int userId, std::string friendsList) = 0;
	virtual void approveFriendReq(int userId, int friendRequsetId) = 0;
	virtual void rejectFriendReq(int userId, int friendRequsetId) = 0;
	virtual void renameFile(int projectId, std::string newFileName, std::string oldFileName) = 0;
};