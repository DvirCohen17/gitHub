#include "Net.h"
#include "Db.h"
#include "Log.h"
#include <ws2tcpip.h>

#pragma comment(lib, "ws2_32.lib")

static const size_t MAX_PENDING_OUTPUT = 128 * 1024 * 1024; // a client that stops reading is dropped
static const int MAX_QUEUED_MESSAGES = 500;                  // stop reading from a client this far behind
static const size_t FILE_CHUNK = 256 * 1024;

std::string frameMessage(const std::string& payload)
{
	char header[16];
	snprintf(header, sizeof(header), "#%08zu", payload.size());
	return header + payload;
}

// ---- ThreadPool -------------------------------------------------------------

ThreadPool::ThreadPool(int threads)
{
	if (threads < 1) threads = 1;
	for (int i = 0; i < threads; i++)
		_threads.emplace_back(&ThreadPool::work, this);
}

ThreadPool::~ThreadPool()
{
	stop();
}

void ThreadPool::post(std::function<void()> job)
{
	{
		std::lock_guard<std::mutex> lock(_mutex);
		if (_stopping) return;
		_jobs.push_back(std::move(job));
	}
	_cv.notify_one();
}

void ThreadPool::stop()
{
	{
		std::lock_guard<std::mutex> lock(_mutex);
		if (_stopping) return;
		_stopping = true;
	}
	_cv.notify_all();
	for (auto& t : _threads)
		if (t.joinable()) t.join();
}

void ThreadPool::work()
{
	while (true)
	{
		std::function<void()> job;
		{
			std::unique_lock<std::mutex> lock(_mutex);
			_cv.wait(lock, [this] { return _stopping || !_jobs.empty(); });
			if (_stopping && _jobs.empty()) return;
			job = std::move(_jobs.front());
			_jobs.pop_front();
		}
		try
		{
			job();
		}
		catch (const std::exception& e)
		{
			LOG_ERROR("Unhandled error in worker: " << e.what());
		}
		catch (...)
		{
			LOG_ERROR("Unhandled error in worker");
		}
	}
}

// ---- Connection -------------------------------------------------------------

Connection::Connection(NetServer& server, SOCKET sock, uint64_t id, std::string ip)
	: id(id), ip(std::move(ip)), _server(server), _sock(sock), _lastActivity(nowMs())
{
}

Connection::~Connection()
{
	if (_sock != INVALID_SOCKET) closesocket(_sock);
}

void Connection::queueBytes(const std::string& bytes)
{
	bool needWake;
	{
		std::lock_guard<std::mutex> lock(_outMutex);
		if (_closed || _closeNow) return;
		_out.append(bytes);
		if (_out.size() - _outOffset > MAX_PENDING_OUTPUT)
		{
			LOG_WARN("Connection " << id << " (" << ip << ") isn't reading its messages; disconnecting");
			_closeNow = true;
		}
		else if (!_file.is_open())
		{
			flushLocked();
		}
		needWake = _closeNow || _out.size() > _outOffset;
	}
	if (needWake) _server.wake();
}

void Connection::send(const std::string& payload)
{
	queueBytes(frameMessage(payload));
}

void Connection::sendRaw(const std::string& bytes)
{
	queueBytes(bytes);
}

void Connection::sendFile(const std::string& path, long long size)
{
	{
		std::lock_guard<std::mutex> lock(_outMutex);
		_file.open(path, std::ios::binary);
		_fileRemaining = _file.is_open() ? size : 0;
	}
	_server.wake();
}

void Connection::close()
{
	{
		std::lock_guard<std::mutex> lock(_outMutex);
		_closeAfterFlush = true;
	}
	_server.wake();
}

void Connection::closeNow()
{
	{
		std::lock_guard<std::mutex> lock(_outMutex);
		_closeNow = true;
	}
	_server.wake();
}

size_t Connection::pendingBytes()
{
	std::lock_guard<std::mutex> lock(_outMutex);
	return _out.size() - _outOffset;
}

