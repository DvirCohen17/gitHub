#pragma once
#include <iostream>
#include <map>
#include <sstream>
#include <fstream>
#include <filesystem>
#include <windows.h>

class FileOperation
{
public:
    bool fileExists(const std::string& fileName);
    void createFile(const std::string& fileName, bool fileType);
    bool createDirectory(const std::string& path);
    bool deleteFile(const std::string& filePath);
    bool deleteDirectory(const std::string& dirPath);
    void getFilesInDirectory(const std::string& directoryPath, std::map<std::string, int>& files);
    std::string readFromFile(const std::string& filePath);
    void updateFile(const std::string& filename, const std::string& data);
};