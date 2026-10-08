#include "Communicator.h"

// Live collaborative editing.
//
// Every open file has one authoritative LiveDocument. A client sends each edit
// tagged with the last revision it has seen. If other edits were applied since,
// the incoming edit is transformed past them (TextOperation::transform) so it
// still means what the user intended, then applied, acknowledged to the sender
// and broadcast to everyone else in the file. Clients do the mirror-image
// transform for their own unacknowledged edits, so all copies converge.

static const size_t MAX_HISTORY = 1000;

Communicator::LiveDocument& Communicator::liveDocument(int fileId)
{
	auto it = m_liveDocs.find(fileId);
	if (it != m_liveDocs.end())
	{
		return it->second;
	}

	auto data = m_filesData.find(fileId);
	std::string content = data != m_filesData.end() ? data->second : m_database->getFileContent(fileId);
	m_filesData[fileId] = content;

	LiveDocument& doc = m_liveDocs[fileId];
	doc.text = utf8ToWide(content);
	return doc;
}

void Communicator::sendFileSnapshot(SOCKET client_sock, int fileId, int code)
{
	LiveDocument& doc = liveDocument(fileId);
	std::string message = std::to_string(code) + std::to_string(fileId) + "|" +
		std::to_string(doc.revision) + "|" + wideToUtf8(doc.text);
	Helper::sendData(client_sock, BUFFER(message.begin(), message.end()));
}

void Communicator::handleFileOperation(SOCKET client_sock, const std::string& payload)
{
	// payload: "<fileId>|<revision>|<operation>"
	size_t bar1 = payload.find('|');
	size_t bar2 = bar1 == std::string::npos ? std::string::npos : payload.find('|', bar1 + 1);
	if (bar2 == std::string::npos)
	{
		throw std::runtime_error("Malformed edit");
	}
	int fileId = std::stoi(payload.substr(0, bar1));
	int revision = std::stoi(payload.substr(bar1 + 1, bar2 - bar1 - 1));

	ClientHandler* client = m_clients[client_sock];
	if (client->getFileId() != fileId)
	{
		return; // edit for a file this client already closed: drop it
	}
	if (!m_database->hasPermissionToProject(client->getProjectId(), client->getId()))
	{
		throw std::runtime_error("You can only view this project");
	}

	LiveDocument& doc = liveDocument(fileId);
	try
	{
		if (revision < doc.historyStart || revision > doc.revision)
		{
			throw std::invalid_argument("revision out of range");
		}

		TextOperation op = TextOperation::decode(payload.substr(bar2 + 1));
		for (size_t k = revision - doc.historyStart; k < doc.history.size(); k++)
		{
			op = TextOperation::transform(op, doc.history[k]).first;
		}
		doc.text = op.apply(doc.text); // throws if the edit doesn't fit the document
		doc.history.push_back(op);
		doc.revision++;
		if (doc.history.size() > MAX_HISTORY)
		{
			size_t drop = doc.history.size() - MAX_HISTORY / 2;
			doc.history.erase(doc.history.begin(), doc.history.begin() + drop);
			doc.historyStart += (int)drop;
		}

		m_filesData[fileId] = wideToUtf8(doc.text);
		m_FileUpdate[fileId] = true;
		needUpdate = true;

		std::string ack = std::to_string(MC_FILE_OP_ACK) + std::to_string(fileId) + "|" + std::to_string(doc.revision);
		Helper::sendData(client_sock, BUFFER(ack.begin(), ack.end()));

		std::string update = std::to_string(MC_FILE_OP_RESP) + std::to_string(fileId) + "|" +
			std::to_string(doc.revision) + "|" + op.encode();
		for (auto& other : m_clients)
		{
			if (other.first != client_sock && other.second->getFileId() == fileId)
			{
				try
				{
					Helper::sendData(other.first, BUFFER(update.begin(), update.end()));
				}
				catch (const std::exception&)
				{
					// that client is going away; its own thread cleans it up
				}
			}
		}
	}
	catch (const std::invalid_argument&)
	{
		// The client's copy can't be reconciled (too far behind, or corrupt):
		// send it the current text so it starts over from a known state.
		sendFileSnapshot(client_sock, fileId, MC_FILE_RESYNC);
	}
}
