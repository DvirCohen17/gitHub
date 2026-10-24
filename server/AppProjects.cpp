#include "App.h"
#include "Log.h"
#include "Util.h"

// Projects: create/edit/delete, membership, invites and invite codes,
// public projects, opening and closing, import and export.

static const size_t MAX_IMPORT_FILES = 1000;
static const size_t MAX_IMPORT_FILE_BYTES = 2 * 1024 * 1024;
static const size_t MAX_IMPORT_TOTAL_BYTES = 40 * 1024 * 1024;

static std::string checkProjectName(const std::string& raw)
{
	std::string name = Util::trim(raw);
	size_t length = Util::utf16Length(name);
	if (length < 1 || length > 50) fail("bad_request", "Project names are 1-50 characters.");
	for (char c : name)
		if ((unsigned char)c < 0x20) fail("bad_request", "Project names can't contain control characters.");
	return name;
}

static std::string checkVisibility(const std::string& v)
{
	if (v != "public" && v != "private") fail("bad_request", "Visibility must be public or private.");
	return v;
}

Json App::hProjectList(Session& s, const Json&)
{
	long long me = s.uid();
	std::vector<long long> ids;
	Json invites = Json::array();
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT p.id FROM projects p JOIN project_members m ON m.project_id = p.id "
			"WHERE m.user_id = ?1 ORDER BY p.updated_at DESC", me);
		while (st.next()) ids.push_back(st.i(0));
		auto inv = _db.query("SELECT p.id, p.name, i.role, IFNULL(i.invited_by, 0) FROM project_invites i JOIN projects p ON p.id = i.project_id "
			"WHERE i.user_id = ?1 ORDER BY i.created_at DESC", me);
		while (inv.next())
		{
			Json invite = Json::object({ { "projectId", inv.i(0) }, { "name", inv.s(1) }, { "role", inv.s(2) } });
			if (inv.i(3)) invite.set("from", userJson(inv.i(3)));
			invites.push(invite);
		}
	}
	Json projects = Json::array();
	for (long long id : ids)
	{
		if (auto p = project(id)) projects.push(projectSummaryJson(*p, me));
	}
	return Json::object({ { "projects", projects }, { "invites", invites } });
}

Json App::hProjectGet(Session& s, const Json& req)
{
	ProjectRow p = requireProject(reqId(req, "id"));
	accessRole(s, p);
	return Json::object({ { "project", projectDetailJson(p, s.uid()) } });
}

// Throws unless every invite names an existing friend and a valid role.
void App::checkInvites(Session& s, const Json& invites)
{
	if (invites.isNull()) return;
	if (!invites.isArray() || invites.size() > 50) fail("bad_request", "Invalid invite list.");
	long long me = s.uid();
	for (const Json& invite : invites.items())
	{
		std::string name = Util::trim(reqString(invite, "name", 100));
		std::string role = optString(invite, "role", 20);
		if (!role.empty() && role != "admin" && role != "member") fail("bad_request", "Invites are for admins or members.");
		auto userId = findUserId(name);
		if (!userId) fail("not_found", "There's no user called '" + name + "'.");
		if (*userId != me && !areFriends(me, *userId))
			fail("forbidden", "You can only invite friends directly. Share the project's invite code with " + name + " instead.");
	}
}

void App::inviteUsers(Session& s, const ProjectRow& p, const Json& invites)
{
	checkInvites(s, invites);
	if (invites.isNull()) return;
	if (!invites.isArray() || invites.size() > 50) fail("bad_request", "Invalid invite list.");
	long long me = s.uid();
	std::vector<long long> invited;
	for (const Json& invite : invites.items())
	{
		std::string name = Util::trim(reqString(invite, "name", 100));
		std::string role = optString(invite, "role", 20);
		if (role.empty()) role = "member";
		if (role != "admin" && role != "member") fail("bad_request", "Invites are for admins or members.");
		long long userId;
		{
			auto lock = _db.lock();
			userId = _db.scalar(0, "SELECT id FROM users WHERE name = ?1", name);
			if (!userId) fail("not_found", "There's no user called '" + name + "'.");
			if (_db.scalar(0, "SELECT 1 FROM project_members WHERE project_id = ?1 AND user_id = ?2", p.id, userId)) continue;
		}
		if (!areFriends(me, userId))
			fail("forbidden", "You can only invite friends directly. Share the project's invite code with " + name + " instead.");
		{
			auto lock = _db.lock();
			_db.exec("INSERT OR REPLACE INTO project_invites (project_id, user_id, role, invited_by, created_at) VALUES (?1, ?2, ?3, ?4, ?5)",
				p.id, userId, role, me, nowMs());
		}
		removeInbox(userId, "projectInvite", std::nullopt, p.id);
		addInbox(userId, "projectInvite", "invited you to join " + p.name, me, p.id);
		invited.push_back(userId);
	}
	for (long long id : invited) sendToUser(id, Json::object({ { "t", "ev.projects" } }));
}

