#include "CodeRunner.h"
#include "App.h"
#include "Db.h"
#include "Log.h"
#include "Util.h"
#include <windows.h>
#include <filesystem>
#include <fstream>
#include <thread>

namespace fs = std::filesystem;

struct CodeRunner::Run
{
	std::string id;
	uint64_t sessionId = 0;
	std::string entry;
	std::string input;
	RunLanguage language;
	fs::path dir;
	std::atomic<bool> stopRequested{ false };
	std::mutex processMutex;
	HANDLE process = nullptr;
};

// Quotes one argument the way CommandLineToArgvW reads it back.
static std::string quoteArg(const std::string& arg)
{
	if (!arg.empty() && arg.find_first_of(" \t\"") == std::string::npos) return arg;
	std::string out = "\"";
	size_t backslashes = 0;
	for (char c : arg)
	{
		if (c == '\\')
		{
			backslashes++;
			continue;
		}
		if (c == '"')
		{
			out.append(backslashes * 2 + 1, '\\');
			out += '"';
		}
		else
		{
			out.append(backslashes, '\\');
			out += c;
		}
		backslashes = 0;
	}
	out.append(backslashes * 2, '\\');
	return out + "\"";
}

// Single-quotes text for sh.
static std::string shellQuote(const std::string& s)
{
	std::string out = "'";
	for (char c : s)
	{
		if (c == '\'') out += "'\\''";
		else out += c;
	}
	return out + "'";
}

static std::wstring widen(const std::string& s)
{
	if (s.empty()) return L"";
	int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), nullptr, 0);
	std::wstring out(n, L'\0');
	MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), &out[0], n);
	return out;
}

// Runs a short command and returns its exit code (-1 if it couldn't start).
static int runQuiet(const std::string& commandLine, DWORD timeoutMs, std::string* output = nullptr)
{
	SECURITY_ATTRIBUTES sa{ sizeof(sa), nullptr, TRUE };
	HANDLE readPipe = nullptr, writePipe = nullptr;
	if (!CreatePipe(&readPipe, &writePipe, &sa, 0)) return -1;
	SetHandleInformation(readPipe, HANDLE_FLAG_INHERIT, 0);

	STARTUPINFOW si{};
	si.cb = sizeof(si);
	si.dwFlags = STARTF_USESTDHANDLES;
	si.hStdOutput = writePipe;
	si.hStdError = writePipe;
	// Empty input: never hand the server's own console input to a child.
	HANDLE nul = CreateFileW(L"NUL", GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, &sa, OPEN_EXISTING, 0, nullptr);
	si.hStdInput = nul;
	PROCESS_INFORMATION pi{};
	std::wstring cmd = widen(commandLine);
	BOOL ok = CreateProcessW(nullptr, &cmd[0], nullptr, nullptr, TRUE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi);
	CloseHandle(writePipe);
	if (nul != INVALID_HANDLE_VALUE) CloseHandle(nul);
	if (!ok)
	{
		CloseHandle(readPipe);
		return -1;
	}
	std::string collected;
	std::thread reader([&] {
		char buf[4096];
		DWORD got;
		while (ReadFile(readPipe, buf, sizeof(buf), &got, nullptr) && got > 0)
			if (collected.size() < 65536) collected.append(buf, got);
	});
	DWORD wait = WaitForSingleObject(pi.hProcess, timeoutMs);
	if (wait == WAIT_TIMEOUT) TerminateProcess(pi.hProcess, 1);
	WaitForSingleObject(pi.hProcess, 5000);
	DWORD code = 1;
	GetExitCodeProcess(pi.hProcess, &code);
	CloseHandle(pi.hProcess);
	CloseHandle(pi.hThread);
	reader.join();
	CloseHandle(readPipe);
	if (output) *output = collected;
	return wait == WAIT_TIMEOUT ? -2 : (int)code;
}

