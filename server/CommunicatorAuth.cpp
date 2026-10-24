#include "Communicator.h"

// Login, logout, sign-up and password change.

void Communicator::login(SOCKET client_sock,
	std::string username, std::string pass, std::string mail)
{
	bool check = false;
	for (auto it = m_clients.begin(); it != m_clients.end(); /* no increment here */) {
		// Check if the socket is connected
		
		if (it->second->getUsername() != "")
		{
			if (!isSocketConnected(it->first)) {
				// If the socket is not connected, clean up and remove the client
				// (shutdown wakes that client's thread so it exits instead of blocking in recv)
				shutdown(it->first, SD_BOTH);
				delete it->second;
				it = m_clients.erase(it); // erase returns the next iterator
				continue;
			}
		}

		// Check if the user is already logged in
		if (it->second->getUsername() == username || it->second->getEmail() == mail) {
			throw std::exception("User already logged in");
			check = true;  // Indicate that the response has been sent
			break;  // Exit the loop
		}

		++it; // increment iterator here
	}

	// If the response has been sent, don't proceed to the second condition
	if (!check)
	{
		if (m_database->doesUserExist(username) && m_database->doesPasswordMatch(username, pass))
		{
			std::string repCode = std::to_string(MC_LOGIN_RESP);
			username = m_database->getUserName(mail, -1);
			mail = m_database->getEmail(username);
			ClientHandler* client_handler = new ClientHandler(m_database->getUserId(username), username, mail);
			client_handler->setWindow(HOME_PAGE);
			m_clients[client_sock] = client_handler;

			std::string lengthString = std::to_string(username.length());
			lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
			repCode += lengthString + username + std::to_string(client_handler->getId());

			Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

			repCode = std::to_string(MC_LOGIN_RESP) + username;
			
			std::string friendsList = m_database->getUserFriends(m_database->getUserId(username)).fiendsList;
			int currentIndex = 0;
			while (currentIndex < friendsList.length())
			{
				// Extract data length for each message
				int dataLength = std::stoi(friendsList.substr(currentIndex, 5));
				currentIndex += 5;

				// Extract data from the response
				std::string name = friendsList.substr(currentIndex, dataLength);
				currentIndex += dataLength;

				m_friends[username].push_back(name);
			}

			notifyAllfriends(repCode, client_sock);

			std::string dirName = username +"'s projects";
			_dirName = dirName;
		}
		else
		{
			throw std::exception("invalid username or password.");
		}
	}
}

void Communicator::logout(SOCKET client_sock)
{
	std::string repCode = std::to_string(MC_LOGOUT_RESP);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
	handleClientDisconnect(client_sock);
}

void Communicator::signUp(SOCKET client_sock, 
	std::string username, std::string pass, std::string mail)
{
	if (!m_database->doesUserExist(username) && !m_database->doesUserExist(mail))
	{
		m_database->addNewUser(username, pass, mail);
		int userId = m_database->getUserId(username);
		m_database->createProfile(username, mail, "", userId);
		std::string repCode = std::to_string(MC_SIGNUP_RESP);
		ClientHandler* client_handler = new ClientHandler(userId, username, mail);
		client_handler->setWindow(HOME_PAGE);
		m_clients[client_sock] = client_handler;
		std::string initialFileContent = repCode + std::to_string(client_handler->getId());
		Helper::sendData(client_sock, BUFFER(initialFileContent.begin(), initialFileContent.end()));

		repCode += username;

		std::string friendsList = m_database->getUserFriends(m_database->getUserId(username)).fiendsList;
		int currentIndex = 0;
		while (currentIndex < friendsList.length())
		{
			// Extract data length for each message
			int dataLength = std::stoi(friendsList.substr(currentIndex, 5));
			currentIndex += 5;

			// Extract data from the response
			std::string name = friendsList.substr(currentIndex, dataLength);
			currentIndex += dataLength;

			m_friends[username].push_back(name);
		}

		notifyAllfriends(repCode, client_sock);

		std::string dirName = username + "'s projects";
		_dirName = dirName;
	}
	else
	{
		throw std::exception("Invalid name or email");
	}
}

void Communicator::forgotPassword(SOCKET client_sock,
	std::string username, std::string pass, std::string oldPass, std::string mail)
{
	bool check = false;
	for (auto it = m_clients.begin(); it != m_clients.end(); ++it)
	{
		if (it->second->getUsername() == username || it->second->getEmail() == mail)
		{
			throw std::exception("User logged in, cant change password");
			check = true;  // Indicate that the response has been sent
			break;  // Exit the loop
		}
	}

	// If the response has been sent, don't proceed to the second condition
	if (!check)
	{
		if (m_database->doesUserExist(username) && m_database->doesPasswordMatch(username, oldPass))
		{
			std::string repCode = std::to_string(MC_FORGOT_PASSW_RESP);
			username = m_database->getUserName(mail, -1);
			mail = m_database->getEmail(username);
			m_database->changePassword(username, oldPass, pass);
			ClientHandler* client_handler = new ClientHandler(m_database->getUserId(username), username, mail);
			m_clients[client_sock] = client_handler;

			std::string lengthString = std::to_string(username.length());
			lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
			repCode += lengthString + username + std::to_string(client_handler->getId());

			Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

			repCode = std::to_string(MC_LOGIN_RESP) + username;
			notifyAllClients(repCode, client_sock, false);
		}
		else
		{
			throw std::exception("invalid username or password.");
		}
	}
}
