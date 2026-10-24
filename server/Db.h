#pragma once
#include "sqlite3.h"
#include <cstdint>
#include <mutex>
#include <optional>
#include <stdexcept>
#include <string>
#include <vector>

// Thin SQLite wrapper. Every value goes through a bound parameter (prepared
// statement); no SQL is ever built from user input.
//
// One connection is shared by all threads. Hold Db::lock() (or a Transaction)
// for as long as you use a statement.

struct DbError : std::runtime_error
{
	using std::runtime_error::runtime_error;
};

struct Blob
{
	const void* data;
	size_t size;
};

class Db;

class Stmt
{
public:
	Stmt(Db& db, sqlite3_stmt* stmt) : _db(&db), _stmt(stmt) {}
	Stmt(Stmt&& other) noexcept : _db(other._db), _stmt(other._stmt) { other._stmt = nullptr; }
	Stmt(const Stmt&) = delete;
	Stmt& operator=(const Stmt&) = delete;
	~Stmt();

	void bind(int index, std::nullptr_t);
	void bind(int index, int value);
	void bind(int index, long long value);
	void bind(int index, bool value);
	void bind(int index, double value);
	void bind(int index, const std::string& value);
	void bind(int index, const char* value);
	void bind(int index, const Blob& value);
	void bind(int index, const std::optional<long long>& value);

	// Steps to the next row; false when there are no more.
	bool next();
	// Runs a statement that returns no rows.
	void run();

	bool isNull(int col);
	long long i(int col);
	double d(int col);
	std::string s(int col);
	std::vector<unsigned char> blob(int col);

private:
	Db* _db;
	sqlite3_stmt* _stmt;
};

class Db
{
public:
	~Db();
	void open(const std::string& path, bool readOnly = false);
	void close();
	bool isOpen() const { return _db != nullptr; }

	std::unique_lock<std::recursive_mutex> lock() { return std::unique_lock<std::recursive_mutex>(_mutex); }

	// Prepares sql and binds args to ?1, ?2, ...
	template <class... Args>
	Stmt query(const char* sql, const Args&... args)
	{
		Stmt st(*this, prepare(sql));
		int index = 1;
		(st.bind(index++, args), ...);
		return st;
	}

	// Runs a statement that returns no rows. Returns the number of changed rows.
	template <class... Args>
	int exec(const char* sql, const Args&... args)
	{
		query(sql, args...).run();
		return sqlite3_changes(_db);
	}

	// First column of the first row, or fallback when there is no row.
	template <class... Args>
	long long scalar(long long fallback, const char* sql, const Args&... args)
	{
		Stmt st = query(sql, args...);
		return st.next() && !st.isNull(0) ? st.i(0) : fallback;
	}

	template <class... Args>
	std::optional<std::string> text(const char* sql, const Args&... args)
	{
		Stmt st = query(sql, args...);
		if (st.next() && !st.isNull(0)) return st.s(0);
		return std::nullopt;
	}

	void execScript(const char* sql); // several statements, no parameters
	long long lastInsertId() { return sqlite3_last_insert_rowid(_db); }
	bool tableExists(const std::string& name);
	bool columnExists(const std::string& table, const std::string& column);

	[[noreturn]] void fail(const std::string& what);
	sqlite3* handle() { return _db; }

private:
	sqlite3_stmt* prepare(const char* sql);

	sqlite3* _db = nullptr;
	std::recursive_mutex _mutex;
};

// BEGIN IMMEDIATE ... COMMIT; rolls back unless commit() was called.
class Transaction
{
public:
	explicit Transaction(Db& db);
	~Transaction();
	void commit();

private:
	Db& _db;
	std::unique_lock<std::recursive_mutex> _lock;
	bool _done = false;
	bool _outer = false;
};

long long nowMs();
