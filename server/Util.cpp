#include "Util.h"
#include <windows.h>
#include <bcrypt.h>
#include <stdexcept>

#pragma comment(lib, "bcrypt.lib")

static const char* BASE64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

std::string Util::base64Encode(const std::string& bytes)
{
	std::string out;
	out.reserve((bytes.size() + 2) / 3 * 4);
	size_t i = 0;
	while (i + 2 < bytes.size())
	{
		unsigned v = ((unsigned char)bytes[i] << 16) | ((unsigned char)bytes[i + 1] << 8) | (unsigned char)bytes[i + 2];
		out += BASE64[v >> 18];
		out += BASE64[(v >> 12) & 63];
		out += BASE64[(v >> 6) & 63];
		out += BASE64[v & 63];
		i += 3;
	}
	if (i + 1 == bytes.size())
	{
		unsigned v = (unsigned char)bytes[i] << 16;
		out += BASE64[v >> 18];
		out += BASE64[(v >> 12) & 63];
		out += "==";
	}
	else if (i + 2 == bytes.size())
	{
		unsigned v = ((unsigned char)bytes[i] << 16) | ((unsigned char)bytes[i + 1] << 8);
		out += BASE64[v >> 18];
		out += BASE64[(v >> 12) & 63];
		out += BASE64[(v >> 6) & 63];
		out += '=';
	}
	return out;
}

bool Util::base64Decode(const std::string& text, std::string& bytes)
{
	bytes.clear();
	bytes.reserve(text.size() / 4 * 3);
	unsigned v = 0;
	int bits = 0;
	size_t padding = 0;
	for (char c : text)
	{
		int d;
		if (c >= 'A' && c <= 'Z') d = c - 'A';
		else if (c >= 'a' && c <= 'z') d = c - 'a' + 26;
		else if (c >= '0' && c <= '9') d = c - '0' + 52;
		else if (c == '+') d = 62;
		else if (c == '/') d = 63;
		else if (c == '=') { padding++; continue; }
		else return false;
		if (padding) return false; // data after padding
		v = (v << 6) | d;
		bits += 6;
		if (bits >= 8)
		{
			bits -= 8;
			bytes += (char)((v >> bits) & 0xFF);
		}
	}
	return padding <= 2;
}

std::string Util::sha256Hex(const std::string& bytes)
{
	unsigned char hash[32];
	if (BCryptHash(BCRYPT_SHA256_ALG_HANDLE, nullptr, 0, (PUCHAR)bytes.data(), (ULONG)bytes.size(), hash, sizeof(hash)) != 0)
		throw std::runtime_error("SHA-256 failed");
	static const char* HEX = "0123456789abcdef";
	std::string out;
	for (unsigned char b : hash)
	{
		out += HEX[b >> 4];
		out += HEX[b & 15];
	}
	return out;
}

std::string Util::randomCode(size_t length)
{
	static const char* ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 32 symbols
	std::string random(length, '\0');
	if (BCryptGenRandom(nullptr, (PUCHAR)&random[0], (ULONG)length, BCRYPT_USE_SYSTEM_PREFERRED_RNG) != 0)
		throw std::runtime_error("random number generator failed");
	std::string out;
	for (char c : random) out += ALPHABET[(unsigned char)c % 32];
	return out;
}

std::string Util::trim(const std::string& s)
{
	size_t a = s.find_first_not_of(" \t\r\n");
	if (a == std::string::npos) return "";
	size_t b = s.find_last_not_of(" \t\r\n");
	return s.substr(a, b - a + 1);
}

std::string Util::lower(const std::string& s)
{
	std::string out = s;
	for (char& c : out)
		if (c >= 'A' && c <= 'Z') c = c - 'A' + 'a';
	return out;
}

size_t Util::utf16Length(const std::string& utf8)
{
	size_t n = 0;
	for (unsigned char c : utf8)
	{
		if ((c & 0xC0) != 0x80) n++;    // first byte of a character
		if ((c & 0xF8) == 0xF0) n++;    // 4-byte sequences are a surrogate pair
	}
	return n;
}

std::string Util::formatDuration(long long ms)
{
	long long s = ms / 1000;
	long long d = s / 86400, h = (s / 3600) % 24, m = (s / 60) % 60;
	std::string out;
	if (d) out += std::to_string(d) + "d ";
	if (d || h) out += std::to_string(h) + "h ";
	out += std::to_string(m) + "m";
	return out;
}

std::string Util::truncateUtf8(const std::string& text, size_t maxBytes)
{
	if (text.size() <= maxBytes) return text;
	size_t cut = maxBytes > 3 ? maxBytes - 3 : 0;
	while (cut > 0 && ((unsigned char)text[cut] & 0xC0) == 0x80) cut--; // don't split a character
	return text.substr(0, cut) + "...";
}
