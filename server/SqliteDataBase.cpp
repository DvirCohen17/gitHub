#include "SqliteDataBase.h"

int callback_data(void* data, int argc, char** argv, char** azColName)
{
	std::list<std::string>* list_data = (std::list<std::string>*)data;

	for (int i = 0; i < argc; i++) {
		list_data->push_back(argv[i]);
	}
	return 0;
}

int callback_users(void* data, int argc, char** argv, char** azColName)
{
	std::list<ClientHandler>* list_users = (std::list<ClientHandler>*)data;
	ClientHandler user;

	for (int i = 0; i < argc; i++) {
		if (std::string(azColName[i]) == "user_name") {
			user.setUsername(argv[i]);
		}
		else if (std::string(azColName[i]) == "mail") {
			user.setEmail(argv[i]);
		}
		else if (std::string(azColName[i]) == "password") {
			user.setPass(argv[i]);
		}
		else if (std::string(azColName[i]) == "id") {
			user.setId(std::stoi(argv[i]));
		}
	}
	list_users->push_back(user);
	return 0;
}

int callback_chats(void* data, int argc, char** argv, char** azColName)
{
	std::list<Chat>* list_chats = (std::list<Chat>*)data;
	Chat chat;

	for (int i = 0; i < argc; i++) {
		if (std::string(azColName[i]) == "projectId") {
			chat.projectId = std::stoi((argv[i]));
		}
		else if (std::string(azColName[i]) == "data") {
			chat.data = argv[i];
		}
	}
	list_chats->push_back(chat);
	return 0;
}

int callback_Permissions(void* data, int argc, char** argv, char** azColName)
{
	std::list<Permission>* list_permissions = (std::list<Permission>*)data;
	Permission perm;

	for (int i = 0; i < argc; i++) {
		if (std::string(azColName[i]) == "fileId") {
			perm.fileId = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "projectId") {
			perm.fileId = std::stoi(argv[i]);
		}
	}
	list_permissions->push_back(perm);
	return 0;
}

int callback_PermissionReq(void* data, int argc, char** argv, char** azColName)
{
	std::list<PermissionReq>* list_permissionReq = (std::list<PermissionReq>*)data;
	PermissionReq req;

	for (int i = 0; i < argc; i++) {
		if (std::string(azColName[i]) == "fileId") {
			req.fileId = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "userId") {
			req.userId = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "creatorId") {
			req.creatorId = std::stoi(argv[i]);
		}
	}
	list_permissionReq->push_back(req);
	return 0;
}

int callback_File(void* data, int argc, char** argv, char** azColName)
{
	std::list<FileDetail>* list_files = (std::list<FileDetail>*)data;
	FileDetail file;

	for (int i = 0; i < argc; i++) {
		if (std::string(azColName[i]) == "fileName") {
			file.fileName = argv[i];
		}
		else if (std::string(azColName[i]) == "creatorId") {
			file.creatorId = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "fileId") {
			file.fileId = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "ProjectId") {
			file.projectId = std::stoi(argv[i]);
		}
	}
	list_files->push_back(file);
	return 0;
}

int callback_FileDetail(void* data, int argc, char** argv, char** azColName)
{
	std::list<FileDetail>* list_files = (std::list<FileDetail>*)data;
	FileDetail file;

	for (int i = 0; i < argc; i++) {
		if (std::string(azColName[i]) == "FileId") {
			file.fileId = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "Data") {
			file.content = argv[i];
		}
	}
	list_files->push_back(file);
	return 0;
}

int callback_ProfInfo(void* data, int argc, char** argv, char** azColName)
{
	std::list<ProfileInfo>* list_profInfo = (std::list<ProfileInfo>*)data;
	ProfileInfo info;

	for (int i = 0; i < argc; i++) {
		if (std::string(azColName[i]) == "Bio") {
			info.bio = argv[i];
		}
		else if (std::string(azColName[i]) == "Email") {
			info.email = argv[i];
		}
		else if (std::string(azColName[i]) == "Name") {
			info.name = argv[i];
		}
		else if (std::string(azColName[i]) == "userId") {
			info.userId = std::stoi(argv[i]);
		}
	}
	list_profInfo->push_back(info);
	return 0;
}

int callback_Project(void* data, int argc, char** argv, char** azColName)
{
	std::list<Project>* list_project = (std::list<Project>*)data;
	Project project;

	for (int i = 0; i < argc; i++) {
		if (std::string(azColName[i]) == "creatorId") {
			project.userId = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "Id") {
			project.projectId = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "ProjectName") {
			project.name = argv[i];
		}
		else if (std::string(azColName[i]) == "codeLan") {
			project.codeLan = argv[i];
		}

	}
	list_project->push_back(project);
	return 0;
}

int callback_ProjectPermission(void* data, int argc, char** argv, char** azColName)
{
	std::list<ProjectPermission>* list_permissions = (std::list<ProjectPermission>*)data;
	ProjectPermission perm;

	for (int i = 0; i < argc; i++) {
		if (std::string(azColName[i]) == "projectId") {
			perm.projectId = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "userId") {
			perm.userId = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "role") {
			perm.role = argv[i];
		}
	}
	list_permissions->push_back(perm);
	return 0;
}

int callback_ProjectJoinInvite(void* data, int argc, char** argv, char** azColName)
{
	std::list<ProjectJoinInvite>* list_invites = (std::list<ProjectJoinInvite>*)data;
	ProjectJoinInvite invite;

	for (int i = 0; i < argc; i++) {
		if (std::string(azColName[i]) == "projectId") {
			invite.projectId = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "role") {
			invite.role = argv[i];
		}
		else if (std::string(azColName[i]) == "userId") {
			invite.userId = std::stoi(argv[i]);
		}
	}
	list_invites->push_back(invite);
	return 0;
}

int callback_Messages(void* data, int argc, char** argv, char** azColName)
{
	std::list<Message>* list_messages = (std::list<Message>*)data;
	Message msg;

	for (int i = 0; i < argc; i++) {
		if (std::string(azColName[i]) == "Text") {
			msg.data = argv[i];
		}
		else if (std::string(azColName[i]) == "senderId") {
			msg.senderId = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "id") {
			msg.id = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "itemId") {
			msg.itemId = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "mode") {
			msg.mode = std::stoi(argv[i]);
		}
	}
	list_messages->push_back(msg);
	return 0;
}

