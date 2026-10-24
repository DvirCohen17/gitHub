#include "App.h"
#include "CodeRunner.h"
#include "Log.h"
#include "Util.h"
#include <algorithm>
#include <fstream>
#include <sstream>

// ---- request helpers ----------------------------------------------------------

void fail(const std::string& code, const std::string& message)
{
	throw ApiError(code, message);
}

const std::string& reqString(const Json& req, const char* key, size_t maxBytes, bool allowEmpty)
{
	const Json& v = req[key];
	if (!v.isString()) fail("bad_request", std::string("Missing text field '") + key + "'.");
	if (!allowEmpty && Util::trim(v.asString()).empty()) fail("bad_request", std::string("'") + key + "' can't be empty.");
	if (v.asString().size() > maxBytes) fail("bad_request", std::string("'") + key + "' is too long.");
	return v.asString();
}

std::string optString(const Json& req, const char* key, size_t maxBytes)
{
	if (req[key].isNull()) return "";
	return reqString(req, key, maxBytes, true);
}

long long reqId(const Json& req, const char* key)
{
	const Json& v = req[key];
	if (!v.isInt() || v.asInt() <= 0) fail("bad_request", std::string("Missing id '") + key + "'.");
	return v.asInt();
}

bool reqBool(const Json& req, const char* key)
{
	if (!req[key].isBool()) fail("bad_request", std::string("Missing true/false field '") + key + "'.");
	return req[key].asBool();
}

bool optBool(const Json& req, const char* key, bool fallback)
{
	return req[key].isBool() ? req[key].asBool() : fallback;
}

// ---- rate limiting --------------------------------------------------------------

bool TokenBucket::take(double rate, double burst, double cost)
{
	long long now = nowMs();
	if (last == 0)
	{
		tokens = burst;
	}
	else
	{
		tokens = std::min(burst, tokens + (now - last) * rate / 1000.0);
	}
	last = now;
	if (tokens < cost) return false;
	tokens -= cost;
	return true;
}

void FailureLimiter::prune(std::deque<long long>& times, long long windowMs)
{
	long long cutoff = nowMs() - windowMs;
	while (!times.empty() && times.front() < cutoff) times.pop_front();
}

long long FailureLimiter::lockedFor(const std::string& key, int maxFailures, long long windowMs)
{
	std::lock_guard<std::mutex> lock(_mutex);
	auto it = _failures.find(key);
	if (it == _failures.end()) return 0;
	prune(it->second, windowMs);
	if ((int)it->second.size() < maxFailures)
	{
		if (it->second.empty()) _failures.erase(it);
		return 0;
	}
	// Locked until the oldest counted failure leaves the window.
	return (it->second.front() + windowMs - nowMs()) / 1000 + 1;
}

void FailureLimiter::fail(const std::string& key)
{
	std::lock_guard<std::mutex> lock(_mutex);
	auto& times = _failures[key];
	times.push_back(nowMs());
	if (times.size() > 100) times.pop_front();
}

void FailureLimiter::reset(const std::string& key)
{
	std::lock_guard<std::mutex> lock(_mutex);
	_failures.erase(key);
}

// ---- App ----------------------------------------------------------------------------

App::App(Config& config, Db& db) : _cfg(config), _db(db), _startedAt(nowMs())
{
	_runner = std::make_unique<CodeRunner>(config, [this](uint64_t sessionId, const Json& message) {
		sendToSession(sessionId, message);
	});
	registerRoutes();
}

App::~App() = default;

