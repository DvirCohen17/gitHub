#pragma once
#include "Config.h"
#include "Json.h"
#include <atomic>
#include <functional>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

struct RunFile
{
	std::string path;
	std::string text;
};

// Runs a project's code in a throw-away Docker container (no network, limited
// memory, CPU, processes and time) and streams its output back as
// ev.run.output / ev.run.exit messages.
class CodeRunner
{
public:
	using Send = std::function<void(uint64_t sessionId, const Json& message)>;

	CodeRunner(const Config& config, Send send);
	~CodeRunner();

	// Returns the run id. Throws ApiError when it can't start.
	std::string start(uint64_t sessionId, const std::vector<RunFile>& files, const std::string& entry, const std::string& input);
	void stop(uint64_t sessionId, const std::string& runId);
	void stopAllFor(uint64_t sessionId);
	void stopAll();
	size_t running();
	bool supports(const std::string& path) const;

	struct Run;

private:
	void execute(std::shared_ptr<Run> run);
	bool dockerAvailable(std::string& problem);

	const Config& _cfg;
	Send _send;
	std::mutex _mutex;
	std::map<std::string, std::shared_ptr<Run>> _runs;
	std::atomic<long long> _nextId{ 1 };
	long long _dockerCheckedAt = 0;
	bool _dockerOk = false;
	std::string _dockerProblem;
};
