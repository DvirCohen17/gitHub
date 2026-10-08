#include "Communicator.h"

// Per-project to-do list / issues.

void Communicator::loadCurrentIssues(SOCKET client_sock, int projectId)
{
	std::string lengthString;
	std::string repCode = std::to_string(MC_GET_CURRENT_PROJECT_ISSUES_RESP);
	std::list<Issues> currentIssues = m_database->getCurrentProjectIssues(projectId);

	for (auto issue : currentIssues)
	{
		lengthString = std::to_string(issue.data.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + issue.data;

		lengthString = std::to_string(issue.usersAssigment.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + issue.usersAssigment;

		lengthString = std::to_string(issue.date.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + issue.date;
		
		lengthString = std::to_string(std::to_string(issue.id).length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + std::to_string(issue.id);
	}

	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::loadCompletedIssues(SOCKET client_sock, int projectId)
{
	std::string lengthString;
	std::string repCode = std::to_string(MC_GET_COMPLETED_PROJECT_ISSUES_RESP);
	std::list<Issues> currentIssues = m_database->getCompletedProjectIssues(projectId);

	for (auto issue : currentIssues)
	{
		lengthString = std::to_string(issue.data.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + issue.data;

		lengthString = std::to_string(issue.usersAssigment.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + issue.usersAssigment;

		lengthString = std::to_string(issue.date.length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + issue.date;

		lengthString = std::to_string(std::to_string(issue.id).length());
		lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + std::to_string(issue.id);
	}

	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::getIssue(SOCKET client_sock, int issueId)
{
	std::string lengthString;
	std::string repCode = std::to_string(MC_GET_CURRENT_PROJECT_ISSUES_RESP);
	Issues currentIssues = m_database->getIssue(issueId);

	lengthString = std::to_string(currentIssues.data.length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	repCode += lengthString + currentIssues.data;

	lengthString = std::to_string(currentIssues.usersAssigment.length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	repCode += lengthString + currentIssues.usersAssigment;

	lengthString = std::to_string(currentIssues.date.length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	repCode += lengthString + currentIssues.date;

	lengthString = std::to_string(std::to_string(currentIssues.id).length());
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;
	repCode += lengthString + std::to_string(currentIssues.id);

	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::markIssueAsComplete(SOCKET client_sock, int issueId)
{
	std::string lengthString;
	std::string repCode = std::to_string(MC_MARK_TASK_AS_COMPLETED_RESP);

	m_database->MarkAsComplete(issueId);

	repCode += std::to_string(issueId);
	notifyAllclientsOnIssueWindow(repCode, client_sock, "");
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::markIssueAsNotComplete(SOCKET client_sock, int issueId)
{
	std::string lengthString;
	std::string repCode = std::to_string(MC_MARK_TASK_AS_NOT_COMPLETED_RESP);
	m_database->MarkAsNotComplete(issueId);

	repCode += std::to_string(issueId);
	notifyAllclientsOnIssueWindow(repCode, client_sock, "");
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::addTask(SOCKET client_sock, std::string issueData, std::string assignedPeople, std::string issueDate)
{
	std::string lengthString;
	std::string repCode = std::to_string(MC_ADD_TASK_RESP);
	m_database->AddIssue(m_clients[client_sock]->getProjectId(), issueData, issueDate, assignedPeople);
	m_clients[client_sock]->setWindow(TO_TO_LIST_PAGE);
	m_clients[client_sock]->setIssue(-1);
	std::string msg = " Assigned you to a new issue on - " + m_clients[client_sock]->getProjectName();
	notifyAllclientsOnIssueWindow(repCode, client_sock, msg);
	repCode = std::to_string(MC_BACK_TO_TO_DO_LIST_PAGE_RESP);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::modifyIssue(SOCKET client_sock, std::string issueData, std::string assignedPeople, std::string issueDate, int issueId)
{
	std::string lengthString;
	std::string repCode = std::to_string(MC_MODIFY_ISSUE_RESP);
	m_database->UpdateIssue(issueId, issueData, issueDate, assignedPeople);
	m_clients[client_sock]->setWindow(TO_TO_LIST_PAGE);
	m_clients[client_sock]->setIssue(-1);
	notifyAllclientsOnIssueWindow(repCode, client_sock, "");
	repCode = std::to_string(MC_BACK_TO_TO_DO_LIST_PAGE_RESP);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::deleteTask(SOCKET client_sock, int issueId)
{
	std::string lengthString;
	std::string repCode = std::to_string(MC_DELETE_TASK_RESP);
	m_database->DeleteIssue(issueId);

	repCode += std::to_string(issueId);
	notifyAllclientsOnIssueWindow(repCode, client_sock, "");
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}