void App::registerRoutes()
{
	auto add = [this](const char* type, Handler h, int flags = 0, double cost = 1) {
		_routes[type] = Route{ h, flags, cost };
	};
	add("hello", &App::hHello, PreLogin);
	add("ping", &App::hPing, PreLogin, 0.1);
	add("auth.login", &App::hLogin, PreLogin, 5);
	add("auth.signup", &App::hSignup, PreLogin, 5);
	add("auth.changePassword", &App::hChangePassword, PreLogin, 5);
	add("auth.logout", &App::hLogout);

	add("user.profile", &App::hProfile);
	add("user.updateProfile", &App::hUpdateProfile);
	add("user.setAvatar", &App::hSetAvatar, 0, 10);
	add("user.avatar", &App::hAvatar);
	add("user.search", &App::hSearchUsers);
	add("presence.set", &App::hSetPresence);
	add("friends.list", &App::hFriends);
	add("friends.request", &App::hFriendRequest, 0, 3);
	add("friends.respond", &App::hFriendRespond);
	add("friends.remove", &App::hFriendRemove);
	add("inbox.list", &App::hInbox);
	add("inbox.markRead", &App::hInboxMarkRead);
	add("inbox.delete", &App::hInboxDelete);
	add("activity.feed", &App::hActivity);

	add("project.list", &App::hProjectList);
	add("project.get", &App::hProjectGet);
	add("project.create", &App::hProjectCreate, 0, 5);
	add("project.update", &App::hProjectUpdate, 0, 3);
	add("project.delete", &App::hProjectDelete, 0, 3);
	add("project.leave", &App::hProjectLeave);
	add("project.respondInvite", &App::hProjectRespondInvite);
	add("project.inviteCode", &App::hProjectInviteCode);
	add("project.join", &App::hProjectJoin, 0, 5);
	add("project.respondJoin", &App::hProjectRespondJoin);
	add("project.public", &App::hProjectPublic);
	add("project.open", &App::hProjectOpen);
	add("project.close", &App::hProjectClose);
	add("project.export", &App::hProjectExport, 0, 5);
	add("project.import", &App::hProjectImport, 0, 20);

	add("file.create", &App::hFileCreate);
	add("file.rename", &App::hFileRename);
	add("file.delete", &App::hFileDelete);
	add("folder.create", &App::hFolderCreate);
	add("folder.rename", &App::hFolderRename);
	add("folder.delete", &App::hFolderDelete);
	add("file.open", &App::hFileOpen, SelfReply);
	add("file.close", &App::hFileClose);
	add("file.op", &App::hFileOp, SelfReply, 0.5);
	add("file.cursor", &App::hFileCursor, NoReply, 0.25);
	add("file.versions", &App::hFileVersions);
	add("file.version", &App::hFileVersion);
	add("file.checkpoint", &App::hFileCheckpoint, 0, 3);
	add("file.restore", &App::hFileRestore, 0, 3);

	add("chat.history", &App::hChatHistory);
	add("chat.send", &App::hChatSend);
	add("chat.edit", &App::hChatEdit);
	add("chat.delete", &App::hChatDelete);
	add("chat.typing", &App::hChatTyping, NoReply, 0.25);

	add("task.list", &App::hTaskList);
	add("task.create", &App::hTaskCreate);
	add("task.update", &App::hTaskUpdate);
	add("task.setDone", &App::hTaskSetDone);
	add("task.delete", &App::hTaskDelete);

	add("run.start", &App::hRunStart, 0, 5);
	add("run.stop", &App::hRunStop);

	add("admin.stats", &App::hAdminStats, AdminOnly);
	add("admin.users", &App::hAdminUsers, AdminOnly);
	add("admin.projects", &App::hAdminProjects, AdminOnly);
	add("admin.ban", &App::hAdminBan, AdminOnly);
	add("admin.kick", &App::hAdminKick, AdminOnly);
	add("admin.setAdmin", &App::hAdminSetAdmin, AdminOnly);
	add("admin.deleteProject", &App::hAdminDeleteProject, AdminOnly);
}

// ---- connections ----------------------------------------------------------------

void App::onOpen(const ConnectionPtr& conn)
{
	auto s = std::make_shared<Session>();
	s->id = conn->id;
	s->conn = conn;
	s->ip = conn->ip;
	conn->context = s;
	std::unique_lock<std::shared_mutex> lock(_sessionsMutex);
	_sessions[conn->id] = s;
}

SessionPtr App::sessionFor(const ConnectionPtr& conn)
{
	return std::static_pointer_cast<Session>(conn->context);
}

void App::onClose(const ConnectionPtr& conn)
{
	SessionPtr s = sessionFor(conn);
	if (!s) return;
	endSession(*s);
	_runner->stopAllFor(s->id);
	std::unique_lock<std::shared_mutex> lock(_sessionsMutex);
	_sessions.erase(conn->id);
	conn->context.reset();
}

