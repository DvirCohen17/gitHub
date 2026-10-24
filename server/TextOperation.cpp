#include "TextOperation.h"
#include <windows.h>
#include <stdexcept>

TextOperation& TextOperation::retain(size_t n)
{
	if (n == 0) return *this;
	_base += n;
	_target += n;
	if (!_ops.empty() && _ops.back().kind == Component::Retain)
		_ops.back().count += n;
	else
		_ops.push_back({ Component::Retain, n, L"" });
	return *this;
}

TextOperation& TextOperation::insert(const std::wstring& s)
{
	if (s.empty()) return *this;
	_target += s.size();
	if (!_ops.empty() && _ops.back().kind == Component::Insert)
	{
		_ops.back().text += s;
	}
	else if (!_ops.empty() && _ops.back().kind == Component::Delete)
	{
		// canonical form: inserts come before deletes at the same position
		if (_ops.size() >= 2 && _ops[_ops.size() - 2].kind == Component::Insert)
			_ops[_ops.size() - 2].text += s;
		else
			_ops.insert(_ops.end() - 1, { Component::Insert, 0, s });
	}
	else
	{
		_ops.push_back({ Component::Insert, 0, s });
	}
	return *this;
}

TextOperation& TextOperation::remove(size_t n)
{
	if (n == 0) return *this;
	_base += n;
	if (!_ops.empty() && _ops.back().kind == Component::Delete)
		_ops.back().count += n;
	else
		_ops.push_back({ Component::Delete, n, L"" });
	return *this;
}

bool TextOperation::isNoop() const
{
	return _ops.empty() || (_ops.size() == 1 && _ops[0].kind == Component::Retain);
}

std::wstring TextOperation::apply(const std::wstring& document) const
{
	if (document.size() != _base)
		throw std::invalid_argument("operation does not match the document length");
	std::wstring result;
	result.reserve(_target);
	size_t pos = 0;
	for (const Component& c : _ops)
	{
		switch (c.kind)
		{
		case Component::Retain: result.append(document, pos, c.count); pos += c.count; break;
		case Component::Insert: result += c.text; break;
		case Component::Delete: pos += c.count; break;
		}
	}
	return result;
}

std::string TextOperation::encode() const
{
	std::string out;
	for (const Component& c : _ops)
	{
		switch (c.kind)
		{
		case Component::Retain: out += "r" + std::to_string(c.count) + ","; break;
		case Component::Delete: out += "d" + std::to_string(c.count) + ","; break;
		case Component::Insert:
		{
			std::string utf8 = wideToUtf8(c.text);
			out += "i" + std::to_string(utf8.size()) + ":" + utf8;
			break;
		}
		}
	}
	return out;
}

static size_t parseCount(const std::string& wire, size_t& i, char terminator)
{
	size_t end = wire.find(terminator, i);
	if (end == std::string::npos || end == i || end - i > 9)
		throw std::invalid_argument("malformed operation");
	for (size_t k = i; k < end; k++)
		if (wire[k] < '0' || wire[k] > '9')
			throw std::invalid_argument("malformed operation");
	size_t n = std::stoul(wire.substr(i, end - i));
	i = end + 1;
	return n;
}

TextOperation TextOperation::decode(const std::string& wire)
{
	TextOperation op;
	size_t i = 0;
	while (i < wire.size())
	{
		char kind = wire[i++];
		if (kind == 'i')
		{
			size_t n = parseCount(wire, i, ':');
			if (i + n > wire.size())
				throw std::invalid_argument("malformed operation");
			op.insert(utf8ToWide(wire.substr(i, n)));
			i += n;
		}
		else if (kind == 'r')
			op.retain(parseCount(wire, i, ','));
		else if (kind == 'd')
			op.remove(parseCount(wire, i, ','));
		else
			throw std::invalid_argument("malformed operation");
	}
	return op;
}

std::pair<TextOperation, TextOperation> TextOperation::transform(const TextOperation& a, const TextOperation& b)
{
	if (a._base != b._base)
		throw std::invalid_argument("operations were made against different documents");

	TextOperation ap, bp;
	size_t i = 0, j = 0;
	// Current (partly consumed) components; count tracks what is left of them.
	bool hasX = !a._ops.empty(), hasY = !b._ops.empty();
	Component x = hasX ? a._ops[0] : Component{};
	Component y = hasY ? b._ops[0] : Component{};

	auto nextX = [&]() { i++; hasX = i < a._ops.size(); if (hasX) x = a._ops[i]; };
	auto nextY = [&]() { j++; hasY = j < b._ops.size(); if (hasY) y = b._ops[j]; };

	while (hasX || hasY)
	{
		if (hasX && x.kind == Component::Insert)
		{
			ap.insert(x.text);
			bp.retain(x.text.size());
			nextX();
			continue;
		}
		if (hasY && y.kind == Component::Insert)
		{
			ap.retain(y.text.size());
			bp.insert(y.text);
			nextY();
			continue;
		}
		if (!hasX || !hasY)
			throw std::invalid_argument("operations have different lengths");

		size_t m = x.count < y.count ? x.count : y.count;
		if (x.kind == Component::Retain && y.kind == Component::Retain)
		{
			ap.retain(m);
			bp.retain(m);
		}
		else if (x.kind == Component::Delete && y.kind == Component::Retain)
		{
			ap.remove(m);
		}
		else if (x.kind == Component::Retain && y.kind == Component::Delete)
		{
			bp.remove(m);
		}
		// both delete the same text: nothing left to do for either

		x.count -= m;
		y.count -= m;
		if (x.count == 0) nextX();
		if (y.count == 0) nextY();
	}
	return { ap, bp };
}

std::wstring utf8ToWide(const std::string& s)
{
	if (s.empty()) return L"";
	int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), nullptr, 0);
	std::wstring out(n, L'\0');
	MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), &out[0], n);
	return out;
}

std::string wideToUtf8(const std::wstring& s)
{
	if (s.empty()) return "";
	int n = WideCharToMultiByte(CP_UTF8, 0, s.data(), (int)s.size(), nullptr, 0, nullptr, nullptr);
	std::string out(n, '\0');
	WideCharToMultiByte(CP_UTF8, 0, s.data(), (int)s.size(), &out[0], n, nullptr, nullptr);
	return out;
}