int callback_Issues(void* data, int argc, char** argv, char** azColName)
{
	std::list<Issues>* list_issues = (std::list<Issues>*)data;
	Issues issue;

	for (int i = 0; i < argc; i++) {
		if (std::string(azColName[i]) == "data") {
			issue.data = argv[i];
		}
		else if (std::string(azColName[i]) == "projectId") {
			issue.projectId = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "id") {
			issue.id = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "status") {
			issue.status = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "date") {
			issue.date = argv[i];
		}
		else if (std::string(azColName[i]) == "usersAssigment") {
			issue.usersAssigment = argv[i];
		}
	}
	list_issues->push_back(issue);
	return 0;
}

int callback_ClientVersion(void* data, int argc, char** argv, char** azColName)
{
	std::list<std::string>* list_invites = (std::list<std::string>*)data;
	std::string invite;

	for (int i = 0; i < argc; i++) {
		if (std::string(azColName[i]) == "Version") {
			invite = std::stoi(argv[i]);
		}
	}
	list_invites->push_back(invite);
	return 0;
}


int callback_Friend(void* data, int argc, char** argv, char** azColName)
{
	std::list<Friends>* list_friends = (std::list<Friends>*)data;
	Friends friends;

	for (int i = 0; i < argc; i++) {
		if (std::string(azColName[i]) == "userId") {
			friends.userId = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "friendsList") {
			friends.fiendsList = argv[i];
		}

	}
	list_friends->push_back(friends);
	return 0;
}

int callback_FriendReq(void* data, int argc, char** argv, char** azColName)
{
	std::list<FriendReq>* list_friends = (std::list<FriendReq>*)data;
	FriendReq friends;

	for (int i = 0; i < argc; i++) {
		if (std::string(azColName[i]) == "userId") {
			friends.userId = std::stoi(argv[i]);
		}
		else if (std::string(azColName[i]) == "friendReqId") {
			friends.friendReqId = std::stoi(argv[i]);
		}

	}
	list_friends->push_back(friends);
	return 0;

}

bool SqliteDataBase::send(sqlite3* db, std::string msg)
{
	const char* sqlStatement = msg.c_str();
	char* errMessage = nullptr;
	int res = sqlite3_exec(db, sqlStatement, nullptr, nullptr, &errMessage);
	if (res != SQLITE_OK)
		return false;

	return true;
}

bool SqliteDataBase::send_users(sqlite3* db, std::string msg, std::list<ClientHandler>* users)
{
	const char* sqlStatement = msg.c_str();
	char* errMessage = nullptr;
	int res = sqlite3_exec(db, sqlStatement, callback_users, users, &errMessage);
	if (res != SQLITE_OK)
		return false;

	return true;
}

bool SqliteDataBase::send_chats(sqlite3* db, std::string msg, std::list<Chat>* chats)
{
	const char* sqlStatement = msg.c_str();
	char* errMessage = nullptr;
	int res = sqlite3_exec(db, sqlStatement, callback_chats, chats, &errMessage);
	if (res != SQLITE_OK)
		return false;

	return true;
}

bool SqliteDataBase::send_Permissions(sqlite3* db, std::string msg, std::list<Permission>* data)
{
	const char* sqlStatement = msg.c_str();
	char* errMessage = nullptr;
	int res = sqlite3_exec(db, sqlStatement, callback_Permissions, data, &errMessage);
	if (res != SQLITE_OK)
		return false;

	return true;
}

bool SqliteDataBase::send_PermissionReq(sqlite3* db, std::string msg, std::list<PermissionReq>* data)
{
	const char* sqlStatement = msg.c_str();
	char* errMessage = nullptr;
	int res = sqlite3_exec(db, sqlStatement, callback_PermissionReq, data, &errMessage);
	if (res != SQLITE_OK)
		return false;

	return true;
}

bool SqliteDataBase::send_file(sqlite3* db, std::string msg, std::list<FileDetail>* data)
{
	const char* sqlStatement = msg.c_str();
	char* errMessage = nullptr;
	int res = sqlite3_exec(db, sqlStatement, callback_File, data, &errMessage);
	if (res != SQLITE_OK)
		return false;

	return true;
}

bool SqliteDataBase::send_fileData(sqlite3* db, std::string msg, std::list<FileDetail>* data)
{
	const char* sqlStatement = msg.c_str();
	char* errMessage = nullptr;
	int res = sqlite3_exec(db, sqlStatement, callback_FileDetail, data, &errMessage);
	if (res != SQLITE_OK)
		return false;

	return true;
}

bool SqliteDataBase::send_profInfo(sqlite3* db, std::string msg, std::list<ProfileInfo>* data)
{
	const char* sqlStatement = msg.c_str();
	char* errMessage = nullptr;
	int res = sqlite3_exec(db, sqlStatement, callback_ProfInfo, data, &errMessage);
	if (res != SQLITE_OK)
		return false;

	return true;
}

bool SqliteDataBase::send_Projects(sqlite3* db, std::string msg, std::list<Project>* data)
{
	const char* sqlStatement = msg.c_str();
	char* errMessage = nullptr;
	int res = sqlite3_exec(db, sqlStatement, callback_Project, data, &errMessage);
	if (res != SQLITE_OK)
		return false;

	return true;
}

bool SqliteDataBase::send_ProjectPermissions(sqlite3* db, std::string msg, std::list<ProjectPermission>* data)
{
	const char* sqlStatement = msg.c_str();
	char* errMessage = nullptr;
	int res = sqlite3_exec(db, sqlStatement, callback_ProjectPermission, data, &errMessage);
	if (res != SQLITE_OK)
		return false;

	return true;
}

bool SqliteDataBase::send_ProjectJoinInvite(sqlite3* db, std::string msg, std::list<ProjectJoinInvite>* data)
{
	const char* sqlStatement = msg.c_str();
	char* errMessage = nullptr;
	int res = sqlite3_exec(db, sqlStatement, callback_ProjectJoinInvite, data, &errMessage);
	if (res != SQLITE_OK)
		return false;

	return true;
}

bool SqliteDataBase::send_ClientVersion(sqlite3* db, std::string msg, std::list<std::string>* data)
{
	const char* sqlStatement = msg.c_str();
	char* errMessage = nullptr;
	int res = sqlite3_exec(db, sqlStatement, callback_ProjectJoinInvite, data, &errMessage);
	if (res != SQLITE_OK)
		return false;

	return true;
}

