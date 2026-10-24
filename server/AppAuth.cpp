#include "App.h"
#include "Log.h"
#include "PasswordHasher.h"
#include "Util.h"

// Connection handshake, sign-in, sign-up and password change.

static const char* SERVER_VERSION = "2.0.0";

static bool isPasswordHash(const std::string& s)
{
	// The client sends SHA-256(password) as 64 hex digits, never the password itself.
	if (s.size() != 64) return false;
	for (char c : s)
		if (!isxdigit((unsigned char)c)) return false;
	return true;
}

static bool validUserName(const std::string& name)
{
	if (name.size() < 3 || name.size() > 20) return false;
	for (char c : name)
		if (!isalnum((unsigned char)c) && c != '.' && c != '_' && c != '-') return false;
	return true;
}

static bool validEmail(const std::string& email)
{
	size_t at = email.find('@');
	return email.size() <= 100 && at != std::string::npos && at > 0 && email.find('.', at) != std::string::npos
		&& email.find(' ') == std::string::npos && email.back() != '.';
}

Json App::hHello(Session&, const Json& req)
{
	std::string theirs = optString(req, "clientVersion", 50);
	std::string required = clientVersion();
	return Json::object({
		{ "serverVersion", SERVER_VERSION },
		{ "protocol", 2 },
		{ "clientVersion", required },
		{ "updateRequired", !required.empty() && !theirs.empty() && theirs != required },
	});
}

Json App::hPing(Session&, const Json&)
{
	return Json::object();
}

Json App::startSession(Session& s, long long userId)
{
	if (s.uid()) endSession(s); // signing in as someone else on the same connection

	std::string name, email, bio, avatar;
	bool admin;
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT name, IFNULL(email, ''), bio, avatar, is_admin FROM users WHERE id = ?1", userId);
		if (!st.next()) fail("not_found", "That account no longer exists.");
		name = st.s(0);
		email = st.s(1);
		bio = st.s(2);
		avatar = st.s(3);
		admin = st.i(4) != 0 || isAdminName(name);
		_db.exec("UPDATE users SET last_seen = ?1 WHERE id = ?2", nowMs(), userId);
	}
	{
		std::lock_guard<std::mutex> lock(s.m);
		s.userId = userId;
		s.name = name;
		s.isAdmin = admin;
		s.status = "online";
	}
	LOG_INFO(name << " signed in from " << s.ip << " (session " << s.id << ")");
	notifyPresence(userId);
	return Json::object({
		{ "user", userJson(userId, name, avatar) }, { "email", email }, { "bio", bio },
		{ "isAdmin", admin }, { "unread", unreadCount(userId) },
	});
}

Json App::hLogin(Session& s, const Json& req)
{
	std::string user = Util::trim(reqString(req, "user", 100));
	const std::string& password = reqString(req, "password", 100);
	std::string userKey = "u:" + Util::lower(user);
	std::string ipKey = "ip:" + s.ip;
	long long windowMs = (long long)_cfg.loginWindowMinutes * 60000;

	long long wait = std::max(_loginLimiter.lockedFor(userKey, _cfg.loginMaxFailures, windowMs),
		_loginLimiter.lockedFor(ipKey, _cfg.loginMaxFailures * 4, windowMs));
	if (wait > 0)
	{
		ApiError e("rate_limited", "Too many failed sign-in attempts. Try again in " + std::to_string((wait + 59) / 60) + " minute(s).");
		e.retryAfter = wait;
		throw e;
	}

	long long userId = 0;
	std::string stored, banReason;
	bool banned = false;
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT id, password, banned, ban_reason FROM users WHERE name = ?1 OR email = ?1", user);
		if (st.next())
		{
			userId = st.i(0);
			stored = st.s(1);
			banned = st.i(2) != 0;
			banReason = st.s(3);
		}
	}
	bool upgrade = false;
	if (!userId || !isPasswordHash(password) || !PasswordHasher::verify(password, stored, upgrade))
	{
		_loginLimiter.fail(userKey);
		_loginLimiter.fail(ipKey);
		LOG_INFO("Failed sign-in for '" << user << "' from " << s.ip);
		fail("bad_request", "Wrong user name or password.");
	}
	if (banned)
	{
		fail("banned", "This account has been suspended" + (banReason.empty() ? std::string(".") : ": " + banReason));
	}
	_loginLimiter.reset(userKey);
	if (upgrade)
	{
		auto lock = _db.lock();
		_db.exec("UPDATE users SET password = ?1 WHERE id = ?2", PasswordHasher::hash(password), userId);
	}
	return startSession(s, userId);
}

