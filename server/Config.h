#pragma once
#include <map>
#include <string>
#include <vector>

// How to run one kind of source file (picked by file extension).
struct RunLanguage
{
	std::string image;    // Docker image
	std::string command;  // shell command run in /code; {file} = entry file, {dir} = its folder
};

// Server settings: server.json next to the executable, overridden by the
// command line (--port, --db, --config, --log-level).
struct Config
{
	int port = 12345;
	std::string database = "githubdemo.sqlite";
	std::string legacyDatabase = "syncDBTemp.sqlite"; // imported once if `database` doesn't exist yet
	std::string logFile = "logs/server.log";
	std::string logLevel = "info";
	std::string installerPackage = "gitHubDemo.zip";
	std::string clientVersion;          // empty = use the version stored in the database
	int workerThreads = 0;              // 0 = number of CPU cores
	int saveIntervalSeconds = 3;        // how often edited files are written to the database
	int versionIntervalMinutes = 10;    // automatic version snapshot at most this often per file
	int maxVersionsPerFile = 200;
	int idleTimeoutSeconds = 120;       // close connections that send nothing (clients ping every 30 s)
	std::vector<std::string> admins;    // user names that are always server admins

	// Rate limits
	int loginMaxFailures = 5;           // failed logins per user name or IP ...
	int loginWindowMinutes = 15;        // ... within this window lock it for the same time
	int signupsPerHourPerIp = 5;
	int requestsPerSecond = 40;         // sustained, per connection
	int requestBurst = 400;
	int chatMessagesPer10Seconds = 10;

	// Running code
	bool runEnabled = true;
	std::string docker = "docker";
	int runTimeoutSeconds = 10;
	int runMemoryMb = 256;
	double runCpus = 1.0;
	int runMaxConcurrent = 4;
	int runMaxOutputKb = 256;
	std::map<std::string, RunLanguage> runLanguages; // ".py" -> ...

	std::string configPath = "server.json";

	// Loads configPath (writing a default file if there is none), then applies
	// command-line overrides. Returns false and prints usage on bad arguments.
	bool load(int argc, char** argv);
	std::string describe() const;
};