Json App::hProjectCreate(Session& s, const Json& req)
{
	std::string name = checkProjectName(reqString(req, "name", 400));
	std::string language = Util::trim(optString(req, "language", 30));
	std::string description = Util::trim(optString(req, "description", 2000));
	if (Util::utf16Length(description) > 500) fail("bad_request", "The description can be up to 500 characters.");
	std::string visibility = req["visibility"].isNull() ? "private" : checkVisibility(reqString(req, "visibility", 20));
	long long me = s.uid();
	checkInvites(s, req["invites"]); // before creating anything
	long long id;
	{
		auto lock = _db.lock();
		if (_db.scalar(0, "SELECT 1 FROM projects WHERE name = ?1", name)) fail("conflict", "A project with this name already exists.");
		Transaction tx(_db);
		long long now = nowMs();
		_db.exec("INSERT INTO projects (name, language, description, visibility, owner_id, created_at, updated_at) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?6)",
			name, language, description, visibility, me, now);
		id = _db.lastInsertId();
		_db.exec("INSERT INTO project_members (project_id, user_id, role, joined_at) VALUES (?1, ?2, 'owner', ?3)", id, me, now);
		tx.commit();
	}
	ProjectRow p = requireProject(id);
	inviteUsers(s, p, req["invites"]);
	LOG_INFO(s.name << " created project '" << name << "' (" << id << ")");
	addActivity(id, me, "project.created", "created the project");
	sendToUser(me, Json::object({ { "t", "ev.projects" } }));
	return Json::object({ { "project", projectDetailJson(p, me) } });
}

Json App::hProjectUpdate(Session& s, const Json& req)
{
	long long id = reqId(req, "id");
	std::string myRole = requireProjectAdmin(s, id);
	ProjectRow p = requireProject(id);
	long long me = s.uid();
	std::vector<std::string> changes;

	{
		auto lock = _db.lock();
		Transaction tx(_db);
		if (req.has("name"))
		{
			std::string name = checkProjectName(reqString(req, "name", 400));
			if (name != p.name)
			{
				if (_db.scalar(0, "SELECT 1 FROM projects WHERE name = ?1 AND id <> ?2", name, id))
					fail("conflict", "A project with this name already exists.");
				_db.exec("UPDATE projects SET name = ?1 WHERE id = ?2", name, id);
				changes.push_back("renamed the project to " + name);
			}
		}
		if (req.has("description"))
		{
			std::string description = Util::trim(reqString(req, "description", 2000, true));
			if (Util::utf16Length(description) > 500) fail("bad_request", "The description can be up to 500 characters.");
			_db.exec("UPDATE projects SET description = ?1 WHERE id = ?2", description, id);
		}
		if (req.has("visibility"))
		{
			std::string visibility = checkVisibility(reqString(req, "visibility", 20));
			if (visibility != p.visibility)
			{
				_db.exec("UPDATE projects SET visibility = ?1 WHERE id = ?2", visibility, id);
				changes.push_back(visibility == "public" ? "made the project public" : "made the project private");
			}
		}
		for (const Json& change : req["roles"].items())
		{
			long long userId = reqId(change, "userId");
			std::string role = reqString(change, "role", 20);
			std::string current = _db.text("SELECT role FROM project_members WHERE project_id = ?1 AND user_id = ?2", id, userId).value_or("");
			if (current.empty() || current == role) continue;
			if (role == "owner")
			{
				if (myRole != "owner") fail("forbidden", "Only the owner can hand over the project.");
				_db.exec("UPDATE project_members SET role = 'admin' WHERE project_id = ?1 AND user_id = ?2", id, me);
				_db.exec("UPDATE project_members SET role = 'owner' WHERE project_id = ?1 AND user_id = ?2", id, userId);
				_db.exec("UPDATE projects SET owner_id = ?1 WHERE id = ?2", userId, id);
				myRole = "admin";
				changes.push_back("made " + userName(userId) + " the owner");
			}
			else if (role == "admin" || role == "member")
			{
				if (current == "owner") fail("forbidden", "The owner's role can't be changed. Hand over ownership first.");
				if (current == "admin" && myRole != "owner") fail("forbidden", "Only the owner can change an admin's role.");
				_db.exec("UPDATE project_members SET role = ?1 WHERE project_id = ?2 AND user_id = ?3", role, id, userId);
			}
			else
			{
				fail("bad_request", "Unknown role '" + role + "'.");
			}
		}
		for (const Json& cancel : req["cancelInvites"].items())
		{
			if (cancel.isInt()) _db.exec("DELETE FROM project_invites WHERE project_id = ?1 AND user_id = ?2", id, cancel.asInt());
		}
		tx.commit();
	}
	for (const Json& cancel : req["cancelInvites"].items())
	{
		if (!cancel.isInt()) continue;
		removeInbox(cancel.asInt(), "projectInvite", std::nullopt, id);
		sendToUser(cancel.asInt(), Json::object({ { "t", "ev.projects" } }));
	}
	for (const Json& removed : req["remove"].items())
	{
		if (!removed.isInt() || removed.asInt() == me) continue;
		std::string role = roleOf(id, removed.asInt());
		if (role.empty()) continue;
		if (role == "owner") fail("forbidden", "The owner can't be removed.");
		if (role == "admin" && myRole != "owner") fail("forbidden", "Only the owner can remove an admin.");
		std::string name = userName(removed.asInt());
		removeMember(p, removed.asInt(), "You were removed from " + p.name + ".");
		addInbox(removed.asInt(), "info", "removed you from " + p.name, me, 0);
		changes.push_back("removed " + name);
	}
	p = requireProject(id);
	inviteUsers(s, p, req["invites"]);

	for (const std::string& change : changes) addActivity(id, me, "project.updated", change);
	touchProject(id);
	notifyProjectChanged(id);
	return Json::object({ { "project", projectDetailJson(requireProject(id), me) } });
}