bool SqliteDataBase::send_Messages(sqlite3* db, std::string msg, std::list<Message>* data)
{
	const char* sqlStatement = msg.c_str();
	char* errMessage = nullptr;
	int res = sqlite3_exec(db, sqlStatement, callback_Messages, data, &errMessage);
	if (res != SQLITE_OK)
		return false;

	return true;
}

bool SqliteDataBase::send_Issues(sqlite3* db, std::string msg, std::list<Issues>* data)
{
	const char* sqlStatement = msg.c_str();
	char* errMessage = nullptr;
	int res = sqlite3_exec(db, sqlStatement, callback_Issues, data, &errMessage);
	if (res != SQLITE_OK)
		return false;

	return true;
}

bool SqliteDataBase::send_Friends(sqlite3* db, std::string msg, std::list<Friends>* data)
{
	const char* sqlStatement = msg.c_str();
	char* errMessage = nullptr;
	int res = sqlite3_exec(db, sqlStatement, callback_Friend, data, &errMessage);
	if (res != SQLITE_OK)
		return false;

	return true;
}

bool SqliteDataBase::send_FriendReq(sqlite3* db, std::string msg, std::list<FriendReq>* data)
{
	const char* sqlStatement = msg.c_str();
	char* errMessage = nullptr;
	int res = sqlite3_exec(db, sqlStatement, callback_FriendReq, data, &errMessage);
	if (res != SQLITE_OK)
		return false;

	return true;
}

bool SqliteDataBase::send_data(sqlite3* db, std::string msg, std::list<std::string>* data)
{
	const char* sqlStatement = msg.c_str();
	char* errMessage = nullptr;
	int res = sqlite3_exec(db, sqlStatement, callback_data, data, &errMessage);
	if (res != SQLITE_OK)
		return false;

	return true;
}


bool SqliteDataBase::open()
{
	std::string dbFileName = "syncDBTemp.sqlite";
	int file_exist = _access(dbFileName.c_str(), 0);
	int res = sqlite3_open(dbFileName.c_str(), &_db);

	if (res != SQLITE_OK) {
		_db = nullptr;
		std::cout << "Failed to open DB" << std::endl;
		return -1;
	}
	if (file_exist != 0) {
		std::string msg;

		msg = "CREATE TABLE 'users' ("
			" id INTEGER PRIMARY KEY AUTOINCREMENT,"
			" user_name TEXT UNIQUE NOT NULL,"
			" password TEXT NOT NULL,"
			" mail TEXT NOT NULL);";
		send(_db, msg);
		msg = "CREATE TABLE 'chats' ("
			" id INTEGER PRIMARY KEY AUTOINCREMENT,"
			" fileName TEXT UNIQUE NOT NULL,"
			" projectId INTEGER UNIQUE NOT NULL,"
			" data TEXT NOT NULL,"
			"FOREIGN KEY(projectId) REFERENCES UserProjects(Id),"
			");";
		send(_db, msg);
		msg = "CREATE TABLE Files ("
			"fileId INTEGER PRIMARY KEY AUTOINCREMENT,"
			"creatorId INTEGER,"
			"projectName TEXT,"
			"FOREIGN KEY(creatorId) REFERENCES users(id)"
			"); ";
		send(_db, msg);
		msg = "CREATE TABLE UserPermissions ("
			"id INTEGER PRIMARY KEY AUTOINCREMENT,"
			"userId INTEGER,"
			"fileId INTEGER,"
			"FOREIGN KEY(userId) REFERENCES Users(id),"
			"FOREIGN KEY(fileId) REFERENCES Files(fileId),"
			"UNIQUE(userId, fileId)"
			"); ";
		send(_db, msg);
		msg = "CREATE TABLE PermissionRequests ("
			"id INTEGER PRIMARY KEY AUTOINCREMENT,"
			"fileId INTEGER,"
			"creatorId INTEGER,"
			"userId INTEGER,"
			"FOREIGN KEY(creatorId) REFERENCES Users(id),"
			"FOREIGN KEY(fileId) REFERENCES Files(fileId),"
			"FOREIGN KEY(userId) REFERENCES Users(id)"
			"); ";
		send(_db, msg);
		msg = "CREATE TABLE ProfileInfo ("
			"Name	TEXT,"
			"Email	TEXT,"
			"Bio	TEXT,"
			"userId	INTEGER,"
			"FOREIGN KEY(userId) REFERENCES users(id)"
			");";
		send(_db, msg);
		msg = "CREATE TABLE UserProjects ("
			"creatorId	INTEGER,"
			"Id	INTEGER,"
			"ProjectName	TEXT,"
			"codeLan	TEXT,"
			"PRIMARY KEY(Id AUTOINCREMENT),"
			"FOREIGN KEY(creatorId) REFERENCES users(id)"
			"); ";
		send(_db, msg);
		msg = "CREATE TABLE ProjectPermission ("
			"id	INTEGER,"
			"userId	INTEGER,"
			"projectId	INTEGER,"
			"role	TEXT,"
			"FOREIGN KEY(userId) REFERENCES users(id),"
			"FOREIGN KEY(projectId) REFERENCES UserProjects(Id),"
			"PRIMARY KEY(id AUTOINCREMENT)"
			"); ";
		send(_db, msg);
		msg = "CREATE TABLE UserFriends ("
			"id	INTEGER,"
			"userId	INTEGER,"
			"friendsList	TEXT,"
			"PRIMARY KEY(id AUTOINCREMENT),"
			"FOREIGN KEY(userId) REFERENCES users(id)"
			");";
		send(_db, msg);
		msg = "CREATE TABLE ProjectJoinInvite ("
			"id	INTEGER, "
			"projectId	INTEGER, "
			"role	TEXT, "
			"userId	INTEGER, "
			"FOREIGN KEY(projectiD) REFERENCES UserProjects(Id), "
			"FOREIGN KEY(userId) REFERENCES users(id),"
			"PRIMARY KEY(id AUTOINCREMENT)"
			"); ";
		send(_db, msg);
		msg = "CREATE TABLE FilesData ("
			"Id	INTEGER,"
			"FileId	INTEGER,"
			"Data	TEXT,"
			"FOREIGN KEY(FileId) REFERENCES Files(fileId),"
			"PRIMARY KEY(Id AUTOINCREMENT)"
			"); ";
		send(_db, msg);
		msg = "CREATE TABLE Messages ("
			"Text	TEXT,"
			"id	INTEGER,"
			"reciverId	INTEGER,"
			"senderId	INTEGER,"
			"mode	INTEGER,"
			"itemId	INTEGER,"
			"FOREIGN KEY(reciverId) REFERENCES users(id),"
			"FOREIGN KEY(senderId) REFERENCES users(id),"
			"PRIMARY KEY(id AUTOINCREMENT)"
			"); ";
		send(_db, msg);
		msg = "CREATE TABLE Issues ("
			"id	INTEGER,"
			"projectId	INTEGER,"
			"data	TEXT,"
			"usersAssigment	TEXT,"
			"date	TEXT,"
			"status	INTEGER,"
			"PRIMARY KEY(id AUTOINCREMENT),"
			"FOREIGN KEY(projectId) REFERENCES UserProjects(Id)"
			"); ";
		send(_db, msg);
		msg = "CREATE TABLE ClientVersion ("
			"Version	TEXT,"
			"PRIMARY KEY(Version)"
			"); ";
		send(_db, msg);
	}
	return true;
}

