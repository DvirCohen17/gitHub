#pragma once
#include <iostream>

class ClientHandler
{
private:
    int _id;                // ID field
    std::string file_name;
    std::string project_name;
    std::string window;
    std::string _userName;
    std::string _pass;
    std::string _email;

public:
    ClientHandler(int clientId, std::string userName, std::string email);  // Constructor with ID parameter
    ClientHandler();
    ~ClientHandler();

    int getId() const;     // Getter for the ID
    std::string getFileName() const;
    std::string getProjectName() const;
    std::string getPass() const;
    std::string getEmail() const;
    std::string getUsername() const;
    std::string getWindow() const;

    void setFileName(const std::string newName);
    void setUsername(const std::string newName);
    void setProjectName(const std::string newName);
    void setPass(const std::string newName);
    void setEmail(const std::string newName);
    void setWindow(const std::string newName);
    void setId(const int id);
};
