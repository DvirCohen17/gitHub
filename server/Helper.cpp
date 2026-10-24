#include "Helper.h"

static void sendAll(const SOCKET sc, const char* data, size_t length);

bool Helper::isSocketConnected(const SOCKET sockfd)
{
    if (sockfd == INVALID_SOCKET) {
        return false;
    }

    // Use a non-blocking I/O operation to test the socket
    fd_set readfds;
    FD_ZERO(&readfds);
    FD_SET(sockfd, &readfds);

    timeval timeout = { 0 };
    int result = select(0, &readfds, nullptr, nullptr, &timeout);

    if (result == SOCKET_ERROR) {
        // Handle socket error
        int error = WSAGetLastError();
        if (error == WSAENOTCONN || error == WSAECONNABORTED || error == WSAECONNRESET) {
            return false;
        }
        return false;
    }

    if (result == 0) {
        // No data available for read, but socket may still be alive
        return true;
    }

    // Check if the socket is readable
    char buffer[1];
    int bytesRead = recv(sockfd, buffer, sizeof(buffer), MSG_PEEK);

    if (bytesRead == SOCKET_ERROR) {
        int error = WSAGetLastError();
        if (error == WSAENOTCONN || error == WSAECONNABORTED || error == WSAECONNRESET) {
            return false;
        }
        return false;
    }

    // If recv returns 0, the connection is closed
    if (bytesRead == 0) {
        return false;
    }

    // Socket is connected and functioning
    return true;
}

void Helper::sendData(const SOCKET sc, const BUFFER message)
{
	std::string framed = frame(std::string(message.begin(), message.end()));
	sendAll(sc, framed.data(), framed.size());
}

// ---- Message framing -------------------------------------------------------
// TCP is a byte stream, so every message is sent as
//   '#' + 8 ASCII digits (payload length in bytes) + payload
// which lets the receiver read exactly one whole message, however long it is
// and however many messages arrive back to back.

static const char FRAME_MARKER = '#';
static const int FRAME_LENGTH_DIGITS = 8;

static void sendAll(const SOCKET sc, const char* data, size_t length)
{
	while (length > 0)
	{
		int sent = send(sc, data, (int)length, 0);
		if (sent == SOCKET_ERROR)
		{
			throw std::exception("Error while sending message to client");
		}
		data += sent;
		length -= sent;
	}
}

static void recvAll(const SOCKET sc, char* data, size_t length)
{
	while (length > 0)
	{
		int got = recv(sc, data, (int)length, 0);
		if (got == 0)
		{
			throw std::exception("Error while receiving from socket: connection closed");
		}
		if (got == SOCKET_ERROR)
		{
			throw std::exception("Error while receiving from socket");
		}
		data += got;
		length -= got;
	}
}

std::string Helper::frame(const std::string& payload)
{
	std::string length = std::to_string(payload.size());
	return FRAME_MARKER + std::string(FRAME_LENGTH_DIGITS - length.size(), '0') + length + payload;
}

void Helper::sendRaw(const SOCKET sc, const std::string& data)
{
	sendAll(sc, data.data(), data.size());
}

BUFFER Helper::receiveMessage(const SOCKET sc, bool& legacyUnframed)
{
	char first = 0;
	int got = recv(sc, &first, 1, 0);
	if (got == 0)
	{
		return BUFFER(); // orderly disconnect
	}
	if (got == SOCKET_ERROR)
	{
		throw std::exception("Error while receiving from socket");
	}

	if (first != FRAME_MARKER)
	{
		// Clients/installers from before framing send bare requests. Only the
		// version check and installer download are accepted that way (see
		// handleNewClient), which lets old installs discover they need to update.
		legacyUnframed = true;
		BUFFER rest = getPartFromSocket(sc, 511);
		rest.insert(rest.begin(), first);
		return rest;
	}

	legacyUnframed = false;
	char digits[FRAME_LENGTH_DIGITS + 1] = { 0 };
	recvAll(sc, digits, FRAME_LENGTH_DIGITS);
	for (int i = 0; i < FRAME_LENGTH_DIGITS; i++)
	{
		if (digits[i] < '0' || digits[i] > '9')
		{
			throw std::exception("Error while receiving from socket: malformed frame");
		}
	}
	size_t length = std::stoul(digits);
	if (length == 0 || length > MAX_MESSAGE_SIZE)
	{
		throw std::exception("Error while receiving from socket: bad frame length");
	}

	BUFFER payload(length);
	recvAll(sc, payload.data(), length);
	return payload;
}

BUFFER Helper::getPartFromSocket(const SOCKET sc, const int bytesNum)
{
	return getPartFromSocket(sc, bytesNum, 0);
}

BUFFER Helper::getPartFromSocket(const SOCKET sc, const int bytesNum, const int flags)
{
	if (bytesNum == 0)
	{
		return BUFFER();
	}

	BUFFER recieved(bytesNum);
	int bytes_recieved = recv(sc, &recieved[0], bytesNum, flags);
	if (bytes_recieved == SOCKET_ERROR)
	{
		// Must say "receiving": IsConnectionError() matches on this text.
		std::string s = "Error while receiving from socket: ";
		s += std::to_string(sc);
		throw std::exception(s.c_str());
	}
	recieved.resize(bytes_recieved);
	return recieved;
}

bool Helper::IsConnectionError(const std::exception& e)
{
	// Check if the exception message contains a specific string indicating a connection error
	return (std::string(e.what()).find("Error while receiving from socket") != std::string::npos) ||
		(std::string(e.what()).find("Error while sending message to client") != std::string::npos);
}