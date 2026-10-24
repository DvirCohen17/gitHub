#include "App.h"
#include "Log.h"
#include "Util.h"

// Files and folders, live co-editing, cursors and version history.
//
// Every open file has one authoritative LiveDoc in its project's Room. A client
// sends each edit tagged with the last revision it has seen. If other edits were
// applied since, the incoming edit is transformed past them (TextOperation::transform)
// so it still means what the user intended, then applied, acknowledged and sent to
// everyone else with the file open. Clients do the mirror-image transform for their
// own unacknowledged edits, so all copies converge.

static const size_t MAX_HISTORY = 1000;
static const size_t MAX_DOCUMENT_CHARS = 2 * 1024 * 1024;

std::string normalizePath(const std::string& raw)
{
	std::string path = Util::trim(raw);
	for (char& c : path)
		if (c == '\\') c = '/';
	std::string out, segment;
	int depth = 0;
	auto finish = [&]() {
		std::string seg = Util::trim(segment);
		segment.clear();
		if (seg.empty()) return;
		if (seg == "." || seg == "..") fail("bad_request", "Paths can't contain '.' or '..'.");
		if (seg.size() > 100) fail("bad_request", "File and folder names are at most 100 characters.");
		for (char c : seg)
		{
			if ((unsigned char)c < 0x20 || strchr("<>:\"|?*", c))
				fail("bad_request", "Names can't contain < > : \" | ? or *.");
		}
		if (seg.back() == '.') fail("bad_request", "Names can't end with a dot.");
		if (++depth > 10) fail("bad_request", "Folders can be nested at most 10 deep.");
		if (!out.empty()) out += '/';
		out += seg;
	};
	for (char c : path)
	{
		if (c == '/') finish();
		else segment += c;
	}
	finish();
	if (out.empty()) fail("bad_request", "Enter a name.");
	if (out.size() > 300) fail("bad_request", "That path is too long.");
	return out;
}

void App::ensureParentFolders(long long projectId, const std::string& path)
{
	auto lock = _db.lock();
	size_t slash = path.find('/');
	while (slash != std::string::npos)
	{
		_db.exec("INSERT OR IGNORE INTO folders (project_id, path) VALUES (?1, ?2)", projectId, path.substr(0, slash));
		slash = path.find('/', slash + 1);
	}
}

void App::broadcastTree(long long projectId)
{
	Json folders;
	Json files = treeJson(projectId, folders);
	broadcastToRoom(projectId, Json::object({ { "t", "ev.tree" }, { "projectId", projectId }, { "files", files }, { "folders", folders } }));
}

// Files that were deleted: close them for everyone.
void App::dropDocs(long long projectId, const std::set<long long>& fileIds)
{
	RoomPtr r = room(projectId, false);
	if (!r) return;
	std::lock_guard<std::mutex> lock(r->m);
	for (long long fileId : fileIds)
	{
		r->docs.erase(fileId);
		broadcast(*r, Json::object({ { "t", "ev.file.deleted" }, { "fileId", fileId } }));
	}
	for (auto& entry : r->sessions)
	{
		std::lock_guard<std::mutex> sl(entry.second->m);
		for (long long fileId : fileIds)
		{
			entry.second->openFiles.erase(fileId);
			if (entry.second->activeFile == fileId) entry.second->activeFile = 0;
		}
	}
}

Json App::hFileCreate(Session& s, const Json& req)
{
	long long projectId = reqId(req, "projectId");
	requireWriter(s, projectId);
	std::string path = normalizePath(reqString(req, "path", 1000));
	long long id;
	{
		auto lock = _db.lock();
		if (_db.scalar(0, "SELECT 1 FROM files WHERE project_id = ?1 AND path = ?2", projectId, path))
			fail("conflict", "A file with this name already exists.");
		if (_db.scalar(0, "SELECT 1 FROM folders WHERE project_id = ?1 AND path = ?2", projectId, path))
			fail("conflict", "A folder with this name already exists.");
		long long now = nowMs();
		_db.exec("INSERT INTO files (project_id, path, created_by, created_at, updated_at) VALUES (?1, ?2, ?3, ?4, ?4)",
			projectId, path, s.uid(), now);
		id = _db.lastInsertId();
		ensureParentFolders(projectId, path);
	}
	broadcastTree(projectId);
	addActivity(projectId, s.uid(), "file.created", "created " + path);
	return Json::object({ { "file", Json::object({ { "id", id }, { "path", path } }) } });
}

