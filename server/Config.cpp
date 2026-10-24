#include "Config.h"
#include "Json.h"
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>

static void defaultLanguages(std::map<std::string, RunLanguage>& languages)
{
	languages[".py"] = { "python:3.12-alpine", "python3 -u {file}" };
	languages[".js"] = { "node:20-alpine", "node {file}" };
	languages[".c"] = { "gcc:13", "cd {dir} && gcc -O2 -o /tmp/program *.c -lm && /tmp/program" };
	languages[".cpp"] = { "gcc:13", "cd {dir} && g++ -std=c++20 -O2 -o /tmp/program *.cpp && /tmp/program" };
	languages[".cc"] = languages[".cpp"];
	languages[".java"] = { "eclipse-temurin:21-jdk", "java {file}" };
}

static Json toJson(const Config& c)
{
	Json run = Json::object({
		{ "enabled", c.runEnabled },
		{ "docker", c.docker },
		{ "timeoutSeconds", c.runTimeoutSeconds },
		{ "memoryMb", c.runMemoryMb },
		{ "cpus", c.runCpus },
		{ "maxConcurrent", c.runMaxConcurrent },
		{ "maxOutputKb", c.runMaxOutputKb },
	});
	Json languages = Json::object();
	for (const auto& l : c.runLanguages)
	{
		languages.set(l.first, Json::object({ { "image", l.second.image }, { "command", l.second.command } }));
	}
	run.set("languages", languages);

	Json admins = Json::array();
	for (const auto& a : c.admins) admins.push(a);

	return Json::object({
		{ "port", c.port },
		{ "database", c.database },
		{ "legacyDatabase", c.legacyDatabase },
		{ "logFile", c.logFile },
		{ "logLevel", c.logLevel },
		{ "installerPackage", c.installerPackage },
		{ "clientVersion", c.clientVersion },
		{ "workerThreads", c.workerThreads },
		{ "saveIntervalSeconds", c.saveIntervalSeconds },
		{ "versionIntervalMinutes", c.versionIntervalMinutes },
		{ "maxVersionsPerFile", c.maxVersionsPerFile },
		{ "idleTimeoutSeconds", c.idleTimeoutSeconds },
		{ "admins", admins },
		{ "rateLimits", Json::object({
			{ "loginMaxFailures", c.loginMaxFailures },
			{ "loginWindowMinutes", c.loginWindowMinutes },
			{ "signupsPerHourPerIp", c.signupsPerHourPerIp },
			{ "requestsPerSecond", c.requestsPerSecond },
			{ "requestBurst", c.requestBurst },
			{ "chatMessagesPer10Seconds", c.chatMessagesPer10Seconds },
		}) },
		{ "run", run },
	});
}

static std::string pretty(const Json& j, int indent = 0)
{
	std::string pad(indent * 2, ' ');
	if (j.isObject() && j.size() > 0)
	{
		std::string out = "{\n";
		const auto& m = j.members();
		for (size_t i = 0; i < m.size(); i++)
		{
			out += pad + "  ";
			jsonQuote(out, m[i].first);
			out += ": " + pretty(m[i].second, indent + 1);
			out += i + 1 < m.size() ? ",\n" : "\n";
		}
		return out + pad + "}";
	}
	return j.dump();
}

template <class T> static void read(const Json& j, const char* key, T& target);
template <> void read(const Json& j, const char* key, int& target) { if (j[key].isInt()) target = (int)j[key].asInt(); }
template <> void read(const Json& j, const char* key, double& target) { if (j[key].isNumber()) target = j[key].asDouble(); }
template <> void read(const Json& j, const char* key, bool& target) { if (j[key].isBool()) target = j[key].asBool(); }
template <> void read(const Json& j, const char* key, std::string& target) { if (j[key].isString()) target = j[key].asString(); }