// Removes a member everywhere: membership, task assignments, open sessions.
void App::removeMember(const ProjectRow& p, long long userId, const std::string& reason)
{
	{
		auto lock = _db.lock();
		Transaction tx(_db);
		_db.exec("DELETE FROM project_members WHERE project_id = ?1 AND user_id = ?2", p.id, userId);
		_db.exec("DELETE FROM task_assignees WHERE user_id = ?1 AND task_id IN (SELECT id FROM tasks WHERE project_id = ?2)", userId, p.id);
		tx.commit();
	}
	for (auto& session : sessionsOf(userId))
	{
		bool open;
		{
			std::lock_guard<std::mutex> lock(session->m);
			open = session->openProjects.count(p.id) > 0;
		}
		if (!open) continue;
		// A public project stays readable; otherwise close it for them.
		closeProjectFor(*session, p.id);
		session->send(Json::object({ { "t", "ev.project.removed" }, { "id", p.id }, { "reason", reason } }));
	}
	sendToUser(userId, Json::object({ { "t", "ev.projects" } }));
	notifyProjectChanged(p.id);
}

void App::deleteProjectNow(const ProjectRow& p, const std::string& reason)
{
	std::set<long long> members = memberIds(p.id);
	RoomPtr r = room(p.id, false);
	if (r)
	{
		std::vector<SessionPtr> sessions;
		{
			std::lock_guard<std::mutex> lock(r->m);
			for (auto& s : r->sessions) sessions.push_back(s.second);
		}
		for (auto& s : sessions)
		{
			closeProjectFor(*s, p.id);
			s->send(Json::object({ { "t", "ev.project.deleted" }, { "id", p.id }, { "reason", reason } }));
		}
		std::lock_guard<std::mutex> lock(r->m);
		r->docs.clear(); // nothing left to save
	}
	{
		auto lock = _db.lock();
		_db.exec("DELETE FROM projects WHERE id = ?1", p.id); // everything else cascades
	}
	LOG_INFO("Project '" << p.name << "' (" << p.id << ") deleted: " << reason);
	sendToUsers(members, Json::object({ { "t", "ev.projects" } }));
}

