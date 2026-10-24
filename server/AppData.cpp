#include "App.h"
#include "Log.h"
#include "Util.h"

// Lookups and notifications shared by the request handlers.

Json App::userJson(long long id, const std::string& name, const std::string& avatar)
{
	return Json::object({ { "id", id }, { "name", name }, { "avatar", avatar } });
}

Json App::userJson(long long userId)
{
	auto lock = _db.lock();
	auto st = _db.query("SELECT name, avatar FROM users WHERE id = ?1", userId);
	if (!st.next()) return Json::object({ { "id", userId }, { "name", "(deleted user)" }, { "avatar", "" } });
	return userJson(userId, st.s(0), st.s(1));
}

std::optional<long long> App::findUserId(const std::string& nameOrEmail)
{
	auto lock = _db.lock();
	long long id = _db.scalar(0, "SELECT id FROM users WHERE name = ?1 OR email = ?1", Util::trim(nameOrEmail));
	if (!id) return std::nullopt;
	return id;
}

std::string App::userName(long long userId)
{
	auto lock = _db.lock();
	return _db.text("SELECT name FROM users WHERE id = ?1", userId).value_or("(deleted user)");
}

// Online/away/offline, and which project and file the user is in when the
// viewer is a member of that project.
Json App::presenceJson(long long userId, long long viewerId)
{
	std::string status = "offline";
	long long project = 0, file = 0, latest = 0;
	for (auto& s : sessionsOf(userId))
	{
		std::lock_guard<std::mutex> lock(s->m);
		if (status != "online") status = s->status;
		if (s->activeProject && (long long)s->id > latest)
		{
			latest = (long long)s->id;
			project = s->activeProject;
			file = s->activeFile;
		}
	}
	Json presence = Json::object({ { "status", status } });
	if (project && (viewerId == userId || !roleOf(project, viewerId).empty()))
	{
		presence.set("projectId", project);
		auto lock = _db.lock();
		if (auto name = _db.text("SELECT name FROM projects WHERE id = ?1", project)) presence.set("projectName", *name);
		if (file)
		{
			if (auto path = _db.text("SELECT path FROM files WHERE id = ?1", file)) presence.set("file", *path);
		}
	}
	return presence;
}

// Sends the user's current presence to friends and project co-members who are online.
void App::notifyPresence(long long userId)
{
	std::set<long long> related = relatedUsers(userId);
	related.insert(userId); // their other devices
	for (auto& s : allSessions())
	{
		long long viewer = s->uid();
		if (!viewer || !related.count(viewer)) continue;
		s->send(Json::object({ { "t", "ev.presence" }, { "userId", userId }, { "presence", presenceJson(userId, viewer) } }));
	}
}

std::set<long long> App::friendIds(long long userId)
{
	auto lock = _db.lock();
	std::set<long long> ids;
	auto st = _db.query("SELECT user_b FROM friendships WHERE user_a = ?1 UNION SELECT user_a FROM friendships WHERE user_b = ?1", userId);
	while (st.next()) ids.insert(st.i(0));
	return ids;
}

bool App::areFriends(long long a, long long b)
{
	auto lock = _db.lock();
	return _db.scalar(0, "SELECT 1 FROM friendships WHERE user_a = ?1 AND user_b = ?2", std::min(a, b), std::max(a, b)) == 1;
}

std::set<long long> App::relatedUsers(long long userId)
{
	std::set<long long> ids = friendIds(userId);
	auto lock = _db.lock();
	auto st = _db.query("SELECT DISTINCT m2.user_id FROM project_members m1 JOIN project_members m2 ON m1.project_id = m2.project_id "
		"WHERE m1.user_id = ?1 AND m2.user_id <> ?1", userId);
	while (st.next()) ids.insert(st.i(0));
	return ids;
}