bool Connection::flushLocked()
{
	while (true)
	{
		if (_outOffset == _out.size() && _file.is_open())
		{
			// refill from the file being streamed
			_out.clear();
			_outOffset = 0;
			if (_fileRemaining <= 0)
			{
				_file.close();
				continue;
			}
			std::string chunk(FILE_CHUNK, '\0');
			_file.read(&chunk[0], (std::streamsize)std::min<long long>(FILE_CHUNK, _fileRemaining));
			std::streamsize got = _file.gcount();
			if (got <= 0)
			{
				_file.close();
				_fileRemaining = 0;
				continue;
			}
			chunk.resize((size_t)got);
			_fileRemaining -= got;
			_out = std::move(chunk);
		}
		if (_outOffset == _out.size())
		{
			_out.clear();
			_outOffset = 0;
			return true;
		}
		int sent = ::send(_sock, _out.data() + _outOffset, (int)std::min<size_t>(_out.size() - _outOffset, 1 << 20), 0);
		if (sent == SOCKET_ERROR)
		{
			return WSAGetLastError() == WSAEWOULDBLOCK;
		}
		_outOffset += sent;
		if (_outOffset > (1 << 20))
		{
			_out.erase(0, _outOffset);
			_outOffset = 0;
		}
	}
}

bool Connection::wantsWrite()
{
	std::lock_guard<std::mutex> lock(_outMutex);
	return _out.size() > _outOffset || _file.is_open();
}

// ---- NetServer ----------------------------------------------------------------

NetServer::NetServer(ThreadPool& pool, NetHandler& handler, int idleTimeoutSeconds)
	: _pool(pool), _handler(handler), _idleTimeoutMs(idleTimeoutSeconds * 1000)
{
}

NetServer::~NetServer()
{
	if (_listen != INVALID_SOCKET) closesocket(_listen);
	if (_wakeSock != INVALID_SOCKET) closesocket(_wakeSock);
}

void NetServer::listen(int port)
{
	_listen = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
	if (_listen == INVALID_SOCKET) throw std::runtime_error("Can't create the server socket");

	// Don't let another program bind the same port while we're using it.
	BOOL exclusive = TRUE;
	setsockopt(_listen, SOL_SOCKET, SO_EXCLUSIVEADDRUSE, (const char*)&exclusive, sizeof(exclusive));

	sockaddr_in addr{};
	addr.sin_family = AF_INET;
	addr.sin_port = htons((u_short)port);
	addr.sin_addr.s_addr = INADDR_ANY;
	if (bind(_listen, (sockaddr*)&addr, sizeof(addr)) == SOCKET_ERROR)
		throw std::runtime_error("Port " + std::to_string(port) + " is already in use (error " + std::to_string(WSAGetLastError()) + ")");
	if (::listen(_listen, SOMAXCONN) == SOCKET_ERROR)
		throw std::runtime_error("Can't listen on port " + std::to_string(port));
	u_long nonBlocking = 1;
	ioctlsocket(_listen, FIONBIO, &nonBlocking);

	// A UDP socket connected to itself: writing a byte wakes up WSAPoll.
	_wakeSock = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
	sockaddr_in loop{};
	loop.sin_family = AF_INET;
	loop.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
	int length = sizeof(loop);
	if (_wakeSock == INVALID_SOCKET || bind(_wakeSock, (sockaddr*)&loop, sizeof(loop)) == SOCKET_ERROR
		|| getsockname(_wakeSock, (sockaddr*)&loop, &length) == SOCKET_ERROR
		|| connect(_wakeSock, (sockaddr*)&loop, sizeof(loop)) == SOCKET_ERROR)
	{
		throw std::runtime_error("Can't create the wake-up socket");
	}
	ioctlsocket(_wakeSock, FIONBIO, &nonBlocking);
}

void NetServer::wake()
{
	char b = 1;
	::send(_wakeSock, &b, 1, 0);
}

void NetServer::stop()
{
	_stopping = true;
	wake();
}

size_t NetServer::connectionCount()
{
	std::lock_guard<std::mutex> lock(_connMutex);
	return _conns.size();
}