bool SqliteDataBase::close()
{
	sqlite3_close(_db);
	_db = nullptr;
	return true;
}

std::list<ClientHandler> SqliteDataBase::getAllUsers()
{
	std::string msg = "SELECT * FROM users;";
	std::list<ClientHandler> listOfUsers = {};
	send_users(_db, msg, &listOfUsers);

	return listOfUsers;
}

int SqliteDataBase::getUserId(std::string username)
{
	std::string msg = "SELECT id FROM users WHERE user_name = \'" + username + "\';";
	std::list<std::string> list_data;
	send_data(_db, msg, &list_data);
	int id;

	if (list_data.empty())
	{
		throw std::exception("Failed to find user id");
	}
	for (const auto& per : list_data)
	{
		id = atoi(per.c_str());
	}
	return id;
}

std::string SqliteDataBase::getUserName(std::string username, int id)
{
	std::list<ClientHandler> users_list = getAllUsers();

	if (!users_list.empty())
	{
		for (auto user : users_list)
		{
			if (user.getUsername() == username || user.getEmail() == username || user.getId() == id)
			{
				return user.getUsername();
			}
		}
	}
	return "";
}

std::string SqliteDataBase::getEmail(std::string username)
{
	std::list<ClientHandler> users_list = getAllUsers();

	if (!users_list.empty())
	{
		for (auto user : users_list)
		{
			if (user.getUsername() == username || user.getEmail() == username)
			{
				return user.getEmail();
			}
		}
	}
	return "";
}

bool SqliteDataBase::doesUserExist(std::string username)
{
	std::list<ClientHandler> users_list = getAllUsers();

	if (!users_list.empty())
	{
		for (auto user : users_list)
		{
			if (user.getUsername() == username || user.getEmail() == username)
			{
				return true;
			}
		}
	}
	return false;
}

bool SqliteDataBase::doesPasswordMatch(std::string username, std::string password)
{
	std::list<ClientHandler> users_list = getAllUsers();

	if (!users_list.empty())
	{
		for (auto user : users_list)
		{
			if (user.getUsername() == username && user.getPass() == password || user.getEmail() == username && user.getPass() == password)
			{
				return true;
			}
		}
	}
	return false;
}


bool SqliteDataBase::addNewUser(std::string username, std::string password, std::string email)
{
	std::string msg;

	msg = "INSERT INTO users (user_name, password, mail) "
		"VALUES (\'" + username + "\', \'" + password + "\', \'" + email + "\');";

	return send(_db, msg);
}

void SqliteDataBase::changePassword(std::string username, std::string opdPass, std::string newPass)
{
	std::string msg;
	
	msg = "UPDATE users SET password = \'" + newPass + "\' WHERE user_name = \'" + username + "\'; ";

	send(_db, msg);
}

void SqliteDataBase::UpdateChat(const int projectId, const std::string& data)
{
	DeleteChat(projectId);

	std::string msg = "INSERT INTO chats (projectId, data) VALUES (" + std::to_string(projectId) + ", \"" + data + "\"); ";

	send(_db, msg);
}

void SqliteDataBase::createChat(const int projectId)
{
	std::string msg;

	msg = "INSERT INTO chats (projectId, data) VALUES (" + std::to_string(projectId) + ", ''); ";

	send(_db, msg);
}

void SqliteDataBase::DeleteChat(const int projectId)
{
	std::string msg;

	msg = "DELETE FROM chats WHERE projectId = " + std::to_string(projectId) + ";";

	send(_db, msg);
}

std::string SqliteDataBase::GetChatData(const int projectId)
{
	std::string msg;
	std::list<Chat> chatList; // This list will store the result data

	// Assuming 'projectName' is a unique identifier in the 'chats' table

	msg = "SELECT * FROM chats WHERE projectId = " + std::to_string(projectId) + ";";

	send_chats(_db, msg, &chatList);

	for (const Chat& data : chatList) {
		if (data.projectId == projectId)
		{
			return data.data;
		}
	}
}

void SqliteDataBase::addPermissionRequest(int userId, int fileId, int creatorId) {
	std::string msg = "INSERT INTO PermissionRequests (userId, fileId, creatorId) "
		"VALUES(" + std::to_string(userId) + ", " + std::to_string(fileId) + ", " + std::to_string(creatorId) + "); ";
	send(_db, msg);
}

std::list<PermissionReq> SqliteDataBase::getPermissionRequests(int userId) {
	std::string msg = "SELECT * FROM PermissionRequests WHERE creatorId = \'" + std::to_string(userId) + "\';";

	std::list<PermissionReq> requestList;
	send_PermissionReq(_db, msg, &requestList);

	return requestList;
}

bool SqliteDataBase::doesPermissionRequestExist(int userId, int fileId, int creatorId) {
	std::string msg = "SELECT * FROM PermissionRequests WHERE userId = '" + std::to_string(userId) +
		"' AND fileId = '" + std::to_string(fileId) +
		"' AND creatorId = '" + std::to_string(creatorId) + "';";

	std::list<PermissionReq> requestList;
	send_PermissionReq(_db, msg, &requestList);

	return !requestList.empty();
}