Json App::hFileRename(Session& s, const Json& req)
{
	FileRow f = requireFile(reqId(req, "fileId"));
	requireWriter(s, f.projectId);
	std::string path = normalizePath(reqString(req, "path", 1000));
	if (path == f.path) return Json::object();
	{
		auto lock = _db.lock();
		if (_db.scalar(0, "SELECT 1 FROM files WHERE project_id = ?1 AND path = ?2 AND id <> ?3", f.projectId, path, f.id))
			fail("conflict", "A file with this name already exists.");
		if (_db.scalar(0, "SELECT 1 FROM folders WHERE project_id = ?1 AND path = ?2", f.projectId, path))
			fail("conflict", "A folder with this name already exists.");
		_db.exec("UPDATE files SET path = ?1, updated_at = ?2 WHERE id = ?3", path, nowMs(), f.id);
		ensureParentFolders(f.projectId, path);
	}
	broadcastTree(f.projectId);
	addActivity(f.projectId, s.uid(), "file.renamed", "renamed " + f.path + " to " + path);
	return Json::object();
}

Json App::hFileDelete(Session& s, const Json& req)
{
	FileRow f = requireFile(reqId(req, "fileId"));
	requireWriter(s, f.projectId);
	{
		auto lock = _db.lock();
		_db.exec("DELETE FROM files WHERE id = ?1", f.id);
	}
	dropDocs(f.projectId, { f.id });
	broadcastTree(f.projectId);
	addActivity(f.projectId, s.uid(), "file.deleted", "deleted " + f.path);
	return Json::object();
}

Json App::hFolderCreate(Session& s, const Json& req)
{
	long long projectId = reqId(req, "projectId");
	requireWriter(s, projectId);
	std::string path = normalizePath(reqString(req, "path", 1000));
	{
		auto lock = _db.lock();
		if (_db.scalar(0, "SELECT 1 FROM files WHERE project_id = ?1 AND path = ?2", projectId, path))
			fail("conflict", "A file with this name already exists.");
		_db.exec("INSERT OR IGNORE INTO folders (project_id, path) VALUES (?1, ?2)", projectId, path);
		ensureParentFolders(projectId, path);
	}
	broadcastTree(projectId);
	return Json::object();
}

Json App::hFolderRename(Session& s, const Json& req)
{
	long long projectId = reqId(req, "projectId");
	requireWriter(s, projectId);
	std::string from = normalizePath(reqString(req, "path", 1000));
	std::string to = normalizePath(reqString(req, "newPath", 1000));
	if (from == to) return Json::object();
	if (to.rfind(from + "/", 0) == 0) fail("bad_request", "A folder can't be moved into itself.");
	{
		auto lock = _db.lock();
		if (!_db.scalar(0, "SELECT 1 FROM folders WHERE project_id = ?1 AND path = ?2", projectId, from))
			fail("not_found", "That folder no longer exists.");
		// Would any moved file or folder land on an existing one?
		long long clash = _db.scalar(0,
			"SELECT 1 FROM files a WHERE a.project_id = ?1 AND (a.path = ?3 OR substr(a.path, 1, length(?3) + 1) = ?3 || '/') "
			"AND NOT (substr(a.path, 1, length(?2) + 1) = ?2 || '/') "
			"AND EXISTS (SELECT 1 FROM files b WHERE b.project_id = ?1 AND substr(b.path, 1, length(?2) + 1) = ?2 || '/' "
			"AND ?3 || substr(b.path, length(?2) + 1) = a.path)", projectId, from, to);
		if (clash || _db.scalar(0, "SELECT 1 FROM files WHERE project_id = ?1 AND path = ?2", projectId, to))
			fail("conflict", "Something with that name already exists there.");
		Transaction tx(_db);
		_db.exec("UPDATE files SET path = ?3 || substr(path, length(?2) + 1), updated_at = ?4 "
			"WHERE project_id = ?1 AND substr(path, 1, length(?2) + 1) = ?2 || '/'", projectId, from, to, nowMs());
		_db.exec("UPDATE OR IGNORE folders SET path = ?3 || substr(path, length(?2) + 1) "
			"WHERE project_id = ?1 AND (path = ?2 OR substr(path, 1, length(?2) + 1) = ?2 || '/')", projectId, from, to);
		_db.exec("DELETE FROM folders WHERE project_id = ?1 AND (path = ?2 OR substr(path, 1, length(?2) + 1) = ?2 || '/')", projectId, from);
		ensureParentFolders(projectId, to + "/x");
		tx.commit();
	}
	broadcastTree(projectId);
	addActivity(projectId, s.uid(), "folder.renamed", "moved folder " + from + " to " + to);
	return Json::object();
}

