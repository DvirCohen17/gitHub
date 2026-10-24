#include "App.h"
#include "CodeRunner.h"
#include "Log.h"

// Running a project's code (see CodeRunner).

static const size_t MAX_RUN_BYTES = 10 * 1024 * 1024;

Json App::hRunStart(Session& s, const Json& req)
{
	FileRow entry = requireFile(reqId(req, "fileId"));
	requireMember(s, entry.projectId);
	std::string input = optString(req, "stdin", 64 * 1024);
	if (!_runner->supports(entry.path))
		fail("bad_request", "This kind of file can't be run. Supported: .py, .js, .c, .cpp, .java.");

	// Every file of the project goes into the container, with live text for open ones.
	std::map<long long, std::string> live;
	if (RoomPtr r = room(entry.projectId, false))
	{
		std::lock_guard<std::mutex> lock(r->m);
		for (auto& d : r->docs) live[d.first] = wideToUtf8(d.second.text);
	}
	std::vector<RunFile> files;
	size_t total = 0;
	{
		auto lock = _db.lock();
		auto st = _db.query("SELECT id, path, content FROM files WHERE project_id = ?1", entry.projectId);
		while (st.next())
		{
			RunFile f{ st.s(1), live.count(st.i(0)) ? live[st.i(0)] : st.s(2) };
			total += f.text.size();
			if (total > MAX_RUN_BYTES) fail("bad_request", "The project is too large to run (10 MB at most).");
			files.push_back(std::move(f));
		}
	}
	std::string runId = _runner->start(s.id, files, entry.path, input);
	LOG_INFO(s.name << " runs " << entry.path << " (project " << entry.projectId << ", run " << runId << ")");
	return Json::object({ { "runId", runId } });
}

Json App::hRunStop(Session& s, const Json& req)
{
	_runner->stop(s.id, reqString(req, "runId", 50));
	return Json::object();
}