std::optional<ProjectRow> App::project(long long projectId)
{
	auto lock = _db.lock();
	auto st = _db.query("SELECT id, name, language, description, visibility, IFNULL(owner_id, 0), IFNULL(invite_code, ''), "
		"invite_auto_approve, created_at, updated_at FROM projects WHERE id = ?1", projectId);
	if (!st.next()) return std::nullopt;
	ProjectRow p;
	p.id = st.i(0);
	p.name = st.s(1);
	p.language = st.s(2);
	p.description = st.s(3);
	p.visibility = st.s(4);
	p.ownerId = st.i(5);
	p.inviteCode = st.s(6);
	p.inviteAutoApprove = st.i(7) != 0;
	p.createdAt = st.i(8);
	p.updatedAt = st.i(9);
	return p;
}

ProjectRow App::requireProject(long long projectId)
{
	auto p = project(projectId);
	if (!p) fail("not_found", "That project no longer exists.");
	return *p;
}

std::string App::roleOf(long long projectId, long long userId)
{
	auto lock = _db.lock();
	return _db.text("SELECT role FROM project_members WHERE project_id = ?1 AND user_id = ?2", projectId, userId).value_or("");
}

std::set<long long> App::memberIds(long long projectId)
{
	auto lock = _db.lock();
	std::set<long long> ids;
	auto st = _db.query("SELECT user_id FROM project_members WHERE project_id = ?1", projectId);
	while (st.next()) ids.insert(st.i(0));
	return ids;
}

std::string App::requireMember(Session& s, long long projectId)
{
	requireProject(projectId);
	std::string role = roleOf(projectId, s.uid());
	if (role.empty()) fail("forbidden", "You're not a member of this project.");
	return role;
}

std::string App::requireProjectAdmin(Session& s, long long projectId)
{
	std::string role = requireMember(s, projectId);
	if (role != "owner" && role != "admin") fail("forbidden", "Only the project's owner and admins can do that.");
	return role;
}

std::string App::accessRole(Session& s, const ProjectRow& p)
{
	std::string role = roleOf(p.id, s.uid());
	if (!role.empty()) return role;
	if (p.visibility == "public") return "viewer";
	fail("forbidden", "This project is private.");
}

std::string App::requireWriter(Session& s, long long projectId)
{
	std::string role = requireMember(s, projectId);
	return role;
}

FileRow App::requireFile(long long fileId)
{
	auto lock = _db.lock();
	auto st = _db.query("SELECT id, project_id, path FROM files WHERE id = ?1", fileId);
	if (!st.next()) fail("not_found", "That file no longer exists.");
	return FileRow{ st.i(0), st.i(1), st.s(2) };
}

Json App::projectSummaryJson(const ProjectRow& p, long long viewerId)
{
	auto lock = _db.lock();
	long long members = _db.scalar(0, "SELECT COUNT(*) FROM project_members WHERE project_id = ?1", p.id);
	std::string role = _db.text("SELECT role FROM project_members WHERE project_id = ?1 AND user_id = ?2", p.id, viewerId).value_or("viewer");
	Json j = Json::object({
		{ "id", p.id }, { "name", p.name }, { "language", p.language }, { "description", p.description },
		{ "visibility", p.visibility }, { "role", role }, { "members", members }, { "updatedAt", p.updatedAt },
	});
	if (p.ownerId) j.set("owner", userJson(p.ownerId));
	return j;
}