Json App::hFolderDelete(Session& s, const Json& req)
{
	long long projectId = reqId(req, "projectId");
	requireWriter(s, projectId);
	std::string path = normalizePath(reqString(req, "path", 1000));
	std::set<long long> removed;
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT id FROM files WHERE project_id = ?1 AND substr(path, 1, length(?2) + 1) = ?2 || '/'", projectId, path);
		while (st.next()) removed.insert(st.i(0));
		Transaction tx(_db);
		_db.exec("DELETE FROM files WHERE project_id = ?1 AND substr(path, 1, length(?2) + 1) = ?2 || '/'", projectId, path);
		_db.exec("DELETE FROM folders WHERE project_id = ?1 AND (path = ?2 OR substr(path, 1, length(?2) + 1) = ?2 || '/')", projectId, path);
		tx.commit();
	}
	dropDocs(projectId, removed);
	broadcastTree(projectId);
	addActivity(projectId, s.uid(), "folder.deleted", "deleted folder " + path + (removed.empty() ? "" : " and " + std::to_string(removed.size()) + " file(s)"));
	return Json::object();
}

// ---- live editing ----

Json App::hFileOpen(Session& s, const Json& req)
{
	FileRow f = requireFile(reqId(req, "fileId"));
	{
		std::lock_guard<std::mutex> lock(s.m);
		if (!s.openProjects.count(f.projectId)) fail("forbidden", "Open the project first.");
	}
	SessionPtr self = sessionFor(s.conn);
	RoomPtr r = room(f.projectId, true);
	Json result;
	{
		std::lock_guard<std::mutex> lock(r->m);
		LiveDoc& doc = loadDoc(*r, f.id);
		doc.viewers[s.id] = self;
		Json cursors = Json::array();
		for (auto& c : doc.cursors)
		{
			if (c.first == s.id) continue;
			cursors.push(Json::object({ { "userId", c.second.userId }, { "anchor", (long long)c.second.anchor }, { "head", (long long)c.second.head } }));
		}
		Json users = fileUsersJson(doc);
		result = Json::object({
			{ "fileId", f.id }, { "path", f.path }, { "rev", doc.rev }, { "text", wideToUtf8(doc.text) },
			{ "cursors", cursors }, { "users", users },
		});
		Json ev = Json::object({ { "t", "ev.file.users" }, { "fileId", f.id }, { "users", users } });
		std::string payload = ev.dump();
		for (auto& v : doc.viewers)
			if (v.first != s.id) v.second->conn->send(payload);
	}
	long long userId;
	{
		std::lock_guard<std::mutex> lock(s.m);
		s.openFiles[f.id] = f.projectId;
		s.activeFile = f.id;
		s.activeProject = f.projectId;
		userId = s.userId;
	}
	notifyPresence(userId);
	return result;
}

Json App::hFileClose(Session& s, const Json& req)
{
	long long fileId = reqId(req, "fileId");
	long long projectId;
	{
		std::lock_guard<std::mutex> lock(s.m);
		auto it = s.openFiles.find(fileId);
		if (it == s.openFiles.end()) return Json::object();
		projectId = it->second;
	}
	if (RoomPtr r = room(projectId, false))
	{
		std::lock_guard<std::mutex> lock(r->m);
		closeFileLocked(*r, s, fileId);
	}
	notifyPresence(s.uid());
	return Json::object();
}

