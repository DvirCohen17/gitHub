#include "App.h"
#include "Log.h"
#include "Util.h"

// Profiles, avatars, search, presence, friends, inbox and the activity feed.

static const char* AVATAR_PRESETS[] = {
	"fox", "cat", "dog", "panda", "koala", "tiger", "lion", "frog",
	"monkey", "penguin", "owl", "octopus", "unicorn", "bear", "rabbit", "robot",
};
static const size_t MAX_AVATAR_BYTES = 512 * 1024;

static std::string likePattern(const std::string& text)
{
	std::string out = "%";
	for (char c : text)
	{
		if (c == '%' || c == '_' || c == '\\') out += '\\';
		out += c;
	}
	return out + "%";
}

static std::string relation(Db& db, long long me, long long other)
{
	if (me == other) return "self";
	if (db.scalar(0, "SELECT 1 FROM friendships WHERE user_a = ?1 AND user_b = ?2", std::min(me, other), std::max(me, other))) return "friend";
	if (db.scalar(0, "SELECT 1 FROM friend_requests WHERE from_id = ?1 AND to_id = ?2", me, other)) return "requestSent";
	if (db.scalar(0, "SELECT 1 FROM friend_requests WHERE from_id = ?1 AND to_id = ?2", other, me)) return "requestReceived";
	return "none";
}

Json App::hProfile(Session& s, const Json& req)
{
	std::string name = Util::trim(reqString(req, "name", 100));
	long long me = s.uid();
	long long id;
	Json result;
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT id, name, avatar, bio, IFNULL(email, ''), created_at FROM users WHERE name = ?1", name);
		if (!st.next()) fail("not_found", "There's no user called '" + name + "'.");
		id = st.i(0);
		result = Json::object({
			{ "user", userJson(id, st.s(1), st.s(2)) }, { "bio", st.s(3) }, { "createdAt", st.i(5) },
			{ "relation", relation(_db, me, id) },
		});
		if (id == me) result.set("email", st.s(4));

		// Projects both of you are in, plus their public projects.
		Json projects = Json::array();
		auto p = _db.query(
			"SELECT p.id, p.name, p.language, p.visibility, m.role FROM projects p JOIN project_members m ON m.project_id = p.id "
			"WHERE m.user_id = ?1 AND (p.visibility = 'public' OR ?1 = ?2 OR EXISTS "
			"(SELECT 1 FROM project_members me WHERE me.project_id = p.id AND me.user_id = ?2)) ORDER BY p.updated_at DESC", id, me);
		while (p.next())
		{
			projects.push(Json::object({ { "id", p.i(0) }, { "name", p.s(1) }, { "language", p.s(2) }, { "visibility", p.s(3) }, { "role", p.s(4) } }));
		}
		result.set("projects", projects);
		result.set("friends", _db.scalar(0, "SELECT COUNT(*) FROM friendships WHERE user_a = ?1 OR user_b = ?1", id));
	}
	result.set("presence", presenceJson(id, me));
	return result;
}

Json App::hUpdateProfile(Session& s, const Json& req)
{
	std::string bio = Util::trim(reqString(req, "bio", 2000, true));
	if (Util::utf16Length(bio) > 500) fail("bad_request", "Your bio can be up to 500 characters.");
	auto lock = _db.lock();
	_db.exec("UPDATE users SET bio = ?1 WHERE id = ?2", bio, s.uid());
	return Json::object({ { "bio", bio } });
}

// Reads width and height from a PNG header. False if it isn't a PNG.
static bool pngSize(const std::string& png, unsigned& width, unsigned& height)
{
	static const unsigned char SIGNATURE[8] = { 0x89, 'P', 'N', 'G', 0x0D, 0x0A, 0x1A, 0x0A };
	if (png.size() < 33 || memcmp(png.data(), SIGNATURE, 8) != 0 || png.compare(12, 4, "IHDR") != 0) return false;
	auto be32 = [&](size_t at) {
		return ((unsigned)(unsigned char)png[at] << 24) | ((unsigned)(unsigned char)png[at + 1] << 16)
			| ((unsigned)(unsigned char)png[at + 2] << 8) | (unsigned)(unsigned char)png[at + 3];
	};
	width = be32(16);
	height = be32(20);
	return true;
}