// Leaves every project and file, then tells friends the user went offline.
void App::endSession(Session& s)
{
	std::vector<long long> projects;
	long long userId;
	{
		std::lock_guard<std::mutex> lock(s.m);
		for (auto& p : s.openProjects) projects.push_back(p.first);
		userId = s.userId;
	}
	for (long long projectId : projects) closeProjectFor(s, projectId);
	{
		std::lock_guard<std::mutex> lock(s.m);
		s.userId = 0;
		s.name.clear();
		s.isAdmin = false;
	}
	if (userId)
	{
		{
			auto lock = _db.lock();
			_db.exec("UPDATE users SET last_seen = ?1 WHERE id = ?2", nowMs(), userId);
		}
		LOG_INFO("Session " << s.id << " signed out user " << userId);
		notifyPresence(userId);
	}
}

void App::onLegacyMessage(const ConnectionPtr& conn, const std::string& payload)
{
	// Builds from before framing: answer what they need to update themselves.
	if (payload.rfind("501", 0) == 0)
	{
		conn->sendRaw("502" + clientVersion());
	}
	else if (payload.rfind("500", 0) == 0)
	{
		std::ifstream file(_cfg.installerPackage, std::ios::binary | std::ios::ate);
		if (file)
		{
			conn->sendFile(_cfg.installerPackage, (long long)file.tellg());
		}
		conn->close();
	}
	else
	{
		conn->close();
	}
}

void App::onMessage(const ConnectionPtr& conn, const std::string& payload)
{
	SessionPtr s = sessionFor(conn);
	if (!s) return;
	{
		std::lock_guard<std::mutex> lock(_rateMutex);
		long long now = nowMs();
		_recentRequests.push_back(now);
		while (!_recentRequests.empty() && _recentRequests.front() < now - 60000) _recentRequests.pop_front();
	}
	_requests++;

	if (payload[0] != '{')
	{
		// Installer / old client: "501" version check, "500" package download.
		if (payload.rfind("501", 0) == 0)
		{
			conn->send("502" + clientVersion());
		}
		else if (payload.rfind("500", 0) == 0)
		{
			std::ifstream file(_cfg.installerPackage, std::ios::binary | std::ios::ate);
			if (!file)
			{
				conn->send("200The installation package is not available on the server.");
			}
			else
			{
				long long size = (long long)file.tellg();
				conn->send("500" + std::to_string(size));
				conn->sendFile(_cfg.installerPackage, size);
				LOG_INFO("Sending the client package (" << size << " bytes) to " << conn->ip);
			}
			conn->close();
		}
		else
		{
			LOG_WARN("Connection " << conn->id << " (" << conn->ip << ") sent an unknown request; disconnecting");
			conn->close();
		}
		return;
	}

	Json req;
	try
	{
		req = Json::parse(payload);
	}
	catch (const std::exception& e)
	{
		conn->send(Json::object({ { "t", "error" }, { "ok", false }, { "code", "bad_request" }, { "error", e.what() } }).dump());
		return;
	}
	const Json& typeField = req["t"];
	std::string type = typeField.isString() ? typeField.asString() : "";
	Json reply = Json::object({ { "t", type } });
	if (req.has("rid")) reply.set("rid", req["rid"]);

	auto route = _routes.find(type);
	if (route == _routes.end())
	{
		reply.set("ok", false).set("code", "bad_request").set("error", "Unknown request '" + type + "'.");
		s->send(reply);
		return;
	}

	Json result;
	try
	{
		result = dispatch(*s, type, req, route->second);
		if (route->second.flags & (NoReply | SelfReply)) return;
		reply.set("ok", true);
		for (const auto& m : result.members()) reply.set(m.first, m.second);
	}
	catch (const ApiError& e)
	{
		if ((route->second.flags & NoReply) && e.code != "not_logged_in") return;
		reply.set("ok", false).set("code", e.code).set("error", e.what());
		if (e.retryAfter) reply.set("retryAfter", e.retryAfter);
	}
	catch (const DbError& e)
	{
		LOG_ERROR(type << " failed: " << e.what());
		reply.set("ok", false).set("code", "internal").set("error", "Something went wrong on the server. Please try again.");
	}
	catch (const std::exception& e)
	{
		// Typed reads of request fields throw runtime_error for the wrong type.
		reply.set("ok", false).set("code", "bad_request").set("error", std::string("Invalid request: ") + e.what());
	}
	s->send(reply);
}

