#include "Communicator.h"

// Window navigation and static resources (code styles, mail images).

const std::unordered_map<std::string, std::string> codeStyles = {
	{"C++-Mode", ".\\codeStyles\\C++-Mode.xshd"},
	{"Python-Mode", ".\\codeStyles\\Python-Mode.xshd"},
	{"C#-Mode", ".\\codeStyles\\C#-Mode.xshd"},
	{"Java-Mode", ".\\codeStyles\\Java-Mode.xshd"},
	{"JavaScript-Mode", ".\\codeStyles\\JavaScript-Mode.xshd"}
};

const std::unordered_map<std::string, std::string> mailImages = {
	{"mail_image_Blue", ".\\MailImages\\mail_image_Blue.png"},
	{"mail_image_CyberPunk", ".\\MailImages\\mail_image_CyberPunk.png"},
	{"mail_image_Green", ".\\MailImages\\mail_image_Green.png"},
	{"mail_image_Light", ".\\MailImages\\mail_image_Light.png"},
	{"mail_image_Matrix", ".\\MailImages\\mail_image_Matrix.png"},
	{"mail_image_Red", ".\\MailImages\\mail_image_Red.png"},
	{"mail_image_SolarizedDark", ".\\MailImages\\mail_image_SolarizedDark.png"},
	{"mail_image_SolarizedLight", ".\\MailImages\\mail_image_SolarizedLight.png"},
	{"mail_image_Vintage", ".\\MailImages\\mail_image_Vintage.png"}
};

void Communicator::backToMainPage(SOCKET client_sock)
{
	std::string repCode = std::to_string(MC_BACK_TO_HOME_PAGE_RESP);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
	m_clients[client_sock]->setWindow(HOME_PAGE);
}

void Communicator::backToProjectPage(SOCKET client_sock)
{
	m_clients[client_sock]->setWindow(PROJECT_PAGE);
}

void Communicator::getMailImages(SOCKET client_sock)
{
	std::string repCode = std::to_string(MC_GET_MAIL_IMAGES_RESP);

	// Send the number of images first
	size_t imageCount = mailImages.size();
	std::string imageCountStr = std::to_string(imageCount);
	repCode += std::string(3 - imageCountStr.length(), '0') + imageCountStr;

	// Send the initial message containing the response code and the number of images
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));

	// Loop through each image and send it individually
	for (const auto& file : mailImages)
	{
		std::string path = file.second;
		std::vector<unsigned char> data = fileOperationHandler.ReadImageFile(path);

		std::string fileName = file.first;
		std::string fileNameLengthStr = std::to_string(fileName.length());
		fileNameLengthStr = std::string(3 - fileNameLengthStr.length(), '0') + fileNameLengthStr;

		std::string dataLengthStr = std::to_string(data.size());
		dataLengthStr = std::string(5 - dataLengthStr.length(), '0') + dataLengthStr;

		// Prepare the message for this image
		std::string imageMessage = fileNameLengthStr + fileName + dataLengthStr;
		Helper::sendData(client_sock, BUFFER(imageMessage.begin(), imageMessage.end()));

		imageMessage = "";
		imageMessage.insert(imageMessage.end(), data.begin(), data.end());

		// Send the image message
		Helper::sendData(client_sock, BUFFER(imageMessage.begin(), imageMessage.end()));
	}
}

void Communicator::getCodeStyles(SOCKET client_sock)
{
	std::string repCode = std::to_string(MC_GET_CODE_STYLES_RESP);
	std::string path;
	std::string lengthString;
	for (auto file : codeStyles)
	{
		path = file.second;
		std::string data = fileOperationHandler.readFromFile(path);

		lengthString = std::to_string(file.first.length());
		lengthString = std::string(3 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + file.first;

		lengthString = std::to_string(data.length());
		lengthString = std::string(7 - lengthString.length(), '0') + lengthString;
		repCode += lengthString + data;
	}

	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::moveToSettings(SOCKET client_sock)
{
	std::string repCode = std::to_string(MC_SETTINGS_RESP);
	m_clients[client_sock]->setWindow(SETTINGS_PAGE);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::moveToMessages(SOCKET client_sock)
{
	std::string repCode = std::to_string(MC_MOVE_TO_MESSAGES_RESP);
	m_clients[client_sock]->setWindow(MESSAGES_PAGE);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::moveToToDoWindow(SOCKET client_sock)
{
	std::string repCode = std::to_string(MC_MOVE_TO_TO_DO_LIST_RESP);
	m_clients[client_sock]->setWindow(TO_TO_LIST_PAGE);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::moveToProjectWindow(SOCKET client_sock, int projectId)
{
	m_clients[client_sock]->setWindow(PROJECT_PAGE);

	Project project = m_database->getProject("", projectId);

	std::string lengthString = std::to_string((project.codeLan.length()));
	lengthString = std::string(5 - lengthString.length(), '0') + lengthString;

	std::string isEditable = "true";

	std::string projectIdStr = std::to_string(projectId);
	std::string IdlengthString = std::to_string(projectIdStr.length());
	IdlengthString = std::string(5 - IdlengthString.length(), '0') + IdlengthString;

	std::string projectNameLen = std::to_string(project.name.length());
	projectNameLen = std::string(5 - projectNameLen.length(), '0') + projectNameLen;

	std::string repCode = std::to_string(MC_MOVE_TO_PROJECT_PAGE_RESP) + projectNameLen + project.name
		+ IdlengthString + projectIdStr + lengthString + project.codeLan + isEditable;

	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}

void Communicator::moveToIssueData(SOCKET client_sock, int issueId)
{
	std::string repCode = std::to_string(MC_MOVE_TO_ISSUE_DATA_WINDOW_RESP);
	m_clients[client_sock]->setWindow(ISSUE_DATA_PAGE);
	m_clients[client_sock]->setIssue(issueId);
	repCode += std::to_string(issueId);
	Helper::sendData(client_sock, BUFFER(repCode.begin(), repCode.end()));
}
