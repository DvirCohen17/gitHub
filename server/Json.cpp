#include "Json.h"
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>

static const Json NULL_JSON;

Json::Json(double d) : _type(Type::Number), _num(d)
{
	if (std::isfinite(d) && d == std::floor(d) && std::fabs(d) < 9.0e15)
	{
		_int = (long long)d;
		_isInt = true;
	}
}

Json Json::array()
{
	Json j;
	j._type = Type::Array;
	return j;
}

Json Json::array(std::initializer_list<Json> items)
{
	Json j = array();
	j._arr.assign(items.begin(), items.end());
	return j;
}

Json Json::object()
{
	Json j;
	j._type = Type::Object;
	return j;
}

Json Json::object(std::initializer_list<std::pair<std::string, Json>> members)
{
	Json j = object();
	for (const auto& m : members)
	{
		j.set(m.first, m.second);
	}
	return j;
}

bool Json::asBool() const
{
	if (_type != Type::Bool) throw std::runtime_error("expected true/false");
	return _bool;
}

long long Json::asInt() const
{
	if (_type != Type::Number || !_isInt) throw std::runtime_error("expected a whole number");
	return _int;
}

double Json::asDouble() const
{
	if (_type != Type::Number) throw std::runtime_error("expected a number");
	return _num;
}

const std::string& Json::asString() const
{
	if (_type != Type::String) throw std::runtime_error("expected text");
	return _str;
}

bool Json::has(const std::string& key) const
{
	if (_type != Type::Object) return false;
	for (const auto& m : _obj)
		if (m.first == key) return true;
	return false;
}

const Json& Json::operator[](const std::string& key) const
{
	if (_type == Type::Object)
	{
		for (const auto& m : _obj)
			if (m.first == key) return m.second;
	}
	return NULL_JSON;
}

Json& Json::operator[](const std::string& key)
{
	if (_type == Type::Null) _type = Type::Object;
	if (_type != Type::Object) throw std::runtime_error("not an object");
	for (auto& m : _obj)
		if (m.first == key) return m.second;
	_obj.emplace_back(key, Json());
	return _obj.back().second;
}

Json& Json::set(const std::string& key, Json value)
{
	(*this)[key] = std::move(value);
	return *this;
}

void Json::erase(const std::string& key)
{
	for (auto it = _obj.begin(); it != _obj.end(); ++it)
	{
		if (it->first == key)
		{
			_obj.erase(it);
			return;
		}
	}
}

const std::vector<std::pair<std::string, Json>>& Json::members() const
{
	static const std::vector<std::pair<std::string, Json>> none;
	return _type == Type::Object ? _obj : none;
}

size_t Json::size() const
{
	if (_type == Type::Array) return _arr.size();
	if (_type == Type::Object) return _obj.size();
	return 0;
}

const Json& Json::operator[](size_t index) const
{
	if (_type != Type::Array || index >= _arr.size()) return NULL_JSON;
	return _arr[index];
}

Json& Json::push(Json value)
{
	if (_type == Type::Null) _type = Type::Array;
	if (_type != Type::Array) throw std::runtime_error("not an array");
	_arr.push_back(std::move(value));
	return _arr.back();
}

const std::vector<Json>& Json::items() const
{
	static const std::vector<Json> none;
	return _type == Type::Array ? _arr : none;
}

// ---- Serialization ---------------------------------------------------------

void jsonQuote(std::string& out, const std::string& s)
{
	out += '"';
	for (unsigned char c : s)
	{
		switch (c)
		{
		case '"': out += "\\\""; break;
		case '\\': out += "\\\\"; break;
		case '\n': out += "\\n"; break;
		case '\r': out += "\\r"; break;
		case '\t': out += "\\t"; break;
		case '\b': out += "\\b"; break;
		case '\f': out += "\\f"; break;
		default:
			if (c < 0x20)
			{
				char buf[8];
				snprintf(buf, sizeof(buf), "\\u%04x", c);
				out += buf;
			}
			else
			{
				out += (char)c;
			}
		}
	}
	out += '"';
}