void App::sendReply(Session& s, const Json& req, const Json& fields)
{
	Json reply = Json::object({ { "t", req["t"] } });
	if (req.has("rid")) reply.set("rid", req["rid"]);
	reply.set("ok", true);
	for (const auto& m : fields.members()) reply.set(m.first, m.second);
	s.send(reply);
}

Json App::dispatch(Session& s, const std::string& type, const Json& req, const Route& route)
{
	if (!s.bucket.take(_cfg.requestsPerSecond, _cfg.requestBurst, route.cost))
	{
		if (++s.limitedInARow > 2000)
		{
			LOG_WARN("Session " << s.id << " (" << s.ip << ") keeps exceeding the request limit; disconnecting");
			s.conn->close();
		}
		ApiError e("rate_limited", "You're doing that too fast. Please slow down.");
		e.retryAfter = 1;
		throw e;
	}
	s.limitedInARow = 0;

	long long userId;
	bool isAdmin;
	{
		std::lock_guard<std::mutex> lock(s.m);
		userId = s.userId;
		isAdmin = s.isAdmin;
	}
	if (!(route.flags & PreLogin) && userId == 0) fail("not_logged_in", "Please sign in first.");
	if ((route.flags & AdminOnly) && !isAdmin) fail("forbidden", "Only server admins can do that.");
	LOG_DEBUG("session " << s.id << " user " << userId << ": " << type);
	return (this->*route.handler)(s, req);
}

// ---- sending ----------------------------------------------------------------------

std::vector<SessionPtr> App::allSessions()
{
	std::shared_lock<std::shared_mutex> lock(_sessionsMutex);
	std::vector<SessionPtr> out;
	out.reserve(_sessions.size());
	for (auto& entry : _sessions) out.push_back(entry.second);
	return out;
}

std::vector<SessionPtr> App::sessionsOf(long long userId)
{
	std::vector<SessionPtr> out;
	for (auto& s : allSessions())
	{
		if (s->uid() == userId) out.push_back(s);
	}
	return out;
}

bool App::isOnline(long long userId)
{
	return !sessionsOf(userId).empty();
}

void App::sendToSession(uint64_t sessionId, const Json& message)
{
	SessionPtr s;
	{
		std::shared_lock<std::shared_mutex> lock(_sessionsMutex);
		auto it = _sessions.find(sessionId);
		if (it == _sessions.end()) return;
		s = it->second;
	}
	s->send(message);
}

void App::sendToUser(long long userId, const Json& message)
{
	if (userId <= 0) return;
	std::string payload = message.dump();
	for (auto& s : sessionsOf(userId)) s->conn->send(payload);
}

void App::sendToUsers(const std::set<long long>& userIds, const Json& message)
{
	if (userIds.empty()) return;
	std::string payload = message.dump();
	for (auto& s : allSessions())
	{
		if (userIds.count(s->uid())) s->conn->send(payload);
	}
}

void App::kickUser(long long userId, const std::string& reason)
{
	Json message = Json::object({ { "t", "ev.kicked" }, { "reason", reason } });
	for (auto& s : sessionsOf(userId))
	{
		s->send(message);
		s->conn->close();
	}
}

// ---- rooms -------------------------------------------------------------------------

RoomPtr App::room(long long projectId, bool create)
{
	std::lock_guard<std::mutex> lock(_roomsMutex);
	auto it = _rooms.find(projectId);
	if (it != _rooms.end()) return it->second;
	if (!create) return nullptr;
	auto r = std::make_shared<Room>();
	r->projectId = projectId;
	_rooms[projectId] = r;
	return r;
}

void App::broadcast(Room& room, const Json& message, uint64_t exceptSession)
{
	std::string payload = message.dump();
	for (auto& entry : room.sessions)
	{
		if (entry.first != exceptSession) entry.second->conn->send(payload);
	}
}

void App::broadcastToRoom(long long projectId, const Json& message, uint64_t exceptSession)
{
	RoomPtr r = room(projectId, false);
	if (!r) return;
	std::lock_guard<std::mutex> lock(r->m);
	broadcast(*r, message, exceptSession);
}

