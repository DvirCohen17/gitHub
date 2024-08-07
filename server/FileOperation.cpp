#include "FileOperation.h"

std::string FileOperation::readFromFile(const std::string& filePath)
{
    std::ifstream file(filePath);
    if (!file.is_open())
    {
        throw std::runtime_error("Failed to open file: " + filePath);
    }

    std::string content((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
    file.close();

    return content;
}

bool FileOperation::fileExists(const std::string& fileName)
{
    std::ifstream file(fileName);
    return file.good();
}

void FileOperation::createFile(const std::string& fileName, bool fileType)
{
    std::ofstream file(fileName);
    if (file.is_open())
    {
        if (fileType)
        {
            //file << "Initial content of the file.";

        }
        file.close();
    }
    else
    {
        std::cerr << "Error creating the file: " << fileName << std::endl;
    }
}

bool FileOperation::createDirectory(const std::string& path) {
    try {
        std::filesystem::create_directory(path);
        return true; // Return true if directory creation succeeded
    }
    catch (const std::filesystem::filesystem_error& ex) {
        // Handle the exception or error if directory creation fails
        // You might want to log the error or handle it in another way
        return false; // Return false on failure
    }
}

bool FileOperation::deleteFile(const std::string& filePath)
{
    if (std::remove(filePath.c_str()) == 0)
    {
        return true;
    }
    else
    {
        return false;
    }
}

bool FileOperation::deleteDirectory(const std::string& dirPath) {
    // Convert string to wide string for Windows API
    std::wstring wideDirPath = std::wstring(dirPath.begin(), dirPath.end());

    // Delete directory recursively
    if (!RemoveDirectoryW(wideDirPath.c_str())) {
        std::cerr << "Failed to delete directory: " << dirPath << std::endl;
        return false;
    }

    std::cout << "Directory deleted successfully: " << dirPath << std::endl;
    return true;
}

void FileOperation::reNameDirecroy(const std::string& oldDirName, const std::string& newDirName) {
    // Convert string to wide string for Windows API
    std::rename(oldDirName.c_str(), newDirName.c_str());
}

void FileOperation::reNameFile(const std::string& oldFileName, const std::string& newFileName) {
    // Convert string to wide string for Windows API
    std::rename(oldFileName.c_str(), newFileName.c_str());
}

void FileOperation::getFilesInDirectory(const std::string& directoryPath, std::map<std::string, int>& files) {
    try {
        
        for (const auto& entry : std::filesystem::directory_iterator(directoryPath)) {
            if (std::filesystem::is_regular_file(entry)) {
                std::string fileName = entry.path().filename().string();
                int fileSize = static_cast<int>(std::filesystem::file_size(entry.path()));

                // Check if the file already exists in the map
                auto it = files.find(fileName);
                if (it == files.end()) {
                    files[fileName] = 0;
                }
            }
        }
        
    }
    catch (const std::exception& e) {
        std::cerr << "Error reading directory: " << e.what() << std::endl;
    }
}

void FileOperation::updateFile(const std::string& filename, const std::string& data)
{
    // Open the file in out mode to truncate and write
    std::fstream file(filename, std::ios::out | std::ios::trunc);

    if (!file.is_open()) {
        throw std::runtime_error("Failed to open file: " + filename);
    }

    // Write the new data to the file
    file << data;

    // Close the file
    file.close();
}

size_t FileOperation::getImageSize(const std::string& filename) {
    std::ifstream file(filename, std::ios::binary | std::ios::ate); // Open file at the end

    if (!file.is_open()) {
        throw std::runtime_error("Failed to open file: " + filename);
    }

    std::streamsize fileSize = file.tellg(); // Get file size
    file.seekg(0, std::ios::beg); // Move file pointer back to the beginning

    if (fileSize < 0) {
        throw std::runtime_error("Failed to determine file size: " + filename);
    }

    file.close(); // Close the file

    return static_cast<size_t>(fileSize);
}

std::vector<unsigned char> FileOperation::ReadImageFile(const std::string& filename) {
    std::ifstream file(filename, std::ios::binary | std::ios::ate); // Open file at the end

    if (!file.is_open()) {
        throw std::runtime_error("Failed to open file: " + filename);
    }

    std::streamsize fileSize = file.tellg(); // Get file size
    file.seekg(0, std::ios::beg); // Move file pointer back to the beginning

    if (fileSize < 0) {
        throw std::runtime_error("Failed to determine file size: " + filename);
    }

    // Read the file into a vector
    std::vector<unsigned char> buffer(fileSize);
    if (!file.read(reinterpret_cast<char*>(buffer.data()), fileSize)) {
        throw std::runtime_error("Failed to read file: " + filename);
    }

    file.close(); // Close the file

    return buffer;
}