void Json::dumpTo(std::string& out) const
{
	switch (_type)
	{
	case Type::Null: out += "null"; break;
	case Type::Bool: out += _bool ? "true" : "false"; break;
	case Type::Number:
		if (_isInt)
		{
			out += std::to_string(_int);
		}
		else if (!std::isfinite(_num))
		{
			out += "null";
		}
		else
		{
			char buf[32];
			snprintf(buf, sizeof(buf), "%.17g", _num);
			out += buf;
		}
		break;
	case Type::String: jsonQuote(out, _str); break;
	case Type::Array:
		out += '[';
		for (size_t i = 0; i < _arr.size(); i++)
		{
			if (i) out += ',';
			_arr[i].dumpTo(out);
		}
		out += ']';
		break;
	case Type::Object:
		out += '{';
		for (size_t i = 0; i < _obj.size(); i++)
		{
			if (i) out += ',';
			jsonQuote(out, _obj[i].first);
			out += ':';
			_obj[i].second.dumpTo(out);
		}
		out += '}';
		break;
	}
}

std::string Json::dump() const
{
	std::string out;
	dumpTo(out);
	return out;
}

// ---- Parsing ---------------------------------------------------------------

namespace
{
	const int MAX_DEPTH = 64;

	struct Parser
	{
		const std::string& s;
		size_t i = 0;

		[[noreturn]] void fail(const char* what)
		{
			throw std::runtime_error(std::string("invalid JSON: ") + what + " at offset " + std::to_string(i));
		}