Json App::projectDetailJson(const ProjectRow& p, long long viewerId)
{
	Json members = Json::array();
	Json invites = Json::array();
	std::string myRole = "viewer";
	std::vector<std::pair<long long, std::string>> memberRows;
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT u.id, u.name, u.avatar, m.role FROM project_members m JOIN users u ON u.id = m.user_id "
			"WHERE m.project_id = ?1 ORDER BY CASE m.role WHEN 'owner' THEN 0 WHEN 'admin' THEN 1 ELSE 2 END, u.name", p.id);
		while (st.next())
		{
			members.push(Json::object({ { "user", userJson(st.i(0), st.s(1), st.s(2)) }, { "role", st.s(3) } }));
			memberRows.push_back({ st.i(0), st.s(3) });
			if (st.i(0) == viewerId) myRole = st.s(3);
		}
		auto inv = _db.query("SELECT u.id, u.name, u.avatar, i.role FROM project_invites i JOIN users u ON u.id = i.user_id "
			"WHERE i.project_id = ?1 ORDER BY u.name", p.id);
		while (inv.next())
			invites.push(Json::object({ { "user", userJson(inv.i(0), inv.s(1), inv.s(2)) }, { "role", inv.s(3) } }));
	}
	// presence, outside the database lock
	Json withPresence = Json::array();
	for (size_t k = 0; k < members.size(); k++)
	{
		Json m = members[k];
		m.set("presence", presenceJson(memberRows[k].first, viewerId));
		withPresence.push(m);
	}
	Json j = Json::object({
		{ "id", p.id }, { "name", p.name }, { "language", p.language }, { "description", p.description },
		{ "visibility", p.visibility }, { "myRole", myRole }, { "members", withPresence },
		{ "createdAt", p.createdAt }, { "updatedAt", p.updatedAt },
	});
	if (p.ownerId) j.set("owner", userJson(p.ownerId));
	if (myRole == "owner" || myRole == "admin")
	{
		j.set("invites", invites);
		j.set("inviteCode", p.inviteCode);
		j.set("inviteAutoApprove", p.inviteAutoApprove);
	}
	return j;
}

Json App::treeJson(long long projectId, Json& foldersOut)
{
	auto lock = _db.lock();
	Json files = Json::array();
	auto st = _db.query("SELECT id, path FROM files WHERE project_id = ?1 ORDER BY path", projectId);
	while (st.next()) files.push(Json::object({ { "id", st.i(0) }, { "path", st.s(1) } }));
	foldersOut = Json::array();
	auto f = _db.query("SELECT path FROM folders WHERE project_id = ?1 ORDER BY path", projectId);
	while (f.next()) foldersOut.push(f.s(0));
	return files;
}

Json App::taskJson(long long taskId)
{
	auto lock = _db.lock();
	auto st = _db.query("SELECT id, text, due, done, IFNULL(created_by, 0), created_at, completed_at FROM tasks WHERE id = ?1", taskId);
	if (!st.next()) fail("not_found", "That task no longer exists.");
	Json task = Json::object({
		{ "id", st.i(0) }, { "text", st.s(1) }, { "due", st.s(2) }, { "done", st.i(3) != 0 },
		{ "createdAt", st.i(5) },
	});
	if (st.i(4)) task.set("createdBy", userJson(st.i(4)));
	if (!st.isNull(6)) task.set("completedAt", st.i(6));
	Json assignees = Json::array();
	auto a = _db.query("SELECT u.id, u.name, u.avatar FROM task_assignees t JOIN users u ON u.id = t.user_id WHERE t.task_id = ?1 ORDER BY u.name", taskId);
	while (a.next()) assignees.push(userJson(a.i(0), a.s(1), a.s(2)));
	task.set("assignees", assignees);
	return task;
}

Json App::chatMessageJson(long long messageId)
{
	auto lock = _db.lock();
	auto st = _db.query("SELECT c.id, c.text, c.created_at, c.edited_at, c.deleted, IFNULL(c.user_id, 0), IFNULL(u.name, '(deleted user)'), IFNULL(u.avatar, '') "
		"FROM chat_messages c LEFT JOIN users u ON u.id = c.user_id WHERE c.id = ?1", messageId);
	if (!st.next()) fail("not_found", "That message no longer exists.");
	bool deleted = st.i(4) != 0;
	Json m = Json::object({
		{ "id", st.i(0) }, { "user", userJson(st.i(5), st.s(6), st.s(7)) }, { "text", deleted ? "" : st.s(1) },
		{ "createdAt", st.i(2) }, { "deleted", deleted },
	});
	if (!st.isNull(3)) m.set("editedAt", st.i(3));
	return m;
}

long long App::unreadCount(long long userId)
{
	auto lock = _db.lock();
	return _db.scalar(0, "SELECT COUNT(*) FROM inbox WHERE user_id = ?1 AND read = 0", userId);
}

