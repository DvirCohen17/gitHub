#pragma once
#include <sstream>
#include <string>

// Thread-safe logging to the console and a log file (rotated at 5 MB).
namespace Log
{
	enum class Level { Debug = 0, Info = 1, Warn = 2, Error = 3 };

	void init(const std::string& filePath, Level minLevel);
	Level parseLevel(const std::string& name, Level fallback);
	void write(Level level, const std::string& message);
	bool enabled(Level level);
	// While true, console output is printed above the admin prompt.
	void setConsolePrompt(const std::string& prompt);
}

#define LOG_AT(level, expr) do { if (Log::enabled(level)) { std::ostringstream _log_ss; _log_ss << expr; Log::write(level, _log_ss.str()); } } while (0)
#define LOG_DEBUG(expr) LOG_AT(Log::Level::Debug, expr)
#define LOG_INFO(expr) LOG_AT(Log::Level::Info, expr)
#define LOG_WARN(expr) LOG_AT(Log::Level::Warn, expr)
#define LOG_ERROR(expr) LOG_AT(Log::Level::Error, expr)