		void skipSpace()
		{
			while (i < s.size() && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
		}

		bool consume(const char* word)
		{
			size_t n = strlen(word);
			if (s.compare(i, n, word) == 0)
			{
				i += n;
				return true;
			}
			return false;
		}

		static void appendUtf8(std::string& out, unsigned cp)
		{
			if (cp < 0x80) out += (char)cp;
			else if (cp < 0x800)
			{
				out += (char)(0xC0 | (cp >> 6));
				out += (char)(0x80 | (cp & 0x3F));
			}
			else if (cp < 0x10000)
			{
				out += (char)(0xE0 | (cp >> 12));
				out += (char)(0x80 | ((cp >> 6) & 0x3F));
				out += (char)(0x80 | (cp & 0x3F));
			}
			else
			{
				out += (char)(0xF0 | (cp >> 18));
				out += (char)(0x80 | ((cp >> 12) & 0x3F));
				out += (char)(0x80 | ((cp >> 6) & 0x3F));
				out += (char)(0x80 | (cp & 0x3F));
			}
		}

		unsigned hex4()
		{
			if (i + 4 > s.size()) fail("short \\u escape");
			unsigned v = 0;
			for (int k = 0; k < 4; k++)
			{
				char c = s[i++];
				v <<= 4;
				if (c >= '0' && c <= '9') v |= c - '0';
				else if (c >= 'a' && c <= 'f') v |= c - 'a' + 10;
				else if (c >= 'A' && c <= 'F') v |= c - 'A' + 10;
				else fail("bad \\u escape");
			}
			return v;
		}

		std::string parseString()
		{
			i++; // opening quote
			std::string out;
			while (true)
			{
				if (i >= s.size()) fail("unterminated string");
				char c = s[i++];
				if (c == '"') break;
				if ((unsigned char)c < 0x20) fail("control character in string");
				if (c != '\\')
				{
					out += c;
					continue;
				}
				if (i >= s.size()) fail("unterminated escape");
				char e = s[i++];
				switch (e)
				{
				case '"': out += '"'; break;
				case '\\': out += '\\'; break;
				case '/': out += '/'; break;
				case 'b': out += '\b'; break;
				case 'f': out += '\f'; break;
				case 'n': out += '\n'; break;
				case 'r': out += '\r'; break;
				case 't': out += '\t'; break;
				case 'u':
				{
					unsigned cp = hex4();
					if (cp >= 0xD800 && cp <= 0xDBFF)
					{
						if (i + 6 <= s.size() && s[i] == '\\' && s[i + 1] == 'u')
						{
							i += 2;
							unsigned low = hex4();
							if (low < 0xDC00 || low > 0xDFFF) fail("bad surrogate pair");
							cp = 0x10000 + ((cp - 0xD800) << 10) + (low - 0xDC00);
						}
						else
						{
							fail("lone surrogate");
						}
					}
					else if (cp >= 0xDC00 && cp <= 0xDFFF)
					{
						fail("lone surrogate");
					}
					appendUtf8(out, cp);
					break;
				}
				default: fail("bad escape");
				}
			}
			return out;
		}

		Json parseNumber()
		{
			size_t start = i;
			if (s[i] == '-') i++;
			bool isInt = true;
			while (i < s.size() && isdigit((unsigned char)s[i])) i++;
			if (i < s.size() && s[i] == '.')
			{
				isInt = false;
				i++;
				while (i < s.size() && isdigit((unsigned char)s[i])) i++;
			}
			if (i < s.size() && (s[i] == 'e' || s[i] == 'E'))
			{
				isInt = false;
				i++;
				if (i < s.size() && (s[i] == '+' || s[i] == '-')) i++;
				while (i < s.size() && isdigit((unsigned char)s[i])) i++;
			}
			std::string text = s.substr(start, i - start);
			if (text.empty() || text == "-") fail("bad number");
			if (isInt && text.size() < 18)
			{
				return Json(std::strtoll(text.c_str(), nullptr, 10));
			}
			return Json(std::strtod(text.c_str(), nullptr));
		}

		Json parseValue(int depth)
		{
			if (depth > MAX_DEPTH) fail("nested too deeply");
			skipSpace();
			if (i >= s.size()) fail("unexpected end");
			char c = s[i];
			if (c == '{')
			{
				i++;
				Json obj = Json::object();
				skipSpace();
				if (i < s.size() && s[i] == '}')
				{
					i++;
					return obj;
				}
				while (true)
				{
					skipSpace();
					if (i >= s.size() || s[i] != '"') fail("expected key");
					std::string key = parseString();
					skipSpace();
					if (i >= s.size() || s[i] != ':') fail("expected ':'");
					i++;
					obj.set(key, parseValue(depth + 1));
					skipSpace();
					if (i < s.size() && s[i] == ',') { i++; continue; }
					if (i < s.size() && s[i] == '}') { i++; break; }
					fail("expected ',' or '}'");
				}
				return obj;
			}
			if (c == '[')
			{
				i++;
				Json arr = Json::array();
				skipSpace();
				if (i < s.size() && s[i] == ']')
				{
					i++;
					return arr;
				}
				while (true)
				{
					arr.push(parseValue(depth + 1));
					skipSpace();
					if (i < s.size() && s[i] == ',') { i++; continue; }
					if (i < s.size() && s[i] == ']') { i++; break; }
					fail("expected ',' or ']'");
				}
				return arr;
			}
			if (c == '"') return Json(parseString());
			if (c == '-' || isdigit((unsigned char)c)) return parseNumber();
			if (consume("true")) return Json(true);
			if (consume("false")) return Json(false);
			if (consume("null")) return Json();
			fail("unexpected character");
		}
	};
}

Json Json::parse(const std::string& text)
{
	if (!isValidUtf8(text))
	{
		throw std::runtime_error("invalid JSON: not UTF-8");
	}
	Parser p{ text };
	Json value = p.parseValue(0);
	p.skipSpace();
	if (p.i != text.size()) p.fail("trailing characters");
	return value;
}

bool isValidUtf8(const std::string& s)
{
	size_t i = 0;
	while (i < s.size())
	{
		unsigned char c = s[i];
		int extra;
		unsigned cp;
		if (c < 0x80) { i++; continue; }
		else if ((c & 0xE0) == 0xC0) { extra = 1; cp = c & 0x1F; }
		else if ((c & 0xF0) == 0xE0) { extra = 2; cp = c & 0x0F; }
		else if ((c & 0xF8) == 0xF0) { extra = 3; cp = c & 0x07; }
		else return false;
		if (i + extra >= s.size()) return false;
		for (int k = 1; k <= extra; k++)
		{
			unsigned char cc = s[i + k];
			if ((cc & 0xC0) != 0x80) return false;
			cp = (cp << 6) | (cc & 0x3F);
		}
		if ((extra == 1 && cp < 0x80) || (extra == 2 && cp < 0x800) || (extra == 3 && cp < 0x10000)
			|| cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF))
		{
			return false;
		}
		i += extra + 1;
	}
	return true;
}