// Stores an inbox item and pushes it to the user's open sessions (they show a notification).
void App::addInbox(long long userId, const std::string& kind, const std::string& text, long long fromId, long long projectId)
{
	if (userId <= 0) return;
	long long id;
	std::string projectName;
	{
		auto lock = _db.lock();
		std::optional<long long> from, proj;
		if (fromId) from = fromId;
		if (projectId) proj = projectId;
		_db.exec("INSERT INTO inbox (user_id, kind, text, from_id, project_id, created_at) VALUES (?1, ?2, ?3, ?4, ?5, ?6)",
			userId, kind, text, from, proj, nowMs());
		id = _db.lastInsertId();
		if (projectId) projectName = _db.text("SELECT name FROM projects WHERE id = ?1", projectId).value_or("");
		// Inboxes don't grow forever: keep the newest 500 items.
		_db.exec("DELETE FROM inbox WHERE user_id = ?1 AND id NOT IN (SELECT id FROM inbox WHERE user_id = ?1 ORDER BY id DESC LIMIT 500)", userId);
	}
	Json item = Json::object({ { "id", id }, { "kind", kind }, { "text", text }, { "read", false }, { "createdAt", nowMs() } });
	if (fromId) item.set("from", userJson(fromId));
	if (projectId)
	{
		item.set("projectId", projectId);
		item.set("projectName", projectName);
	}
	sendToUser(userId, Json::object({ { "t", "ev.inbox" }, { "item", item }, { "unread", unreadCount(userId) } }));
}

// Removes inbox items that no longer need an answer (e.g. an invite that was withdrawn).
void App::removeInbox(long long userId, const std::string& kind, std::optional<long long> fromId, std::optional<long long> projectId)
{
	auto lock = _db.lock();
	if (fromId && projectId)
		_db.exec("DELETE FROM inbox WHERE user_id = ?1 AND kind = ?2 AND from_id = ?3 AND project_id = ?4", userId, kind, *fromId, *projectId);
	else if (fromId)
		_db.exec("DELETE FROM inbox WHERE user_id = ?1 AND kind = ?2 AND from_id = ?3", userId, kind, *fromId);
	else if (projectId)
		_db.exec("DELETE FROM inbox WHERE user_id = ?1 AND kind = ?2 AND project_id = ?3", userId, kind, *projectId);
}

void App::addActivity(long long projectId, long long userId, const std::string& kind, const std::string& text)
{
	long long id;
	std::string projectName;
	{
		auto lock = _db.lock();
		std::optional<long long> user;
		if (userId) user = userId;
		_db.exec("INSERT INTO activity (project_id, user_id, kind, text, created_at) VALUES (?1, ?2, ?3, ?4, ?5)",
			projectId, user, kind, text, nowMs());
		id = _db.lastInsertId();
		projectName = _db.text("SELECT name FROM projects WHERE id = ?1", projectId).value_or("");
		_db.exec("UPDATE projects SET updated_at = ?1 WHERE id = ?2", nowMs(), projectId);
	}
	Json item = Json::object({
		{ "id", id }, { "projectId", projectId }, { "projectName", projectName }, { "kind", kind },
		{ "text", text }, { "createdAt", nowMs() },
	});
	if (userId) item.set("user", userJson(userId));
	sendToUsers(memberIds(projectId), Json::object({ { "t", "ev.activity" }, { "item", item } }));
}

void App::touchProject(long long projectId)
{
	auto lock = _db.lock();
	_db.exec("UPDATE projects SET updated_at = ?1 WHERE id = ?2", nowMs(), projectId);
}

// Project settings or membership changed: refresh everyone who has it open and
// everyone whose project list shows it.
void App::notifyProjectChanged(long long projectId)
{
	auto p = project(projectId);
	if (!p) return;
	RoomPtr r = room(projectId, false);
	if (r)
	{
		std::vector<SessionPtr> sessions;
		{
			std::lock_guard<std::mutex> lock(r->m);
			for (auto& s : r->sessions) sessions.push_back(s.second);
		}
		for (auto& s : sessions)
			s->send(Json::object({ { "t", "ev.project" }, { "project", projectDetailJson(*p, s->uid()) } }));
	}
	sendToUsers(memberIds(projectId), Json::object({ { "t", "ev.projects" } }));
}
