#pragma once
#include <string>

// Server-side password storage.
// Stored format: "pbkdf2$<iterations>$<salt hex>$<hash hex>" (PBKDF2-HMAC-SHA256).
// Rows written before this existed hold the raw client hash; verify() still
// accepts those and reports needsUpgrade so the caller can re-store them.
class PasswordHasher
{
public:
	static std::string hash(const std::string& password);
	static bool verify(const std::string& password, const std::string& stored, bool& needsUpgrade);
};
