#pragma once
#include "Config.h"
#include "Db.h"
#include "Json.h"
#include "Net.h"
#include "TextOperation.h"
#include <atomic>
#include <condition_variable>
#include <deque>
#include <map>
#include <memory>
#include <mutex>
#include <optional>
#include <set>
#include <shared_mutex>
#include <string>
#include <unordered_map>
#include <vector>

class CodeRunner;

// An error reported to the client: {"ok": false, "code": code, "error": message}.
struct ApiError : std::runtime_error
{
	std::string code;
	long long retryAfter = 0; // seconds, for rate_limited
	ApiError(std::string code, const std::string& message) : std::runtime_error(message), code(std::move(code)) {}
};

// Request-rate limit: `rate` tokens per second, up to `burst` saved up.
struct TokenBucket
{
	double tokens = 0;
	long long last = 0;
	bool take(double rate, double burst, double cost = 1);
};

// Counts failures per key (user name, IP) and locks the key when there are too many.
class FailureLimiter
{
public:
	// Seconds until key may try again, 0 if it may now.
	long long lockedFor(const std::string& key, int maxFailures, long long windowMs);
	void fail(const std::string& key);
	void reset(const std::string& key);

private:
	std::mutex _mutex;
	std::unordered_map<std::string, std::deque<long long>> _failures;
	void prune(std::deque<long long>& times, long long windowMs);
};

struct Session
{
	uint64_t id = 0;
	ConnectionPtr conn;
	std::string ip;

	// Set once at login (by this session's own requests), read by others under `m`.
	std::mutex m;
	long long userId = 0;
	std::string name;
	bool isAdmin = false;
	std::string status = "online";          // online / away
	long long activeProject = 0;
	long long activeFile = 0;
	std::map<long long, std::string> openProjects; // projectId -> role (owner/admin/member/viewer)
	std::map<long long, long long> openFiles;       // fileId -> projectId

	// Only touched by this session's own requests (they run one at a time).
	TokenBucket bucket;
	std::deque<long long> chatTimes;
	long long lastTyping = 0;
	int limitedInARow = 0;

	void send(const Json& message) { conn->send(message.dump()); }
	long long uid() { std::lock_guard<std::mutex> lock(m); return userId; }
};
using SessionPtr = std::shared_ptr<Session>;

struct Cursor
{
	long long userId = 0;
	size_t anchor = 0;
	size_t head = 0;
};

// The live copy of a file while anyone has it open.
struct LiveDoc
{
	long long fileId = 0;
	std::wstring text;
	int rev = 0;
	int historyStart = 0;                 // revision before history[0]
	std::vector<TextOperation> history;
	std::map<uint64_t, SessionPtr> viewers;
	std::map<uint64_t, Cursor> cursors;
	bool dirty = false;                   // not yet written to the database
	bool changedSinceVersion = false;
	long long lastVersionAt = 0;
	long long lastEditor = 0;
};

// Everything about one project that is shared by the people who have it open.
// Each room has its own lock, so separate projects never wait for each other.
struct Room
{
	long long projectId = 0;
	std::mutex m;
	std::map<uint64_t, SessionPtr> sessions;
	std::map<long long, LiveDoc> docs;   // fileId -> live copy
};
using RoomPtr = std::shared_ptr<Room>;

struct ProjectRow
{
	long long id = 0;
	std::string name, language, description, visibility, inviteCode;
	long long ownerId = 0;
	bool inviteAutoApprove = false;
	long long createdAt = 0, updatedAt = 0;
};

struct FileRow
{
	long long id = 0;
	long long projectId = 0;
	std::string path;
};

class App : public NetHandler
{
public:
	App(Config& config, Db& db);
	~App();

	void attach(NetServer* net) { _net = net; }
	void autosaveLoop();             // runs until stop()
	void stop();
	void saveAll();                  // writes every edited file now
	std::string consoleCommand(const std::string& line); // admin console; returns the output

	// NetHandler
	void onOpen(const ConnectionPtr& conn) override;
	void onMessage(const ConnectionPtr& conn, const std::string& payload) override;
	void onLegacyMessage(const ConnectionPtr& conn, const std::string& payload) override;
	void onClose(const ConnectionPtr& conn) override;

	// Used by the code runner.
	void sendToSession(uint64_t sessionId, const Json& message);

private:
	using Handler = Json (App::*)(Session&, const Json&);
	// SelfReply: the handler sends its own success reply (with sendReply) while it still
	// holds a project lock, so the reply can't be overtaken by events sent after it.
	enum Flags { PreLogin = 1, NoReply = 2, AdminOnly = 4, SelfReply = 8 };
	struct Route { Handler handler; int flags; double cost; };
	void registerRoutes();
	Json dispatch(Session& s, const std::string& type, const Json& req, const Route& route);
	void sendReply(Session& s, const Json& req, const Json& fields);