Json App::hProjectDelete(Session& s, const Json& req)
{
	long long id = reqId(req, "id");
	if (requireMember(s, id) != "owner") fail("forbidden", "Only the project's owner can delete it.");
	ProjectRow p = requireProject(id);
	long long me = s.uid();
	std::set<long long> members = memberIds(id);
	deleteProjectNow(p, s.name + " deleted the project.");
	for (long long member : members)
		if (member != me) addInbox(member, "info", "deleted the project " + p.name, me, 0);
	return Json::object();
}

Json App::hProjectLeave(Session& s, const Json& req)
{
	long long id = reqId(req, "id");
	std::string role = requireMember(s, id);
	ProjectRow p = requireProject(id);
	long long me = s.uid();
	if (role == "owner")
	{
		// Hand the project to an admin (or else the longest-standing member).
		long long next;
		{
			auto lock = _db.lock();
			next = _db.scalar(0, "SELECT user_id FROM project_members WHERE project_id = ?1 AND user_id <> ?2 "
				"ORDER BY CASE role WHEN 'admin' THEN 0 ELSE 1 END, joined_at LIMIT 1", id, me);
			if (next)
			{
				_db.exec("UPDATE project_members SET role = 'owner' WHERE project_id = ?1 AND user_id = ?2", id, next);
				_db.exec("UPDATE projects SET owner_id = ?1 WHERE id = ?2", next, id);
			}
		}
		if (!next)
		{
			deleteProjectNow(p, "The last member left the project.");
			return Json::object({ { "deleted", true } });
		}
		addInbox(next, "info", "left " + p.name + " and made you its owner", me, id);
	}
	removeMember(p, me, "You left " + p.name + ".");
	addActivity(id, me, "member.left", "left the project");
	return Json::object();
}

Json App::hProjectRespondInvite(Session& s, const Json& req)
{
	long long id = reqId(req, "id");
	bool accept = reqBool(req, "accept");
	long long me = s.uid();
	ProjectRow p = requireProject(id);
	std::string role;
	long long inviter = 0;
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT role, IFNULL(invited_by, 0) FROM project_invites WHERE project_id = ?1 AND user_id = ?2", id, me);
		if (!st.next()) fail("not_found", "That invite was withdrawn.");
		role = st.s(0);
		inviter = st.i(1);
		Transaction tx(_db);
		_db.exec("DELETE FROM project_invites WHERE project_id = ?1 AND user_id = ?2", id, me);
		if (accept)
			_db.exec("INSERT OR IGNORE INTO project_members (project_id, user_id, role, joined_at) VALUES (?1, ?2, ?3, ?4)", id, me, role, nowMs());
		_db.exec("DELETE FROM join_requests WHERE project_id = ?1 AND user_id = ?2", id, me);
		tx.commit();
	}
	removeInbox(me, "projectInvite", std::nullopt, id);
	if (accept)
	{
		addActivity(id, me, "member.joined", "joined the project");
		if (inviter) addInbox(inviter, "info", "accepted your invite to " + p.name, me, id);
		notifyProjectChanged(id);
		notifyPresence(me);
	}
	sendToUser(me, Json::object({ { "t", "ev.projects" } }));
	return Json::object();
}

Json App::hProjectInviteCode(Session& s, const Json& req)
{
	long long id = reqId(req, "id");
	requireProjectAdmin(s, id);
	std::string action = reqString(req, "action", 20);
	std::string code;
	{
		auto lock = _db.lock();
		if (action == "create")
		{
			do code = Util::randomCode(8);
			while (_db.scalar(0, "SELECT 1 FROM projects WHERE invite_code = ?1", code));
			_db.exec("UPDATE projects SET invite_code = ?1, invite_auto_approve = ?2 WHERE id = ?3",
				code, optBool(req, "autoApprove", false), id);
		}
		else if (action == "revoke")
		{
			_db.exec("UPDATE projects SET invite_code = NULL WHERE id = ?1", id);
		}
		else if (action == "settings")
		{
			_db.exec("UPDATE projects SET invite_auto_approve = ?1 WHERE id = ?2", optBool(req, "autoApprove", false), id);
			code = _db.text("SELECT invite_code FROM projects WHERE id = ?1", id).value_or("");
		}
		else
		{
			fail("bad_request", "Unknown action.");
		}
	}
	notifyProjectChanged(id);
	return Json::object({ { "inviteCode", code } });
}

