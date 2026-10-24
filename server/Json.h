#pragma once
#include <cstdint>
#include <initializer_list>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

// A small JSON value: parse, build and serialize. Objects keep insertion order.
// Strings are UTF-8.
class Json
{
public:
	enum class Type { Null, Bool, Number, String, Array, Object };

	Json() = default;
	Json(std::nullptr_t) {}
	Json(bool b) : _type(Type::Bool), _bool(b) {}
	Json(int n) : _type(Type::Number), _int(n), _num(n), _isInt(true) {}
	Json(long n) : _type(Type::Number), _int(n), _num((double)n), _isInt(true) {}
	Json(long long n) : _type(Type::Number), _int(n), _num((double)n), _isInt(true) {}
	Json(unsigned n) : _type(Type::Number), _int(n), _num(n), _isInt(true) {}
	Json(unsigned long long n) : _type(Type::Number), _int((long long)n), _num((double)n), _isInt(true) {}
	Json(double d);
	Json(const char* s) : _type(Type::String), _str(s) {}
	Json(std::string s) : _type(Type::String), _str(std::move(s)) {}

	static Json array();
	static Json array(std::initializer_list<Json> items);
	static Json object();
	static Json object(std::initializer_list<std::pair<std::string, Json>> members);

	Type type() const { return _type; }
	bool isNull() const { return _type == Type::Null; }
	bool isBool() const { return _type == Type::Bool; }
	bool isNumber() const { return _type == Type::Number; }
	bool isInt() const { return _type == Type::Number && _isInt; }
	bool isString() const { return _type == Type::String; }
	bool isArray() const { return _type == Type::Array; }
	bool isObject() const { return _type == Type::Object; }

	// Typed reads. Throw std::runtime_error on the wrong type.
	bool asBool() const;
	long long asInt() const;
	double asDouble() const;
	const std::string& asString() const;

	// Object access. The const version returns a null value for missing keys.
	bool has(const std::string& key) const;
	const Json& operator[](const std::string& key) const;
	const Json& operator[](const char* key) const { return (*this)[std::string(key)]; }
	Json& operator[](const std::string& key);        // turns null into an object, adds missing keys
	Json& operator[](const char* key) { return (*this)[std::string(key)]; }
	Json& set(const std::string& key, Json value);   // chainable
	void erase(const std::string& key);
	const std::vector<std::pair<std::string, Json>>& members() const;

	// Array access.
	size_t size() const;
	const Json& operator[](size_t index) const;
	const Json& operator[](int index) const { return (*this)[(size_t)index]; }
	Json& push(Json value);                           // turns null into an array
	const std::vector<Json>& items() const;

	std::string dump() const;
	static Json parse(const std::string& text);       // throws std::runtime_error

private:
	void dumpTo(std::string& out) const;

	Type _type = Type::Null;
	bool _bool = false;
	long long _int = 0;
	double _num = 0;
	bool _isInt = false;
	std::string _str;
	std::vector<Json> _arr;
	std::vector<std::pair<std::string, Json>> _obj;
};

// Appends s as a quoted JSON string.
void jsonQuote(std::string& out, const std::string& s);
// True when s is well-formed UTF-8.
bool isValidUtf8(const std::string& s);
