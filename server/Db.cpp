#include "Db.h"
#include <chrono>

long long nowMs()
{
	return std::chrono::duration_cast<std::chrono::milliseconds>(
		std::chrono::system_clock::now().time_since_epoch()).count();
}

// ---- Stmt -----------------------------------------------------------------

Stmt::~Stmt()
{
	if (_stmt) sqlite3_finalize(_stmt);
}

void Stmt::bind(int index, std::nullptr_t) { sqlite3_bind_null(_stmt, index); }
void Stmt::bind(int index, int value) { sqlite3_bind_int64(_stmt, index, value); }
void Stmt::bind(int index, long long value) { sqlite3_bind_int64(_stmt, index, value); }
void Stmt::bind(int index, bool value) { sqlite3_bind_int(_stmt, index, value ? 1 : 0); }
void Stmt::bind(int index, double value) { sqlite3_bind_double(_stmt, index, value); }
void Stmt::bind(int index, const std::string& value)
{
	sqlite3_bind_text(_stmt, index, value.data(), (int)value.size(), SQLITE_TRANSIENT);
}
void Stmt::bind(int index, const char* value)
{
	sqlite3_bind_text(_stmt, index, value, -1, SQLITE_TRANSIENT);
}
void Stmt::bind(int index, const Blob& value)
{
	sqlite3_bind_blob(_stmt, index, value.data, (int)value.size, SQLITE_TRANSIENT);
}
void Stmt::bind(int index, const std::optional<long long>& value)
{
	if (value) sqlite3_bind_int64(_stmt, index, *value);
	else sqlite3_bind_null(_stmt, index);
}

bool Stmt::next()
{
	int rc = sqlite3_step(_stmt);
	if (rc == SQLITE_ROW) return true;
	if (rc == SQLITE_DONE) return false;
	_db->fail(sqlite3_sql(_stmt));
}

void Stmt::run()
{
	while (next()) {}
}

bool Stmt::isNull(int col) { return sqlite3_column_type(_stmt, col) == SQLITE_NULL; }
long long Stmt::i(int col) { return sqlite3_column_int64(_stmt, col); }
double Stmt::d(int col) { return sqlite3_column_double(_stmt, col); }

std::string Stmt::s(int col)
{
	const unsigned char* text = sqlite3_column_text(_stmt, col);
	int size = sqlite3_column_bytes(_stmt, col);
	return text ? std::string((const char*)text, size) : std::string();
}

std::vector<unsigned char> Stmt::blob(int col)
{
	const unsigned char* data = (const unsigned char*)sqlite3_column_blob(_stmt, col);
	int size = sqlite3_column_bytes(_stmt, col);
	return data ? std::vector<unsigned char>(data, data + size) : std::vector<unsigned char>();
}

// ---- Db -------------------------------------------------------------------

Db::~Db()
{
	close();
}

void Db::open(const std::string& path, bool readOnly)
{
	auto guard = lock();
	int flags = readOnly ? SQLITE_OPEN_READONLY : SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE;
	int rc = sqlite3_open_v2(path.c_str(), &_db, flags | SQLITE_OPEN_FULLMUTEX, nullptr);
	if (rc != SQLITE_OK)
	{
		std::string message = _db ? sqlite3_errmsg(_db) : "out of memory";
		sqlite3_close(_db);
		_db = nullptr;
		throw DbError("Can't open database " + path + ": " + message);
	}
	sqlite3_busy_timeout(_db, 5000);
	if (readOnly) return;
	execScript(
		"PRAGMA journal_mode = WAL;"
		"PRAGMA synchronous = NORMAL;"
		"PRAGMA foreign_keys = ON;");
}

void Db::close()
{
	auto guard = lock();
	if (_db)
	{
		sqlite3_close_v2(_db);
		_db = nullptr;
	}
}

sqlite3_stmt* Db::prepare(const char* sql)
{
	sqlite3_stmt* stmt = nullptr;
	if (sqlite3_prepare_v2(_db, sql, -1, &stmt, nullptr) != SQLITE_OK)
	{
		fail(sql);
	}
	return stmt;
}

void Db::execScript(const char* sql)
{
	char* error = nullptr;
	if (sqlite3_exec(_db, sql, nullptr, nullptr, &error) != SQLITE_OK)
	{
		std::string message = error ? error : "unknown error";
		sqlite3_free(error);
		throw DbError(message);
	}
}

bool Db::tableExists(const std::string& name)
{
	return scalar(0, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = ?1", name) == 1;
}

bool Db::columnExists(const std::string& table, const std::string& column)
{
	return scalar(0, "SELECT 1 FROM pragma_table_info(?1) WHERE name = ?2", table, column) == 1;
}

void Db::fail(const std::string& what)
{
	throw DbError(std::string(sqlite3_errmsg(_db)) + " [" + what + "]");
}

// ---- Transaction ------------------------------------------------------------

Transaction::Transaction(Db& db) : _db(db), _lock(db.lock())
{
	// Nested transactions (a helper called inside another transaction) just
	// join the outer one.
	_outer = sqlite3_get_autocommit(_db.handle()) != 0;
	if (_outer) _db.execScript("BEGIN IMMEDIATE");
}

Transaction::~Transaction()
{
	if (_outer && !_done)
	{
		try { _db.execScript("ROLLBACK"); }
		catch (...) {}
	}
}

void Transaction::commit()
{
	if (_outer && !_done) _db.execScript("COMMIT");
	_done = true;
}
