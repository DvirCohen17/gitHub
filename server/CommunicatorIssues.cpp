#include "Communicator.h"
#include <set>

// Per-project to-do list / issues.
//
// Wire format of one task (lists and single task):
//   <5-digit len><text> <5-digit len><assignees> <5-digit len><due date> <5-digit len><id>
// where assignees is a run of <5-digit len><user name>. Lengths are byte counts.

static std::string field(const std::string& value)
{
	std::string length = std::to_string(value.size());
	return std::string(5 - length.size(), '0') + length + value;
}

static std::string encodeTask(const Issues& issue)
{
	return field(issue.data) + field(issue.usersAssigment) + field(issue.date) + field(std::to_string(issue.id));
}

static std::vector<std::string> parseAssignees(const std::string& assignedPeople)
{
	std::vector<std::string> names;
	size_t index = 0;
	while (index + 5 <= assignedPeople.size())
	{
		size_t length = std::stoul(assignedPeople.substr(index, 5));
		index += 5;
		if (index + length > assignedPeople.size())
		{
			throw std::runtime_error("Malformed assignee list.");
		}
		names.push_back(assignedPeople.substr(index, length));
		index += length;
	}
	return names;
}

// Throws unless the task exists and belongs to a project this client can edit.
Issues Communicator::requireEditableTask(SOCKET client_sock, int issueId)
{
	Issues issue = m_database->getIssue(issueId);
	ClientHandler* client = m_clients[client_sock];
	if (issue.id != issueId || issue.projectId != client->getProjectId()
		|| !m_database->hasPermissionToProject(issue.projectId, client->getId()))
	{
		throw std::runtime_error("That task isn't part of your project.");
	}
	return issue;
}

void Communicator::validateTask(int projectId, const std::string& text, const std::string& assignedPeople, const std::string& date)
{
	if (text.find_first_not_of(" \t\r\n") == std::string::npos || text.size() > 1000)
	{
		throw std::runtime_error("A task needs a description (up to 1000 characters).");
	}
	if (date.empty() || date.size() > 20)
	{
		throw std::runtime_error("Pick a due date.");
	}
	std::map<std::string, std::string> participants = m_database->getProjectParticipants(projectId);
	for (const std::string& name : parseAssignees(assignedPeople))
	{
		if (participants.find(name) == participants.end())
		{
			throw std::runtime_error(name + " isn't a member of this project.");
		}
	}
}

// Tell everyone looking at this project's to-do list (including the sender) what changed.
void Communicator::notifyTaskChange(SOCKET client_sock, const std::string& update)
{
	int projectId = m_clients[client_sock]->getProjectId();
	for (auto& pair : m_clients)
	{
		if (pair.second->getWindow() == TO_TO_LIST_PAGE && pair.second->getProjectId() == projectId)
		{
			try
			{
				Helper::sendData(pair.first, BUFFER(update.begin(), update.end()));
			}
			catch (const std::exception&) {}
		}
	}
}

// Inbox message for each newly assigned user (not the sender), online or not.
void Communicator::notifyAssignees(SOCKET client_sock, const std::string& assignedPeople, const std::string& previouslyAssigned, const std::string& msg)
{
	std::vector<std::string> before = parseAssignees(previouslyAssigned);
	std::set<std::string> alreadyAssigned(before.begin(), before.end());
	int senderId = m_clients[client_sock]->getId();

	for (const std::string& name : parseAssignees(assignedPeople))
	{
		if (alreadyAssigned.count(name))
		{
			continue;
		}
		int userId = m_database->getUserId(name);
		if (userId == senderId)
		{
			continue;
		}
		m_database->AddMsg(senderId, userId, msg, 0, -1);
		for (auto& pair : m_clients)
		{
			if (pair.second->getId() == userId && pair.second->getWindow() == HOME_PAGE)
			{
				std::string ping = std::to_string(MC_SEND_MSG_RESP);
				try
				{
					Helper::sendData(pair.first, BUFFER(ping.begin(), ping.end()));
				}
				catch (const std::exception&) {}
			}
		}
	}
}

void Communicator::loadCurrentIssues(SOCKET client_sock, int projectId)
{
	std::string repCode = std::to_string(MC_GET_CURRENT_PROJECT_ISSUES_RESP);
	for (const Issues& issue : m_database->getCurrentProjectIssues(projectId))
	{
		repCode += encodeTask(issue);
	}
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::loadCompletedIssues(SOCKET client_sock, int projectId)
{
	std::string repCode = std::to_string(MC_GET_COMPLETED_PROJECT_ISSUES_RESP);
	for (const Issues& issue : m_database->getCompletedProjectIssues(projectId))
	{
		repCode += encodeTask(issue);
	}
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::getIssue(SOCKET client_sock, int issueId)
{
	Issues issue = requireEditableTask(client_sock, issueId);
	std::string repCode = std::to_string(MC_GET_ISSUE_RESP) + encodeTask(issue);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::markIssueAsComplete(SOCKET client_sock, int issueId)
{
	requireEditableTask(client_sock, issueId);
	m_database->MarkAsComplete(issueId);
	notifyTaskChange(client_sock, std::to_string(MC_MARK_TASK_AS_COMPLETED_RESP) + std::to_string(issueId));
}

void Communicator::markIssueAsNotComplete(SOCKET client_sock, int issueId)
{
	requireEditableTask(client_sock, issueId);
	m_database->MarkAsNotComplete(issueId);
	notifyTaskChange(client_sock, std::to_string(MC_MARK_TASK_AS_NOT_COMPLETED_RESP) + std::to_string(issueId));
}

void Communicator::addTask(SOCKET client_sock, std::string issueData, std::string assignedPeople, std::string issueDate)
{
	ClientHandler* client = m_clients[client_sock];
	if (!m_database->hasPermissionToProject(client->getProjectId(), client->getId()))
	{
		throw std::runtime_error("Only project members can add tasks.");
	}
	validateTask(client->getProjectId(), issueData, assignedPeople, issueDate);

	m_database->AddIssue(client->getProjectId(), issueData, issueDate, assignedPeople);
	client->setWindow(TO_TO_LIST_PAGE);
	client->setIssue(-1);

	notifyAssignees(client_sock, assignedPeople, "", " assigned you a task in " + client->getProjectName());
	notifyTaskChange(client_sock, std::to_string(MC_ADD_TASK_RESP));

	std::string repCode = std::to_string(MC_BACK_TO_TO_DO_LIST_PAGE_RESP);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::modifyIssue(SOCKET client_sock, std::string issueData, std::string assignedPeople, std::string issueDate, int issueId)
{
	Issues before = requireEditableTask(client_sock, issueId);
	ClientHandler* client = m_clients[client_sock];
	validateTask(client->getProjectId(), issueData, assignedPeople, issueDate);

	m_database->UpdateIssue(issueId, issueData, issueDate, assignedPeople);
	client->setWindow(TO_TO_LIST_PAGE);
	client->setIssue(-1);

	notifyAssignees(client_sock, assignedPeople, before.usersAssigment, " assigned you a task in " + client->getProjectName());
	notifyTaskChange(client_sock, std::to_string(MC_MODIFY_ISSUE_RESP));

	std::string repCode = std::to_string(MC_BACK_TO_TO_DO_LIST_PAGE_RESP);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::deleteTask(SOCKET client_sock, int issueId)
{
	requireEditableTask(client_sock, issueId);
	m_database->DeleteIssue(issueId);
	notifyTaskChange(client_sock, std::to_string(MC_DELETE_TASK_RESP) + std::to_string(issueId));
}