Json App::hProjectJoin(Session& s, const Json& req)
{
	std::string code = Util::trim(reqString(req, "code", 200));
	// Accept a pasted link too: githubdemo://join/CODE
	size_t slash = code.find_last_of('/');
	if (slash != std::string::npos) code = code.substr(slash + 1);
	for (char& c : code) c = (char)toupper((unsigned char)c);
	long long me = s.uid();

	long long id;
	bool autoApprove;
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT id, invite_auto_approve FROM projects WHERE invite_code = ?1", code);
		if (!st.next()) fail("not_found", "That invite code isn't valid (it may have been replaced).");
		id = st.i(0);
		autoApprove = st.i(1) != 0;
	}
	ProjectRow p = requireProject(id);
	if (!roleOf(id, me).empty())
		return Json::object({ { "status", "joined" }, { "projectId", id }, { "name", p.name } });

	if (autoApprove)
	{
		{
			auto lock = _db.lock();
			Transaction tx(_db);
			_db.exec("INSERT OR IGNORE INTO project_members (project_id, user_id, role, joined_at) VALUES (?1, ?2, 'member', ?3)", id, me, nowMs());
			_db.exec("DELETE FROM project_invites WHERE project_id = ?1 AND user_id = ?2", id, me);
			_db.exec("DELETE FROM join_requests WHERE project_id = ?1 AND user_id = ?2", id, me);
			tx.commit();
		}
		removeInbox(me, "projectInvite", std::nullopt, id);
		addActivity(id, me, "member.joined", "joined the project with an invite code");
		notifyProjectChanged(id);
		sendToUser(me, Json::object({ { "t", "ev.projects" } }));
		return Json::object({ { "status", "joined" }, { "projectId", id }, { "name", p.name } });
	}

	bool alreadyAsked;
	std::vector<long long> admins;
	{
		auto lock = _db.lock();
		alreadyAsked = _db.scalar(0, "SELECT 1 FROM join_requests WHERE project_id = ?1 AND user_id = ?2", id, me) == 1;
		if (!alreadyAsked)
			_db.exec("INSERT INTO join_requests (project_id, user_id, created_at) VALUES (?1, ?2, ?3)", id, me, nowMs());
		auto st = _db.query("SELECT user_id FROM project_members WHERE project_id = ?1 AND role IN ('owner', 'admin')", id);
		while (st.next()) admins.push_back(st.i(0));
	}
	if (!alreadyAsked)
		for (long long admin : admins) addInbox(admin, "joinRequest", "asked to join " + p.name, me, id);
	return Json::object({ { "status", "requested" }, { "projectId", id }, { "name", p.name } });
}

Json App::hProjectRespondJoin(Session& s, const Json& req)
{
	long long id = reqId(req, "projectId");
	long long userId = reqId(req, "userId");
	bool accept = reqBool(req, "accept");
	requireProjectAdmin(s, id);
	ProjectRow p = requireProject(id);
	std::vector<long long> admins;
	{
		auto lock = _db.lock();
		Transaction tx(_db);
		if (_db.exec("DELETE FROM join_requests WHERE project_id = ?1 AND user_id = ?2", id, userId) == 0)
			fail("not_found", "That request was already handled.");
		if (accept)
			_db.exec("INSERT OR IGNORE INTO project_members (project_id, user_id, role, joined_at) VALUES (?1, ?2, 'member', ?3)", id, userId, nowMs());
		tx.commit();
		auto st = _db.query("SELECT user_id FROM project_members WHERE project_id = ?1 AND role IN ('owner', 'admin')", id);
		while (st.next()) admins.push_back(st.i(0));
	}
	for (long long admin : admins) removeInbox(admin, "joinRequest", userId, id);
	addInbox(userId, "info", (accept ? "approved your request to join " : "declined your request to join ") + p.name, s.uid(), accept ? id : 0);
	if (accept)
	{
		addActivity(id, userId, "member.joined", "joined the project");
		notifyProjectChanged(id);
		sendToUser(userId, Json::object({ { "t", "ev.projects" } }));
	}
	return Json::object();
}