// Keeps whole UTF-8 characters: returns the complete prefix, leaves the rest in `pending`.
static std::string takeComplete(std::string& pending)
{
	size_t end = pending.size();
	size_t i = end;
	// look back at most 3 bytes for an unfinished multi-byte character
	for (int k = 0; k < 3 && i > 0; k++)
	{
		unsigned char c = pending[i - 1];
		if ((c & 0xC0) == 0x80) { i--; continue; }
		if (c >= 0xC0)
		{
			size_t need = (c & 0xE0) == 0xC0 ? 2 : (c & 0xF0) == 0xE0 ? 3 : 4;
			if (end - (i - 1) < need) end = i - 1;
		}
		break;
	}
	std::string out = pending.substr(0, end);
	pending.erase(0, end);
	if (!isValidUtf8(out))
	{
		for (char& c : out)
			if ((unsigned char)c >= 0x80) c = '?';
	}
	return out;
}

CodeRunner::CodeRunner(const Config& config, Send send) : _cfg(config), _send(std::move(send))
{
}

CodeRunner::~CodeRunner()
{
	stopAll();
}

bool CodeRunner::supports(const std::string& path) const
{
	std::string ext = Util::lower(fs::path(path).extension().string());
	return _cfg.runLanguages.count(ext) > 0;
}

size_t CodeRunner::running()
{
	std::lock_guard<std::mutex> lock(_mutex);
	return _runs.size();
}

bool CodeRunner::dockerAvailable(std::string& problem)
{
	std::lock_guard<std::mutex> lock(_mutex);
	long long now = nowMs();
	if (now - _dockerCheckedAt < (_dockerOk ? 300000 : 15000))
	{
		problem = _dockerProblem;
		return _dockerOk;
	}
	std::string output;
	int code = runQuiet(_cfg.docker + " version --format {{.Server.Version}}", 15000, &output);
	_dockerCheckedAt = now;
	_dockerOk = code == 0;
	if (code == -1) _dockerProblem = "Docker isn't installed on the server, so programs can't be run.";
	else if (code != 0) _dockerProblem = "Docker isn't running on the server, so programs can't be run.";
	else _dockerProblem.clear();
	if (!_dockerOk) LOG_WARN("Run code: " << _dockerProblem << " (" << Util::trim(output) << ")");
	problem = _dockerProblem;
	return _dockerOk;
}

std::string CodeRunner::start(uint64_t sessionId, const std::vector<RunFile>& files, const std::string& entry, const std::string& input)
{
	if (!_cfg.runEnabled) throw ApiError("unavailable", "Running code is turned off on this server.");
	std::string ext = Util::lower(fs::path(entry).extension().string());
	auto language = _cfg.runLanguages.find(ext);
	if (language == _cfg.runLanguages.end())
		throw ApiError("bad_request", "Files ending in '" + ext + "' can't be run. Supported: .py, .js, .c, .cpp, .java.");
	std::string problem;
	if (!dockerAvailable(problem)) throw ApiError("unavailable", problem);

	auto run = std::make_shared<Run>();
	{
		std::lock_guard<std::mutex> lock(_mutex);
		if ((int)_runs.size() >= _cfg.runMaxConcurrent)
			throw ApiError("unavailable", "The server is busy running other programs. Try again in a moment.");
		for (auto& r : _runs)
			if (r.second->sessionId == sessionId)
				throw ApiError("conflict", "Your previous program is still running. Stop it first.");
		run->id = "r" + std::to_string(_nextId++) + Util::randomCode(4);
		run->sessionId = sessionId;
		_runs[run->id] = run;
	}
	run->entry = entry;
	run->input = input;
	run->language = language->second;
	try
	{
		run->dir = fs::temp_directory_path() / "githubdemo-runs" / run->id;
		fs::create_directories(run->dir);
		for (const RunFile& f : files)
		{
			fs::path target = run->dir / fs::u8path(f.path);
			fs::create_directories(target.parent_path());
			std::ofstream out(target, std::ios::binary);
			out.write(f.text.data(), (std::streamsize)f.text.size());
		}
	}
	catch (const std::exception& e)
	{
		std::lock_guard<std::mutex> lock(_mutex);
		_runs.erase(run->id);
		LOG_ERROR("Run code: can't prepare files: " << e.what());
		throw ApiError("internal", "The server couldn't prepare the files to run.");
	}
	std::thread(&CodeRunner::execute, this, run).detach();
	return run->id;
}