void App::closeProjectFor(Session& s, long long projectId)
{
	RoomPtr r = room(projectId, false);
	if (r)
	{
		std::lock_guard<std::mutex> lock(r->m);
		std::vector<long long> files;
		{
			std::lock_guard<std::mutex> sl(s.m);
			for (auto& f : s.openFiles)
				if (f.second == projectId) files.push_back(f.first);
		}
		for (long long fileId : files) closeFileLocked(*r, s, fileId);
		r->sessions.erase(s.id);
	}
	long long userId;
	{
		std::lock_guard<std::mutex> sl(s.m);
		s.openProjects.erase(projectId);
		for (auto it = s.openFiles.begin(); it != s.openFiles.end();)
			it = it->second == projectId ? s.openFiles.erase(it) : std::next(it);
		if (s.activeProject == projectId)
		{
			s.activeProject = 0;
			s.activeFile = 0;
		}
		userId = s.userId;
	}
	if (userId) notifyPresence(userId);
}

Json App::fileUsersJson(LiveDoc& doc)
{
	Json users = Json::array();
	std::set<long long> seen;
	for (auto& v : doc.viewers)
	{
		long long id = v.second->uid();
		if (id && seen.insert(id).second) users.push(userJson(id));
	}
	return users;
}

void App::closeFileLocked(Room& room, Session& s, long long fileId)
{
	{
		std::lock_guard<std::mutex> sl(s.m);
		s.openFiles.erase(fileId);
		if (s.activeFile == fileId) s.activeFile = 0;
	}
	auto it = room.docs.find(fileId);
	if (it == room.docs.end()) return;
	LiveDoc& doc = it->second;
	if (!doc.viewers.erase(s.id)) return;
	doc.cursors.erase(s.id);
	Json users = Json::object({ { "t", "ev.file.users" }, { "fileId", fileId }, { "users", fileUsersJson(doc) } });
	std::string payload = users.dump();
	for (auto& v : doc.viewers) v.second->conn->send(payload);
}

LiveDoc& App::loadDoc(Room& room, long long fileId)
{
	auto it = room.docs.find(fileId);
	if (it != room.docs.end()) return it->second;
	std::string content;
	long long lastVersion = 0;
	{
		auto lock = _db.lock();
		auto text = _db.text("SELECT content FROM files WHERE id = ?1", fileId);
		if (!text) fail("not_found", "That file no longer exists.");
		content = *text;
		lastVersion = _db.scalar(0, "SELECT MAX(created_at) FROM file_versions WHERE file_id = ?1", fileId);
	}
	LiveDoc& doc = room.docs[fileId];
	doc.fileId = fileId;
	doc.text = utf8ToWide(content);
	doc.lastVersionAt = lastVersion;
	return doc;
}

// Applies an edit that didn't come from a client (restoring a version) and
// sends it to everyone with the file open.
void App::applyServerEdit(Room& room, LiveDoc& doc, const TextOperation& op, long long userId)
{
	doc.text = op.apply(doc.text);
	doc.history.push_back(op);
	doc.rev++;
	doc.dirty = true;
	doc.changedSinceVersion = true;
	doc.lastEditor = userId;
	for (auto& c : doc.cursors)
	{
		c.second.anchor = op.transformIndex(c.second.anchor);
		c.second.head = op.transformIndex(c.second.head);
	}
	Json message = Json::object({ { "t", "ev.file.op" }, { "fileId", doc.fileId }, { "rev", doc.rev }, { "op", op.toJson() }, { "userId", userId } });
	std::string payload = message.dump();
	for (auto& v : doc.viewers) v.second->conn->send(payload);
}

// ---- saving -----------------------------------------------------------------------

