#pragma once
#include <winsock2.h>
#include <atomic>
#include <condition_variable>
#include <deque>
#include <fstream>
#include <functional>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

// Largest message accepted from a client (a project import is the biggest).
const size_t MAX_MESSAGE_SIZE = 64 * 1024 * 1024;

// Fixed number of worker threads running queued jobs.
class ThreadPool
{
public:
	explicit ThreadPool(int threads);
	~ThreadPool();
	void post(std::function<void()> job);
	void stop();
	int size() const { return (int)_threads.size(); }

private:
	void work();
	std::vector<std::thread> _threads;
	std::deque<std::function<void()>> _jobs;
	std::mutex _mutex;
	std::condition_variable _cv;
	bool _stopping = false;
};

class NetServer;

// One client connection. Messages from it are handled one at a time, in order,
// on the thread pool; sending is safe from any thread and never blocks.
class Connection : public std::enable_shared_from_this<Connection>
{
public:
	Connection(NetServer& server, SOCKET sock, uint64_t id, std::string ip);
	~Connection();

	const uint64_t id;
	const std::string ip;

	void send(const std::string& payload);  // one framed message
	void sendRaw(const std::string& bytes); // unframed (replies to pre-framing clients)
	void sendFile(const std::string& path, long long size); // streams raw bytes after whatever is queued
	void close();                            // after everything queued has been sent
	void closeNow();
	bool isClosed() const { return _closed; }
	size_t pendingBytes();

	std::shared_ptr<void> context;           // set by the application (its session object)

private:
	friend class NetServer;
	void queueBytes(const std::string& bytes);
	bool flushLocked();                      // false on a socket error
	bool wantsWrite();

	NetServer& _server;
	SOCKET _sock;
	std::atomic<bool> _closed{ false };
	std::atomic<long long> _lastActivity;

	// outgoing
	std::mutex _outMutex;
	std::string _out;
	size_t _outOffset = 0;
	std::ifstream _file;
	long long _fileRemaining = 0;
	bool _closeAfterFlush = false;
	bool _closeNow = false;

	// incoming
	std::string _in;
	bool _framingKnown = false;
	bool _legacy = false;

	// in-order processing
	struct Item
	{
		enum Kind { Message, Legacy, Closed } kind;
		std::string payload;
	};
	std::mutex _queueMutex;
	std::deque<Item> _queue;
	bool _scheduled = false;
	std::atomic<int> _queued{ 0 };
};

using ConnectionPtr = std::shared_ptr<Connection>;

class NetHandler
{
public:
	virtual ~NetHandler() = default;
	virtual void onOpen(const ConnectionPtr& conn) = 0;
	virtual void onMessage(const ConnectionPtr& conn, const std::string& payload) = 0;
	// A request from a client that predates framing (bare "501" / "500").
	virtual void onLegacyMessage(const ConnectionPtr& conn, const std::string& payload) = 0;
	virtual void onClose(const ConnectionPtr& conn) = 0;
};

class NetServer
{
public:
	NetServer(ThreadPool& pool, NetHandler& handler, int idleTimeoutSeconds);
	~NetServer();

	void listen(int port);   // throws on failure
	void run();              // blocks until stop()
	void stop();
	void wake();
	size_t connectionCount();

private:
	friend class Connection;
	void acceptAll();
	void readFrom(const ConnectionPtr& conn);
	void enqueue(const ConnectionPtr& conn, Connection::Item item);
	void drain(ConnectionPtr conn);
	void closeConnection(const ConnectionPtr& conn);

	ThreadPool& _pool;
	NetHandler& _handler;
	int _idleTimeoutMs;
	SOCKET _listen = INVALID_SOCKET;
	SOCKET _wakeSock = INVALID_SOCKET;
	std::atomic<bool> _stopping{ false };
	std::mutex _connMutex;
	std::map<SOCKET, ConnectionPtr> _conns;
	uint64_t _nextId = 1;
};

std::string frameMessage(const std::string& payload);
