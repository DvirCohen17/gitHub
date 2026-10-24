#pragma once
#include <string>
#include <utility>
#include <vector>
#include "Json.h"

// A change to a text document, used to keep everyone editing a file in sync
// (operational transformation, same algorithm as ot.js).
//
// An operation walks the whole document: retain n characters, delete n
// characters, or insert text. Positions are UTF-16 code units (std::wstring on
// Windows), which is what the C# editor uses, so both sides always agree.
//
// JSON form (same as ot.js): n > 0 retain, n < 0 delete, "text" insert.
class TextOperation
{
public:
	struct Component
	{
		enum Kind { Retain, Insert, Delete } kind;
		size_t count;        // Retain / Delete
		std::wstring text;   // Insert
	};

	TextOperation& retain(size_t n);
	TextOperation& insert(const std::wstring& s);
	TextOperation& remove(size_t n);

	size_t baseLength() const { return _base; }
	size_t targetLength() const { return _target; }
	bool isNoop() const;

	// Throws std::invalid_argument if the operation doesn't fit the document.
	std::wstring apply(const std::wstring& document) const;

	Json toJson() const;
	static TextOperation fromJson(const Json& json); // throws std::invalid_argument
	// Where a cursor at index (in the document before) is after this operation.
	size_t transformIndex(size_t index) const;

	// a and b were made against the same document. Returns (a', b') such that
	// applying a then b' gives the same text as applying b then a'.
	// At equal positions a's insertion goes first.
	static std::pair<TextOperation, TextOperation> transform(const TextOperation& a, const TextOperation& b);

private:
	std::vector<Component> _ops;
	size_t _base = 0;
	size_t _target = 0;
};

std::wstring utf8ToWide(const std::string& s);
std::string wideToUtf8(const std::wstring& s);