Json App::hSetAvatar(Session& s, const Json& req)
{
	std::string avatar;
	if (req["preset"].isString())
	{
		const std::string& preset = req["preset"].asString();
		bool known = false;
		for (const char* p : AVATAR_PRESETS) known = known || preset == p;
		if (!known) fail("bad_request", "Unknown avatar.");
		avatar = "p:" + preset;
	}
	else if (req["png"].isString())
	{
		std::string png;
		if (req["png"].asString().size() > MAX_AVATAR_BYTES * 4 / 3 + 8 || !Util::base64Decode(req["png"].asString(), png))
			fail("bad_request", "The picture is too large (512 KB at most).");
		unsigned width = 0, height = 0;
		if (!pngSize(png, width, height)) fail("bad_request", "Profile pictures must be PNG images.");
		if (width != height || width < 32 || width > 1024) fail("bad_request", "Profile pictures must be square, 32 to 1024 pixels.");
		if (png.size() > MAX_AVATAR_BYTES) fail("bad_request", "The picture is too large (512 KB at most).");
		std::string hash = Util::sha256Hex(png);
		auto lock = _db.lock();
		_db.exec("INSERT OR IGNORE INTO avatars (hash, png, created_at) VALUES (?1, ?2, ?3)", hash, Blob{ png.data(), png.size() }, nowMs());
		avatar = "u:" + hash;
	}
	long long me = s.uid();
	{
		auto lock = _db.lock();
		_db.exec("UPDATE users SET avatar = ?1 WHERE id = ?2", avatar, me);
		// Forget uploaded pictures nobody uses any more.
		_db.exec("DELETE FROM avatars WHERE NOT EXISTS (SELECT 1 FROM users WHERE avatar = 'u:' || avatars.hash)");
	}
	// Everyone who might be showing this user refreshes their picture.
	std::set<long long> related = relatedUsers(me);
	related.insert(me);
	sendToUsers(related, Json::object({ { "t", "ev.user" }, { "user", userJson(me) } }));
	return Json::object({ { "avatar", avatar } });
}

Json App::hAvatar(Session&, const Json& req)
{
	const std::string& hash = reqString(req, "hash", 64);
	auto lock = _db.lock();
	auto st = _db.query("SELECT png FROM avatars WHERE hash = ?1", hash);
	if (!st.next()) fail("not_found", "That picture no longer exists.");
	auto bytes = st.blob(0);
	return Json::object({ { "png", Util::base64Encode(std::string(bytes.begin(), bytes.end())) } });
}

Json App::hSearchUsers(Session& s, const Json& req)
{
	std::string query = Util::trim(reqString(req, "query", 100));
	long long me = s.uid();
	Json users = Json::array();
	auto lock = _db.lock();
	// Names match anywhere; emails only exactly (so emails can't be harvested).
	auto st = _db.query("SELECT id, name, avatar FROM users WHERE (name LIKE ?1 ESCAPE '\\' OR email = ?2) AND banned = 0 "
		"ORDER BY CASE WHEN name = ?2 THEN 0 WHEN name LIKE ?3 ESCAPE '\\' THEN 1 ELSE 2 END, name LIMIT 30",
		likePattern(query), query, likePattern(query).substr(1));
	while (st.next())
	{
		Json u = userJson(st.i(0), st.s(1), st.s(2));
		u.set("relation", relation(_db, me, st.i(0)));
		users.push(u);
	}
	return Json::object({ { "users", users } });
}

Json App::hSetPresence(Session& s, const Json& req)
{
	std::string status = optString(req, "status", 20);
	if (!status.empty() && status != "online" && status != "away") fail("bad_request", "Unknown status.");
	long long userId;
	{
		std::lock_guard<std::mutex> lock(s.m);
		if (!status.empty()) s.status = status;
		if (req.has("projectId"))
		{
			long long project = req["projectId"].isInt() ? req["projectId"].asInt() : 0;
			s.activeProject = s.openProjects.count(project) ? project : 0;
			if (!s.activeProject) s.activeFile = 0;
		}
		if (req.has("fileId"))
		{
			long long file = req["fileId"].isInt() ? req["fileId"].asInt() : 0;
			s.activeFile = s.openFiles.count(file) ? file : 0;
		}
		userId = s.userId;
	}
	notifyPresence(userId);
	return Json::object();
}

// ---- friends ----

