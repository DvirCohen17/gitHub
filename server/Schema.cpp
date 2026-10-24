#include "Schema.h"
#include "Log.h"
#include <map>
#include <set>

static const int SCHEMA_VERSION = 1;

static const char* SCHEMA_SQL = R"SQL(
CREATE TABLE IF NOT EXISTS users (
	id          INTEGER PRIMARY KEY,
	name        TEXT NOT NULL UNIQUE COLLATE NOCASE,
	email       TEXT UNIQUE COLLATE NOCASE,
	password    TEXT NOT NULL,
	bio         TEXT NOT NULL DEFAULT '',
	avatar      TEXT NOT NULL DEFAULT '',
	is_admin    INTEGER NOT NULL DEFAULT 0,
	banned      INTEGER NOT NULL DEFAULT 0,
	ban_reason  TEXT NOT NULL DEFAULT '',
	created_at  INTEGER NOT NULL,
	last_seen   INTEGER
);

CREATE TABLE IF NOT EXISTS avatars (
	hash        TEXT PRIMARY KEY,
	png         BLOB NOT NULL,
	created_at  INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS friendships (
	user_a      INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
	user_b      INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
	created_at  INTEGER NOT NULL,
	PRIMARY KEY (user_a, user_b),
	CHECK (user_a < user_b)
);
CREATE INDEX IF NOT EXISTS friendships_b ON friendships(user_b);

CREATE TABLE IF NOT EXISTS friend_requests (
	from_id     INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
	to_id       INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
	created_at  INTEGER NOT NULL,
	PRIMARY KEY (from_id, to_id)
);
CREATE INDEX IF NOT EXISTS friend_requests_to ON friend_requests(to_id);

CREATE TABLE IF NOT EXISTS projects (
	id                  INTEGER PRIMARY KEY,
	name                TEXT NOT NULL UNIQUE COLLATE NOCASE,
	language            TEXT NOT NULL DEFAULT '',
	description         TEXT NOT NULL DEFAULT '',
	visibility          TEXT NOT NULL DEFAULT 'private' CHECK (visibility IN ('private', 'public')),
	owner_id            INTEGER REFERENCES users(id) ON DELETE SET NULL,
	invite_code         TEXT UNIQUE,
	invite_auto_approve INTEGER NOT NULL DEFAULT 0,
	created_at          INTEGER NOT NULL,
	updated_at          INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS project_members (
	project_id  INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
	user_id     INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
	role        TEXT NOT NULL CHECK (role IN ('owner', 'admin', 'member')),
	joined_at   INTEGER NOT NULL,
	PRIMARY KEY (project_id, user_id)
);
CREATE INDEX IF NOT EXISTS project_members_user ON project_members(user_id);

CREATE TABLE IF NOT EXISTS project_invites (
	project_id  INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
	user_id     INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
	role        TEXT NOT NULL CHECK (role IN ('admin', 'member')),
	invited_by  INTEGER REFERENCES users(id) ON DELETE SET NULL,
	created_at  INTEGER NOT NULL,
	PRIMARY KEY (project_id, user_id)
);
CREATE INDEX IF NOT EXISTS project_invites_user ON project_invites(user_id);

CREATE TABLE IF NOT EXISTS join_requests (
	project_id  INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
	user_id     INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
	created_at  INTEGER NOT NULL,
	PRIMARY KEY (project_id, user_id)
);

CREATE TABLE IF NOT EXISTS folders (
	project_id  INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
	path        TEXT NOT NULL,
	PRIMARY KEY (project_id, path)
);

CREATE TABLE IF NOT EXISTS files (
	id          INTEGER PRIMARY KEY,
	project_id  INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
	path        TEXT NOT NULL,
	content     TEXT NOT NULL DEFAULT '',
	created_by  INTEGER REFERENCES users(id) ON DELETE SET NULL,
	created_at  INTEGER NOT NULL,
	updated_at  INTEGER NOT NULL,
	UNIQUE (project_id, path)
);

CREATE TABLE IF NOT EXISTS file_versions (
	id          INTEGER PRIMARY KEY,
	file_id     INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
	content     TEXT NOT NULL,
	label       TEXT NOT NULL DEFAULT '',
	user_id     INTEGER REFERENCES users(id) ON DELETE SET NULL,
	created_at  INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS file_versions_file ON file_versions(file_id, id);

CREATE TABLE IF NOT EXISTS chat_messages (
	id          INTEGER PRIMARY KEY,
	project_id  INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
	user_id     INTEGER REFERENCES users(id) ON DELETE SET NULL,
	text        TEXT NOT NULL,
	created_at  INTEGER NOT NULL,
	edited_at   INTEGER,
	deleted     INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS chat_messages_project ON chat_messages(project_id, id);

CREATE TABLE IF NOT EXISTS tasks (
	id           INTEGER PRIMARY KEY,
	project_id   INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
	text         TEXT NOT NULL,
	due          TEXT NOT NULL,
	done         INTEGER NOT NULL DEFAULT 0,
	created_by   INTEGER REFERENCES users(id) ON DELETE SET NULL,
	created_at   INTEGER NOT NULL,
	completed_at INTEGER
);
CREATE INDEX IF NOT EXISTS tasks_project ON tasks(project_id);

CREATE TABLE IF NOT EXISTS task_assignees (
	task_id     INTEGER NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
	user_id     INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
	PRIMARY KEY (task_id, user_id)
);
CREATE INDEX IF NOT EXISTS task_assignees_user ON task_assignees(user_id);

CREATE TABLE IF NOT EXISTS inbox (
	id          INTEGER PRIMARY KEY,
	user_id     INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
	kind        TEXT NOT NULL,
	text        TEXT NOT NULL,
	from_id     INTEGER REFERENCES users(id) ON DELETE SET NULL,
	project_id  INTEGER REFERENCES projects(id) ON DELETE CASCADE,
	read        INTEGER NOT NULL DEFAULT 0,
	created_at  INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS inbox_user ON inbox(user_id, id);

CREATE TABLE IF NOT EXISTS activity (
	id          INTEGER PRIMARY KEY,
	project_id  INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
	user_id     INTEGER REFERENCES users(id) ON DELETE SET NULL,
	kind        TEXT NOT NULL,
	text        TEXT NOT NULL,
	created_at  INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS activity_project ON activity(project_id, id);

CREATE TABLE IF NOT EXISTS settings (
	key         TEXT PRIMARY KEY,
	value       TEXT NOT NULL
);
)SQL";

void Schema::create(Db& db)
{
	auto lock = db.lock();
	long long version = db.scalar(0, "PRAGMA user_version");
	if (version > SCHEMA_VERSION)
	{
		throw DbError("The database was written by a newer server (schema " + std::to_string(version) + ").");
	}
	db.execScript(SCHEMA_SQL);
	db.execScript(("PRAGMA user_version = " + std::to_string(SCHEMA_VERSION)).c_str());
}

// ---- Import from the v1 server ---------------------------------------------

namespace
{
	// v1 stored lists as runs of <5-digit byte length><text>.
	std::vector<std::string> splitLengthPrefixed(const std::string& s)
	{
		std::vector<std::string> out;
		size_t i = 0;
		while (i + 5 <= s.size())
		{
			size_t length = 0;
			try { length = std::stoul(s.substr(i, 5)); }
			catch (...) { break; }
			i += 5;
			if (i + length > s.size()) break;
			out.push_back(s.substr(i, length));
			i += length;
		}
		return out;
	}

	std::string trim(const std::string& s)
	{
		size_t a = s.find_first_not_of(" \t\r\n");
		if (a == std::string::npos) return "";
		size_t b = s.find_last_not_of(" \t\r\n");
		return s.substr(a, b - a + 1);
	}

	// Due dates were yyyy-MM-dd, or MM/dd/yyyy in older clients.
	std::string normalizeDate(const std::string& date)
	{
		int y, m, d;
		if (sscanf_s(date.c_str(), "%d-%d-%d", &y, &m, &d) == 3 && y > 1900)
		{
		}
		else if (sscanf_s(date.c_str(), "%d/%d/%d", &m, &d, &y) == 3 && y > 1900)
		{
		}
		else
		{
			return date;
		}
		char buf[16];
		snprintf(buf, sizeof(buf), "%04d-%02d-%02d", y, m, d);
		return buf;
	}

	std::string uniqueName(Db& db, const char* existsSql, const std::string& wanted, long long id)
	{
		if (db.scalar(0, existsSql, wanted) == 0) return wanted;
		std::string name = wanted + "_" + std::to_string(id);
		LOG_WARN("Import: '" << wanted << "' clashes with an existing name (names are now case-insensitive); renamed to '" << name << "'");
		return name;
	}
}

void Schema::importLegacy(Db& db, const std::string& legacyPath)
{
	Db old;
	old.open(legacyPath, true);
	auto oldLock = old.lock();
	Transaction tx(db);
	long long now = nowMs();

	std::map<std::string, long long> userIds; // name -> id
	std::set<long long> knownUsers;

	// Users and bios
	if (old.tableExists("users"))
	{
		std::map<long long, std::string> bios;
		if (old.tableExists("ProfileInfo"))
		{
			auto st = old.query("SELECT userId, Bio FROM ProfileInfo");
			while (st.next()) bios[st.i(0)] = st.s(1);
		}
		auto st = old.query("SELECT id, user_name, password, mail FROM users ORDER BY id");
		while (st.next())
		{
			long long id = st.i(0);
			std::string name = uniqueName(db, "SELECT 1 FROM users WHERE name = ?1", st.s(1), id);
			std::string email = trim(st.s(3));
			std::optional<std::string> emailValue;
			if (!email.empty() && db.scalar(0, "SELECT 1 FROM users WHERE email = ?1", email) == 0)
				emailValue = email;
			else if (!email.empty())
				LOG_WARN("Import: email of '" << name << "' is used by another account; left empty");
			Stmt insert = db.query("INSERT INTO users (id, name, email, password, bio, created_at) VALUES (?1, ?2, ?3, ?4, ?5, ?6)",
				id, name, nullptr, st.s(2), bios[id], now);
			if (emailValue) insert.bind(3, *emailValue);
			insert.run();
			userIds[st.s(1)] = id;
			knownUsers.insert(id);
		}
	}
	auto userId = [&](const std::string& name) -> std::optional<long long> {
		auto it = userIds.find(name);
		if (it == userIds.end()) return std::nullopt;
		return it->second;
	};

	// Friends: UserFriends.friendsList is a length-prefixed list of names
	if (old.tableExists("UserFriends"))
	{
		auto st = old.query("SELECT userId, friendsList FROM UserFriends");
		while (st.next())
		{
			long long a = st.i(0);
			for (const std::string& name : splitLengthPrefixed(st.s(1)))
			{
				auto b = userId(name);
				if (!b || *b == a || !knownUsers.count(a)) continue;
				db.exec("INSERT OR IGNORE INTO friendships (user_a, user_b, created_at) VALUES (?1, ?2, ?3)",
					std::min(a, *b), std::max(a, *b), now);
			}
		}
	}
	// FriendReq: userId = sender, friendReqId = receiver
	if (old.tableExists("FriendReq"))
	{
		auto st = old.query("SELECT userId, friendReqId FROM FriendReq");
		while (st.next())
		{
			long long from = st.i(0), to = st.i(1);
			if (from == to || !knownUsers.count(from) || !knownUsers.count(to)) continue;
			if (db.scalar(0, "SELECT 1 FROM friendships WHERE user_a = ?1 AND user_b = ?2", std::min(from, to), std::max(from, to))) continue;
			db.exec("INSERT OR IGNORE INTO friend_requests (from_id, to_id, created_at) VALUES (?1, ?2, ?3)", from, to, now);
		}
	}

	// Projects, members, invites
	std::set<long long> knownProjects;
	if (old.tableExists("UserProjects"))
	{
		auto st = old.query("SELECT Id, ProjectName, codeLan, creatorId FROM UserProjects ORDER BY Id");
		while (st.next())
		{
			long long id = st.i(0);
			std::string name = trim(st.s(1));
			if (name.empty()) name = "Project " + std::to_string(id);
			name = uniqueName(db, "SELECT 1 FROM projects WHERE name = ?1", name, id);
			std::optional<long long> owner;
			if (knownUsers.count(st.i(3))) owner = st.i(3);
			db.exec("INSERT INTO projects (id, name, language, owner_id, created_at, updated_at) VALUES (?1, ?2, ?3, ?4, ?5, ?5)",
				id, name, st.s(2), owner, now);
			if (owner)
			{
				db.exec("INSERT INTO project_members (project_id, user_id, role, joined_at) VALUES (?1, ?2, 'owner', ?3)", id, *owner, now);
			}
			knownProjects.insert(id);
		}
	}
	if (old.tableExists("ProjectPermission"))
	{
		auto st = old.query("SELECT projectId, userId, role FROM ProjectPermission");
		while (st.next())
		{
			long long project = st.i(0), user = st.i(1);
			if (!knownProjects.count(project) || !knownUsers.count(user)) continue;
			std::string role = st.s(2) == "admin" ? "admin" : "member";
			db.exec("INSERT OR IGNORE INTO project_members (project_id, user_id, role, joined_at) VALUES (?1, ?2, ?3, ?4)",
				project, user, role, now);
		}
	}
	if (old.tableExists("ProjectJoinInvite"))
	{
		auto st = old.query("SELECT projectId, userId, role FROM ProjectJoinInvite");
		while (st.next())
		{
			long long project = st.i(0), user = st.i(1);
			if (!knownProjects.count(project) || !knownUsers.count(user)) continue;
			if (db.scalar(0, "SELECT 1 FROM project_members WHERE project_id = ?1 AND user_id = ?2", project, user)) continue;
			std::string role = st.s(2) == "admin" ? "admin" : "member";
			db.exec("INSERT OR IGNORE INTO project_invites (project_id, user_id, role, created_at) VALUES (?1, ?2, ?3, ?4)",
				project, user, role, now);
		}
	}

	// Files and their contents
	if (old.tableExists("Files"))
	{
		std::map<long long, std::string> contents;
		if (old.tableExists("FilesData"))
		{
			auto st = old.query("SELECT FileId, Data FROM FilesData ORDER BY Id");
			while (st.next()) contents[st.i(0)] = st.s(1);
		}
		auto st = old.query("SELECT fileId, fileName, ProjectId, creatorId FROM Files ORDER BY fileId");
		while (st.next())
		{
			long long id = st.i(0), project = st.i(2);
			if (!knownProjects.count(project)) continue;
			std::string path = trim(st.s(1));
			for (char& c : path) if (c == '/' || c == '\\') c = '_';
			if (path.empty()) path = "file" + std::to_string(id);
			if (db.scalar(0, "SELECT 1 FROM files WHERE project_id = ?1 AND path = ?2", project, path))
				path = std::to_string(id) + "_" + path;
			std::optional<long long> creator;
			if (knownUsers.count(st.i(3))) creator = st.i(3);
			db.exec("INSERT INTO files (id, project_id, path, content, created_by, created_at, updated_at) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?6)",
				id, project, path, contents[id], creator, now);
		}
	}

	// Chat: one text per project made of <len>message<len>sender runs
	if (old.tableExists("chats"))
	{
		auto st = old.query("SELECT projectId, data FROM chats");
		while (st.next())
		{
			long long project = st.i(0);
			if (!knownProjects.count(project)) continue;
			std::vector<std::string> parts = splitLengthPrefixed(st.s(1));
			long long at = now - (long long)parts.size() * 1000; // keep the order
			for (size_t k = 0; k + 1 < parts.size(); k += 2)
			{
				db.exec("INSERT INTO chat_messages (project_id, user_id, text, created_at) VALUES (?1, ?2, ?3, ?4)",
					project, userId(parts[k + 1]), parts[k], at += 1000);
			}
		}
	}

	// Tasks
	if (old.tableExists("Issues"))
	{
		auto st = old.query("SELECT id, projectId, data, usersAssigment, date, status FROM Issues ORDER BY id");
		while (st.next())
		{
			long long id = st.i(0), project = st.i(1);
			if (!knownProjects.count(project)) continue;
			bool done = st.i(5) != 0;
			db.exec("INSERT INTO tasks (id, project_id, text, due, done, created_at, completed_at) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)",
				id, project, st.s(2), normalizeDate(trim(st.s(4))), done, now,
				done ? std::optional<long long>(now) : std::nullopt);
			for (const std::string& name : splitLengthPrefixed(st.s(3)))
			{
				if (auto user = userId(name))
					db.exec("INSERT OR IGNORE INTO task_assignees (task_id, user_id) VALUES (?1, ?2)", id, *user);
			}
		}
	}

	// Inbox: mode 0 = note, 1 = friend request (itemId = sender), 2 = project invite (itemId = project)
	if (old.tableExists("Messages"))
	{
		auto st = old.query("SELECT reciverId, senderId, Text, mode, itemId FROM Messages ORDER BY id");
		while (st.next())
		{
			long long to = st.i(0);
			if (!knownUsers.count(to)) continue;
			std::optional<long long> from;
			if (knownUsers.count(st.i(1))) from = st.i(1);
			int mode = (int)st.i(3);
			std::string kind = mode == 1 ? "friendRequest" : mode == 2 ? "projectInvite" : "info";
			std::optional<long long> project;
			if (mode == 2 && knownProjects.count(st.i(4))) project = st.i(4);
			if (mode == 2 && !project) continue; // the project is gone
			db.exec("INSERT INTO inbox (user_id, kind, text, from_id, project_id, created_at) VALUES (?1, ?2, ?3, ?4, ?5, ?6)",
				to, kind, trim(st.s(2)), from, project, now);
		}
	}

	if (old.tableExists("ClientVersion"))
	{
		if (auto version = old.text("SELECT Version FROM ClientVersion LIMIT 1"))
			db.exec("INSERT OR REPLACE INTO settings (key, value) VALUES ('client_version', ?1)", *version);
	}

	tx.commit();
	LOG_INFO("Imported " << db.scalar(0, "SELECT COUNT(*) FROM users") << " users, "
		<< db.scalar(0, "SELECT COUNT(*) FROM projects") << " projects, "
		<< db.scalar(0, "SELECT COUNT(*) FROM files") << " files, "
		<< db.scalar(0, "SELECT COUNT(*) FROM chat_messages") << " chat messages and "
		<< db.scalar(0, "SELECT COUNT(*) FROM tasks") << " tasks from " << legacyPath);
}
