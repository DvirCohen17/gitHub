#pragma once
#include <string>
#include <vector>

namespace Util
{
	std::string base64Encode(const std::string& bytes);
	bool base64Decode(const std::string& text, std::string& bytes); // false on bad input
	std::string sha256Hex(const std::string& bytes);
	std::string randomCode(size_t length);       // letters and digits that can't be confused (no 0/O, 1/I)
	std::string trim(const std::string& s);
	std::string lower(const std::string& s);     // ASCII only
	size_t utf16Length(const std::string& utf8); // characters as the client counts them
	std::string formatDuration(long long ms);
	// At most maxBytes, cut at a character boundary, with "..." when shortened.
	std::string truncateUtf8(const std::string& text, size_t maxBytes);
}