void App::writeVersion(long long fileId, const std::string& text, const std::string& label, long long userId)
{
	auto lock = _db.lock();
	auto last = _db.text("SELECT content FROM file_versions WHERE file_id = ?1 ORDER BY id DESC LIMIT 1", fileId);
	if (label.empty() && last && *last == text) return; // nothing new
	std::optional<long long> user;
	if (userId) user = userId;
	_db.exec("INSERT INTO file_versions (file_id, content, label, user_id, created_at) VALUES (?1, ?2, ?3, ?4, ?5)",
		fileId, text, label, user, nowMs());
	// Keep the newest automatic versions; named checkpoints are kept forever.
	_db.exec("DELETE FROM file_versions WHERE file_id = ?1 AND label = '' AND id NOT IN "
		"(SELECT id FROM file_versions WHERE file_id = ?1 AND label = '' ORDER BY id DESC LIMIT ?2)",
		fileId, _cfg.maxVersionsPerFile);
}

void App::saveRoom(const RoomPtr& room, bool unloadIdle)
{
	struct Write
	{
		long long fileId;
		std::string text;
		bool content;
		bool version;
		long long editor;
	};
	std::vector<Write> writes;
	long long now = nowMs();
	long long versionInterval = (long long)_cfg.versionIntervalMinutes * 60000;
	{
		std::lock_guard<std::mutex> lock(room->m);
		for (auto it = room->docs.begin(); it != room->docs.end();)
		{
			LiveDoc& doc = it->second;
			bool idle = doc.viewers.empty();
			bool version = doc.changedSinceVersion && (idle || now - doc.lastVersionAt >= versionInterval);
			if (doc.dirty || version)
			{
				writes.push_back({ doc.fileId, wideToUtf8(doc.text), doc.dirty, version, doc.lastEditor });
				doc.dirty = false;
				if (version)
				{
					doc.changedSinceVersion = false;
					doc.lastVersionAt = now;
				}
			}
			it = idle && unloadIdle ? room->docs.erase(it) : std::next(it);
		}
	}
	for (const Write& w : writes)
	{
		try
		{
			auto lock = _db.lock();
			if (w.content)
				_db.exec("UPDATE files SET content = ?1, updated_at = ?2 WHERE id = ?3", w.text, now, w.fileId);
			if (w.version)
				writeVersion(w.fileId, w.text, "", w.editor);
		}
		catch (const std::exception& e)
		{
			LOG_ERROR("Saving file " << w.fileId << " failed: " << e.what());
		}
	}
	if (!writes.empty())
	{
		LOG_DEBUG("Saved " << writes.size() << " file(s) of project " << room->projectId);
		auto lock = _db.lock();
		_db.exec("UPDATE projects SET updated_at = ?1 WHERE id = ?2", now, room->projectId);
	}
}

void App::autosaveLoop()
{
	while (true)
	{
		{
			std::unique_lock<std::mutex> lock(_stopMutex);
			_stopCv.wait_for(lock, std::chrono::seconds(_cfg.saveIntervalSeconds), [this] { return _stopping; });
			if (_stopping) return;
		}
		std::vector<RoomPtr> rooms;
		{
			std::lock_guard<std::mutex> lock(_roomsMutex);
			for (auto& r : _rooms) rooms.push_back(r.second);
		}
		for (auto& r : rooms) saveRoom(r, true);
	}
}

void App::saveAll()
{
	std::vector<RoomPtr> rooms;
	{
		std::lock_guard<std::mutex> lock(_roomsMutex);
		for (auto& r : _rooms) rooms.push_back(r.second);
	}
	for (auto& r : rooms)
	{
		{
			// a final version of everything edited since the last one
			std::lock_guard<std::mutex> lock(r->m);
			for (auto& d : r->docs)
				if (d.second.changedSinceVersion) d.second.lastVersionAt = 0;
		}
		saveRoom(r, false);
	}
}

void App::stop()
{
	{
		std::lock_guard<std::mutex> lock(_stopMutex);
		_stopping = true;
	}
	_stopCv.notify_all();
	_runner->stopAll();
}

// ---- admin console ----------------------------------------------------------------