Json App::hFriends(Session& s, const Json&)
{
	long long me = s.uid();
	Json friends = Json::array(), incoming = Json::array(), outgoing = Json::array();
	std::vector<long long> friendList;
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT u.id, u.name, u.avatar FROM users u WHERE u.id IN "
			"(SELECT user_b FROM friendships WHERE user_a = ?1 UNION SELECT user_a FROM friendships WHERE user_b = ?1) ORDER BY u.name", me);
		while (st.next())
		{
			friends.push(userJson(st.i(0), st.s(1), st.s(2)));
			friendList.push_back(st.i(0));
		}
		auto in = _db.query("SELECT u.id, u.name, u.avatar FROM friend_requests r JOIN users u ON u.id = r.from_id WHERE r.to_id = ?1 ORDER BY r.created_at DESC", me);
		while (in.next()) incoming.push(userJson(in.i(0), in.s(1), in.s(2)));
		auto out = _db.query("SELECT u.id, u.name, u.avatar FROM friend_requests r JOIN users u ON u.id = r.to_id WHERE r.from_id = ?1 ORDER BY r.created_at DESC", me);
		while (out.next()) outgoing.push(userJson(out.i(0), out.s(1), out.s(2)));
	}
	Json withPresence = Json::array();
	for (size_t k = 0; k < friendList.size(); k++)
	{
		Json f = friends[k];
		f.set("presence", presenceJson(friendList[k], me));
		withPresence.push(f);
	}
	return Json::object({ { "friends", withPresence }, { "incoming", incoming }, { "outgoing", outgoing } });
}

Json App::hFriendRequest(Session& s, const Json& req)
{
	std::string name = Util::trim(reqString(req, "name", 100));
	long long me = s.uid();
	long long other;
	std::string state;
	{
		auto lock = _db.lock();
		other = _db.scalar(0, "SELECT id FROM users WHERE name = ?1", name);
		if (!other) fail("not_found", "There's no user called '" + name + "'.");
		if (other == me) fail("bad_request", "You can't add yourself as a friend.");
		std::string rel = relation(_db, me, other);
		if (rel == "friend") fail("conflict", "You're already friends.");
		if (rel == "requestSent") return Json::object({ { "state", "sent" } });
		Transaction tx(_db);
		if (rel == "requestReceived")
		{
			// They already asked: this makes you friends.
			_db.exec("DELETE FROM friend_requests WHERE from_id = ?1 AND to_id = ?2", other, me);
			_db.exec("INSERT OR IGNORE INTO friendships (user_a, user_b, created_at) VALUES (?1, ?2, ?3)", std::min(me, other), std::max(me, other), nowMs());
			state = "friends";
		}
		else
		{
			_db.exec("INSERT INTO friend_requests (from_id, to_id, created_at) VALUES (?1, ?2, ?3)", me, other, nowMs());
			state = "sent";
		}
		tx.commit();
	}
	if (state == "friends")
	{
		removeInbox(me, "friendRequest", other, std::nullopt);
		addInbox(other, "info", "accepted your friend request", me, 0);
	}
	else
	{
		addInbox(other, "friendRequest", "sent you a friend request", me, 0);
	}
	Json ev = Json::object({ { "t", "ev.friends" } });
	sendToUsers({ me, other }, ev);
	if (state == "friends")
	{
		notifyPresence(me);
		notifyPresence(other);
	}
	return Json::object({ { "state", state } });
}

Json App::hFriendRespond(Session& s, const Json& req)
{
	long long other = reqId(req, "userId");
	bool accept = reqBool(req, "accept");
	long long me = s.uid();
	{
		auto lock = _db.lock();
		Transaction tx(_db);
		if (_db.exec("DELETE FROM friend_requests WHERE from_id = ?1 AND to_id = ?2", other, me) == 0)
			fail("not_found", "That friend request was withdrawn.");
		if (accept)
			_db.exec("INSERT OR IGNORE INTO friendships (user_a, user_b, created_at) VALUES (?1, ?2, ?3)", std::min(me, other), std::max(me, other), nowMs());
		tx.commit();
	}
	removeInbox(me, "friendRequest", other, std::nullopt);
	if (accept) addInbox(other, "info", "accepted your friend request", me, 0);
	sendToUsers({ me, other }, Json::object({ { "t", "ev.friends" } }));
	if (accept)
	{
		notifyPresence(me);
		notifyPresence(other);
	}
	return Json::object();
}