Json App::hFileOp(Session& s, const Json& req)
{
	long long fileId = reqId(req, "fileId");
	const Json& revField = req["rev"];
	if (!revField.isInt()) fail("bad_request", "Missing revision.");
	long long revision = revField.asInt();
	long long projectId, userId;
	std::string role;
	{
		std::lock_guard<std::mutex> lock(s.m);
		auto it = s.openFiles.find(fileId);
		if (it == s.openFiles.end()) fail("bad_request", "That file isn't open.");
		projectId = it->second;
		role = s.openProjects[projectId];
		userId = s.userId;
	}
	if (role == "viewer") fail("forbidden", "You can only view this project.");

	RoomPtr r = room(projectId, false);
	if (!r) fail("bad_request", "That file isn't open.");
	std::lock_guard<std::mutex> lock(r->m);
	auto docIt = r->docs.find(fileId);
	if (docIt == r->docs.end() || !docIt->second.viewers.count(s.id)) fail("bad_request", "That file isn't open.");
	LiveDoc& doc = docIt->second;

	auto resync = [&]() {
		return Json::object({ { "resync", true }, { "rev", doc.rev }, { "text", wideToUtf8(doc.text) } });
	};
	TextOperation op;
	try
	{
		if (revision < doc.historyStart || revision > doc.rev) return resync();
		op = TextOperation::fromJson(req["op"]);
		for (size_t k = (size_t)(revision - doc.historyStart); k < doc.history.size(); k++)
			op = TextOperation::transform(op, doc.history[k]).first;
		if (op.targetLength() > MAX_DOCUMENT_CHARS) fail("bad_request", "Files can be at most 2 million characters.");
		doc.text = op.apply(doc.text); // throws if the edit doesn't fit the document
	}
	catch (const std::invalid_argument&)
	{
		// This client's copy can't be reconciled (too far behind, or corrupt):
		// it starts over from the current text.
		return resync();
	}
	doc.history.push_back(op);
	doc.rev++;
	if (doc.history.size() > MAX_HISTORY)
	{
		size_t drop = doc.history.size() - MAX_HISTORY / 2;
		doc.history.erase(doc.history.begin(), doc.history.begin() + drop);
		doc.historyStart += (int)drop;
	}
	doc.dirty = true;
	doc.changedSinceVersion = true;
	doc.lastEditor = userId;
	for (auto& c : doc.cursors)
	{
		if (c.first == s.id) continue;
		c.second.anchor = op.transformIndex(c.second.anchor);
		c.second.head = op.transformIndex(c.second.head);
	}

	Json update = Json::object({ { "t", "ev.file.op" }, { "fileId", fileId }, { "rev", doc.rev }, { "op", op.toJson() }, { "userId", userId } });
	std::string payload = update.dump();
	for (auto& v : doc.viewers)
		if (v.first != s.id) v.second->conn->send(payload);
	return Json::object({ { "rev", doc.rev } });
}

Json App::hFileCursor(Session& s, const Json& req)
{
	long long fileId = reqId(req, "fileId");
	if (!req["rev"].isInt() || !req["anchor"].isInt() || !req["head"].isInt()) return Json::object();
	long long revision = req["rev"].asInt();
	long long anchor = req["anchor"].asInt(), head = req["head"].asInt();
	if (anchor < 0 || head < 0) return Json::object();
	long long projectId, userId;
	{
		std::lock_guard<std::mutex> lock(s.m);
		auto it = s.openFiles.find(fileId);
		if (it == s.openFiles.end()) return Json::object();
		projectId = it->second;
		userId = s.userId;
	}
	RoomPtr r = room(projectId, false);
	if (!r) return Json::object();
	std::lock_guard<std::mutex> lock(r->m);
	auto docIt = r->docs.find(fileId);
	if (docIt == r->docs.end() || !docIt->second.viewers.count(s.id)) return Json::object();
	LiveDoc& doc = docIt->second;
	if (revision < doc.historyStart || revision > doc.rev) return Json::object(); // too old to place

	size_t a = (size_t)anchor, h = (size_t)head;
	for (size_t k = (size_t)(revision - doc.historyStart); k < doc.history.size(); k++)
	{
		if (a > doc.history[k].baseLength() || h > doc.history[k].baseLength()) return Json::object();
		a = doc.history[k].transformIndex(a);
		h = doc.history[k].transformIndex(h);
	}
	a = std::min(a, doc.text.size());
	h = std::min(h, doc.text.size());
	doc.cursors[s.id] = Cursor{ userId, a, h };

	Json ev = Json::object({ { "t", "ev.file.cursor" }, { "fileId", fileId }, { "userId", userId }, { "sessionId", (long long)s.id },
		{ "rev", doc.rev }, { "anchor", (long long)a }, { "head", (long long)h } });
	std::string payload = ev.dump();
	for (auto& v : doc.viewers)
		if (v.first != s.id) v.second->conn->send(payload);
	return Json::object();
}

