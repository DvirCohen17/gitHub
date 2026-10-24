#include "Log.h"
#include <chrono>
#include <ctime>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <mutex>

namespace
{
	std::mutex g_mutex;
	std::ofstream g_file;
	std::string g_path;
	Log::Level g_level = Log::Level::Info;
	std::string g_prompt;
	const std::uintmax_t MAX_FILE_SIZE = 5 * 1024 * 1024;

	const char* levelName(Log::Level level)
	{
		switch (level)
		{
		case Log::Level::Debug: return "DEBUG";
		case Log::Level::Info: return "INFO ";
		case Log::Level::Warn: return "WARN ";
		default: return "ERROR";
		}
	}

	std::string timestamp()
	{
		auto now = std::chrono::system_clock::now();
		std::time_t t = std::chrono::system_clock::to_time_t(now);
		int ms = (int)(std::chrono::duration_cast<std::chrono::milliseconds>(now.time_since_epoch()).count() % 1000);
		std::tm tm{};
		localtime_s(&tm, &t);
		char buf[40];
		snprintf(buf, sizeof(buf), "%04d-%02d-%02d %02d:%02d:%02d.%03d",
			tm.tm_year + 1900, tm.tm_mon + 1, tm.tm_mday, tm.tm_hour, tm.tm_min, tm.tm_sec, ms);
		return buf;
	}

	void rotateIfNeeded()
	{
		if (g_path.empty() || !g_file.is_open()) return;
		if ((std::uintmax_t)g_file.tellp() < MAX_FILE_SIZE) return;
		g_file.close();
		std::error_code ec;
		std::filesystem::rename(g_path, g_path + ".1", ec); // replaces the previous .1
		g_file.open(g_path, std::ios::app);
	}
}

void Log::init(const std::string& filePath, Level minLevel)
{
	std::lock_guard<std::mutex> lock(g_mutex);
	g_level = minLevel;
	g_path = filePath;
	if (!filePath.empty())
	{
		std::error_code ec;
		std::filesystem::path parent = std::filesystem::path(filePath).parent_path();
		if (!parent.empty()) std::filesystem::create_directories(parent, ec);
		g_file.open(filePath, std::ios::app);
		if (!g_file.is_open())
		{
			std::cerr << "Can't open log file " << filePath << "; logging to the console only." << std::endl;
		}
	}
}

Log::Level Log::parseLevel(const std::string& name, Level fallback)
{
	if (name == "debug") return Level::Debug;
	if (name == "info") return Level::Info;
	if (name == "warn" || name == "warning") return Level::Warn;
	if (name == "error") return Level::Error;
	return fallback;
}

bool Log::enabled(Level level)
{
	return level >= g_level;
}

void Log::setConsolePrompt(const std::string& prompt)
{
	std::lock_guard<std::mutex> lock(g_mutex);
	g_prompt = prompt;
}

void Log::write(Level level, const std::string& message)
{
	std::string line = timestamp() + " " + levelName(level) + " " + message;
	std::lock_guard<std::mutex> lock(g_mutex);
	if (g_file.is_open())
	{
		g_file << line << '\n';
		g_file.flush();
		rotateIfNeeded();
	}
	// Clear the prompt line, print, then redraw the prompt.
	std::ostream& out = level >= Level::Warn ? std::cerr : std::cout;
	if (!g_prompt.empty()) std::cout << "\r" << std::string(g_prompt.size() + 40, ' ') << "\r";
	out << line << std::endl;
	if (!g_prompt.empty()) std::cout << g_prompt << std::flush;
}