std::string App::consoleCommand(const std::string& line)
{
	std::istringstream in(line);
	std::string command;
	in >> command;
	command = Util::lower(command);
	std::string arg;
	in >> arg;
	std::string rest;
	std::getline(in, rest);
	rest = Util::trim(rest);
	std::ostringstream out;

	auto userByName = [this](const std::string& name) -> long long {
		auto lock = _db.lock();
		return _db.scalar(0, "SELECT id FROM users WHERE name = ?1", name);
	};

	if (command == "help" || command == "?")
	{
		out << "Commands:\n"
			"  status                 server statistics\n"
			"  sessions               who is connected\n"
			"  users [text]           find accounts\n"
			"  kick <user>            disconnect a user\n"
			"  ban <user> [reason]    block a user from signing in\n"
			"  unban <user>\n"
			"  admin <user> on|off    give or take server-admin rights\n"
			"  version [x.y]          show or set the client version clients must run\n"
			"  save                   write all open files now\n"
			"  exit                   save everything and stop the server";
	}
	else if (command == "status")
	{
		Json s = statsJson();
		out << "Up " << Util::formatDuration(s["uptime"].asInt()) << ", " << s["sessions"].asInt() << " connected ("
			<< s["onlineUsers"].asInt() << " signed in), " << s["users"].asInt() << " accounts, " << s["projects"].asInt()
			<< " projects, " << s["openFiles"].asInt() << " files open, " << s["requestsPerMinute"].asInt()
			<< " requests in the last minute, " << s["runs"].asInt() << " programs running.";
	}
	else if (command == "sessions")
	{
		auto sessions = allSessions();
		out << sessions.size() << " connection(s)";
		for (auto& s : sessions)
		{
			std::lock_guard<std::mutex> lock(s->m);
			out << "\n  #" << s->id << "  " << s->ip << "  " << (s->userId ? s->name : "(not signed in)");
			if (s->userId) out << "  " << s->status << (s->activeProject ? "  in project " + std::to_string(s->activeProject) : "");
		}
	}
	else if (command == "users")
	{
		auto lock = _db.lock();
		std::string pattern = "%" + arg + "%";
		auto st = _db.query("SELECT id, name, IFNULL(email, ''), is_admin, banned FROM users "
			"WHERE name LIKE ?1 OR email LIKE ?1 ORDER BY name LIMIT 50", pattern);
		int n = 0;
		while (st.next())
		{
			n++;
			out << "  " << st.i(0) << "  " << st.s(1) << "  " << st.s(2) << (st.i(3) ? "  [admin]" : "") << (st.i(4) ? "  [banned]" : "") << "\n";
		}
		out << n << " user(s)";
	}
	else if (command == "kick" || command == "ban" || command == "unban" || command == "admin")
	{
		long long id = userByName(arg);
		if (!id) return "No user named '" + arg + "'.";
		if (command == "kick")
		{
			kickUser(id, "You were disconnected by an administrator.");
			out << "Disconnected " << arg << ".";
		}
		else if (command == "ban" || command == "unban")
		{
			setBanned(id, command == "ban", rest);
			out << (command == "ban" ? "Banned " : "Unbanned ") << arg << ".";
		}
		else
		{
			bool on = Util::lower(rest) != "off";
			{
				auto lock = _db.lock();
				_db.exec("UPDATE users SET is_admin = ?1 WHERE id = ?2", on, id);
			}
			for (auto& s : sessionsOf(id))
			{
				std::lock_guard<std::mutex> lock(s->m);
				s->isAdmin = on || isAdminName(s->name);
			}
			out << arg << (on ? " is now a server admin (takes effect at their next sign-in for the admin page)." : " is no longer a server admin.");
		}
	}
	else if (command == "version")
	{
		if (!arg.empty())
		{
			auto lock = _db.lock();
			_db.exec("INSERT OR REPLACE INTO settings (key, value) VALUES ('client_version', ?1)", arg);
		}
		std::string v = clientVersion();
		out << "Clients must run version " << (v.empty() ? "(any)" : v)
			<< (_cfg.clientVersion.empty() ? "" : " (set in server.json)") << ".";
	}
	else if (command == "save")
	{
		saveAll();
		out << "Saved.";
	}
	else if (!command.empty())
	{
		out << "Unknown command '" << command << "'. Type help.";
	}
	return out.str();
}

std::string App::clientVersion()
{
	if (!_cfg.clientVersion.empty()) return _cfg.clientVersion;
	auto lock = _db.lock();
	return _db.text("SELECT value FROM settings WHERE key = 'client_version'").value_or("");
}

bool App::isAdminName(const std::string& name)
{
	for (const auto& a : _cfg.admins)
		if (Util::lower(a) == Util::lower(name)) return true;
	return false;
}