	// ---- sessions & sending ----
	SessionPtr sessionFor(const ConnectionPtr& conn);
	std::vector<SessionPtr> sessionsOf(long long userId);
	std::vector<SessionPtr> allSessions();
	bool isOnline(long long userId);
	void sendToUser(long long userId, const Json& message);
	void sendToUsers(const std::set<long long>& userIds, const Json& message);
	void endSession(Session& s);
	void kickUser(long long userId, const std::string& reason);

	// ---- rooms ----
	RoomPtr room(long long projectId, bool create);
	void broadcast(Room& room, const Json& message, uint64_t exceptSession = 0); // room.m must be held
	void broadcastToRoom(long long projectId, const Json& message, uint64_t exceptSession = 0);
	void closeProjectFor(Session& s, long long projectId);
	void closeFileLocked(Room& room, Session& s, long long fileId);
	LiveDoc& loadDoc(Room& room, long long fileId);  // room.m must be held
	void applyServerEdit(Room& room, LiveDoc& doc, const TextOperation& op, long long userId); // room.m held
	void saveRoom(const RoomPtr& room, bool unloadIdle);
	void writeVersion(long long fileId, const std::string& text, const std::string& label, long long userId);
	Json fileUsersJson(LiveDoc& doc);

	// ---- data helpers (AppData.cpp) ----
	Json userJson(long long userId);
	Json userJson(long long id, const std::string& name, const std::string& avatar);
	std::optional<long long> findUserId(const std::string& nameOrEmail);
	std::string userName(long long userId);
	Json presenceJson(long long userId, long long viewerId);
	void notifyPresence(long long userId);
	std::set<long long> friendIds(long long userId);
	bool areFriends(long long a, long long b);
	std::set<long long> relatedUsers(long long userId); // friends and people sharing a project
	std::optional<ProjectRow> project(long long projectId);
	std::string roleOf(long long projectId, long long userId); // "" when not a member
	std::set<long long> memberIds(long long projectId);
	ProjectRow requireProject(long long projectId);
	std::string requireMember(Session& s, long long projectId);           // returns role
	std::string requireProjectAdmin(Session& s, long long projectId);     // owner or admin
	std::string accessRole(Session& s, const ProjectRow& p);              // member role, "viewer" for public, else throws
	std::string requireWriter(Session& s, long long projectId);           // member who has the project open
	FileRow requireFile(long long fileId);
	Json projectSummaryJson(const ProjectRow& p, long long viewerId);
	Json projectDetailJson(const ProjectRow& p, long long viewerId);
	Json treeJson(long long projectId, Json& foldersOut);
	Json taskJson(long long taskId);
	Json chatMessageJson(long long messageId);
	void addInbox(long long userId, const std::string& kind, const std::string& text, long long fromId, long long projectId);
	void removeInbox(long long userId, const std::string& kind, std::optional<long long> fromId, std::optional<long long> projectId);
	long long unreadCount(long long userId);
	void addActivity(long long projectId, long long userId, const std::string& kind, const std::string& text);
	void touchProject(long long projectId);
	void notifyProjectChanged(long long projectId);  // ev.project to the room, ev.projects to members
	std::string clientVersion();
	bool isAdminName(const std::string& name);