void CodeRunner::execute(std::shared_ptr<Run> run)
{
	long long started = nowMs();
	auto output = [&](const char* stream, const std::string& text) {
		if (!text.empty())
			_send(run->sessionId, Json::object({ { "t", "ev.run.output" }, { "runId", run->id }, { "stream", stream }, { "text", text } }));
	};
	Json exit = Json::object({ { "t", "ev.run.exit" }, { "runId", run->id } });
	bool timedOut = false;
	long long exitCode = -1;
	std::string error;

	do
	{
		// First run of a language on this server: download its image (no time limit).
		const std::string& image = run->language.image;
		if (runQuiet(_cfg.docker + " image inspect " + quoteArg(image), 30000) != 0)
		{
			output("system", "Preparing " + image + " (first run on this server, this can take a minute)...\n");
			std::string pullOutput;
			int pulled = runQuiet(_cfg.docker + " pull " + quoteArg(image), 600000, &pullOutput);
			if (pulled != 0)
			{
				error = "Couldn't download " + image + " on the server.";
				LOG_WARN("Run code: docker pull " << image << " failed: " << Util::trim(pullOutput));
				break;
			}
		}
		if (run->stopRequested) { error = "Stopped."; break; }

		std::string entry = "/code/" + run->entry;
		std::string dir = fs::path(entry).parent_path().generic_string();
		std::string command = run->language.command;
		for (auto& placeholder : { std::make_pair(std::string("{file}"), shellQuote(entry)), std::make_pair(std::string("{dir}"), shellQuote(dir)) })
		{
			size_t at;
			while ((at = command.find(placeholder.first)) != std::string::npos) command.replace(at, placeholder.first.size(), placeholder.second);
		}

		char memory[32], cpus[32];
		snprintf(memory, sizeof(memory), "%dm", _cfg.runMemoryMb);
		snprintf(cpus, sizeof(cpus), "%.2f", _cfg.runCpus);
		std::string commandLine = _cfg.docker + " run --rm -i --name gh-run-" + run->id +
			" --network none --memory " + memory + " --memory-swap " + memory + " --cpus " + cpus +
			" --pids-limit 128 --read-only --tmpfs /tmp:rw,exec,size=64m --security-opt no-new-privileges --cap-drop ALL" +
			" --user 65534:65534 --env HOME=/tmp --mount " + quoteArg("type=bind,source=" + run->dir.string() + ",target=/code,readonly") +
			" -w /code " + quoteArg(image) + " sh -c " + quoteArg(command);
		LOG_DEBUG("Run code: " << commandLine);

		SECURITY_ATTRIBUTES sa{ sizeof(sa), nullptr, TRUE };
		HANDLE outRead, outWrite, errRead, errWrite, inRead, inWrite;
		CreatePipe(&outRead, &outWrite, &sa, 0);
		CreatePipe(&errRead, &errWrite, &sa, 0);
		CreatePipe(&inRead, &inWrite, &sa, 0);
		SetHandleInformation(outRead, HANDLE_FLAG_INHERIT, 0);
		SetHandleInformation(errRead, HANDLE_FLAG_INHERIT, 0);
		SetHandleInformation(inWrite, HANDLE_FLAG_INHERIT, 0);

		STARTUPINFOW si{};
		si.cb = sizeof(si);
		si.dwFlags = STARTF_USESTDHANDLES;
		si.hStdOutput = outWrite;
		si.hStdError = errWrite;
		si.hStdInput = inRead;
		PROCESS_INFORMATION pi{};
		std::wstring cmd = widen(commandLine);
		BOOL ok = CreateProcessW(nullptr, &cmd[0], nullptr, nullptr, TRUE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi);
		CloseHandle(outWrite);
		CloseHandle(errWrite);
		CloseHandle(inRead);
		if (!ok)
		{
			CloseHandle(outRead);
			CloseHandle(errRead);
			CloseHandle(inWrite);
			error = "The server couldn't start Docker.";
			break;
		}
		{
			std::lock_guard<std::mutex> lock(run->processMutex);
			run->process = pi.hProcess;
		}

		// Program input, then end-of-file.
		std::thread writer([&] {
			DWORD written;
			if (!run->input.empty()) WriteFile(inWrite, run->input.data(), (DWORD)run->input.size(), &written, nullptr);
			CloseHandle(inWrite);
		});

		std::atomic<size_t> total{ 0 };
		std::atomic<bool> truncated{ false };
		size_t limit = (size_t)_cfg.runMaxOutputKb * 1024;
		auto pump = [&](HANDLE pipe, const char* stream) {
			std::string pending;
			char buf[4096];
			DWORD got;
			while (ReadFile(pipe, buf, sizeof(buf), &got, nullptr) && got > 0)
			{
				if (truncated) continue; // keep draining so the program isn't blocked
				if (total.fetch_add(got) + got > limit)
				{
					truncated = true;
					output("system", "\n[Output limit reached; the program was stopped.]\n");
					runQuiet(_cfg.docker + " kill gh-run-" + run->id, 15000);
					continue;
				}
				pending.append(buf, got);
				output(stream, takeComplete(pending));
			}
			if (!truncated) output(stream, pending);
		};
		std::thread outReader(pump, outRead, "stdout");
		std::thread errReader(pump, errRead, "stderr");

		// Container start-up takes a moment, so allow a few seconds on top of the limit.
		DWORD limitMs = (DWORD)_cfg.runTimeoutSeconds * 1000 + 4000;
		DWORD wait = WaitForSingleObject(pi.hProcess, limitMs);
		if (wait == WAIT_TIMEOUT)
		{
			timedOut = !run->stopRequested;
			runQuiet(_cfg.docker + " kill gh-run-" + run->id, 15000);
			if (WaitForSingleObject(pi.hProcess, 10000) == WAIT_TIMEOUT) TerminateProcess(pi.hProcess, 1);
		}
		DWORD code = 1;
		GetExitCodeProcess(pi.hProcess, &code);
		exitCode = (long long)code;
		writer.join();
		outReader.join();
		errReader.join();
		CloseHandle(outRead);
		CloseHandle(errRead);
		{
			std::lock_guard<std::mutex> lock(run->processMutex);
			run->process = nullptr;
		}
		CloseHandle(pi.hProcess);
		CloseHandle(pi.hThread);
		if (run->stopRequested) error = "Stopped.";
		else if (exitCode == 137 && !timedOut && !truncated) error = "The program ran out of memory (" + std::to_string(_cfg.runMemoryMb) + " MB limit).";
		else if (exitCode == 125) error = "Docker couldn't start the program.";
	} while (false);

	exit.set("exitCode", exitCode);
	exit.set("timedOut", timedOut);
	exit.set("durationMs", nowMs() - started);
	if (timedOut) exit.set("error", "The program ran longer than " + std::to_string(_cfg.runTimeoutSeconds) + " seconds and was stopped.");
	else if (!error.empty()) exit.set("error", error);
	_send(run->sessionId, exit);

	std::error_code ec;
	fs::remove_all(run->dir, ec);
	std::lock_guard<std::mutex> lock(_mutex);
	_runs.erase(run->id);
}

void CodeRunner::stop(uint64_t sessionId, const std::string& runId)
{
	std::shared_ptr<Run> run;
	{
		std::lock_guard<std::mutex> lock(_mutex);
		auto it = _runs.find(runId);
		if (it == _runs.end() || it->second->sessionId != sessionId) return;
		run = it->second;
	}
	run->stopRequested = true;
	std::thread([this, run] { runQuiet(_cfg.docker + " kill gh-run-" + run->id, 15000); }).detach();
}

void CodeRunner::stopAllFor(uint64_t sessionId)
{
	std::vector<std::string> ids;
	{
		std::lock_guard<std::mutex> lock(_mutex);
		for (auto& r : _runs)
			if (r.second->sessionId == sessionId) ids.push_back(r.first);
	}
	for (auto& id : ids) stop(sessionId, id);
}

void CodeRunner::stopAll()
{
	std::vector<std::shared_ptr<Run>> runs;
	{
		std::lock_guard<std::mutex> lock(_mutex);
		for (auto& r : _runs) runs.push_back(r.second);
	}
	for (auto& run : runs)
	{
		run->stopRequested = true;
		runQuiet(_cfg.docker + " kill gh-run-" + run->id, 15000);
	}
}