void SqliteDataBase::addUserPermission(int userId, int fileId) {
	std::string msg = "INSERT INTO UserPermissions (userId, fileId) "
		"VALUES (" + std::to_string(userId) + "," + std::to_string(fileId) + ");";
	send(_db, msg);
}

std::list<Permission> SqliteDataBase::getUserPermissions(int userId) {
	std::string msg = "SELECT * FROM UserPermissions WHERE userId = " + std::to_string(userId) + ";";

	std::list<Permission> permissionList;
	send_Permissions(_db, msg, &permissionList);

	return permissionList;
}

bool SqliteDataBase::hasPermission(int userId, int fileId) {
	std::string msg = "SELECT * from UserPermissions WHERE userId = " + std::to_string(userId) +
		" AND fileId = " + std::to_string(fileId) + ";";

	std::list<Permission> permissionList;
	send_Permissions(_db, msg, &permissionList);
	if (!permissionList.empty())
	{
		return true;
	}
	return false;
}

void SqliteDataBase::addFile(int userId, const std::string& fileName, int projectId) {
	std::string msg = "INSERT INTO Files (creatorId, fileName, ProjectId) "
		"VALUES (" + std::to_string(userId) + ", \'" + fileName + "\'," + std::to_string(projectId) + ");";
	send(_db, msg);
	msg = "INSERT INTO FilesData (FileId, Data) VALUES (" + std::to_string(getFileDetails(fileName, projectId).fileId) + ", \'\');";
	send(_db, msg);
}

void SqliteDataBase::deleteFile(const std::string& fileName, const int projectId) {
	std::string msg = "DELETE FROM FilesData WHERE FileId = " + std::to_string(getFileDetails(fileName, projectId).fileId) + ";";
	send(_db, msg);

	msg = "DELETE FROM Files WHERE fileName = \'" + fileName + "\' AND ProjectId = " + std::to_string(projectId) + ";";
	send(_db, msg);
}

void SqliteDataBase::renameFile(int projectId, std::string newFileName, std::string oldFileName) {
	std::string msg = "UPDATE Files SET fileName = \'" + newFileName + "\'" +
		"WHERE projectId = " + std::to_string(projectId) + " AND fileName = \'" + oldFileName + "; ";
	send(_db, msg);
}

void SqliteDataBase::updateFile(int fileId, std::string content) {
	std::string msg = "UPDATE FilesData SET Data = \'" + content + "\'" +
		" WHERE FileId = " + std::to_string(fileId) + "; ";
	send(_db, msg);
}

void SqliteDataBase::deleteAllProjectFiles(const int projectId) {
	std::string msg = "DELETE * FROM Files WHERE ProjectId = \'" + std::to_string(projectId) + "\';";
	send(_db, msg);
}

void SqliteDataBase::deletePermissionRequests(int userId, int fileId) {
	std::string msg = "DELETE FROM PermissionRequests WHERE fileId = " + std::to_string(fileId) + " AND userId = " + std::to_string(userId) + ";";
	send(_db, msg);
}

void SqliteDataBase::deleteAllPermissionReq(int fileId) {
	std::string msg = "DELETE FROM PermissionRequests WHERE fileId = " + std::to_string(fileId) + ";";
	send(_db, msg);
}

void SqliteDataBase::deletePermission(int fileId) {
	std::string msg = "DELETE FROM UserPermissions WHERE fileId = " + std::to_string(fileId) + ";";
	send(_db, msg);
}

FileDetail SqliteDataBase::getFileDetails(const std::string& fileName, const int projectId) {
	std::string msg = "SELECT * FROM Files WHERE fileName = \'" + fileName + "\' AND ProjectId = " + std::to_string(projectId) + "; ";

	std::list<FileDetail> fileList;
	FileDetail emptyFile;
	emptyFile.fileName = "";
	send_file(_db, msg, &fileList);

	for (const FileDetail& data : fileList) {
		if (data.fileName == fileName)
		{
			return data;
		}
	}
	return emptyFile;
}

std::string SqliteDataBase::getFileContent(const int fileId) {
	std::string msg = "SELECT * FROM FilesData WHERE FileId = " + std::to_string(fileId) + "; ";

	std::list<FileDetail> fileList;
	send_fileData(_db, msg, &fileList);

	for (const FileDetail& data : fileList) {
		if (data.fileId == fileId)
		{
			return data.content;
		}
	}
	return "";
}

std::string SqliteDataBase::getFileName(const int fileId)
{
	std::string msg = "SELECT * FROM Files WHERE fileId = " + std::to_string(fileId) + ";";

	std::list<FileDetail> fileList;
	send_file(_db, msg, &fileList);

	for (const FileDetail& data : fileList) {
		if (data.fileId == fileId)
		{
			return data.fileName;
		}
	}
}

std::map<std::string, int> SqliteDataBase::getUserPermissionDetails(int userId)
{
	std::string msg = "SELECT * from UserPermissions WHERE userId = " + std::to_string(userId) + ";";
	std::list<Permission> permissionList;
	send_Permissions(_db, msg, &permissionList);

	msg = "SELECT * FROM PermissionRequests WHERE userId = '" + std::to_string(userId) + "';";
	std::list<PermissionReq> requestList;
	send_PermissionReq(_db, msg, &requestList);

	std::map<std::string, int> files;

	for (const Permission& per : permissionList)
	{
		files[getFileName(per.fileId)] = 1; //1 - approved
	}
	
	for (const PermissionReq& req : requestList)
	{
		files[getFileName(req.fileId)] = 0; //0 - pernding
	}

	return files;
}

ProfileInfo SqliteDataBase::getUsersInfo(int userId)
{
	std::string msg = "SELECT * from ProfileInfo WHERE userId = " + std::to_string(userId) + ";";
	std::list<ProfileInfo> infoList;
	send_profInfo(_db, msg, &infoList);

	for (auto info : infoList)
	{
		if (info.userId == userId)
		{
			return info;
		}
	}
}

std::list<std::string> SqliteDataBase::searchUsers(std::string searchCommand)
{
	std::string msg = "SELECT * FROM ProfileInfo WHERE name LIKE \'" + searchCommand + "%\';";
	std::list<ProfileInfo> infoList;
	
	if (!searchCommand.empty())
	{
		send_profInfo(_db, msg, &infoList);
	}
	std::list<std::string> result;
	for (auto user : infoList)
	{
		result.push_back(user.name);
	}
	return result;
}