// ---- versions ----

Json App::hFileVersions(Session& s, const Json& req)
{
	FileRow f = requireFile(reqId(req, "fileId"));
	accessRole(s, requireProject(f.projectId));
	Json versions = Json::array();
	auto lock = _db.lock();
	auto st = _db.query("SELECT v.id, v.label, v.created_at, IFNULL(v.user_id, 0), IFNULL(u.name, ''), IFNULL(u.avatar, ''), length(v.content) "
		"FROM file_versions v LEFT JOIN users u ON u.id = v.user_id WHERE v.file_id = ?1 ORDER BY v.id DESC LIMIT 300", f.id);
	while (st.next())
	{
		Json v = Json::object({ { "id", st.i(0) }, { "label", st.s(1) }, { "createdAt", st.i(2) }, { "size", st.i(6) } });
		if (st.i(3)) v.set("user", userJson(st.i(3), st.s(4), st.s(5)));
		versions.push(v);
	}
	return Json::object({ { "versions", versions } });
}

Json App::hFileVersion(Session& s, const Json& req)
{
	long long versionId = reqId(req, "versionId");
	long long fileId;
	std::string text;
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT file_id, content FROM file_versions WHERE id = ?1", versionId);
		if (!st.next()) fail("not_found", "That version no longer exists.");
		fileId = st.i(0);
		text = st.s(1);
	}
	FileRow f = requireFile(fileId);
	accessRole(s, requireProject(f.projectId));
	return Json::object({ { "fileId", fileId }, { "text", text } });
}

Json App::hFileCheckpoint(Session& s, const Json& req)
{
	FileRow f = requireFile(reqId(req, "fileId"));
	requireWriter(s, f.projectId);
	std::string label = Util::trim(reqString(req, "label", 200));
	if (Util::utf16Length(label) > 60) fail("bad_request", "Version names are at most 60 characters.");
	std::string text;
	bool live = false;
	if (RoomPtr r = room(f.projectId, false))
	{
		std::lock_guard<std::mutex> lock(r->m);
		auto it = r->docs.find(f.id);
		if (it != r->docs.end())
		{
			text = wideToUtf8(it->second.text);
			it->second.changedSinceVersion = false;
			it->second.lastVersionAt = nowMs();
			live = true;
		}
	}
	if (!live)
	{
		auto lock = _db.lock();
		text = _db.text("SELECT content FROM files WHERE id = ?1", f.id).value_or("");
	}
	writeVersion(f.id, text, label, s.uid());
	addActivity(f.projectId, s.uid(), "version.created", "saved version \"" + label + "\" of " + f.path);
	return Json::object();
}

Json App::hFileRestore(Session& s, const Json& req)
{
	long long versionId = reqId(req, "versionId");
	long long fileId, createdAt;
	std::string text;
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT file_id, content, created_at FROM file_versions WHERE id = ?1", versionId);
		if (!st.next()) fail("not_found", "That version no longer exists.");
		fileId = st.i(0);
		text = st.s(1);
		createdAt = st.i(2);
	}
	FileRow f = requireFile(fileId);
	requireWriter(s, f.projectId);
	RoomPtr r = room(f.projectId, true);
	{
		std::lock_guard<std::mutex> lock(r->m);
		LiveDoc& doc = loadDoc(*r, fileId);
		std::wstring target = utf8ToWide(text);
		if (doc.text == target) return Json::object();
		writeVersion(fileId, wideToUtf8(doc.text), "Before restore", s.uid());
		TextOperation op;
		op.remove(doc.text.size());
		op.insert(target);
		applyServerEdit(*r, doc, op, s.uid());
	}
	addActivity(f.projectId, s.uid(), "version.restored", "restored an earlier version of " + f.path);
	(void)createdAt;
	return Json::object();
}
