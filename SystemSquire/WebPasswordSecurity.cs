using System;
using System.Security.Cryptography;

namespace SystemSquire
{
    public static class WebPasswordSecurity
    {
        private const int SaltSizeBytes = 16;
        private const int HashSizeBytes = 32;
        private const int Iterations = 150000;

        public static (string Hash, string Salt) CreateHash(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentException("Password cannot be empty.", nameof(password));
            }

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSizeBytes);

            return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
        }

        public static bool Verify(string password, string storedHash, string storedSalt)
        {
            if (string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(storedHash) ||
                string.IsNullOrWhiteSpace(storedSalt))
            {
                return false;
            }

            try
            {
                byte[] expectedHash = Convert.FromBase64String(storedHash);
                byte[] salt = Convert.FromBase64String(storedSalt);

                byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                    password,
                    salt,
                    Iterations,
                    HashAlgorithmName.SHA256,
                    expectedHash.Length);

                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch
            {
                return false;
            }
        }
    }
}