void SqliteDataBase::createProfile(std::string username, std::string email, std::string bio, int userId)
{
	std::string msg;

	msg = "INSERT INTO ProfileInfo (Name, Email, Bio, userId) "
		"VALUES (\'" + username + "\', \'" + email + "\', \'" + bio + "\'," + std::to_string(userId) + ");";

	send(_db, msg);

	msg = "INSERT INTO UserFriends (userId, friendsList) "
		"VALUES (" + std::to_string(userId) + ", \'\');";

	send(_db, msg);

}

std::list<Message> SqliteDataBase::getUserMessages(int userId)
{
	std::string msg;
	std::list<Message> messagesList;

	msg = "SELECT * FROM Messages WHERE reciverId = " + std::to_string(userId) + ";";


	send_Messages(_db, msg, &messagesList);

	return messagesList;
}

void SqliteDataBase::AddMsg(const int senderId, const int reciverId, const std::string& data, const int mode, const int itemId)
{
	std::string msg = "INSERT INTO Messages (reciverId, senderId, Text, mode, itemId) VALUES "
		"(" + std::to_string(reciverId) + "," + std::to_string(senderId) + ", \'" + data + "\'," 
		+ std::to_string(mode) + "," + std::to_string(itemId) + "); ";

	send(_db, msg);
}

void SqliteDataBase::MarkAsRead(const int messageId)
{
	std::string msg = "DELETE FROM Messages WHERE id = " + std::to_string(messageId) + ";";
	send(_db, msg);
}

void SqliteDataBase::DeleteMessage(const int itemId, const int userId)
{
	std::string msg = "DELETE FROM Messages WHERE itemId = " + std::to_string(itemId) + " AND reciverId = " + std::to_string(userId) + ";";
	send(_db, msg);
}


void SqliteDataBase::modifyProfile(std::string username, std::string email, std::string bio, int userId)
{
	std::string msg;

	msg = "UPDATE ProfileInfo SET Name = \'" + username + "\', Email = \'" + email
		+ "\', Bio = \'" + bio + "\' WHERE userId = " + std::to_string(userId) + "; ";

	send(_db, msg);
}

std::list<Project> SqliteDataBase::getAllProjects(int userId)
{
	std::string msg = "SELECT * from UserProjects WHERE creatorId = " + std::to_string(userId) + ";";
	std::list<Project> projectList;
	send_Projects(_db, msg, &projectList);
	
	return projectList;
}

std::list<ProjectJoinInvite> SqliteDataBase::getUserProjectInvite(int userId)
{
	std::string msg = "SELECT * from ProjectJoinInvite WHERE userId = " + std::to_string(userId) + ";";
	std::list<ProjectJoinInvite> projectList;
	send_ProjectJoinInvite(_db, msg, &projectList);

	return projectList;

}

ProjectJoinInvite SqliteDataBase::getAUserProjectInvite(int userId, int projectId)
{
	std::string msg = "SELECT * from ProjectJoinInvite WHERE userId = " + std::to_string(userId) + " AND projectId = " + std::to_string(projectId) + " ;";
	std::list<ProjectJoinInvite> projectList;
	send_ProjectJoinInvite(_db, msg, &projectList);

	for (auto invite : projectList)
	{
		if (invite.projectId == projectId && invite.userId == userId)
		{
			return invite;
		}
	}

}

std::list<ProjectPermission> SqliteDataBase::getUserProjectPermission(int userId)
{
	std::string msg = "SELECT * from ProjectPermission WHERE userId = " + std::to_string(userId) + ";";
	std::list<ProjectPermission> projectList;
	send_ProjectPermissions(_db, msg, &projectList);

	return projectList;

}

int SqliteDataBase::getNextAdmin(int projectId)
{
	std::string msg = "SELECT * from ProjectPermission WHERE role = \'admin\' AND projectId = " + std::to_string(projectId) + ";";
	std::list<ProjectPermission> projectList;
	send_ProjectPermissions(_db, msg, &projectList);

	Project project = getProject("", projectId);

	if (projectList.empty())
	{
		msg = "SELECT * from ProjectPermission WHERE role = \'participant\' AND projectId = " + std::to_string(projectId) + ";";
		std::list<ProjectPermission> projectList;
		send_ProjectPermissions(_db, msg, &projectList);

		if (projectList.empty())
		{
			deleteAllProjectFiles(project.projectId);
			DeleteChat(project.projectId);
			DeleteAllIssue(projectId);
			deleteProject(project.name);
			return -1;
		}
	}

	for (auto user : projectList)
	{
		return user.userId;
	}
}

std::map<std::string, std::string> SqliteDataBase::getProjectParticipants(int projectId)
{
	std::string msg = "SELECT * from ProjectPermission WHERE projectId = " + std::to_string(projectId) + ";";
	std::list<ProjectPermission> projectList;
	send_ProjectPermissions(_db, msg, &projectList);

	std::map<std::string, std::string> result;

	for (auto user : projectList)
	{
		 result[getUserName("", user.userId)] = user.role;
	}
	return result;
}

std::string SqliteDataBase::getUserRoleInProject(int userId, int projectId)
{
	std::string msg = "SELECT * from ProjectPermission WHERE userId = " + std::to_string(userId) + " AND projectId = " + std::to_string(projectId) + "; ";
	std::list<ProjectPermission> projectPermList;
	send_ProjectPermissions(_db, msg, &projectPermList);

	for (auto role : projectPermList)
	{
		return role.role;
	}
	return "";
}

Project SqliteDataBase::getProject(std::string projectName, int projectId)
{
	std::string msg = "SELECT * from UserProjects WHERE ProjectName = \'" + projectName + "\';";
	std::list<Project> projectList;
	send_Projects(_db, msg, &projectList);

	for (auto project : projectList)
	{
		if (project.name == projectName)
		{
			return project;
		}
	}

	msg = "SELECT * from UserProjects WHERE Id = " + std::to_string(projectId) + ";";
	send_Projects(_db, msg, &projectList);

	for (auto project : projectList)
	{
		if (project.projectId == projectId)
		{
			return project;
		}
	}
}