Json App::hFriendRemove(Session& s, const Json& req)
{
	long long other = reqId(req, "userId");
	long long me = s.uid();
	int changed;
	{
		auto lock = _db.lock();
		Transaction tx(_db);
		changed = _db.exec("DELETE FROM friendships WHERE user_a = ?1 AND user_b = ?2", std::min(me, other), std::max(me, other));
		changed += _db.exec("DELETE FROM friend_requests WHERE (from_id = ?1 AND to_id = ?2) OR (from_id = ?2 AND to_id = ?1)", me, other);
		tx.commit();
	}
	if (changed)
	{
		removeInbox(other, "friendRequest", me, std::nullopt);
		removeInbox(me, "friendRequest", other, std::nullopt);
		sendToUsers({ me, other }, Json::object({ { "t", "ev.friends" } }));
	}
	return Json::object();
}

// ---- inbox & activity ----

Json App::hInbox(Session& s, const Json&)
{
	long long me = s.uid();
	Json items = Json::array();
	auto lock = _db.lock();
	auto st = _db.query(
		"SELECT i.id, i.kind, i.text, i.read, i.created_at, IFNULL(i.from_id, 0), IFNULL(u.name, ''), IFNULL(u.avatar, ''), "
		"IFNULL(i.project_id, 0), IFNULL(p.name, '') FROM inbox i LEFT JOIN users u ON u.id = i.from_id "
		"LEFT JOIN projects p ON p.id = i.project_id WHERE i.user_id = ?1 ORDER BY i.id DESC LIMIT 200", me);
	while (st.next())
	{
		Json item = Json::object({ { "id", st.i(0) }, { "kind", st.s(1) }, { "text", st.s(2) }, { "read", st.i(3) != 0 }, { "createdAt", st.i(4) } });
		if (st.i(5)) item.set("from", userJson(st.i(5), st.s(6), st.s(7)));
		if (st.i(8))
		{
			item.set("projectId", st.i(8));
			item.set("projectName", st.s(9));
		}
		items.push(item);
	}
	return Json::object({ { "items", items }, { "unread", unreadCount(me) } });
}

Json App::hInboxMarkRead(Session& s, const Json& req)
{
	long long me = s.uid();
	{
		auto lock = _db.lock();
		if (optBool(req, "all", false))
			_db.exec("UPDATE inbox SET read = 1 WHERE user_id = ?1", me);
		else
			_db.exec("UPDATE inbox SET read = 1 WHERE user_id = ?1 AND id = ?2", me, reqId(req, "id"));
	}
	return Json::object({ { "unread", unreadCount(me) } });
}

Json App::hInboxDelete(Session& s, const Json& req)
{
	long long me = s.uid();
	{
		auto lock = _db.lock();
		_db.exec("DELETE FROM inbox WHERE user_id = ?1 AND id = ?2", me, reqId(req, "id"));
	}
	return Json::object({ { "unread", unreadCount(me) } });
}

Json App::hActivity(Session& s, const Json& req)
{
	long long me = s.uid();
	long long before = req["before"].isInt() ? req["before"].asInt() : (1LL << 62);
	Json items = Json::array();
	auto lock = _db.lock();
	auto st = _db.query(
		"SELECT a.id, a.project_id, p.name, a.kind, a.text, a.created_at, IFNULL(a.user_id, 0), IFNULL(u.name, ''), IFNULL(u.avatar, '') "
		"FROM activity a JOIN projects p ON p.id = a.project_id LEFT JOIN users u ON u.id = a.user_id "
		"WHERE a.project_id IN (SELECT project_id FROM project_members WHERE user_id = ?1) AND a.id < ?2 "
		"ORDER BY a.id DESC LIMIT 40", me, before);
	while (st.next())
	{
		Json item = Json::object({
			{ "id", st.i(0) }, { "projectId", st.i(1) }, { "projectName", st.s(2) }, { "kind", st.s(3) },
			{ "text", st.s(4) }, { "createdAt", st.i(5) },
		});
		if (st.i(6)) item.set("user", userJson(st.i(6), st.s(7), st.s(8)));
		items.push(item);
	}
	return Json::object({ { "items", items } });
}
