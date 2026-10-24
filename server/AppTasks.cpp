#include "App.h"
#include "Util.h"

// Per-project task list.

static std::string checkTaskText(const std::string& raw)
{
	std::string text = Util::trim(raw);
	if (text.empty()) fail("bad_request", "A task needs a description.");
	if (Util::utf16Length(text) > 1000) fail("bad_request", "Task descriptions are limited to 1000 characters.");
	return text;
}

static std::string checkDue(const std::string& due)
{
	int y, m, d;
	char extra;
	if (due.size() != 10 || sscanf_s(due.c_str(), "%4d-%2d-%2d%c", &y, &m, &d, &extra, 1) != 3
		|| y < 2000 || y > 2200 || m < 1 || m > 12 || d < 1 || d > 31)
	{
		fail("bad_request", "Pick a due date.");
	}
	return due;
}

std::set<long long> App::parseAssignees(long long projectId, const Json& assignees)
{
	std::set<long long> ids;
	if (assignees.isNull()) return ids;
	if (!assignees.isArray() || assignees.size() > 100) fail("bad_request", "Invalid assignee list.");
	std::set<long long> members = memberIds(projectId);
	for (const Json& a : assignees.items())
	{
		if (!a.isInt() || !members.count(a.asInt())) fail("bad_request", "Tasks can only be assigned to project members.");
		ids.insert(a.asInt());
	}
	return ids;
}

static long long taskProject(Db& db, long long taskId)
{
	auto lock = db.lock();
	long long projectId = db.scalar(0, "SELECT project_id FROM tasks WHERE id = ?1", taskId);
	if (!projectId) fail("not_found", "That task no longer exists.");
	return projectId;
}

Json App::hTaskList(Session& s, const Json& req)
{
	ProjectRow p = requireProject(reqId(req, "projectId"));
	accessRole(s, p);
	std::vector<long long> ids;
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT id FROM tasks WHERE project_id = ?1 ORDER BY done, due, id", p.id);
		while (st.next()) ids.push_back(st.i(0));
	}
	Json tasks = Json::array();
	for (long long id : ids) tasks.push(taskJson(id));
	return Json::object({ { "tasks", tasks } });
}

Json App::hTaskCreate(Session& s, const Json& req)
{
	long long projectId = reqId(req, "projectId");
	requireMember(s, projectId);
	std::string text = checkTaskText(reqString(req, "text", 8000));
	std::string due = checkDue(reqString(req, "due", 20));
	std::set<long long> assignees = parseAssignees(projectId, req["assignees"]);
	long long me = s.uid();
	long long id;
	{
		auto lock = _db.lock();
		Transaction tx(_db);
		_db.exec("INSERT INTO tasks (project_id, text, due, created_by, created_at) VALUES (?1, ?2, ?3, ?4, ?5)", projectId, text, due, me, nowMs());
		id = _db.lastInsertId();
		for (long long user : assignees) _db.exec("INSERT INTO task_assignees (task_id, user_id) VALUES (?1, ?2)", id, user);
		tx.commit();
	}
	Json task = taskJson(id);
	broadcastToRoom(projectId, Json::object({ { "t", "ev.task" }, { "projectId", projectId }, { "task", task } }), s.id);
	std::string name = requireProject(projectId).name;
	for (long long user : assignees)
		if (user != me) addInbox(user, "task", "assigned you a task in " + name + ": " + Util::truncateUtf8(text, 100), me, projectId);
	addActivity(projectId, me, "task.created", "added the task \"" + Util::truncateUtf8(text, 80) + "\"");
	return Json::object({ { "task", task } });
}

Json App::hTaskUpdate(Session& s, const Json& req)
{
	long long id = reqId(req, "id");
	long long projectId = taskProject(_db, id);
	requireMember(s, projectId);
	std::string text = checkTaskText(reqString(req, "text", 8000));
	std::string due = checkDue(reqString(req, "due", 20));
	std::set<long long> assignees = parseAssignees(projectId, req["assignees"]);
	long long me = s.uid();
	std::set<long long> before;
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT user_id FROM task_assignees WHERE task_id = ?1", id);
		while (st.next()) before.insert(st.i(0));
		Transaction tx(_db);
		_db.exec("UPDATE tasks SET text = ?1, due = ?2 WHERE id = ?3", text, due, id);
		_db.exec("DELETE FROM task_assignees WHERE task_id = ?1", id);
		for (long long user : assignees) _db.exec("INSERT INTO task_assignees (task_id, user_id) VALUES (?1, ?2)", id, user);
		tx.commit();
	}
	Json task = taskJson(id);
	broadcastToRoom(projectId, Json::object({ { "t", "ev.task" }, { "projectId", projectId }, { "task", task } }), s.id);
	std::string name = requireProject(projectId).name;
	for (long long user : assignees)
		if (user != me && !before.count(user)) addInbox(user, "task", "assigned you a task in " + name + ": " + Util::truncateUtf8(text, 100), me, projectId);
	return Json::object({ { "task", task } });
}

Json App::hTaskSetDone(Session& s, const Json& req)
{
	long long id = reqId(req, "id");
	bool done = reqBool(req, "done");
	long long projectId = taskProject(_db, id);
	requireMember(s, projectId);
	std::string text;
	{
		auto lock = _db.lock();
		_db.exec("UPDATE tasks SET done = ?1, completed_at = ?2 WHERE id = ?3", done,
			done ? std::optional<long long>(nowMs()) : std::nullopt, id);
		text = _db.text("SELECT text FROM tasks WHERE id = ?1", id).value_or("");
	}
	Json task = taskJson(id);
	broadcastToRoom(projectId, Json::object({ { "t", "ev.task" }, { "projectId", projectId }, { "task", task } }), s.id);
	if (done) addActivity(projectId, s.uid(), "task.done", "completed \"" + Util::truncateUtf8(text, 80) + "\"");
	return Json::object({ { "task", task } });
}

Json App::hTaskDelete(Session& s, const Json& req)
{
	long long id = reqId(req, "id");
	long long projectId = taskProject(_db, id);
	requireMember(s, projectId);
	{
		auto lock = _db.lock();
		_db.exec("DELETE FROM tasks WHERE id = ?1", id);
	}
	broadcastToRoom(projectId, Json::object({ { "t", "ev.task.deleted" }, { "projectId", projectId }, { "id", id } }), s.id);
	return Json::object();
}
