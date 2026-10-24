#pragma once
#include "Db.h"
#include <string>

namespace Schema
{
	// Creates or upgrades the tables in db.
	void create(Db& db);

	// Copies everything from a database written by the v1 server (syncDBTemp.sqlite)
	// into db, which must be freshly created. The old file is only read.
	void importLegacy(Db& db, const std::string& legacyPath);
}