Json App::hProjectPublic(Session& s, const Json& req)
{
	std::string query = Util::trim(optString(req, "query", 100));
	std::string pattern = "%";
	for (char c : query)
	{
		if (c == '%' || c == '_' || c == '\\') pattern += '\\';
		pattern += c;
	}
	pattern += "%";
	std::vector<long long> ids;
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT id FROM projects WHERE visibility = 'public' AND (name LIKE ?1 ESCAPE '\\' OR description LIKE ?1 ESCAPE '\\') "
			"ORDER BY updated_at DESC LIMIT 50", pattern);
		while (st.next()) ids.push_back(st.i(0));
	}
	Json projects = Json::array();
	for (long long id : ids)
		if (auto p = project(id)) projects.push(projectSummaryJson(*p, s.uid()));
	return Json::object({ { "projects", projects } });
}

Json App::hProjectOpen(Session& s, const Json& req)
{
	ProjectRow p = requireProject(reqId(req, "id"));
	std::string role = accessRole(s, p);
	long long me = s.uid();
	SessionPtr self = sessionFor(s.conn);
	RoomPtr r = room(p.id, true);
	{
		std::lock_guard<std::mutex> lock(r->m);
		r->sessions[s.id] = self;
	}
	{
		std::lock_guard<std::mutex> lock(s.m);
		s.openProjects[p.id] = role;
		s.activeProject = p.id;
		s.activeFile = 0;
	}
	Json folders;
	Json files = treeJson(p.id, folders);
	notifyPresence(me);
	return Json::object({
		{ "project", projectDetailJson(p, me) }, { "files", files }, { "folders", folders },
		{ "readOnly", role == "viewer" }, { "role", role },
	});
}

Json App::hProjectClose(Session& s, const Json& req)
{
	closeProjectFor(s, reqId(req, "id"));
	return Json::object();
}

Json App::hProjectExport(Session& s, const Json& req)
{
	ProjectRow p = requireProject(reqId(req, "id"));
	accessRole(s, p);
	Json folders;
	Json tree = treeJson(p.id, folders);
	// Files being edited right now: take the live text.
	std::map<long long, std::string> live;
	if (RoomPtr r = room(p.id, false))
	{
		std::lock_guard<std::mutex> lock(r->m);
		for (auto& d : r->docs) live[d.first] = wideToUtf8(d.second.text);
	}
	Json files = Json::array();
	auto lock = _db.lock();
	for (const Json& f : tree.items())
	{
		long long id = f["id"].asInt();
		std::string text = live.count(id) ? live[id] : _db.text("SELECT content FROM files WHERE id = ?1", id).value_or("");
		files.push(Json::object({ { "path", f["path"] }, { "text", text } }));
	}
	return Json::object({ { "name", p.name }, { "files", files }, { "folders", folders } });
}

Json App::hProjectImport(Session& s, const Json& req)
{
	long long id = reqId(req, "id");
	requireWriter(s, id);
	const Json& files = req["files"];
	if (!files.isArray()) fail("bad_request", "Missing files.");
	if (files.size() > MAX_IMPORT_FILES) fail("bad_request", "At most 1000 files can be imported at once.");
	size_t total = 0;
	long long me = s.uid();
	int created = 0, skipped = 0;
	{
		auto lock = _db.lock();
		Transaction tx(_db);
		long long now = nowMs();
		for (const Json& folder : req["folders"].items())
		{
			if (!folder.isString()) continue;
			std::string path = normalizePath(folder.asString());
			_db.exec("INSERT OR IGNORE INTO folders (project_id, path) VALUES (?1, ?2)", id, path);
		}
		for (const Json& f : files.items())
		{
			std::string path = normalizePath(reqString(f, "path", 1000));
			const std::string& text = reqString(f, "text", MAX_IMPORT_FILE_BYTES, true);
			total += text.size();
			if (total > MAX_IMPORT_TOTAL_BYTES) fail("bad_request", "The import is too large (40 MB at most).");
			if (_db.scalar(0, "SELECT 1 FROM files WHERE project_id = ?1 AND path = ?2", id, path))
			{
				skipped++;
				continue;
			}
			_db.exec("INSERT INTO files (project_id, path, content, created_by, created_at, updated_at) VALUES (?1, ?2, ?3, ?4, ?5, ?5)",
				id, path, text, me, now);
			ensureParentFolders(id, path);
			created++;
		}
		tx.commit();
	}
	if (created)
	{
		addActivity(id, me, "files.imported", "imported " + std::to_string(created) + " file(s)");
		broadcastTree(id);
	}
	return Json::object({ { "created", created }, { "skipped", skipped } });
}
