#include "PasswordHasher.h"
#include <windows.h>
#include <bcrypt.h>
#include <stdexcept>
#include <vector>

#pragma comment(lib, "bcrypt.lib")

namespace
{
	const char* PREFIX = "pbkdf2";
	const unsigned long ITERATIONS = 200000;
	const size_t SALT_BYTES = 16;
	const size_t HASH_BYTES = 32;

	std::string toHex(const std::vector<unsigned char>& bytes)
	{
		static const char digits[] = "0123456789abcdef";
		std::string out;
		for (unsigned char b : bytes)
		{
			out += digits[b >> 4];
			out += digits[b & 0xF];
		}
		return out;
	}

	std::vector<unsigned char> fromHex(const std::string& hex)
	{
		std::vector<unsigned char> out;
		for (size_t i = 0; i + 1 < hex.size(); i += 2)
		{
			out.push_back((unsigned char)std::stoi(hex.substr(i, 2), nullptr, 16));
		}
		return out;
	}

	std::vector<unsigned char> pbkdf2(const std::string& password, const std::vector<unsigned char>& salt, unsigned long iterations)
	{
		BCRYPT_ALG_HANDLE alg = nullptr;
		if (!BCRYPT_SUCCESS(BCryptOpenAlgorithmProvider(&alg, BCRYPT_SHA256_ALGORITHM, nullptr, BCRYPT_ALG_HANDLE_HMAC_FLAG)))
		{
			throw std::runtime_error("BCryptOpenAlgorithmProvider failed");
		}
		std::vector<unsigned char> key(HASH_BYTES);
		NTSTATUS status = BCryptDeriveKeyPBKDF2(alg,
			(PUCHAR)password.data(), (ULONG)password.size(),
			(PUCHAR)salt.data(), (ULONG)salt.size(),
			iterations, key.data(), (ULONG)key.size(), 0);
		BCryptCloseAlgorithmProvider(alg, 0);
		if (!BCRYPT_SUCCESS(status))
		{
			throw std::runtime_error("BCryptDeriveKeyPBKDF2 failed");
		}
		return key;
	}

	bool constantTimeEquals(const std::string& a, const std::string& b)
	{
		if (a.size() != b.size())
		{
			return false;
		}
		unsigned char diff = 0;
		for (size_t i = 0; i < a.size(); i++)
		{
			diff |= a[i] ^ b[i];
		}
		return diff == 0;
	}
}

std::string PasswordHasher::hash(const std::string& password)
{
	std::vector<unsigned char> salt(SALT_BYTES);
	if (!BCRYPT_SUCCESS(BCryptGenRandom(nullptr, salt.data(), (ULONG)salt.size(), BCRYPT_USE_SYSTEM_PREFERRED_RNG)))
	{
		throw std::runtime_error("BCryptGenRandom failed");
	}
	return std::string(PREFIX) + "$" + std::to_string(ITERATIONS) + "$" + toHex(salt) + "$" + toHex(pbkdf2(password, salt, ITERATIONS));
}

bool PasswordHasher::verify(const std::string& password, const std::string& stored, bool& needsUpgrade)
{
	needsUpgrade = false;

	if (stored.rfind(std::string(PREFIX) + "$", 0) != 0)
	{
		// Legacy row: the raw client hash was stored as-is.
		bool ok = constantTimeEquals(password, stored);
		needsUpgrade = ok;
		return ok;
	}

	size_t p1 = stored.find('$');
	size_t p2 = stored.find('$', p1 + 1);
	size_t p3 = stored.find('$', p2 + 1);
	if (p2 == std::string::npos || p3 == std::string::npos)
	{
		return false;
	}
	unsigned long iterations = std::stoul(stored.substr(p1 + 1, p2 - p1 - 1));
	std::vector<unsigned char> salt = fromHex(stored.substr(p2 + 1, p3 - p2 - 1));
	std::string expected = stored.substr(p3 + 1);

	return constantTimeEquals(toHex(pbkdf2(password, salt, iterations)), expected);
}
