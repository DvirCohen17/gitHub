#include "Helper.h"

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
	const char* data = message.data();

	if (send(sc, data, message.size(), 0) == INVALID_SOCKET)
	{
		throw std::exception("Error while sending message to client");
	}
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
	if (bytes_recieved == INVALID_SOCKET)
	{
		std::string s = "Error while recieving from socket: ";
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