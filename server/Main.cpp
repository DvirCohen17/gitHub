#include "App.h"
#include "Config.h"
#include "Db.h"
#include "Log.h"
#include "Net.h"
#include "Schema.h"
#include "WSAInitializer.h"
#include <filesystem>
#include <iostream>
#include <io.h>
#include <thread>

static App* g_app = nullptr;

// Ctrl+C / closing the console window: save edited files before the process ends.
static BOOL WINAPI onConsoleClose(DWORD)
{
	if (g_app)
	{
		LOG_INFO("Shutting down: saving files...");
		g_app->saveAll();
	}
	return FALSE;
}

static bool openDatabase(Db& db, const Config& cfg)
{
	namespace fs = std::filesystem;
	bool fresh = !fs::exists(cfg.database);
	db.open(cfg.database);
	Schema::create(db);
	if (fresh && !cfg.legacyDatabase.empty() && fs::exists(cfg.legacyDatabase))
	{
		LOG_INFO("First start of this version: importing " << cfg.legacyDatabase << " (the old file is left unchanged)...");
		try
		{
			Schema::importLegacy(db, cfg.legacyDatabase);
		}
		catch (const std::exception& e)
		{
			LOG_ERROR("Import failed: " << e.what());
			db.close();
			// Remove only the half-filled database this run created, so the import is retried next time.
			std::error_code ec;
			for (const char* suffix : { "", "-wal", "-shm" }) fs::remove(cfg.database + suffix, ec);
			return false;
		}
	}
	return true;
}

int main(int argc, char** argv)
{
	SetConsoleOutputCP(CP_UTF8);
	Config cfg;
	if (!cfg.load(argc, argv)) return 2;
	Log::init(cfg.logFile, Log::parseLevel(cfg.logLevel, Log::Level::Info));

	try
	{
		WSAInitializer wsa;
		Db db;
		if (!openDatabase(db, cfg)) return 1;

		int workers = cfg.workerThreads > 0 ? cfg.workerThreads : (int)std::max(2u, std::thread::hardware_concurrency());
		ThreadPool pool(workers);
		App app(cfg, db);
		NetServer net(pool, app, cfg.idleTimeoutSeconds);
		app.attach(&net);
		net.listen(cfg.port);

		g_app = &app;
		SetConsoleCtrlHandler(onConsoleClose, TRUE);

		std::thread netThread([&] { net.run(); });
		std::thread saveThread([&] { app.autosaveLoop(); });
		LOG_INFO("gitHub Demo server listening: " << cfg.describe() << ", " << workers << " worker threads");
		std::cout << "Type 'help' for admin commands." << std::endl;

		// Only draw a prompt for a person typing at a console (not when input is piped).
		const std::string prompt = _isatty(_fileno(stdin)) ? "> " : "";
		Log::setConsolePrompt(prompt);
		std::cout << prompt << std::flush;
		std::string line;
		while (true)
		{
			if (!std::getline(std::cin, line))
			{
				// No console (running as a service): keep running until the process is stopped.
				Log::setConsolePrompt("");
				while (true) std::this_thread::sleep_for(std::chrono::hours(1));
			}
			std::string command = line;
			for (char& c : command) c = (char)tolower((unsigned char)c);
			while (!command.empty() && isspace((unsigned char)command.back())) command.pop_back();
			if (command == "exit" || command == "quit") break;
			std::string output = app.consoleCommand(line);
			if (!output.empty()) std::cout << output << std::endl;
			std::cout << prompt << std::flush;
		}

		Log::setConsolePrompt("");
		LOG_INFO("Shutting down...");
		g_app = nullptr;
		app.stop();
		net.stop();
		netThread.join();
		saveThread.join();
		app.saveAll();
		pool.stop();
		LOG_INFO("Stopped.");
	}
	catch (const std::exception& e)
	{
		LOG_ERROR(e.what());
		return 1;
	}
	return 0;
}