Json App::hSignup(Session& s, const Json& req)
{
	std::string name = Util::trim(reqString(req, "name", 100));
	std::string email = Util::trim(reqString(req, "email", 200));
	const std::string& password = reqString(req, "password", 100);
	if (!validUserName(name))
		fail("bad_request", "User names are 3-20 characters: letters, digits, '.', '_' or '-'.");
	if (!validEmail(email))
		fail("bad_request", "Enter a valid email address.");
	if (!isPasswordHash(password))
		fail("bad_request", "Invalid password.");

	std::string ipKey = "signup:" + s.ip;
	if (long long wait = _signupLimiter.lockedFor(ipKey, _cfg.signupsPerHourPerIp, 3600000))
	{
		ApiError e("rate_limited", "Too many new accounts from this network. Try again later.");
		e.retryAfter = wait;
		throw e;
	}

	long long userId;
	{
		auto lock = _db.lock();
		if (_db.scalar(0, "SELECT 1 FROM users WHERE name = ?1", name))
			fail("conflict", "That user name is taken.");
		if (_db.scalar(0, "SELECT 1 FROM users WHERE email = ?1", email))
			fail("conflict", "An account with this email already exists.");
		_db.exec("INSERT INTO users (name, email, password, is_admin, created_at) VALUES (?1, ?2, ?3, ?4, ?5)",
			name, email, PasswordHasher::hash(password), isAdminName(name), nowMs());
		userId = _db.lastInsertId();
	}
	_signupLimiter.fail(ipKey); // counts sign-ups per network
	LOG_INFO("New account '" << name << "' from " << s.ip);
	return startSession(s, userId);
}

Json App::hChangePassword(Session& s, const Json& req)
{
	std::string user = Util::trim(reqString(req, "user", 100));
	const std::string& oldPassword = reqString(req, "oldPassword", 100);
	const std::string& newPassword = reqString(req, "newPassword", 100);
	std::string userKey = "u:" + Util::lower(user);
	std::string ipKey = "ip:" + s.ip;
	long long windowMs = (long long)_cfg.loginWindowMinutes * 60000;
	if (long long wait = std::max(_loginLimiter.lockedFor(userKey, _cfg.loginMaxFailures, windowMs),
		_loginLimiter.lockedFor(ipKey, _cfg.loginMaxFailures * 4, windowMs)))
	{
		ApiError e("rate_limited", "Too many failed attempts. Try again in " + std::to_string((wait + 59) / 60) + " minute(s).");
		e.retryAfter = wait;
		throw e;
	}
	if (!isPasswordHash(newPassword)) fail("bad_request", "Invalid new password.");

	auto lock = _db.lock();
	auto st = _db.query("SELECT id, password FROM users WHERE name = ?1 OR email = ?1", user);
	bool upgrade = false;
	if (!st.next() || !isPasswordHash(oldPassword) || !PasswordHasher::verify(oldPassword, st.s(1), upgrade))
	{
		_loginLimiter.fail(userKey);
		_loginLimiter.fail(ipKey);
		fail("bad_request", "Wrong user name or current password.");
	}
	_loginLimiter.reset(userKey);
	_db.exec("UPDATE users SET password = ?1 WHERE id = ?2", PasswordHasher::hash(newPassword), st.i(0));
	LOG_INFO("Password changed for '" << user << "' from " << s.ip);
	return Json::object();
}

Json App::hLogout(Session& s, const Json&)
{
	endSession(s);
	return Json::object();
}
