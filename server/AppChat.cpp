#include "App.h"
#include "Util.h"

// Project chat: one row per message; edit, delete, @mentions, typing indicator.

static std::string checkMessage(const std::string& raw)
{
	std::string text = Util::trim(raw);
	if (text.empty()) fail("bad_request", "Can't send an empty message.");
	if (Util::utf16Length(text) > 4000) fail("bad_request", "Messages are limited to 4000 characters.");
	return text;
}

// User names after '@' (names are letters, digits, '.', '_' and '-').
static std::set<std::string> mentionedNames(const std::string& text)
{
	std::set<std::string> names;
	for (size_t i = 0; i < text.size(); i++)
	{
		if (text[i] != '@' || (i > 0 && (isalnum((unsigned char)text[i - 1]) || text[i - 1] == '.'))) continue;
		size_t j = i + 1;
		while (j < text.size() && (isalnum((unsigned char)text[j]) || text[j] == '.' || text[j] == '_' || text[j] == '-')) j++;
		std::string name = text.substr(i + 1, j - i - 1);
		while (!name.empty() && name.back() == '.') name.pop_back(); // "@dana." at the end of a sentence
		if (name.size() >= 3) names.insert(Util::lower(name));
	}
	return names;
}

Json App::hChatHistory(Session& s, const Json& req)
{
	ProjectRow p = requireProject(reqId(req, "projectId"));
	accessRole(s, p);
	long long before = req["before"].isInt() ? req["before"].asInt() : (1LL << 62);
	long long limit = req["limit"].isInt() ? std::max(1LL, std::min(200LL, req["limit"].asInt())) : 60;
	std::vector<long long> ids;
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT id FROM chat_messages WHERE project_id = ?1 AND id < ?2 ORDER BY id DESC LIMIT ?3", p.id, before, limit);
		while (st.next()) ids.push_back(st.i(0));
	}
	Json messages = Json::array();
	for (auto it = ids.rbegin(); it != ids.rend(); ++it) messages.push(chatMessageJson(*it));
	return Json::object({ { "messages", messages }, { "more", (long long)ids.size() == limit } });
}

Json App::hChatSend(Session& s, const Json& req)
{
	long long projectId = reqId(req, "projectId");
	requireMember(s, projectId);
	std::string text = checkMessage(reqString(req, "text", 20000));

	long long now = nowMs();
	while (!s.chatTimes.empty() && s.chatTimes.front() < now - 10000) s.chatTimes.pop_front();
	if ((int)s.chatTimes.size() >= _cfg.chatMessagesPer10Seconds)
	{
		ApiError e("rate_limited", "You're sending messages too fast.");
		e.retryAfter = 10;
		throw e;
	}
	s.chatTimes.push_back(now);

	long long me = s.uid();
	long long id;
	std::string projectName;
	std::vector<long long> mentioned;
	{
		auto lock = _db.lock();
		_db.exec("INSERT INTO chat_messages (project_id, user_id, text, created_at) VALUES (?1, ?2, ?3, ?4)", projectId, me, text, now);
		id = _db.lastInsertId();
		projectName = _db.text("SELECT name FROM projects WHERE id = ?1", projectId).value_or("");
		for (const std::string& name : mentionedNames(text))
		{
			long long userId = _db.scalar(0, "SELECT u.id FROM users u JOIN project_members m ON m.user_id = u.id "
				"WHERE m.project_id = ?1 AND u.name = ?2", projectId, name);
			if (userId && userId != me) mentioned.push_back(userId);
		}
	}
	Json message = chatMessageJson(id);
	broadcastToRoom(projectId, Json::object({ { "t", "ev.chat" }, { "projectId", projectId }, { "message", message } }), s.id);

	std::string snippet = Util::truncateUtf8(text, 120);
	for (long long userId : mentioned)
		addInbox(userId, "mention", "mentioned you in " + projectName + ": " + snippet, me, projectId);
	return Json::object({ { "message", message } });
}

Json App::hChatEdit(Session& s, const Json& req)
{
	long long id = reqId(req, "id");
	std::string text = checkMessage(reqString(req, "text", 20000));
	long long projectId;
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT project_id, IFNULL(user_id, 0), deleted FROM chat_messages WHERE id = ?1", id);
		if (!st.next() || st.i(2)) fail("not_found", "That message was deleted.");
		if (st.i(1) != s.uid()) fail("forbidden", "You can only edit your own messages.");
		projectId = st.i(0);
	}
	requireMember(s, projectId);
	{
		auto lock = _db.lock();
		_db.exec("UPDATE chat_messages SET text = ?1, edited_at = ?2 WHERE id = ?3", text, nowMs(), id);
	}
	Json message = chatMessageJson(id);
	broadcastToRoom(projectId, Json::object({ { "t", "ev.chat" }, { "projectId", projectId }, { "message", message } }), s.id);
	return Json::object({ { "message", message } });
}

Json App::hChatDelete(Session& s, const Json& req)
{
	long long id = reqId(req, "id");
	long long projectId, author;
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT project_id, IFNULL(user_id, 0), deleted FROM chat_messages WHERE id = ?1", id);
		if (!st.next() || st.i(2)) fail("not_found", "That message was already deleted.");
		projectId = st.i(0);
		author = st.i(1);
	}
	std::string role = requireMember(s, projectId);
	if (author != s.uid() && role != "owner" && role != "admin")
		fail("forbidden", "You can only delete your own messages.");
	{
		auto lock = _db.lock();
		_db.exec("UPDATE chat_messages SET deleted = 1, text = '', edited_at = ?1 WHERE id = ?2", nowMs(), id);
	}
	Json message = chatMessageJson(id);
	broadcastToRoom(projectId, Json::object({ { "t", "ev.chat" }, { "projectId", projectId }, { "message", message } }), s.id);
	return Json::object({ { "message", message } });
}

Json App::hChatTyping(Session& s, const Json& req)
{
	long long projectId = reqId(req, "projectId");
	long long now = nowMs();
	if (now - s.lastTyping < 1500) return Json::object();
	s.lastTyping = now;
	{
		std::lock_guard<std::mutex> lock(s.m);
		auto it = s.openProjects.find(projectId);
		if (it == s.openProjects.end() || it->second == "viewer") return Json::object();
	}
	broadcastToRoom(projectId, Json::object({ { "t", "ev.chat.typing" }, { "projectId", projectId }, { "user", userJson(s.uid()) } }), s.id);
	return Json::object();
}
