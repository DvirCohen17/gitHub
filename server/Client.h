#pragma once
#include <string>

class ClientHandler
{
private:
    int _id;
    int _fileId;
    std::string _fileName;
    std::string _fileContent;
    int _issueId;
    int _projectId;
    std::string _projectName;
    std::string window;
    std::string _userName;
    std::string _pass;
    std::string _email;

public:
    ClientHandler(int clientId, std::string userName, std::string email);
    ClientHandler();
    ~ClientHandler();

    int getId() const;
    std::string getFileName() const;
    std::string getFileContent() const;
    int getFileId() const;
    std::string getProjectName() const;
    int getProjectId() const;
    int getIssueId() const;
    std::string getPass() const;
    std::string getEmail() const;
    std::string getUsername() const;
    std::string getWindow() const;

    void setFile(const std::string& newFileName, const std::string&, int fileId);
    void setUsername(const std::string& newName);
    void setProject(const std::string& newProject, int projectId);
    void setPass(const std::string& newPass);
    void setEmail(const std::string& newEmail);
    void setWindow(const std::string& newWindow);
    void setId(int id);
    void setIssue(int id);

};