void NetServer::acceptAll()
{
	while (true)
	{
		sockaddr_in addr{};
		int length = sizeof(addr);
		SOCKET s = accept(_listen, (sockaddr*)&addr, &length);
		if (s == INVALID_SOCKET) return;

		u_long nonBlocking = 1;
		ioctlsocket(s, FIONBIO, &nonBlocking);
		BOOL on = TRUE;
		setsockopt(s, IPPROTO_TCP, TCP_NODELAY, (const char*)&on, sizeof(on));
		setsockopt(s, SOL_SOCKET, SO_KEEPALIVE, (const char*)&on, sizeof(on));

		char ip[INET_ADDRSTRLEN] = "?";
		inet_ntop(AF_INET, &addr.sin_addr, ip, sizeof(ip));
		ConnectionPtr conn = std::make_shared<Connection>(*this, s, _nextId++, ip);
		{
			std::lock_guard<std::mutex> lock(_connMutex);
			_conns[s] = conn;
		}
		LOG_DEBUG("Connection " << conn->id << " from " << ip);
		_handler.onOpen(conn);
	}
}

void NetServer::enqueue(const ConnectionPtr& conn, Connection::Item item)
{
	bool schedule = false;
	{
		std::lock_guard<std::mutex> lock(conn->_queueMutex);
		conn->_queue.push_back(std::move(item));
		conn->_queued++;
		if (!conn->_scheduled)
		{
			conn->_scheduled = true;
			schedule = true;
		}
	}
	if (schedule) _pool.post([this, conn] { drain(conn); });
}

// Runs on a worker: handles this connection's queued messages in order.
void NetServer::drain(ConnectionPtr conn)
{
	for (int handled = 0; handled < 32; handled++)
	{
		Connection::Item item;
		{
			std::lock_guard<std::mutex> lock(conn->_queueMutex);
			if (conn->_queue.empty())
			{
				conn->_scheduled = false;
				return;
			}
			item = std::move(conn->_queue.front());
			conn->_queue.pop_front();
			conn->_queued--;
		}
		try
		{
			switch (item.kind)
			{
			case Connection::Item::Message: _handler.onMessage(conn, item.payload); break;
			case Connection::Item::Legacy: _handler.onLegacyMessage(conn, item.payload); break;
			case Connection::Item::Closed: _handler.onClose(conn); break;
			}
		}
		catch (const std::exception& e)
		{
			LOG_ERROR("Connection " << conn->id << ": " << e.what());
		}
	}
	// Give other connections a turn, then continue.
	_pool.post([this, conn] { drain(conn); });
	wake(); // reading may have been paused while the queue was full
}

void NetServer::readFrom(const ConnectionPtr& conn)
{
	char buf[64 * 1024];
	while (true)
	{
		int got = recv(conn->_sock, buf, sizeof(buf), 0);
		if (got == 0)
		{
			closeConnection(conn);
			return;
		}
		if (got == SOCKET_ERROR)
		{
			if (WSAGetLastError() != WSAEWOULDBLOCK) closeConnection(conn);
			break;
		}
		conn->_in.append(buf, got);
		conn->_lastActivity = nowMs();
		if (got < (int)sizeof(buf)) break;
	}

	std::string& in = conn->_in;
	if (!conn->_framingKnown && !in.empty())
	{
		conn->_framingKnown = true;
		conn->_legacy = in[0] != '#';
	}
	if (conn->_legacy)
	{
		// Builds from before framing send "501" (version) or "500" (download).
		if (in.size() >= 3)
		{
			enqueue(conn, { Connection::Item::Legacy, in });
			in.clear();
		}
		return;
	}

	size_t pos = 0;
	while (in.size() - pos >= 9)
	{
		if (in[pos] != '#')
		{
			LOG_WARN("Connection " << conn->id << " (" << conn->ip << ") sent a malformed frame; disconnecting");
			closeConnection(conn);
			return;
		}
		size_t length = 0;
		for (int k = 1; k <= 8; k++)
		{
			char c = in[pos + k];
			if (c < '0' || c > '9')
			{
				LOG_WARN("Connection " << conn->id << " (" << conn->ip << ") sent a malformed frame; disconnecting");
				closeConnection(conn);
				return;
			}
			length = length * 10 + (c - '0');
		}
		if (length == 0 || length > MAX_MESSAGE_SIZE)
		{
			LOG_WARN("Connection " << conn->id << " (" << conn->ip << ") sent a frame of " << length << " bytes; disconnecting");
			closeConnection(conn);
			return;
		}
		if (in.size() - pos - 9 < length) break;
		enqueue(conn, { Connection::Item::Message, in.substr(pos + 9, length) });
		pos += 9 + length;
	}
	in.erase(0, pos);
}