std::list<FileDetail> SqliteDataBase::getProjectFiles(int projectId)
{
	std::string msg = "SELECT * FROM Files WHERE ProjectId = " + std::to_string(projectId) + ";";

	std::list<FileDetail> fileList;

	send_file(_db, msg, &fileList);

	return fileList;
}

void SqliteDataBase::modifyProjectInfo(int projectId, std::string newProjectName, std::map<ProfileInfo, std::string> addedUsers, std::string codeLan)
{
	std::string msg;


	msg = "UPDATE UserProjects SET projectName = \'" + newProjectName + "\', codeLan = \'" + codeLan + "\' WHERE "
		"Id = " + std::to_string(projectId) + "; ";
	send(_db, msg);


	for (auto user : addedUsers)
	{
		if (!hasPermissionToProject(projectId, user.first.userId))
		{
			createProjectJoinInvite(projectId, user.first.userId, user.second);
		}
		else
		{
			changeUserRoleInProject(projectId, user.first.userId, user.second);
		}
	}
}

void SqliteDataBase::createProject(std::string projectName, std::map<ProfileInfo, std::string> addedUsers, std::string codeLan, int creatorId, int projectId)
{
	std::string msg;

	if (projectId == -1)
	{
		msg = "INSERT INTO UserProjects (creatorId, projectName, codeLan) "
			"VALUES (" + std::to_string(creatorId) + ", \'" + projectName + "\', \'" + codeLan + "\');";

		send(_db, msg);

		Project project = getProject(projectName, -1);

		for (auto user : addedUsers)
		{
			createProjectJoinInvite(project.projectId, user.first.userId, user.second);
		}
	}
	else
	{
		msg = "INSERT INTO UserProjects (creatorId, projectName, codeLan, Id) "
			"VALUES (" + std::to_string(creatorId) + ", \'" + projectName + "\', \'" + codeLan + "\', " + std::to_string(projectId) + ");";
		send(_db, msg);
	}
	
}

void SqliteDataBase::deleteProject(const std::string projectName)
{
	std::string msg = "DELETE FROM UserProjects WHERE ProjectName = \'" + projectName + "\';";
	send(_db, msg);
}

void SqliteDataBase::leaveProject(const std::string projectName, int userId)
{
	Project project = getProject(projectName, -1);
	std::string msg = "DELETE FROM ProjectPermission WHERE projectId = " + std::to_string(project.projectId)
		+ " AND userId = " + std::to_string(userId) + ";";
	send(_db, msg);
}

void SqliteDataBase::createProjectPermission(int projectId, int userId, std::string role)
{
	std::string msg;

	msg = "INSERT INTO ProjectPermission (userId, projectId, role) "
		"VALUES (" + std::to_string(userId) + ", " + std::to_string(projectId) + ", \'" + role + "\' );";

	send(_db, msg);
}

void SqliteDataBase::createProjectJoinInvite(int projectId, int userId, std::string role)
{
	std::string msg;

	msg = "INSERT INTO ProjectJoinInvite (userId, projectId, role) "
		"VALUES (" + std::to_string(userId) + ", " + std::to_string(projectId) + ", \'" + role + "\');";

	send(_db, msg);
}

void SqliteDataBase::deleteProjectJoinInvite(int projectId, int userId)
{
	std::string msg;

	msg = "DELETE FROM ProjectJoinInvite WHERE userId = " + std::to_string(userId) + " AND projectId = " + std::to_string(projectId) + ";";

	send(_db, msg);
}

void SqliteDataBase::acceptProjectJoinInvite(int projectId, int userId, std::string role)
{
	std::string msg;

	msg = "DELETE FROM ProjectJoinInvite WHERE userId = " + std::to_string(userId) + " AND projectId = " + std::to_string(projectId) + ";";

	send(_db, msg);

	createProjectPermission(projectId, userId, role);
}

void SqliteDataBase::deleteAllProjectPermission(int projectId)
{
	std::string msg = "DELETE FROM ProjectPermission WHERE projectId = " + std::to_string(projectId) + ";";
	send(_db, msg);
}

void SqliteDataBase::deleteProjectPermission(int projectId, int userId)
{
	std::string msg = "DELETE FROM ProjectPermission WHERE projectId = " + std::to_string(projectId) +
		" AND userId" + std::to_string(userId) + ";";
	send(_db, msg);
}

void SqliteDataBase::changeUserRoleInProject(int projectId, int userId, std::string role) 
{
	std::string msg = "UPDATE FROM ProjectPermission SET role = \'" + role +  "\' WHERE projectId = " + std::to_string(projectId) +
		" AND userId" + std::to_string(userId) + ";";
	send(_db, msg);
}

bool SqliteDataBase::hasPermissionToProject(int projectId, int userId)
{
	std::string msg = "SELECT * from ProjectPermission WHERE userId = " + std::to_string(userId) +
		" AND projectId = " + std::to_string(projectId) + ";";

	std::list<ProjectPermission> permissionList;
	send_ProjectPermissions(_db, msg, &permissionList);
	if (!permissionList.empty())
	{
		return true;
	}
	return false;
}

bool SqliteDataBase::isCreator(std::string projectName, int userId)
{
	std::string msg = "SELECT * from UserProjects WHERE creatorId = " + std::to_string(userId) +
		" AND ProjectName = \'" + projectName + "\';";

	std::list<Project> projectList;
	send_Projects(_db, msg, &projectList);
	if (!projectList.empty())
	{
		return true;
	}
	return false;
}

Friends SqliteDataBase::getUserFriends(int userId)
{
	std::string msg = "SELECT * from UserFriends WHERE userId = " + std::to_string(userId) + ";";
	std::list<Friends> friendsList;
	send_Friends(_db, msg, &friendsList);

	for (auto firends : friendsList)
	{
		if (firends.userId == userId)
		{
			return firends;
		}
	}
	Friends empty;
	empty.fiendsList = "";
	return empty;
}

std::list<FriendReq> SqliteDataBase::getUserFriendReq(int userId)
{
	std::string msg = "SELECT * from FriendReq WHERE friendReqId = " + std::to_string(userId) + ";";
	std::list<FriendReq> friendsList;
	send_FriendReq(_db, msg, &friendsList);

	return friendsList;
}

std::list<FriendReq> SqliteDataBase::getCurrentUserReqSent(int userId)
{
	std::string msg = "SELECT * from FriendReq WHERE userId = " + std::to_string(userId) + ";";
	std::list<FriendReq> friendsList;
	send_FriendReq(_db, msg, &friendsList);

	return friendsList;
}