static void fromJson(Config& c, const Json& j)
{
	read(j, "port", c.port);
	read(j, "database", c.database);
	read(j, "legacyDatabase", c.legacyDatabase);
	read(j, "logFile", c.logFile);
	read(j, "logLevel", c.logLevel);
	read(j, "installerPackage", c.installerPackage);
	read(j, "clientVersion", c.clientVersion);
	read(j, "workerThreads", c.workerThreads);
	read(j, "saveIntervalSeconds", c.saveIntervalSeconds);
	read(j, "versionIntervalMinutes", c.versionIntervalMinutes);
	read(j, "maxVersionsPerFile", c.maxVersionsPerFile);
	read(j, "idleTimeoutSeconds", c.idleTimeoutSeconds);
	if (j["admins"].isArray())
	{
		c.admins.clear();
		for (const Json& a : j["admins"].items())
			if (a.isString()) c.admins.push_back(a.asString());
	}
	const Json& r = j["rateLimits"];
	read(r, "loginMaxFailures", c.loginMaxFailures);
	read(r, "loginWindowMinutes", c.loginWindowMinutes);
	read(r, "signupsPerHourPerIp", c.signupsPerHourPerIp);
	read(r, "requestsPerSecond", c.requestsPerSecond);
	read(r, "requestBurst", c.requestBurst);
	read(r, "chatMessagesPer10Seconds", c.chatMessagesPer10Seconds);
	const Json& run = j["run"];
	read(run, "enabled", c.runEnabled);
	read(run, "docker", c.docker);
	read(run, "timeoutSeconds", c.runTimeoutSeconds);
	read(run, "memoryMb", c.runMemoryMb);
	read(run, "cpus", c.runCpus);
	read(run, "maxConcurrent", c.runMaxConcurrent);
	read(run, "maxOutputKb", c.runMaxOutputKb);
	if (run["languages"].isObject())
	{
		c.runLanguages.clear();
		for (const auto& l : run["languages"].members())
		{
			RunLanguage lang;
			read(l.second, "image", lang.image);
			read(l.second, "command", lang.command);
			if (!lang.image.empty() && !lang.command.empty()) c.runLanguages[l.first] = lang;
		}
	}
}

static void usage()
{
	std::cout <<
		"Usage: server [--config server.json] [--port N] [--db file.sqlite] [--log-level debug|info|warn|error]\n";
}

bool Config::load(int argc, char** argv)
{
	defaultLanguages(runLanguages);

	// --config has to be known before reading the file; the rest override it.
	for (int i = 1; i + 1 < argc; i++)
	{
		if (std::string(argv[i]) == "--config") configPath = argv[i + 1];
	}

	std::ifstream in(configPath, std::ios::binary);
	if (in)
	{
		std::stringstream ss;
		ss << in.rdbuf();
		try
		{
			fromJson(*this, Json::parse(ss.str()));
		}
		catch (const std::exception& e)
		{
			std::cerr << configPath << ": " << e.what() << std::endl;
			return false;
		}
	}
	else
	{
		std::ofstream out(configPath, std::ios::binary);
		out << pretty(toJson(*this)) << "\n";
		std::cout << "Wrote default settings to " << configPath << std::endl;
	}

	for (int i = 1; i < argc; i++)
	{
		std::string arg = argv[i];
		bool hasValue = i + 1 < argc;
		if (arg == "--help" || arg == "-h")
		{
			usage();
			return false;
		}
		if (!hasValue)
		{
			std::cerr << "Missing value for " << arg << std::endl;
			usage();
			return false;
		}
		std::string value = argv[++i];
		if (arg == "--port")
		{
			try { port = std::stoi(value); }
			catch (...) { port = -1; }
			if (port <= 0 || port > 65535)
			{
				std::cerr << "Invalid port: " << value << std::endl;
				return false;
			}
		}
		else if (arg == "--db") database = value;
		else if (arg == "--log-level") logLevel = value;
		else if (arg == "--config") {}
		else
		{
			std::cerr << "Unknown option " << arg << std::endl;
			usage();
			return false;
		}
	}
	if (saveIntervalSeconds < 1) saveIntervalSeconds = 1;
	return true;
}

std::string Config::describe() const
{
	std::ostringstream ss;
	ss << "port " << port << ", database " << database << ", log " << (logFile.empty() ? "(console)" : logFile)
		<< ", autosave every " << saveIntervalSeconds << " s";
	return ss.str();
}