void NetServer::closeConnection(const ConnectionPtr& conn)
{
	if (conn->_closed.exchange(true)) return;
	{
		std::lock_guard<std::mutex> lock(_connMutex);
		_conns.erase(conn->_sock);
	}
	{
		std::lock_guard<std::mutex> lock(conn->_outMutex);
		shutdown(conn->_sock, SD_BOTH);
		if (conn->_file.is_open()) conn->_file.close();
	}
	LOG_DEBUG("Connection " << conn->id << " closed");
	enqueue(conn, { Connection::Item::Closed, "" });
}

void NetServer::run()
{
	std::vector<WSAPOLLFD> fds;
	std::vector<ConnectionPtr> polled;
	long long lastIdleCheck = nowMs();

	while (!_stopping)
	{
		fds.clear();
		polled.clear();
		fds.push_back({ _listen, POLLRDNORM, 0 });
		fds.push_back({ _wakeSock, POLLRDNORM, 0 });
		{
			std::lock_guard<std::mutex> lock(_connMutex);
			for (auto& entry : _conns)
			{
				ConnectionPtr conn = entry.second;
				SHORT events = 0;
				if (conn->_queued < MAX_QUEUED_MESSAGES) events |= POLLRDNORM;
				if (conn->wantsWrite()) events |= POLLWRNORM;
				fds.push_back({ entry.first, events, 0 });
				polled.push_back(conn);
			}
		}

		int ready = WSAPoll(fds.data(), (ULONG)fds.size(), 1000);
		if (ready == SOCKET_ERROR)
		{
			LOG_ERROR("WSAPoll failed: " << WSAGetLastError());
			Sleep(100);
			continue;
		}

		if (fds[1].revents & POLLRDNORM)
		{
			char drainBuf[256];
			while (recv(_wakeSock, drainBuf, sizeof(drainBuf), 0) > 0) {}
		}
		if (fds[0].revents & POLLRDNORM) acceptAll();

		for (size_t k = 0; k < polled.size(); k++)
		{
			const ConnectionPtr& conn = polled[k];
			SHORT revents = fds[k + 2].revents;
			if (conn->_closed) continue;

			if (revents & (POLLRDNORM | POLLHUP)) readFrom(conn);
			if (conn->_closed) continue;
			if (revents & (POLLERR | POLLNVAL))
			{
				closeConnection(conn);
				continue;
			}

			bool closeIt = false;
			{
				std::lock_guard<std::mutex> lock(conn->_outMutex);
				if (!conn->flushLocked()) closeIt = true;
				else if (conn->_closeNow) closeIt = true;
				else if (conn->_closeAfterFlush && conn->_out.size() == conn->_outOffset && !conn->_file.is_open()) closeIt = true;
			}
			if (closeIt) closeConnection(conn);
		}

		long long now = nowMs();
		if (now - lastIdleCheck > 5000)
		{
			lastIdleCheck = now;
			for (const ConnectionPtr& conn : polled)
			{
				if (!conn->_closed && now - conn->_lastActivity > _idleTimeoutMs && !conn->wantsWrite())
				{
					LOG_INFO("Connection " << conn->id << " (" << conn->ip << ") timed out");
					closeConnection(conn);
				}
			}
		}
	}

	// Shutting down: close everyone.
	std::vector<ConnectionPtr> all;
	{
		std::lock_guard<std::mutex> lock(_connMutex);
		for (auto& entry : _conns) all.push_back(entry.second);
	}
	for (auto& conn : all) closeConnection(conn);
}