void SqliteDataBase::addFriend(int userId, std::string friendsList)
{
	std::string msg;

	msg = "UPDATE UserFriends SET friendsList = \'" + friendsList + "\' WHERE userId = " + std::to_string(userId) + ";";

	send(_db, msg);
}

void SqliteDataBase::addFriendReq(int userId, int friendRequsetId)
{
	std::string msg;

	msg = "INSERT INTO FriendReq (friendReqId, userId) VALUES (" + 
		std::to_string(friendRequsetId) + ", " + std::to_string(userId) + ");";

	send(_db, msg);
}

void SqliteDataBase::removeFriend(int userId, std::string friendsList)
{
	std::string msg;

	msg = "UPDATE UserFriends SET friendsList = \'" + friendsList + "\' WHERE userId = " + std::to_string(userId) + ";";

	send(_db, msg);
}

void SqliteDataBase::approveFriendReq(int userId, int friendRequsetId)
{
	std::string msg = "DELETE FROM FriendReq WHERE friendReqId = " + std::to_string(friendRequsetId) +
		" AND userId = " + std::to_string(userId) + ";";
	send(_db, msg);

	Friends userFriend1 = getUserFriends(friendRequsetId);
	Friends userFriend2 = getUserFriends(userId);

	std::string name1 = getUserName("", friendRequsetId);
	std::string name2 = getUserName("", userId);

	std::string lengthString = std::to_string(name2.length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	userFriend1.fiendsList += lengthString + name2;

	addFriend(friendRequsetId, userFriend1.fiendsList);

	lengthString = std::to_string(name1.length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	userFriend2.fiendsList += lengthString + name1;

	addFriend(userId, userFriend2.fiendsList);
}

void SqliteDataBase::rejectFriendReq(int userId, int friendRequsetId)
{
	std::string msg = "DELETE FROM FriendReq WHERE friendReqId = " + std::to_string(friendRequsetId) +
		" AND userId = " + std::to_string(userId) + ";";
	send(_db, msg);
}

std::string SqliteDataBase::getLatestVersion()
{
	std::string msg = "SELECT * from ClientVersion;";
	std::list<std::string> versionList;
	send_ClientVersion(_db, msg, &versionList);
	
	for (auto item : versionList)
	{
		if (!item.empty())
		{
			return item;
		}
	}
}

std::list<Issues> SqliteDataBase::getCurrentProjectIssues(int projectId)
{
	std::string msg;
	std::list<Issues> issuesList;

	msg = "SELECT * FROM Issues WHERE projectId = " + std::to_string(projectId) + " AND status = 0;";

	send_Issues(_db, msg, &issuesList);

	return issuesList;
}

std::list<Issues> SqliteDataBase::getCompletedProjectIssues(int projectId)
{
	std::string msg;
	std::list<Issues> issuesList;

	msg = "SELECT * FROM Issues WHERE projectId = " + std::to_string(projectId) + " AND status = 1;";


	send_Issues(_db, msg, &issuesList);

	return issuesList;
}

std::list<Issues> SqliteDataBase::getAllProjectIssues(int projectId)
{
	std::string msg;
	std::list<Issues> issuesList;

	msg = "SELECT * FROM Issues WHERE projectId = " + std::to_string(projectId) + ";";


	send_Issues(_db, msg, &issuesList);

	return issuesList;
}

Issues SqliteDataBase::getIssue(int issueId)
{
	std::string msg;
	std::list<Issues> issuesList;

	msg = "SELECT * FROM Issues WHERE id = " + std::to_string(issueId) + ";";


	send_Issues(_db, msg, &issuesList);

	for (auto issue : issuesList)
	{
		if (issue.id == issueId)
		{
			return issue;
		}
	}
}

bool SqliteDataBase::inIssue(int issueId, std::string name)
{
	std::string msg;
	std::list<Issues> issuesList;

	msg = "SELECT * FROM Issues WHERE id = " + std::to_string(issueId) + ";";


	send_Issues(_db, msg, &issuesList);

	for (auto issue : issuesList)
	{
		if (issue.id == issueId)
		{
			int index = 0;
			while (index < issue.usersAssigment.length())
			{
				int length = std::stoi(issue.usersAssigment.substr(index, 5));
				index += 5;
				std::string extractedName = issue.usersAssigment.substr(index, length);
				index += length;
				if (extractedName == name)
				{
					return true;
				}
			}
			return false;
		}
	}
}

void SqliteDataBase::AddIssue(const int projectId, const std::string& data, const std::string& date, const std::string& usersAssigment)
{
	std::string msg = "INSERT INTO Issues (projectId, data, usersAssigment, date, status) VALUES "
		"(" + std::to_string(projectId) + ", \'" + data + "\', \'" + usersAssigment + "\', \'" + date + "\', 0); ";

	send(_db, msg);
}

void SqliteDataBase::UpdateIssue(const int issueId, const std::string& data, const std::string& date, const std::string& usersAssigment)
{
	std::string msg = "UPDATE Issues SET data = \'" + data + "\', usersAssigment = \'" + usersAssigment +
		"\', date = \'" + date + "\' WHERE "
		"id = " + std::to_string(issueId) + "; ";
	send(_db, msg);
}

void SqliteDataBase::MarkAsComplete(const int issueId)
{
	std::string msg = "UPDATE Issues SET status = 1 WHERE id = " + std::to_string(issueId) + "; ";
	send(_db, msg);
}

void SqliteDataBase::MarkAsNotComplete(const int issueId)
{
	std::string msg = "UPDATE Issues SET status = 0 WHERE id = " + std::to_string(issueId) + "; ";
	send(_db, msg);
}

void SqliteDataBase::DeleteIssue(const int issueId)
{
	std::string msg = "DELETE FROM Issues WHERE id = " + std::to_string(issueId) + ";";
	send(_db, msg);
}

void SqliteDataBase::DeleteAllIssue(const int projectId)
{
	std::string msg = "DELETE FROM Issues WHERE projectId = " + std::to_string(projectId) + ";";
	send(_db, msg);
}

void SqliteDataBase::DeleteProjectIssues(const int ptojectId)
{
	std::string msg = "DELETE FROM Issues WHERE projectId = " + std::to_string(ptojectId) + ";";
	send(_db, msg);
}