	// ---- handlers ----
	// connection & account (AppAuth.cpp)
	Json hHello(Session&, const Json&);
	Json hPing(Session&, const Json&);
	Json hLogin(Session&, const Json&);
	Json hSignup(Session&, const Json&);
	Json hChangePassword(Session&, const Json&);
	Json hLogout(Session&, const Json&);
	Json startSession(Session& s, long long userId);
	// people (AppPeople.cpp)
	Json hProfile(Session&, const Json&);
	Json hUpdateProfile(Session&, const Json&);
	Json hSetAvatar(Session&, const Json&);
	Json hAvatar(Session&, const Json&);
	Json hSearchUsers(Session&, const Json&);
	Json hSetPresence(Session&, const Json&);
	Json hFriends(Session&, const Json&);
	Json hFriendRequest(Session&, const Json&);
	Json hFriendRespond(Session&, const Json&);
	Json hFriendRemove(Session&, const Json&);
	Json hInbox(Session&, const Json&);
	Json hInboxMarkRead(Session&, const Json&);
	Json hInboxDelete(Session&, const Json&);
	Json hActivity(Session&, const Json&);
	// projects (AppProjects.cpp)
	Json hProjectList(Session&, const Json&);
	Json hProjectGet(Session&, const Json&);
	Json hProjectCreate(Session&, const Json&);
	Json hProjectUpdate(Session&, const Json&);
	Json hProjectDelete(Session&, const Json&);
	Json hProjectLeave(Session&, const Json&);
	Json hProjectRespondInvite(Session&, const Json&);
	Json hProjectInviteCode(Session&, const Json&);
	Json hProjectJoin(Session&, const Json&);
	Json hProjectRespondJoin(Session&, const Json&);
	Json hProjectPublic(Session&, const Json&);
	Json hProjectOpen(Session&, const Json&);
	Json hProjectClose(Session&, const Json&);
	Json hProjectExport(Session&, const Json&);
	Json hProjectImport(Session&, const Json&);
	void checkInvites(Session& s, const Json& invites);
	void inviteUsers(Session& s, const ProjectRow& p, const Json& invites);
	void deleteProjectNow(const ProjectRow& p, const std::string& reason);
	void removeMember(const ProjectRow& p, long long userId, const std::string& reason);
	// files (AppFiles.cpp)
	Json hFileCreate(Session&, const Json&);
	Json hFileRename(Session&, const Json&);
	Json hFileDelete(Session&, const Json&);
	Json hFolderCreate(Session&, const Json&);
	Json hFolderRename(Session&, const Json&);
	Json hFolderDelete(Session&, const Json&);
	Json hFileOpen(Session&, const Json&);
	Json hFileClose(Session&, const Json&);
	Json hFileOp(Session&, const Json&);
	Json hFileCursor(Session&, const Json&);
	Json hFileVersions(Session&, const Json&);
	Json hFileVersion(Session&, const Json&);
	Json hFileCheckpoint(Session&, const Json&);
	Json hFileRestore(Session&, const Json&);
	void broadcastTree(long long projectId);
	void ensureParentFolders(long long projectId, const std::string& path);
	void dropDocs(long long projectId, const std::set<long long>& fileIds);
	// chat (AppChat.cpp)
	Json hChatHistory(Session&, const Json&);
	Json hChatSend(Session&, const Json&);
	Json hChatEdit(Session&, const Json&);
	Json hChatDelete(Session&, const Json&);
	Json hChatTyping(Session&, const Json&);
	// tasks (AppTasks.cpp)
	Json hTaskList(Session&, const Json&);
	Json hTaskCreate(Session&, const Json&);
	Json hTaskUpdate(Session&, const Json&);
	Json hTaskSetDone(Session&, const Json&);
	Json hTaskDelete(Session&, const Json&);
	std::set<long long> parseAssignees(long long projectId, const Json& assignees);
	// running code (AppRun.cpp)
	Json hRunStart(Session&, const Json&);
	Json hRunStop(Session&, const Json&);
	// administration (AppAdmin.cpp)
	Json hAdminStats(Session&, const Json&);
	Json hAdminUsers(Session&, const Json&);
	Json hAdminProjects(Session&, const Json&);
	Json hAdminBan(Session&, const Json&);
	Json hAdminKick(Session&, const Json&);
	Json hAdminSetAdmin(Session&, const Json&);
	Json hAdminDeleteProject(Session&, const Json&);
	Json statsJson();
	void setBanned(long long userId, bool banned, const std::string& reason);

	Config& _cfg;
	Db& _db;
	NetServer* _net = nullptr;
	std::unique_ptr<CodeRunner> _runner;
	std::unordered_map<std::string, Route> _routes;

	std::shared_mutex _sessionsMutex;
	std::map<uint64_t, SessionPtr> _sessions; // by connection id

	std::mutex _roomsMutex;
	std::map<long long, RoomPtr> _rooms;

	FailureLimiter _loginLimiter;
	FailureLimiter _signupLimiter;

	std::mutex _stopMutex;
	std::condition_variable _stopCv;
	bool _stopping = false;

	long long _startedAt;
	std::atomic<long long> _requests{ 0 };
	std::mutex _rateMutex;
	std::deque<long long> _recentRequests; // timestamps in the last minute (sampled)
};

// ---- request field helpers ----
const std::string& reqString(const Json& req, const char* key, size_t maxBytes = 1000, bool allowEmpty = false);
std::string optString(const Json& req, const char* key, size_t maxBytes = 1000);
long long reqId(const Json& req, const char* key);
bool reqBool(const Json& req, const char* key);
bool optBool(const Json& req, const char* key, bool fallback);
[[noreturn]] void fail(const std::string& code, const std::string& message);
// Cleans up a file or folder path ("src/main.cpp"); throws for invalid names.
std::string normalizePath(const std::string& raw);
