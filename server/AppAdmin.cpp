#include "App.h"
#include "CodeRunner.h"
#include "Log.h"
#include "Util.h"

// Server administration: statistics, accounts, bans, projects.
// Available to server admins in the client and on the server console.

Json App::statsJson()
{
	long long online = 0;
	std::set<long long> users;
	auto sessions = allSessions();
	for (auto& s : sessions)
		if (long long id = s->uid()) users.insert(id);
	online = (long long)users.size();

	long long openFiles = 0;
	{
		std::lock_guard<std::mutex> lock(_roomsMutex);
		for (auto& r : _rooms)
		{
			std::lock_guard<std::mutex> roomLock(r.second->m);
			openFiles += (long long)r.second->docs.size();
		}
	}
	long long perMinute;
	{
		std::lock_guard<std::mutex> lock(_rateMutex);
		long long cutoff = nowMs() - 60000;
		while (!_recentRequests.empty() && _recentRequests.front() < cutoff) _recentRequests.pop_front();
		perMinute = (long long)_recentRequests.size();
	}
	auto lock = _db.lock();
	return Json::object({
		{ "uptime", nowMs() - _startedAt },
		{ "sessions", (long long)sessions.size() },
		{ "onlineUsers", online },
		{ "users", _db.scalar(0, "SELECT COUNT(*) FROM users") },
		{ "bannedUsers", _db.scalar(0, "SELECT COUNT(*) FROM users WHERE banned = 1") },
		{ "projects", _db.scalar(0, "SELECT COUNT(*) FROM projects") },
		{ "files", _db.scalar(0, "SELECT COUNT(*) FROM files") },
		{ "openFiles", openFiles },
		{ "requestsPerMinute", perMinute },
		{ "totalRequests", _requests.load() },
		{ "runs", (long long)_runner->running() },
		{ "runEnabled", _cfg.runEnabled },
	});
}

Json App::hAdminStats(Session&, const Json&)
{
	return statsJson();
}

Json App::hAdminUsers(Session&, const Json& req)
{
	std::string query = Util::trim(optString(req, "query", 100));
	std::string pattern = "%";
	for (char c : query)
	{
		if (c == '%' || c == '_' || c == '\\') pattern += '\\';
		pattern += c;
	}
	pattern += "%";
	struct Row { long long id; Json json; };
	std::vector<Row> rows;
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT id, name, avatar, IFNULL(email, ''), is_admin, banned, ban_reason, created_at, IFNULL(last_seen, 0), "
			"(SELECT COUNT(*) FROM project_members WHERE user_id = users.id) FROM users "
			"WHERE name LIKE ?1 ESCAPE '\\' OR email LIKE ?1 ESCAPE '\\' ORDER BY name LIMIT 200", pattern);
		while (st.next())
		{
			Json u = userJson(st.i(0), st.s(1), st.s(2));
			u.set("email", st.s(3));
			u.set("isAdmin", st.i(4) != 0 || isAdminName(st.s(1)));
			u.set("banned", st.i(5) != 0);
			u.set("banReason", st.s(6));
			u.set("createdAt", st.i(7));
			u.set("lastSeen", st.i(8));
			u.set("projects", st.i(9));
			rows.push_back({ st.i(0), u });
		}
	}
	Json users = Json::array();
	for (auto& r : rows)
	{
		r.json.set("online", isOnline(r.id));
		users.push(r.json);
	}
	return Json::object({ { "users", users } });
}

Json App::hAdminProjects(Session&, const Json& req)
{
	std::string query = Util::trim(optString(req, "query", 100));
	std::string pattern = "%";
	for (char c : query)
	{
		if (c == '%' || c == '_' || c == '\\') pattern += '\\';
		pattern += c;
	}
	pattern += "%";
	Json projects = Json::array();
	auto lock = _db.lock();
	auto st = _db.query("SELECT p.id, p.name, p.visibility, p.created_at, p.updated_at, IFNULL(p.owner_id, 0), "
		"(SELECT COUNT(*) FROM project_members WHERE project_id = p.id), (SELECT COUNT(*) FROM files WHERE project_id = p.id), p.language "
		"FROM projects p WHERE p.name LIKE ?1 ESCAPE '\\' ORDER BY p.updated_at DESC LIMIT 200", pattern);
	while (st.next())
	{
		Json p = Json::object({
			{ "id", st.i(0) }, { "name", st.s(1) }, { "visibility", st.s(2) }, { "createdAt", st.i(3) }, { "updatedAt", st.i(4) },
			{ "members", st.i(6) }, { "files", st.i(7) }, { "language", st.s(8) },
		});
		if (st.i(5)) p.set("owner", userJson(st.i(5)));
		projects.push(p);
	}
	return Json::object({ { "projects", projects } });
}

void App::setBanned(long long userId, bool banned, const std::string& reason)
{
	{
		auto lock = _db.lock();
		_db.exec("UPDATE users SET banned = ?1, ban_reason = ?2 WHERE id = ?3", banned, banned ? reason : std::string(), userId);
	}
	LOG_INFO("User " << userId << (banned ? " banned" : " unbanned") << (reason.empty() ? "" : ": " + reason));
	if (banned) kickUser(userId, "Your account has been suspended" + (reason.empty() ? std::string(".") : ": " + reason));
}

Json App::hAdminBan(Session& s, const Json& req)
{
	long long userId = reqId(req, "userId");
	if (userId == s.uid()) fail("bad_request", "You can't ban yourself.");
	bool banned = reqBool(req, "banned");
	std::string reason = Util::trim(optString(req, "reason", 300));
	if (banned && isAdminName(userName(userId))) fail("forbidden", "Admins listed in server.json can't be banned.");
	setBanned(userId, banned, reason);
	LOG_INFO(s.name << (banned ? " banned " : " unbanned ") << userName(userId));
	return Json::object();
}

Json App::hAdminKick(Session& s, const Json& req)
{
	long long userId = reqId(req, "userId");
	if (userId == s.uid()) fail("bad_request", "You can't disconnect yourself.");
	kickUser(userId, "You were disconnected by an administrator.");
	LOG_INFO(s.name << " disconnected " << userName(userId));
	return Json::object();
}

Json App::hAdminSetAdmin(Session& s, const Json& req)
{
	long long userId = reqId(req, "userId");
	bool admin = reqBool(req, "isAdmin");
	if (userId == s.uid() && !admin) fail("bad_request", "You can't remove your own admin rights.");
	{
		auto lock = _db.lock();
		_db.exec("UPDATE users SET is_admin = ?1 WHERE id = ?2", admin, userId);
	}
	for (auto& session : sessionsOf(userId))
	{
		std::lock_guard<std::mutex> lock(session->m);
		session->isAdmin = admin || isAdminName(session->name);
	}
	LOG_INFO(s.name << (admin ? " made " : " removed admin rights from ") << userName(userId));
	return Json::object();
}

Json App::hAdminDeleteProject(Session& s, const Json& req)
{
	ProjectRow p = requireProject(reqId(req, "id"));
	std::set<long long> members = memberIds(p.id);
	deleteProjectNow(p, "The project was removed by a server administrator.");
	for (long long member : members)
		addInbox(member, "info", "The project " + p.name + " was removed by a server administrator.", 0, 0);
	LOG_INFO(s.name << " deleted project '" << p.name << "'");
	return Json::object();
}
